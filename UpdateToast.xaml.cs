using System;
using System.Diagnostics;
using System.Windows;

namespace WinSync
{
    /// <summary>A small borderless "update available" toast, shown near the main window
    /// on every launch while a newer release exists. Doesn't block or resize
    /// MainWindow. "Update Now" downloads the installer and launches it — no separate
    /// browser tab, no hunting through Downloads for the file — but does show the
    /// installer's own few-click UI rather than updating invisibly in the background.
    /// (An earlier, fully-silent version of this used a hidden script to reinstall —
    /// see UpdateChecker.InstallUpdate for why that turned out to be a real problem
    /// on an unsigned app.) Falls back to opening the release page if the release has
    /// no WinSync-Setup.exe asset for some reason, or if the download/launch fails.</summary>
    public partial class UpdateToast : Window
    {
        private readonly UpdateInfo info;
        private readonly Action beforeExit;

        public UpdateToast(UpdateInfo info, Action beforeExit = null)
        {
            this.info = info;
            this.beforeExit = beforeExit;

            InitializeComponent();

            BodyText.Text = $"WinSync {info.TagName} is out — you're on an older version.";
            BtnUpdate.Click += BtnUpdate_Click;
            BtnLater.Click += (s, e) => Close();
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (!info.CanSelfUpdate)
            {
                // No installer asset on this release for some reason — fall back to
                // the old behavior rather than get stuck with no path forward.
                try { Process.Start(new ProcessStartInfo(info.DownloadUrl ?? info.HtmlUrl) { UseShellExecute = true }); }
                catch { /* nothing sensible to do if the browser won't launch */ }
                Close();
                return;
            }

            BtnUpdate.IsEnabled = false;
            BtnLater.IsEnabled = false;

            try
            {
                BtnUpdate.Content = "Downloading…";
                string installerPath = await UpdateChecker.DownloadUpdateAsync(info);

                BtnUpdate.Content = "Opening installer…";
                BodyText.Text = "Click through the installer that just opened — WinSync will close now to let it update.";

                beforeExit?.Invoke();
                UpdateChecker.InstallUpdate(installerPath); // exits the process
            }
            catch
            {
                // Download or launch failed (offline mid-download, disk full, etc.) —
                // fall back to a manual download rather than leave the user stuck.
                try { Process.Start(new ProcessStartInfo(info.DownloadUrl ?? info.HtmlUrl) { UseShellExecute = true }); }
                catch { /* nothing sensible to do if the browser won't launch either */ }
                Close();
            }
        }

        /// <summary>Positions the toast at the bottom-right of the screen the owner is on.</summary>
        public void ShowNear(Window owner)
        {
            var area = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(owner).Handle).WorkingArea;
            Left = area.Right - Width - 20;
            Top = area.Bottom - Height - 20;
            Show();
        }
    }
}
