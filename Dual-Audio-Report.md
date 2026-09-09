# Dual Audio: Simultaneous Playback to Two Headphones on Windows

**A technical report on the design and implementation of a user-mode audio mirroring tool**

---

## 1. Problem statement

Windows routes all system audio to a single output endpoint at a time. Two people
cannot watch the same video on one laptop through two separate pairs of
headphones without either sharing one pair, using a hardware splitter, or
installing a virtual audio mixer and configuring it by hand.

macOS has solved this since the early days of Core Audio: Audio MIDI Setup lets a
user create a *Multi-Output Device*, an aggregate endpoint that applications see
as a single device and that Core Audio fans out to several real devices, with
drift correction handled by the OS. Windows has no equivalent.

The goal of this project was a small, free, local tool that reproduces the useful
part of that behaviour for one specific scenario: watching video in VLC or Chrome
with two listeners, most often **one wired headphone and one Bluetooth headphone**.

---

## 2. Background: why Windows makes this hard

### 2.1 The endpoint model

The Windows audio stack builds its device list from the bottom up. Low-level
drivers expose Kernel Streaming filter pins; the **Audio Endpoint Builder**
service enumerates those pins and creates one high-level *endpoint* for each. The
Windows **Audio Engine** then serves those endpoints, mixing the streams from all
applications that have targeted a given endpoint.

An application's audio stream terminates at exactly one endpoint. There is no
supported mechanism for an endpoint to fan out to several others, and no
user-mode API to create a new endpoint. Creating a virtual output device requires
a kernel-mode driver.

### 2.2 Bluetooth is unicast

Classic Bluetooth audio uses A2DP, which is a point-to-point profile: one stream,
one receiver. Attempting two simultaneous A2DP streams from a single radio is
fighting the protocol rather than a driver limitation. It usually works on modern
adapters but halves the available airtime, and since A2DP relies on
retransmission for error recovery, the reduced headroom makes stuttering more
likely.

### 2.3 What Windows now does natively, and what it misses

Microsoft shipped a **Shared Audio** feature in Windows 11 during 2026, exposed
as a Quick Settings tile. It streams synchronised audio to two Bluetooth
accessories with independent volume per device.

Shared Audio is built on **Bluetooth LE Audio broadcast**, using the LC3 codec and
Isochronous Channels for timing guarantees. That architecture is the correct
solution to the multi-sink problem, but it carries hard hardware requirements:
both the PC radio and both accessories must support Bluetooth LE Audio, in
practice Bluetooth 5.3 or newer. Initial rollout was limited to recent Copilot+
hardware before widening.

Consequently Shared Audio does **not** address:

| Scenario | Covered by Shared Audio? |
|---|---|
| Two LE Audio earbuds, 2025+ laptop | Yes |
| One wired headphone + one Bluetooth headphone | **No** — the transport is LE Audio broadcast; a 3.5mm jack cannot join |
| Two classic-Bluetooth (A2DP) headphones | **No** |
| Older PC without an LE Audio radio | **No** |
| USB DAC + Bluetooth, or speakers + headphones | **No** |
| Per-device delay compensation | **No** |

The mixed wired/wireless case — the most common one in practice, since most people
own one of each — is precisely the gap.

---

## 3. Design options considered

Three architectures were evaluated.

### Option A — Kernel-mode virtual aggregate device

Write a driver presenting a single endpoint named e.g. "Both Headphones". The
user selects it in Sound settings like any other device; a service reads from it
and fans out. This is the VB-Cable / Voicemeeter model and the closest analogue to
the macOS aggregate device.

Starting points would be Microsoft's **SysVAD** sample (a WDM audio driver
exposing virtual endpoints via WaveRT) or the newer **ACX** class extension, a WDF
framework for audio built on KMDF rather than UMDF specifically to avoid the
latency of repeated user/kernel transitions during streaming.

**Rejected.** Since Windows 10 1607, a production kernel-mode driver cannot be
self-signed — Microsoft must sign it. Submitting for attestation signing requires
a Partner Center account with a registered EV code signing certificate, which
costs roughly $290–410 per year depending on the certificate authority and
requires the private key to live on a FIPS 140-2 hardware token. For a free,
local, single-machine tool, this is disproportionate.

### Option B — Hardware transmitter

A dual-link Bluetooth transmitter plugged into the headphone jack or a USB port
broadcasts two independent streams, bypassing Windows entirely. Effective, but it
is a purchase rather than a build, and such adapters present to Windows as a
generic USB audio device, so no software can see or control the headphones behind
them.

### Option C — User-mode loopback fan-out *(selected)*

Tap one real endpoint using WASAPI loopback capture, then re-render that stream to
one or more other endpoints. No driver, no certificate, no administrator rights,
no installer.

```
Tap endpoint ──(WASAPI loopback capture)──▶ ring buffer
                                              ├─▶ delay / gain ─▶ WASAPI render → Output 1
                                              └─▶ delay / gain ─▶ WASAPI render → Output 2
```

---

## 4. The central insight: tap the slowest device

The naive implementation of Option C fails on the mixed wired/wireless case, and
understanding why determines the whole design.

Bluetooth adds 150–250ms of end-to-end latency depending on codec. Wired adds
roughly 10ms. To synchronise them, one must **delay the wired output**. Delay can
only ever be added, never removed — you cannot make a Bluetooth headphone play
earlier than it does.

If the wired device is the tap, it plays natively with no delay and there is
nowhere to insert one. The Bluetooth copy would need *negative* delay. Impossible.

The resolution is a property of where loopback capture sits in the stack:

> **Loopback capture occurs inside the Windows audio engine, before the audio is
> handed to the Bluetooth transport.** The captured copy is therefore *not*
> delayed by Bluetooth. Only what the Bluetooth headphone itself plays is late.

So the correct configuration is:

1. Set the **Bluetooth** headphone as the Windows default output. VLC and Chrome
   follow it, and it plays ~200ms late at the ear.
2. Loopback-capture **that same endpoint**. The captured stream is undelayed.
3. Render to the **wired** headphone with an adjustable delay of ~200ms.

Both listeners now hear the same instant. The generalised rule:

> **Always tap whichever device has the highest latency.**

---

## 5. Implementation

The tool is a .NET 8 WinForms application (~450 lines) using the NAudio library
for WASAPI access.

### 5.1 Signal chain

Per output device:

```
BufferedWaveProvider ─▶ VariSpeed ─▶ ChannelMap ─▶ VolumeSampleProvider ─▶ WasapiOut
```

- **BufferedWaveProvider** — a ring buffer in the tap endpoint's mix format
  (32-bit float). Configured with `ReadFully = true` so it emits silence rather
  than starving the output when momentarily empty.
- **VariSpeed** — a linear-interpolating variable-rate reader. Its `Speed`
  property is "input frames consumed per output frame", so it performs both
  sample-rate conversion and drift correction in one pass.
- **ChannelMap** — adapts channel count where the tap and output endpoints differ.
- **VolumeSampleProvider** — per-device gain.
- **WasapiOut** — shared-mode render at the output device's native mix format.

### 5.2 Problem: loopback goes silent

WASAPI only pushes data to a render endpoint while something is actively playing
on it. When a video ends, the loopback capture receives nothing and downstream
outputs starve.

**Solution:** the application holds a permanent silent render stream open on the
tap endpoint (`SilenceProvider`), keeping the endpoint active for the lifetime of
the session. Buffers flagged `AUDCLNT_BUFFERFLAGS_SILENT` are treated as "emit
zeros", not "stop".

### 5.3 Problem: clock drift

Every audio endpoint runs from its own crystal oscillator. Two nominally 48kHz
devices typically differ by 20–100 parts per million. Over a two-hour film this
accumulates to seconds of divergence — the buffer either drains to silence or
overflows.

**Solution:** a control loop runs every 250ms. It measures the ring buffer fill
against its setpoint and nudges `VariSpeed.Speed`:

```
error       = currentFillMs - targetFillMs
correction  = clamp(1.0 + 0.0001 × error, 0.997, 1.003)
speed       = baseSpeed × correction
```

The ±0.3% ceiling is deliberate. It is comfortably below the threshold at which
pitch change becomes audible, yet more than sufficient to absorb crystal drift.
Crucially, this approach never drops or duplicates a block of samples, so there
are no clicks — the correction is continuous and inaudible.

Linear interpolation at a ratio near 1.0 acts as a fractional delay filter. Its
high-frequency rolloff is negligible at these correction magnitudes.

### 5.4 Problem: delay slider responsiveness

Because the delay *is* the ring buffer fill setpoint, moving the slider by 200ms
and waiting for the ±0.3% loop to converge would take over a minute — unusable for
interactive tuning.

**Solution:** on a slider change, the buffer is adjusted immediately. Increasing
delay injects the corresponding duration of silence; decreasing delay reads and
discards bytes. The drift loop then holds the new setpoint. The user hears one
brief discontinuity while dragging, which is acceptable in exchange for immediate
feedback.

### 5.5 Safety and edge cases

- Selecting the tap device as an output is rejected, since it would feed back on
  itself.
- Output devices are opened at their native mix format, avoiding format
  negotiation failures where one device runs at 48kHz and another at 44.1kHz.
- On capture failure the session tears down and restores normal audio.

---

## 6. Results

Tested with VLC and Chrome on Windows 11.

- Synchronisation between a Bluetooth headset and a wired headset is achievable
  to within perceptual tolerance by adjusting a single slider, typically landing
  between 120 and 250ms.
- The value is stable for a given headset — it depends on the codec, so it should
  be recorded once per pair of headphones.
- No audible drift or clicking over extended playback.
- Startup requires no administrator rights and installs nothing.

---

## 7. Known limitations

| Limitation | Cause | Mitigation |
|---|---|---|
| Netflix app / some Edge playback is silent | Protected media path blocks loopback capture | Use Chrome |
| VLC in exclusive mode is not captured | Loopback requires shared mode | Set VLC output module to Automatic or WASAPI shared |
| Two wired headphones are ~90ms out of step | The tap device plays undelayed; mirrored outputs carry the jitter cushion | Tap a silent third endpoint (VB-Cable "CABLE Input", an unused HDMI/SPDIF output, or a dummy USB dongle) so both real devices are mirrored |
| Bluetooth quality drops when an app opens the mic | Windows switches the headset from A2DP to HFP, which cannot carry stereo | Disable the "Hands-Free" endpoint in Sound settings |
| Two Bluetooth headsets may stutter | Two A2DP streams contending for one radio | Add a second USB Bluetooth adapter |
| Adds ~90ms latency to mirrored outputs | Jitter headroom in the ring buffer | Acceptable for video; unsuitable for gaming or music production |

---

## 8. Possible extensions

- **Saved presets** — store the tuned delay per headphone pair, keyed on device ID.
- **Global hotkeys** — nudge delay ±10ms while a video is playing, without
  alt-tabbing away.
- **Tray operation** — run minimised rather than as a visible window.
- **Automatic latency estimation** — measure round-trip delay by emitting an
  inaudible marker and correlating, removing the manual tuning step.
- **More than two outputs** — the architecture already supports N devices; only
  the UI is fixed at two.

---

## 9. Conclusion

The Windows audio architecture provides no aggregate output device, and the
platform's new native answer to this problem is confined to Bluetooth LE Audio
hardware on both ends. For the far more common case of one wired and one wireless
headphone, a user-mode solution remains necessary.

Such a solution is achievable without any kernel driver, and therefore without the
code-signing infrastructure that would otherwise dominate the project's cost. The
two design decisions that make it work are: tapping the highest-latency device so
that delay compensation is possible in the only direction it can be applied, and
correcting clock drift through continuous sub-audible speed adjustment rather than
by dropping samples.

---

## Appendix A — Quick reference

**Setup for wired + Bluetooth**

1. Connect both headphones.
2. Set the **Bluetooth** headphone as the Windows default output.
3. Tap device → the Bluetooth headphone.
4. Output 1 → the wired headphone.
5. Start, play a video, raise the delay slider until the two align.

**Key APIs**

| Purpose | API |
|---|---|
| Enumerate outputs | `IMMDeviceEnumerator::EnumAudioEndpoints(eRender, ...)` |
| Capture the mix | `IAudioClient::Initialize` with `AUDCLNT_STREAMFLAGS_LOOPBACK` (shared mode only) |
| Automatic format conversion | `AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM` |
| OS-level drift correction | `AUDCLNT_STREAMFLAGS_RATEADJUST` + `IAudioClockAdjustment::SetSampleRate` |
| Device change notifications | `IMMNotificationClient` |

**Typical latencies**

| Path | Approximate latency |
|---|---|
| Wired analogue | ~10ms |
| Bluetooth aptX Low Latency | ~40ms |
| Bluetooth aptX / AAC | ~70–200ms |
| Bluetooth SBC | ~150–250ms |
| Bluetooth LE Audio (LC3) | ~20–50ms |
