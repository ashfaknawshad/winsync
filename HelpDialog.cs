using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace WinSync
{
    /// <summary>The "?" button's in-app how-to-use guide. Mirrors the README's Usage
    /// section and the website's guide, using the same Device 1/2/3 language as the
    /// main window — Device 1 is whatever Windows is already playing to (no delay
    /// control needed, it's the reference everything else lines up against);
    /// Device 2/3 are the mirrored copies with their own delay and volume.</summary>
    public sealed class HelpDialog : Form
    {
        public HelpDialog()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.WindowBg;
            Size = new Size(420, 600);

            var card = new Card { Location = Point.Empty, Size = Size, Radius = 14f };
            Controls.Add(card);

            var title = new Label
            {
                Text = "How to use WinSync",
                Font = Theme.Sans(13f, FontStyle.Bold),
                ForeColor = Theme.TextPrimary,
                Location = new Point(20, 18),
                AutoSize = true
            };

            var btnClose = new MacButton { Text = "×", Primary = false, Width = 30, Height = 28, Location = new Point(Size.Width - 48, 16) };
            btnClose.Click += (s, e) => Close();

            var scroll = new Panel
            {
                Location = new Point(0, 54),
                Size = new Size(Size.Width, Size.Height - 54 - 44),
                AutoScroll = true,
                BackColor = Color.Transparent
            };

            int y = 4;

            void AddText(string text, float size, bool bold, Color color)
            {
                var font = Theme.Sans(size, bold ? FontStyle.Bold : FontStyle.Regular);
                int width = 360;
                float measuredHeight;
                using (var g = scroll.CreateGraphics())
                    measuredHeight = g.MeasureString(text, font, width).Height;

                var label = new Label
                {
                    Text = text,
                    Font = font,
                    ForeColor = color,
                    Location = new Point(20, y),
                    AutoSize = false,
                    Width = width,
                    Height = (int)Math.Ceiling(measuredHeight) + 4
                };
                scroll.Controls.Add(label);
                y += label.Height + 12;
            }

            AddText("1. Connect both headphones or speakers to your PC.", 9.5f, false, Theme.TextPrimary);
            AddText("2. In Windows' Sound settings, set your slowest device (usually Bluetooth) as the default output.", 9.5f, false, Theme.TextPrimary);

            var img = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(20, y),
                Size = new Size(260, 292),
                BackColor = Theme.ControlBg
            };
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinSync.SoundOutputPanel.png");
                if (stream != null) img.Image = Image.FromStream(stream);
            }
            catch { /* dev run before the resource exists, or a corrupt stream — skip the image */ }
            scroll.Controls.Add(img);
            y += img.Height + 16;

            AddText("3. In WinSync, set Device 1 to that same device. It's already playing — no delay slider needed for it.", 9.5f, false, Theme.TextPrimary);
            AddText("4. Turn on Device 2, set it to your other headphone or speaker, and press Start.", 9.5f, false, Theme.TextPrimary);
            AddText("5. Play a video, then drag Device 2's Delay slider until both headphones line up — usually 120–250ms for Bluetooth.", 9.5f, false, Theme.TextPrimary);
            AddText("Got a third device? Device 3 works exactly the same way as Device 2.", 9f, false, Theme.TextSecondary);

            var link = new Label
            {
                Text = "Full guide with more screenshots →",
                Font = Theme.Sans(9f, FontStyle.Bold),
                ForeColor = Theme.Accent,
                Location = new Point(20, y),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            link.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://winsync.ashfaknawshad.dev") { UseShellExecute = true }); }
                catch { /* nothing sensible to do if the browser won't launch */ }
            };
            scroll.Controls.Add(link);
            y += 30;

            card.Controls.AddRange(new Control[] { title, btnClose, scroll });
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using var path = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), 14f);
            Region = new Region(path);
        }

        /// <summary>Centers the dialog over the owner window.</summary>
        public void ShowNear(Form owner)
        {
            Location = new Point(
                owner.Left + (owner.Width - Width) / 2,
                owner.Top + (owner.Height - Height) / 2);
            Show();
        }
    }
}
