using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSync
{
    /// <summary>Shared palette + helpers for the macOS-inspired light theme.</summary>
    public static class Theme
    {
        public static readonly Color WindowBg = Color.FromArgb(246, 246, 248);
        public static readonly Color CardBg = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(226, 226, 230);
        public static readonly Color Accent = Color.FromArgb(0, 122, 255);
        public static readonly Color AccentPressed = Color.FromArgb(0, 99, 214);
        public static readonly Color TextPrimary = Color.FromArgb(29, 29, 31);
        public static readonly Color TextSecondary = Color.FromArgb(134, 134, 139);
        public static readonly Color TrackOff = Color.FromArgb(40, 120, 120, 128);
        public static readonly Color Danger = Color.FromArgb(255, 59, 48);

        private static Font baseFont;
        public static Font Sans(float size, FontStyle style = FontStyle.Regular)
        {
            if (baseFont == null)
            {
                try { baseFont = new Font("Segoe UI Variable Text", 9f); }
                catch { baseFont = new Font("Segoe UI", 9f); }
            }
            return new Font(baseFont.FontFamily, size, style);
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();

            // Guard against degenerate rects (0/negative size), which can happen
            // transiently during layout, docking or DPI rescale — AddArc throws
            // ArgumentException on a zero-or-negative-size bounding box.
            if (r.Width <= 0f || r.Height <= 0f) return path;

            float d = Math.Max(0f, radius * 2);
            d = Math.Min(d, Math.Min(r.Width, r.Height));

            if (d <= 0f)
            {
                path.AddRectangle(r);
                return path;
            }

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>A white rounded card with a hairline border, macOS "panel" style.</summary>
    public class Card : Panel
    {
        public float Radius = 14f;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.WindowBg;
            Padding = new Padding(18);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundedRect(rect, Radius))
            using (var fill = new SolidBrush(Theme.CardBg))
            using (var pen = new Pen(Theme.CardBorder, 1f))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    /// <summary>Pill-shaped button, filled (primary) or ghost (secondary), macOS style.</summary>
    public class MacButton : Control
    {
        public bool Primary = true;
        private bool hover, pressed;

        public MacButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                      ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.Sans(9.5f, FontStyle.Bold);
            Height = 36;
            Width = 120;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0, 0, Width - 1, Height - 1);

            Color fill, text;
            if (Primary)
            {
                fill = pressed ? Theme.AccentPressed : Theme.Accent;
                if (hover && !pressed) fill = Color.FromArgb(20, Color.White.R, fill.G, fill.B); // unused fallback
                fill = pressed ? Theme.AccentPressed : (hover ? ControlPaint.Light(Theme.Accent, 0.06f) : Theme.Accent);
                text = Color.White;
            }
            else
            {
                fill = pressed ? Color.FromArgb(230, 230, 235) : (hover ? Color.FromArgb(240, 240, 244) : Theme.CardBg);
                text = Theme.TextPrimary;
            }

            using (var path = Theme.RoundedRect(rect, Height / 2f))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
                if (!Primary)
                {
                    using (var pen = new Pen(Theme.CardBorder, 1f)) g.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(rect), text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>macOS-style toggle switch.</summary>
    public class MacToggle : Control
    {
        private bool isChecked;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => isChecked;
            set { if (isChecked == value) return; isChecked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        public MacToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                      ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(40, 22);
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0, 0, Width - 1, Height - 1);

            Color track = isChecked ? Theme.Accent : Color.FromArgb(120, 120, 128);
            if (!isChecked) track = Color.FromArgb(60, track.R, track.G, track.B);

            using (var path = Theme.RoundedRect(rect, Height / 2f))
            using (var b = new SolidBrush(isChecked ? Theme.Accent : Color.FromArgb(233, 233, 235)))
            {
                g.FillPath(b, path);
            }

            float d = Height - 4;
            float x = isChecked ? Width - d - 2 : 2;
            using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                g.FillEllipse(shadow, x, 3, d, d);
            using (var thumb = new SolidBrush(Color.White))
                g.FillEllipse(thumb, x, 2, d, d);
        }
    }

    /// <summary>macOS-style horizontal slider: thin filled track + round thumb.</summary>
    public class MacSlider : Control
    {
        public int Minimum = 0;
        public int Maximum = 100;
        private int value_;
        public event EventHandler ValueChanged;

        public int Value
        {
            get => value_;
            set
            {
                int v = Math.Max(Minimum, Math.Min(Maximum, value));
                if (v == value_) return;
                value_ = v;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public MacSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                      ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Height = 22;
        }

        private const int Thumb = 16;

        private float TrackX0 => Thumb / 2f;
        // Never let the track go inverted (TrackX1 < TrackX0) if the control is
        // narrower than the thumb during transient layout/DPI passes.
        private float TrackX1 => Math.Max(TrackX0, Width - Thumb / 2f);

        private float ValueToX(int v)
        {
            if (Maximum == Minimum) return TrackX0;
            float t = (v - Minimum) / (float)(Maximum - Minimum);
            return TrackX0 + t * (TrackX1 - TrackX0);
        }

        private int XToValue(float x)
        {
            float t = (x - TrackX0) / Math.Max(1f, TrackX1 - TrackX0);
            t = Math.Max(0f, Math.Min(1f, t));
            return (int)Math.Round(Minimum + t * (Maximum - Minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Capture = true;
            Value = XToValue(e.X);
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Value = XToValue(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float midY = Height / 2f;
            float thumbX = ValueToX(value_);

            var full = new RectangleF(TrackX0, midY - 2, TrackX1 - TrackX0, 4);
            using (var path = Theme.RoundedRect(full, 2))
            using (var b = new SolidBrush(Color.FromArgb(45, 120, 120, 128)))
                g.FillPath(b, path);

            var filled = new RectangleF(TrackX0, midY - 2, Math.Max(0, thumbX - TrackX0), 4);
            using (var path = Theme.RoundedRect(filled, 2))
            using (var b = new SolidBrush(Theme.Accent))
                g.FillPath(b, path);

            using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillEllipse(shadow, thumbX - Thumb / 2f, midY - Thumb / 2f + 1, Thumb, Thumb);
            using (var thumb = new SolidBrush(Color.White))
                g.FillEllipse(thumb, thumbX - Thumb / 2f, midY - Thumb / 2f, Thumb, Thumb);
            using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
                g.DrawEllipse(pen, thumbX - Thumb / 2f, midY - Thumb / 2f, Thumb, Thumb);
        }
    }

    /// <summary>Flat-styled combo box matching the light card theme.</summary>
    public class MacComboBox : ComboBox
    {
        public MacComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Color.FromArgb(242, 242, 245);
            ForeColor = Theme.TextPrimary;
            Font = Theme.Sans(9.5f);
        }
    }
}
