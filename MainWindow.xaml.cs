using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using WinFormsIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;

namespace WinSync
{
    /// <summary>
    /// WPF port of the old WinForms MainForm. Layout is a 1:1 coordinate port (see
    /// MainWindow.xaml's header comment for why); the logic below is likewise a
    /// deliberately close port of the WinForms version's Start/Stop/LoadDevices/
    /// UpdateStatus flow, not a rewrite — the goal of this migration was strictly
    /// "same behavior, sharper rendering," and re-deriving the control flow from
    /// scratch would have been a good way to introduce a regression I can't
    /// currently catch visually (see CheckForUpdatesAsync's doc comment on the
    /// broader verification situation this session).
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly Forms.NotifyIcon trayIcon = new Forms.NotifyIcon();
        private readonly Forms.ContextMenuStrip trayMenu = new Forms.ContextMenuStrip();
        private bool trayHintShown;
        private bool isClosing;

        private List<MMDevice> devices = new List<MMDevice>();
        private readonly AudioMirror mirror = new AudioMirror();

        public MainWindow()
        {
            InitializeComponent();

            LoadAppIcon();

            trayMenu.Items.Add("Open WinSync", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, (s, e) => ExitFromTray());
            trayIcon.Text = "WinSync";
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            BtnHelp.Click += (s, e) => new HelpDialog().ShowNear(this);
            BtnCheckUpdate.Click += (s, e) => _ = CheckForUpdatesAsync(manual: true);
            BtnRefresh.Click += (s, e) => LoadDevices();
            BtnStart.Click += (s, e) => Toggle();

            Row1.DelaySlider.ValueChanged += (s, e) => PushDelay(0, (int)Row1.DelaySlider.Value);
            Row2.DelaySlider.ValueChanged += (s, e) => PushDelay(1, (int)Row2.DelaySlider.Value);
            Row1.VolumeSlider.ValueChanged += (s, e) => PushVolume(0, (int)Row1.VolumeSlider.Value);
            Row2.VolumeSlider.ValueChanged += (s, e) => PushVolume(1, (int)Row2.VolumeSlider.Value);

            // AudioMirror raises this from a background thread (the drift timer /
            // WASAPI callback), not the UI thread, so it needs an explicit marshal
            // back — unlike the awaited calls elsewhere in this file, where WPF's
            // Dispatcher-based SynchronizationContext already resumes on the UI
            // thread automatically after an `await`.
            mirror.Failed += ex => Dispatcher.BeginInvoke(new Action(() =>
            {
                Stop();
                MessageBox.Show(this, ex.Message, "Audio error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }));

            uiTimer.Tick += (s, e) => UpdateStatus();
            uiTimer.Start();

            LoadDevices();
            _ = CheckForUpdatesAsync();

            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized) MinimizeToTray();
            };

            Closed += (s, e) =>
            {
                isClosing = true;
                trayIcon.Visible = false;
                trayIcon.Dispose();
                mirror.Dispose();
            };
        }

        private void LoadAppIcon()
        {
            try
            {
                string exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
                using var icon = WinFormsIcon.ExtractAssociatedIcon(exePath);
                if (icon == null) return;

                trayIcon.Icon = icon;

                using var bmp = icon.ToBitmap();
                using var stream = new System.IO.MemoryStream();
                bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = stream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                Icon = bitmapImage;
                AppIconImage.Source = bitmapImage;
            }
            catch { /* dev run without an exe icon, or icon extraction failed — non-fatal */ }
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
                    "Still running — mirroring continues in the background.", Forms.ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            trayIcon.Visible = false;
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
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
        private async System.Threading.Tasks.Task CheckForUpdatesAsync(bool manual = false)
        {
            if (manual)
            {
                BtnCheckUpdate.IsEnabled = false;
                BtnCheckUpdate.Content = "Checking…";
            }

            var result = await UpdateChecker.CheckForUpdateAsync();
            if (isClosing) return;

            if (manual)
            {
                BtnCheckUpdate.IsEnabled = true;
                BtnCheckUpdate.Content = "Check for Updates";
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
                MessageBox.Show(this, msg, "WinSync", MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }

        private void LoadDevices()
        {
            devices = AudioMirror.RenderDevices();
            var names = devices.Select(d => d.FriendlyName).ToArray();

            foreach (var cb in new[] { CbSource, Row1.DeviceCombo, Row2.DeviceCombo })
            {
                object prev = cb.SelectedItem;
                cb.Items.Clear();
                foreach (var n in names) cb.Items.Add(n);
                if (prev != null && cb.Items.Contains(prev)) cb.SelectedItem = prev;
            }

            if (CbSource.SelectedIndex < 0 && devices.Count > 0)
            {
                try
                {
                    var def = AudioMirror.DefaultRenderDevice();
                    int i = devices.FindIndex(d => d.ID == def.ID);
                    CbSource.SelectedIndex = i >= 0 ? i : 0;
                }
                catch { CbSource.SelectedIndex = 0; }
            }

            if (Row1.DeviceCombo.SelectedIndex < 0 && devices.Count > 1)
                Row1.DeviceCombo.SelectedIndex = CbSource.SelectedIndex == 0 ? 1 : 0;

            Row1.EnableToggle.IsChecked = true;
            Row2.EnableToggle.IsChecked = false;
        }

        private IEnumerable<(OutputRowControl row, MMDevice dev, int delay, float vol)> ActiveRows()
        {
            foreach (var r in new[] { Row1, Row2 })
            {
                if (r.EnableToggle.IsChecked != true) continue;
                if (r.DeviceCombo.SelectedIndex < 0) continue;
                yield return (r, devices[r.DeviceCombo.SelectedIndex], (int)r.DelaySlider.Value, (float)(r.VolumeSlider.Value / 100.0));
            }
        }

        private void Toggle()
        {
            if (mirror.Running) Stop();
            else Start();
        }

        private void Start()
        {
            if (CbSource.SelectedIndex < 0) return;
            var source = devices[CbSource.SelectedIndex];
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
                MessageBox.Show(this, ex.Message, "Could not start", MessageBoxButton.OK, MessageBoxImage.Warning);
                mirror.Stop();
                return;
            }

            BtnStart.Content = "Stop";
            CbSource.IsEnabled = false;
            BtnRefresh.IsEnabled = false;
            Row1.DeviceCombo.IsEnabled = Row2.DeviceCombo.IsEnabled = false;
            Row1.EnableToggle.IsEnabled = Row2.EnableToggle.IsEnabled = false;
            Row1.StatusText.Visibility = Row2.StatusText.Visibility = Visibility.Visible;
        }

        private void Stop()
        {
            mirror.Stop();
            BtnStart.Content = "Start";
            CbSource.IsEnabled = true;
            BtnRefresh.IsEnabled = true;
            Row1.DeviceCombo.IsEnabled = Row2.DeviceCombo.IsEnabled = true;
            Row1.EnableToggle.IsEnabled = Row2.EnableToggle.IsEnabled = true;
            Row1.StatusText.Text = Row2.StatusText.Text = "";
            Row1.StatusText.Visibility = Row2.StatusText.Visibility = Visibility.Collapsed;
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
                active[i].StatusText.Text = $"buffer {mirror.Targets[i].CurrentFillMs:0} ms";
        }
    }
}
