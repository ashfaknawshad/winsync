using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSync
{
    /// <summary>Shared palette + helpers for the dark theme: flat blue accent, no gradients.</summary>
    public static class Theme
    {
        public static readonly Color WindowBg = Color.FromArgb(18, 18, 20);
        public static readonly Color CardBg = Color.FromArgb(28, 28, 31);
        public static readonly Color CardBorder = Color.FromArgb(45, 45, 49);
        public static readonly Color Accent = Color.FromArgb(10, 132, 255);
        public static readonly Color AccentPressed = Color.FromArgb(8, 108, 212);
        public static readonly Color TextPrimary = Color.FromArgb(240, 240, 242);
        public static readonly Color TextSecondary = Color.FromArgb(150, 150, 156);
        public static readonly Color TrackOff = Color.FromArgb(255, 60, 60, 64);
        public static readonly Color ControlBg = Color.FromArgb(38, 38, 42);
        public static readonly Color Danger = Color.FromArgb(255, 69, 58);

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
            Font = Theme.Sans(10f, FontStyle.Bold);
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
                fill = pressed ? Color.FromArgb(50, 50, 55) : (hover ? Color.FromArgb(44, 44, 48) : Theme.ControlBg);
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

            using (var path = Theme.RoundedRect(rect, Height / 2f))
            using (var b = new SolidBrush(isChecked ? Theme.Accent : Theme.ControlBg))
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
            using (var b = new SolidBrush(Theme.TrackOff))
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

    /// <summary>
    /// Fully custom-drawn dropdown selector. Not a ComboBox subclass: stock
    /// WinForms ComboBox draws its own border via native control chrome that
    /// ignores BackColor/ForeColor even with FlatStyle.Flat — it always shows
    /// up as a light system-colored edge, which reads badly on a dark theme.
    /// Drawing everything ourselves (box, chevron, and the dropdown list) is
    /// the only way to keep it dark end to end.
    /// </summary>
    public class MacComboBox : Control
    {
        public sealed class ItemCollection
        {
            private readonly List<object> items = new List<object>();
            private readonly MacComboBox owner;
            internal ItemCollection(MacComboBox owner) { this.owner = owner; }
            public int Count => items.Count;
            public object this[int index] => items[index];
            public void Clear() { items.Clear(); owner.SelectedIndex = -1; owner.Invalidate(); }
            public void AddRange(object[] values) { items.AddRange(values); owner.Invalidate(); }
            public void Add(object value) { items.Add(value); owner.Invalidate(); }
            public bool Contains(object value) => items.Contains(value);
            internal List<object> Raw => items;
        }

        public ItemCollection Items { get; }

        private int selectedIndex = -1;
        public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                int v = (value < -1 || value >= Items.Count) ? -1 : value;
                if (v == selectedIndex) return;
                selectedIndex = v;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get => selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null;
            set => SelectedIndex = value == null ? -1 : Items.Raw.IndexOf(value);
        }

        public event EventHandler SelectedIndexChanged;

        private bool hover;

        public MacComboBox()
        {
            Items = new ItemCollection(this);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                      ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.TextPrimary;
            Cursor = Cursors.Hand;
            Font = Theme.Sans(10f);
            Height = 34;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled || Items.Count == 0) return;

            var names = Items.Raw.ConvertAll(o => o?.ToString() ?? "");
            var popup = new MacDropdownPopup(names, SelectedIndex, Width, Font);
            popup.ItemPicked += (s, idx) => SelectedIndex = idx;
            popup.ShowBelow(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0, 0, Width - 1, Height - 1);

            Color fill = !Enabled ? Theme.ControlBg : (hover ? Color.FromArgb(46, 46, 50) : Theme.ControlBg);
            using (var path = Theme.RoundedRect(rect, 8f))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(Theme.CardBorder, 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }

            string text = SelectedItem?.ToString() ?? "";
            var textRect = new Rectangle(12, 0, Width - 34, Height);
            TextRenderer.DrawText(g, text, Font, textRect, Enabled ? Theme.TextPrimary : Theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            float cx = Width - 18, cy = Height / 2f;
            using (var pen = new Pen(Theme.TextSecondary, 1.6f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 4, cy - 2.5f),
                    new PointF(cx, cy + 2.5f),
                    new PointF(cx + 4, cy - 2.5f)
                });
            }
        }
    }

    /// <summary>Borderless popup list shown by <see cref="MacComboBox"/>. Closes on
    /// selection or when it loses focus (click elsewhere, Alt-Tab, etc).</summary>
    internal sealed class MacDropdownPopup : Form
    {
        public event EventHandler<int> ItemPicked;

        private readonly List<string> items;
        private int hoverIndex;
        private const int ItemHeight = 30;
        private const int Pad = 4;

        public MacDropdownPopup(List<string> items, int selected, int width, Font font)
        {
            this.items = items;
            hoverIndex = selected;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.WindowBg; // corners outside the rounded Region show the owner's window bg
            Font = font;
            DoubleBuffered = true;

            int rows = Math.Max(1, Math.Min(items.Count, 8));
            Width = Math.Max(width, 160);
            Height = rows * ItemHeight + Pad * 2;
        }

        public void ShowBelow(Control anchor)
        {
            var screenPt = anchor.Parent.PointToScreen(new Point(anchor.Left, anchor.Bottom + 4));
            var area = Screen.FromControl(anchor).WorkingArea;
            if (screenPt.Y + Height > area.Bottom) screenPt.Y = anchor.Parent.PointToScreen(anchor.Location).Y - Height - 4;
            Location = screenPt;
            Show();
            Activate();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using var path = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), 10f);
            Region = new Region(path);
        }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = (e.Y - Pad) / ItemHeight;
            if (idx < 0 || idx >= items.Count) idx = -1;
            if (idx != hoverIndex) { hoverIndex = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int idx = (e.Y - Pad) / ItemHeight;
            if (idx >= 0 && idx < items.Count)
            {
                ItemPicked?.Invoke(this, idx);
                Close();
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.CardBg)) g.FillRectangle(b, ClientRectangle);
            using (var pen = new Pen(Theme.CardBorder, 1f)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            for (int i = 0; i < items.Count; i++)
            {
                var r = new Rectangle(Pad, Pad + i * ItemHeight, Width - Pad * 2, ItemHeight);
                if (i == hoverIndex)
                {
                    using var path = Theme.RoundedRect(r, 6f);
                    using var hb = new SolidBrush(Theme.Accent);
                    g.FillPath(hb, path);
                }
                TextRenderer.DrawText(g, items[i], Font,
                    new Rectangle(r.X + 10, r.Y, r.Width - 16, r.Height),
                    i == hoverIndex ? Color.White : Theme.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            }
        }
    }
}
