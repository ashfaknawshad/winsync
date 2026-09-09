using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSync
{
    /// <summary>A small borderless "update available" toast, shown near the main window
    /// on every launch while a newer release exists. Doesn't block or resize MainForm.</summary>
    public sealed class UpdateToast : Form
    {
        public UpdateToast(UpdateInfo info)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.WindowBg;
            Size = new Size(320, 156);

            var card = new Card { Location = Point.Empty, Size = Size, Radius = 14f };
            Controls.Add(card);

            var title = new Label
            {
                Text = "Update available",
                Font = Theme.Sans(12f, FontStyle.Bold),
                ForeColor = Theme.TextPrimary,
                Location = new Point(18, 16),
                AutoSize = true
            };
            var body = new Label
            {
                Text = $"WinSync {info.TagName} is out — you're on an older version.",
                Font = Theme.Sans(9f),
                ForeColor = Theme.TextSecondary,
                Location = new Point(18, 42),
                Size = new Size(284, 40)
            };

            var btnUpdate = new MacButton { Text = "Update Now", Primary = true, Location = new Point(18, 104), Width = 130, Height = 32 };
            var btnLater = new MacButton { Text = "Later", Primary = false, Location = new Point(156, 104), Width = 90, Height = 32 };

            btnUpdate.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(info.DownloadUrl ?? info.HtmlUrl) { UseShellExecute = true }); }
                catch { /* nothing sensible to do if the browser won't launch */ }
                Close();
            };
            btnLater.Click += (s, e) => Close();

            card.Controls.AddRange(new Control[] { title, body, btnUpdate, btnLater });
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using var path = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), 14f);
            Region = new Region(path);
        }

        /// <summary>Positions the toast at the bottom-right of the screen the owner is on.</summary>
        public void ShowNear(Form owner)
        {
            var area = Screen.FromControl(owner).WorkingArea;
            Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
            Show();
        }
    }
}
