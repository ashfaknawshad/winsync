using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace WinSync
{
    /// <summary>One mirrored-output card: enable toggle, device picker, delay + volume sliders, live status.</summary>
    public sealed class OutputRow : Card
    {
        public MacToggle EnableToggle;
        public MacComboBox Device;
        public MacSlider Delay;
        public Label DelayLabel;
        public MacSlider Volume;
        public Label VolumeLabel;
        public Label Status;

        public OutputRow(string title)
        {
            Size = new Size(584, 168);

            var titleLabel = new Label
            {
                Text = title,
                Font = Theme.Sans(12f, FontStyle.Bold),
                ForeColor = Theme.TextPrimary,
                Location = new Point(18, 16),
                AutoSize = true
            };

            EnableToggle = new MacToggle { Location = new Point(534, 16) };

            Device = new MacComboBox { Location = new Point(18, 46), Width = 548 };

            var dl = new Label { Text = "Delay", Font = Theme.Sans(9f), ForeColor = Theme.TextSecondary, Location = new Point(18, 88), AutoSize = true };
            Delay = new MacSlider { Location = new Point(18, 104), Width = 460, Minimum = 0, Maximum = 1000, Value = 0 };
            DelayLabel = new Label { Text = "0 ms", Font = Theme.Sans(9f, FontStyle.Bold), ForeColor = Theme.TextPrimary, Location = new Point(490, 100), AutoSize = true, TextAlign = ContentAlignment.MiddleRight };

            var vl = new Label { Text = "Volume", Font = Theme.Sans(9f), ForeColor = Theme.TextSecondary, Location = new Point(18, 132), AutoSize = true };
            Volume = new MacSlider { Location = new Point(18, 148), Width = 460, Minimum = 0, Maximum = 100, Value = 100 };
            VolumeLabel = new Label { Text = "100%", Font = Theme.Sans(9f, FontStyle.Bold), ForeColor = Theme.TextPrimary, Location = new Point(490, 144), AutoSize = true };

            Status = new Label { Location = new Point(18, 132), AutoSize = true, ForeColor = Theme.TextSecondary, Font = Theme.Sans(9f), Text = "", Visible = false };

            Controls.AddRange(new Control[] { titleLabel, EnableToggle, Device, dl, Delay, DelayLabel, vl, Volume, VolumeLabel, Status });

            Delay.ValueChanged += (s, e) => DelayLabel.Text = Delay.Value + " ms";
            Volume.ValueChanged += (s, e) => VolumeLabel.Text = Volume.Value + "%";

            Height = 168;
        }
    }

    public sealed class MainForm : Form
    {
        private readonly PictureBox appIcon = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(40, 40) };
        private readonly Label lblTitle = new Label { Text = "WinSync", Font = Theme.Sans(18f, FontStyle.Bold), ForeColor = Theme.TextPrimary, AutoSize = true };
        private readonly Label lblSubtitle = new Label { Text = "Mirror your system audio to two headphones, perfectly in sync.", Font = Theme.Sans(9f), ForeColor = Theme.TextSecondary, AutoSize = true };

        private readonly MacButton btnCheckUpdate = new MacButton { Text = "Check for Updates", Primary = false, Width = 150, Height = 26 };
        private readonly MacButton btnHelp = new MacButton { Text = "?", Primary = false, Width = 26, Height = 26 };

        private readonly Card sourceCard = new Card { Size = new Size(584, 128) };
        private readonly Label lblSourceCaption = new Label { Text = "DEVICE 1 · ALREADY PLAYING", Font = Theme.Sans(9f, FontStyle.Bold), ForeColor = Theme.TextSecondary, AutoSize = true };
        private readonly Label lblSourceHint = new Label { Text = "This counts as one of your headphones — no delay slider needed here. Pick your slowest device (usually Bluetooth) and set it as the Windows default output.", Font = Theme.Sans(9f), ForeColor = Theme.TextSecondary, AutoSize = false, Width = 500, Height = 48 };
        private readonly MacComboBox cbSource = new MacComboBox { Width = 400 };
        private readonly MacButton btnRefresh = new MacButton { Text = "Refresh", Primary = false, Width = 90, Height = 30 };

        private readonly MacButton btnStart = new MacButton { Text = "Start", Primary = true, Width = 140, Height = 40 };
        private readonly Label lblHint = new Label { AutoSize = true, ForeColor = Theme.TextSecondary, Font = Theme.Sans(9f) };

        private readonly OutputRow row1 = new OutputRow("Device 2");
        private readonly OutputRow row2 = new OutputRow("Device 3 (optional)");
        private readonly Timer uiTimer = new Timer { Interval = 400 };

        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        private bool trayHintShown;

        private List<MMDevice> devices = new List<MMDevice>();
        private readonly AudioMirror mirror = new AudioMirror();

        public MainForm()
        {
            // All layout below is hand-placed in pixels; disable WinForms' automatic
            // font/DPI autoscaling or it inflates every control's size and location
            // once a non-default Font is assigned.
            AutoScaleMode = AutoScaleMode.None;

            Text = "WinSync";
            ClientSize = new Size(620, 660);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Theme.WindowBg;
            Font = Theme.Sans(9f);
            Padding = new Padding(18);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* dev run without exe icon */ }
            try { appIcon.Image = Icon?.ToBitmap(); } catch { }

            trayMenu.Items.Add("Open WinSync", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, (s, e) => ExitFromTray());
            trayIcon.Text = "WinSync";
            trayIcon.Icon = Icon;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            appIcon.Location = new Point(18, 18);
            lblTitle.Location = new Point(68, 18);
            lblSubtitle.Location = new Point(68, 50);
            btnHelp.Location = new Point(418, 26);
            btnCheckUpdate.Location = new Point(452, 26);

            sourceCard.Location = new Point(18, 78);
            lblSourceCaption.Location = new Point(18, 16);
            cbSource.Location = new Point(18, 36);
            btnRefresh.Location = new Point(428, 34);
            lblSourceHint.Location = new Point(18, 72);
            sourceCard.Controls.AddRange(new Control[] { lblSourceCaption, cbSource, btnRefresh, lblSourceHint });

            row1.Location = new Point(18, 216);
            row2.Location = new Point(18, 392);

            btnStart.Location = new Point(18, 580);
            lblHint.Location = new Point(174, 592);
            lblHint.Text = "Play a video, then drag Delay until the two headphones line up.";

            Controls.AddRange(new Control[] { appIcon, lblTitle, lblSubtitle, btnHelp, btnCheckUpdate, sourceCard, row1, row2, btnStart, lblHint });

            btnHelp.Click += (s, e) => new HelpDialog().ShowNear(this);
            btnCheckUpdate.Click += (s, e) => _ = CheckForUpdatesAsync(manual: true);
            btnRefresh.Click += (s, e) => LoadDevices();
            btnStart.Click += (s, e) => Toggle();
            row1.Delay.ValueChanged += (s, e) => PushDelay(0, row1.Delay.Value);
            row2.Delay.ValueChanged += (s, e) => PushDelay(1, row2.Delay.Value);
            row1.Volume.ValueChanged += (s, e) => PushVolume(0, row1.Volume.Value);
            row2.Volume.ValueChanged += (s, e) => PushVolume(1, row2.Volume.Value);

            mirror.Failed += ex => BeginInvoke(new Action(() =>
            {
                Stop();
                MessageBox.Show(this, ex.Message, "Audio error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }));

            uiTimer.Tick += (s, e) => UpdateStatus();
            uiTimer.Start();

            LoadDevices();
            _ = CheckForUpdatesAsync();

            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized) MinimizeToTray();
            };

            FormClosing += (s, e) =>
            {
                trayIcon.Visible = false;
                mirror.Dispose();
            };
        }

        private void MinimizeToTray()
        {
            Hide();
            ShowInTaskbar = false;
            trayIcon.Visible = true;

            if (!trayHintShown)
            {
                trayHintShown = true;
                trayIcon.ShowBalloonTip(2000, "WinSync",
                    "Still running — mirroring continues in the background.", ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            trayIcon.Visible = false;
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ExitFromTray()
        {
            trayIcon.Visible = false;
            Close();
        }

        /// <param name="manual">True when triggered by the "Check for Updates" button —
        /// shows feedback either way (up to date / check failed), unlike the silent
        /// startup check, which only ever speaks up when there's actually an update.</param>
        private async Task CheckForUpdatesAsync(bool manual = false)
        {
            if (manual)
            {
                btnCheckUpdate.Enabled = false;
                btnCheckUpdate.Text = "Checking…";
            }

            var result = await UpdateChecker.CheckForUpdateAsync();
            if (IsDisposed || !IsHandleCreated) return;

            BeginInvoke(new Action(() =>
            {
                if (manual)
                {
                    btnCheckUpdate.Enabled = true;
                    btnCheckUpdate.Text = "Check for Updates";
                }

                if (result.Update != null)
                {
                    new UpdateToast(result.Update, beforeExit: () => mirror.Stop()).ShowNear(this);
                }
                else if (manual)
                {
                    string msg = result.Success
                        ? "You're on the latest version of WinSync."
                        : "Couldn't check for updates — check your internet connection and try again.";
                    MessageBox.Show(this, msg, "WinSync", MessageBoxButtons.OK,
                        result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
            }));
        }

        private void LoadDevices()
        {
            devices = AudioMirror.RenderDevices();
            var names = devices.Select(d => d.FriendlyName).ToArray();

            foreach (var cb in new[] { cbSource, row1.Device, row2.Device })
            {
                object prev = cb.SelectedItem;
                cb.Items.Clear();
                cb.Items.AddRange(names);
                if (prev != null && cb.Items.Contains(prev)) cb.SelectedItem = prev;
            }

            if (cbSource.SelectedIndex < 0 && devices.Count > 0)
            {
                try
                {
                    var def = AudioMirror.DefaultRenderDevice();
                    int i = devices.FindIndex(d => d.ID == def.ID);
                    cbSource.SelectedIndex = i >= 0 ? i : 0;
                }
                catch { cbSource.SelectedIndex = 0; }
            }

            if (row1.Device.SelectedIndex < 0 && devices.Count > 1)
                row1.Device.SelectedIndex = cbSource.SelectedIndex == 0 ? 1 : 0;

            row1.EnableToggle.Checked = true;
            row2.EnableToggle.Checked = false;
        }

        private IEnumerable<(OutputRow row, MMDevice dev, int delay, float vol)> ActiveRows()
        {
            foreach (var r in new[] { row1, row2 })
            {
                if (!r.EnableToggle.Checked) continue;
                if (r.Device.SelectedIndex < 0) continue;
                yield return (r, devices[r.Device.SelectedIndex], r.Delay.Value, r.Volume.Value / 100f);
            }
        }

        private void Toggle()
        {
            if (mirror.Running) Stop();
            else Start();
        }

        private void Start()
        {
            if (cbSource.SelectedIndex < 0) return;
            var source = devices[cbSource.SelectedIndex];
            var outs = ActiveRows().Select(x => (x.dev, x.delay, x.vol)).ToList();

            if (outs.Count == 0)
            {
                MessageBox.Show(this, "Enable at least one output device.", "Nothing to do");
                return;
            }

            try
            {
                mirror.Start(source, outs);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not start", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                mirror.Stop();
                return;
            }

            btnStart.Text = "Stop";
            cbSource.Enabled = false;
            btnRefresh.Enabled = false;
            row1.Device.Enabled = row2.Device.Enabled = false;
            row1.EnableToggle.Enabled = row2.EnableToggle.Enabled = false;
            row1.Status.Visible = row2.Status.Visible = true;
        }

        private void Stop()
        {
            mirror.Stop();
            btnStart.Text = "Start";
            cbSource.Enabled = true;
            btnRefresh.Enabled = true;
            row1.Device.Enabled = row2.Device.Enabled = true;
            row1.EnableToggle.Enabled = row2.EnableToggle.Enabled = true;
            row1.Status.Text = row2.Status.Text = "";
            row1.Status.Visible = row2.Status.Visible = false;
        }

        private void PushDelay(int index, int ms)
        {
            if (!mirror.Running) return;
            if (index < mirror.Targets.Count) mirror.Targets[index].SetDelay(ms);
        }

        private void PushVolume(int index, int pct)
        {
            if (!mirror.Running) return;
            if (index < mirror.Targets.Count) mirror.Targets[index].Volume = pct / 100f;
        }

        private void UpdateStatus()
        {
            if (!mirror.Running) return;
            var active = ActiveRows().Select(x => x.row).ToList();
            for (int i = 0; i < mirror.Targets.Count && i < active.Count; i++)
                active[i].Status.Text = $"buffer {mirror.Targets[i].CurrentFillMs:0} ms";
        }
    }
}
