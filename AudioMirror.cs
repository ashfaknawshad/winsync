using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WinSync
{
    /// <summary>
    /// Linear-interpolating variable-speed reader.
    /// Speed is "input frames consumed per output frame". Setting it to
    /// srcRate/dstRate does sample-rate conversion; nudging it by a fraction
    /// of a percent absorbs clock drift between two independent devices.
    /// </summary>
    public sealed class VariSpeed : ISampleProvider
    {
        private readonly ISampleProvider src;
        private readonly int ch;
        private readonly float[] a;
        private readonly float[] b;
        private readonly float[] readBuf;
        private int readBufFrames;
        private int readBufPos;
        private double phase;
        private bool primed;

        private double speed = 1.0;
        public double Speed
        {
            get { return Volatile.Read(ref speed); }
            set { Volatile.Write(ref speed, value); }
        }

        public WaveFormat WaveFormat { get; }

        public VariSpeed(ISampleProvider src, int outputSampleRate)
        {
            this.src = src;
            ch = src.WaveFormat.Channels;
            a = new float[ch];
            b = new float[ch];
            readBuf = new float[ch * 4096];
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, ch);
        }

        private bool NextFrame(float[] dst)
        {
            if (readBufPos >= readBufFrames)
            {
                int got = src.Read(readBuf, 0, readBuf.Length);
                readBufFrames = got / ch;
                readBufPos = 0;
                if (readBufFrames == 0) return false;
            }
            Array.Copy(readBuf, readBufPos * ch, dst, 0, ch);
            readBufPos++;
            return true;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (!primed)
            {
                NextFrame(a);
                NextFrame(b);
                primed = true;
            }

            double sp = Speed;
            int frames = count / ch;

            for (int f = 0; f < frames; f++)
            {
                int o = offset + f * ch;
                for (int c = 0; c < ch; c++)
                    buffer[o + c] = (float)(a[c] + (b[c] - a[c]) * phase);

                phase += sp;
                while (phase >= 1.0)
                {
                    phase -= 1.0;
                    Array.Copy(b, a, ch);
                    if (!NextFrame(b)) Array.Clear(b, 0, ch); // source dry: fade to silence
                }
            }

            return frames * ch;
        }
    }

    /// <summary>Adapts channel count (stereo -> mono / stereo -> N).</summary>
    public sealed class ChannelMap : ISampleProvider
    {
        private readonly ISampleProvider src;
        private readonly int inCh, outCh;
        private float[] tmp = Array.Empty<float>();

        public WaveFormat WaveFormat { get; }

        public ChannelMap(ISampleProvider src, int outputChannels)
        {
            this.src = src;
            inCh = src.WaveFormat.Channels;
            outCh = outputChannels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, outCh);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (inCh == outCh) return src.Read(buffer, offset, count);

            int frames = count / outCh;
            int need = frames * inCh;
            if (tmp.Length < need) tmp = new float[need];

            int got = src.Read(tmp, 0, need);
            int gotFrames = got / inCh;

            for (int f = 0; f < gotFrames; f++)
            {
                int si = f * inCh;
                int di = offset + f * outCh;

                if (outCh == 1)
                {
                    float sum = 0f;
                    for (int c = 0; c < inCh; c++) sum += tmp[si + c];
                    buffer[di] = sum / inCh;
                }
                else
                {
                    for (int c = 0; c < outCh; c++)
                        buffer[di + c] = c < inCh ? tmp[si + c] : (inCh == 1 ? tmp[si] : 0f);
                }
            }

            return gotFrames * outCh;
        }
    }

    /// <summary>One mirrored output device with its own delay, volume and drift loop.</summary>
    public sealed class MirrorTarget : IDisposable
    {
        private const int CushionMs = 90; // jitter headroom before user delay is added

        public MMDevice Device { get; }
        public string Name { get; }

        private readonly BufferedWaveProvider inputBuffer;
        private readonly VariSpeed vari;
        private readonly VolumeSampleProvider vol;
        private readonly WasapiOut output;
        private readonly WaveFormat sourceFormat;
        private readonly double baseSpeed;
        private readonly object gate = new object();

        private int userDelayMs;

        public MirrorTarget(MMDevice device, WaveFormat sourceFormat, int initialDelayMs, float initialVolume)
        {
            Device = device;
            Name = device.FriendlyName;
            this.sourceFormat = sourceFormat;
            userDelayMs = initialDelayMs;

            var mix = device.AudioClient.MixFormat;

            inputBuffer = new BufferedWaveProvider(sourceFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(6),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };

            vari = new VariSpeed(inputBuffer.ToSampleProvider(), mix.SampleRate);
            baseSpeed = (double)sourceFormat.SampleRate / mix.SampleRate;
            vari.Speed = baseSpeed;

            ISampleProvider chain = new ChannelMap(vari, mix.Channels);
            vol = new VolumeSampleProvider(chain) { Volume = initialVolume };

            output = new WasapiOut(device, AudioClientShareMode.Shared, true, 40);
            output.Init(vol);

            PrimeSilence(TargetFillMs);
            output.Play();
        }

        private int TargetFillMs => CushionMs + Volatile.Read(ref userDelayMs);

        public double CurrentFillMs => inputBuffer.BufferedDuration.TotalMilliseconds;

        public float Volume
        {
            get { return vol.Volume; }
            set { vol.Volume = value; }
        }

        public void AddSamples(byte[] data, int count)
        {
            inputBuffer.AddSamples(data, 0, count);
        }

        private void PrimeSilence(int ms)
        {
            int bytes = Align(sourceFormat.AverageBytesPerSecond * ms / 1000);
            if (bytes > 0) inputBuffer.AddSamples(new byte[bytes], 0, bytes);
        }

        private int Align(int bytes)
        {
            int ba = sourceFormat.BlockAlign;
            return bytes - (bytes % ba);
        }

        /// <summary>Jump the delay immediately rather than waiting for the drift loop.</summary>
        public void SetDelay(int ms)
        {
            lock (gate)
            {
                int oldTarget = TargetFillMs;
                Volatile.Write(ref userDelayMs, ms);
                int delta = TargetFillMs - oldTarget;

                if (delta > 0)
                {
                    PrimeSilence(delta);
                }
                else if (delta < 0)
                {
                    int bytes = Align(sourceFormat.AverageBytesPerSecond * (-delta) / 1000);
                    if (bytes > 0)
                    {
                        var junk = new byte[bytes];
                        inputBuffer.Read(junk, 0, bytes);
                    }
                }
            }
        }

        /// <summary>
        /// Drift correction. Two devices never share a clock, so the buffer fill
        /// wanders. Nudge playback speed by at most +/-0.3%, which is inaudible
        /// as pitch but absorbs the drift without dropping or duplicating blocks.
        /// </summary>
        public void ServiceDrift()
        {
            double errMs = CurrentFillMs - TargetFillMs;
            double correction = 1.0 + 0.0001 * errMs;
            if (correction > 1.003) correction = 1.003;
            if (correction < 0.997) correction = 0.997;
            vari.Speed = baseSpeed * correction;
        }

        public void Dispose()
        {
            try { output.Stop(); } catch { }
            try { output.Dispose(); } catch { }
        }
    }

    /// <summary>Taps one render endpoint and mirrors it to any number of others.</summary>
    public sealed class AudioMirror : IDisposable
    {
        private WasapiLoopbackCapture capture;
        private WasapiOut keepAlive;
        private readonly List<MirrorTarget> targets = new List<MirrorTarget>();
        private System.Threading.Timer driftTimer;
        private bool running;

        public IReadOnlyList<MirrorTarget> Targets => targets;
        public bool Running => running;
        public event Action<Exception> Failed;

        public static List<MMDevice> RenderDevices()
        {
            using var en = new MMDeviceEnumerator();
            return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        }

        public static MMDevice DefaultRenderDevice()
        {
            using var en = new MMDeviceEnumerator();
            return en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        public void Start(MMDevice source, IEnumerable<(MMDevice device, int delayMs, float volume)> outputs)
        {
            if (running) Stop();

            capture = new WasapiLoopbackCapture(source);
            var fmt = capture.WaveFormat;

            foreach (var o in outputs)
            {
                if (string.Equals(o.device.ID, source.ID, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "An output device cannot be the same as the tap device — that would feed back on itself.");
                targets.Add(new MirrorTarget(o.device, fmt, o.delayMs, o.volume));
            }

            capture.DataAvailable += OnData;
            capture.RecordingStopped += (s, e) =>
            {
                if (e.Exception != null) Failed?.Invoke(e.Exception);
            };

            // WASAPI only pushes data to a render endpoint while something is
            // playing on it. Hold an infinite silent stream open on the tap so
            // the loopback never goes dead between videos.
            keepAlive = new WasapiOut(source, AudioClientShareMode.Shared, true, 100);
            keepAlive.Init(new SilenceProvider(fmt));
            keepAlive.Play();

            capture.StartRecording();
            driftTimer = new System.Threading.Timer(_ =>
            {
                foreach (var t in targets) t.ServiceDrift();
            }, null, 500, 250);

            running = true;
        }

        private void OnData(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;
            for (int i = 0; i < targets.Count; i++)
                targets[i].AddSamples(e.Buffer, e.BytesRecorded);
        }

        public void Stop()
        {
            running = false;

            driftTimer?.Dispose();
            driftTimer = null;

            if (capture != null)
            {
                try { capture.StopRecording(); } catch { }
                try { capture.Dispose(); } catch { }
                capture = null;
            }

            if (keepAlive != null)
            {
                try { keepAlive.Stop(); } catch { }
                try { keepAlive.Dispose(); } catch { }
                keepAlive = null;
            }

            foreach (var t in targets) t.Dispose();
            targets.Clear();
        }

        public void Dispose() => Stop();
    }
}
