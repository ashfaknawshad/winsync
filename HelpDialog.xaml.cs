using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace WinSync
{
    /// <summary>The "?" button's in-app how-to-use guide. Mirrors the README's Usage
    /// section and the website's guide, using the same Device 1/2/3 language as the
    /// main window — Device 1 is whatever Windows is already playing to (no delay
    /// control needed, it's the reference everything else lines up against);
    /// Device 2/3 are the mirrored copies with their own delay and volume.</summary>
    public partial class HelpDialog : Window
    {
        public HelpDialog()
        {
            InitializeComponent();

            BtnClose.Click += (s, e) => Close();
            LinkText.MouseLeftButtonUp += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://winsync.ashfaknawshad.dev") { UseShellExecute = true }); }
                catch { /* nothing sensible to do if the browser won't launch */ }
            };

            try { SoundOutputImage.Source = new BitmapImage(new System.Uri("pack://application:,,,/Assets/sound-output-panel.png")); }
            catch { /* image missing from this build — dialog still works without it */ }
        }

        /// <summary>Centers the dialog over the owner window.</summary>
        public void ShowNear(Window owner)
        {
            Left = owner.Left + (owner.Width - Width) / 2;
            Top = owner.Top + (owner.Height - Height) / 2;
            Show();
        }
    }
}
