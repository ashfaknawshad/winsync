<div align="center">

<img src="Assets/winsync_preview.png" width="96" height="96" alt="WinSync icon" />

# WinSync

**Play the same system audio through two headphones on Windows — perfectly in sync.**

Any mix of wired, Bluetooth and USB. No driver. No admin rights.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows)](#)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

**[winsync.ashfaknawshad.dev](https://winsync.ashfaknawshad.dev)**

<img src="Assets/screenshot.png" width="560" alt="WinSync app window" />

</div>

---

Windows routes system audio to a single output device at a time. There's no
built-in way for two people to share one laptop through two separate pairs of
headphones — short of a splitter cable, a virtual mixer you configure by hand,
or Windows 11's Shared Audio, which only works if **both** headphones support
Bluetooth LE Audio.

WinSync solves the common case instead: **one wired headphone + one Bluetooth
headphone** (or any two devices), synchronized to within perceptual tolerance,
with drift correction so it stays that way for a two-hour movie.

## How it works

WinSync taps one real audio endpoint with WASAPI loopback capture and
re-renders that stream to one or more other endpoints, each with its own
adjustable delay and volume.

```
Tap endpoint ──(WASAPI loopback capture)──▶ ring buffer
                                              ├─▶ delay / gain ─▶ WASAPI render → Output 1
                                              └─▶ delay / gain ─▶ WASAPI render → Output 2
```

The key trick: **loopback capture happens inside the Windows audio engine,
before the signal reaches the Bluetooth radio.** So the captured copy is not
delayed by Bluetooth — only what the Bluetooth headphone actually plays is
late. That means you tap the *slowest* device and add delay to the *faster*
one to catch up. Tap the wired device instead and you'd need negative delay on
Bluetooth, which is impossible.

**Rule of thumb: always tap whichever device has the highest latency.**

A background loop also corrects for clock drift — two devices never share a
crystal oscillator, so their sample clocks slowly diverge. WinSync nudges
playback speed by at most ±0.3% every 250ms to hold the buffer at its target
fill level. That's well under the threshold where pitch change is audible, and
because it's a continuous nudge rather than dropping or duplicating samples,
there are no clicks.

For the full design writeup — endpoint architecture, the options considered
and rejected (kernel driver, hardware transmitter), and the drift-correction
math — see [`Dual-Audio-Report.md`](Dual-Audio-Report.md).

## Install

Grab the latest release from [Releases](../../releases). Three options:

- **`WinSync-Setup.exe`** (recommended, ~2MB download) — a bootstrapper that
  checks for the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
  and installs it automatically, silently, only if it's actually missing
  (~56MB, straight from Microsoft — most people already have it and won't
  download anything extra), then installs WinSync. This is the one to hand to
  someone who isn't going to troubleshoot a runtime prompt themselves.
- **`WinSync-Setup.msi`** (~700KB) — the same per-user installer, without the
  runtime bootstrapper wrapped around it. Same Start Menu/desktop shortcut
  checkboxes, clean uninstall via Settings → Apps, no admin rights needed —
  but if the runtime is missing, you'll need to grab it yourself. Good if you
  already know you have the runtime, or for scripted/silent installs.
- **`WinSync.exe`** (~70MB) — a single portable, self-contained file with the
  .NET runtime bundled in. No install, no prerequisites, just run it.

> Windows SmartScreen may warn about an unsigned file on first run. Click
> **More info → Run anyway**. WinSync is free and open source; you can read
> every line of it right here. (Working on getting this signed — see
> [SIGNING.md](SIGNING.md).)

### Build from source

```
git clone https://github.com/ashfaknawshad/winsync.git
cd winsync
dotnet publish -c Release -p:PublishProfile=Portable-win-x64    # standalone exe
dotnet publish -c Release -p:PublishProfile=Installer-win-x64   # small, for the MSI
```

Outputs land in `bin\publish\portable-win-x64\` and `bin\publish\installer-win-x64\`.

Or just `dotnet run` to launch it directly during development (requires the
[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)).

### Build the MSI

```
dotnet tool restore
dotnet publish -c Release -p:PublishProfile=Installer-win-x64
dotnet build Installer -c Release
```

The MSI lands in `Installer\bin\x64\Release\WinSync-Setup.msi` (built with the
[WiX Toolset](https://wixtoolset.org/) v5).

### Build the bootstrapper

Build the MSI first (above), then:

```
dotnet build Bundle -c Release
```

Lands in `Bundle\bin\x64\Release\WinSync-Setup.exe`. It chains the .NET 8
Desktop Runtime installer from Microsoft's own servers, downloading it only
if a runtime check (`netfx:DotNetCoreSearch`, from `WixToolset.Netfx.wixext`)
comes back empty — see the comments in
[`Bundle/Bundle.wxs`](Bundle/Bundle.wxs) for why that needs both a
`DetectCondition` and an `InstallCondition`, not just one.

## Usage

**Wired + Bluetooth (the common case):**

WinSync's window numbers your devices 1, 2 (and optionally 3). **Device 1 is
already playing** — it's whatever Windows' default output is, with no delay
control of its own. Device 2 and 3 are mirrored copies you can delay and
adjust independently. (Easy to miss at first: Device 1 counts as one of your
two headphones — you don't need to also turn on Device 2 *and* a phantom
third one to get two headphones working.)

1. Connect both headphones.
2. Set the **Bluetooth** headphone as the Windows default output (Win+Ctrl+V,
   or Settings → System → Sound). VLC and Chrome will follow it.
3. In WinSync, set **Device 1** to that same Bluetooth headphone.
4. Turn on **Device 2**, and set it to the wired headphone.
5. Press **Start**, play a video, and drag Device 2's **Delay** slider until
   the two headphones line up. Typical landing spot is 120–250ms.

The delay depends on the Bluetooth codec in use, so it varies per headset —
but it's stable for a given one. Worth writing down once you find it.

**Two wired headphones, or perfect symmetry:** Device 1 plays with no added
delay while the mirrored devices sit ~90ms behind, so two wired headphones
alone will drift out of step with each other. Fix: make Device 1 something
nobody listens to — [VB-Cable](https://vb-audio.com/Cable/)'s CABLE Input, an
unused HDMI/SPDIF endpoint, or a spare USB audio dongle — so both real
headphones are mirrored devices with independent sliders.

## Known limitations

| Limitation | Cause | Workaround |
|---|---|---|
| Netflix / some Edge playback is silent | Protected media path blocks loopback capture | Use Chrome |
| VLC in exclusive mode isn't captured | Loopback requires shared mode | VLC → Preferences → Audio → Output module: Automatic or WASAPI |
| Two wired headphones ~90ms out of step | Tap plays undelayed; mirrors carry the jitter cushion | Tap a silent device (VB-Cable, unused HDMI/SPDIF, dummy USB dongle) |
| Bluetooth quality drops when a mic is used | Windows switches A2DP → HFP | Disable the "Hands-Free" endpoint in Sound settings |
| Two Bluetooth headsets stutter | Two A2DP streams contending for one radio | Add a second USB Bluetooth adapter |
| Adds ~90ms latency to mirrored outputs | Jitter headroom in the ring buffer | Fine for video; not for gaming/production |

## Roadmap

- [ ] Saved presets, keyed on device ID
- [ ] Global hotkeys to nudge delay without alt-tabbing
- [ ] Tray/minimized operation
- [ ] Automatic latency estimation (correlated marker instead of manual tuning)
- [ ] Third+ output (the engine already supports N; only the UI is fixed at two)

## Contributing

Issues and PRs welcome. The codebase is small on purpose:

- [`AudioMirror.cs`](AudioMirror.cs) — the audio engine (capture, resample/drift, mirror targets)
- [`MainForm.cs`](MainForm.cs) / [`Controls.cs`](Controls.cs) — the UI
- [`Program.cs`](Program.cs) — entry point

## License

[MIT](LICENSE)
