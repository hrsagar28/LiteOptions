using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using LiteOptions.Input;

namespace LiteOptions.UI
{
    static class UIx
    {
        /// <summary>First opaque background colour up the parent chain (rows are transparent so card corners show through).</summary>
        public static Color SolidBack(Control c)
        {
            for (var p = c?.Parent; p != null; p = p.Parent)
                if (p.BackColor.A == 255) return p.BackColor;
            return Theme.CardBg;
        }

        public static Label Label(string text, Font font = null, Color? color = null)
        {
            return new Label
            {
                Text = text, Font = font ?? Theme.Body, ForeColor = color ?? Theme.Text, AutoSize = true,
                Margin = Padding.Empty, Padding = Padding.Empty, UseMnemonic = false, BackColor = Color.Transparent
            };
        }

        public static Label Section(string text)
        {
            var l = Label(text, Theme.BodyStrong);
            l.Margin = Theme.P(2, 22, 0, 8);
            return l;
        }

        public static FlowLayoutPanel Row(params Control[] controls)
        {
            var f = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty
            };
            foreach (var c in controls)
            {
                if (c is Label) c.Anchor = AnchorStyles.Left;
                f.Controls.Add(c);
            }
            return f;
        }
    }

    // ───────────────────────── base ─────────────────────────

    abstract class FluentControl : Control
    {
        protected bool Hover, Pressed;

        protected FluentControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.Body;
            ForeColor = Theme.Text;
            Margin = Padding.Empty;
        }

        protected Color Surface
        {
            get
            {
                for (Control c = Parent; c != null; c = c.Parent)
                    if (c.BackColor.A == 255) return c.BackColor;
                return Theme.CardBg;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Surface); }

        protected void FocusRing(Graphics g, RectangleF r, float radius)
        {
            if (!Focused || !ShowFocusCues) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rr = RectangleF.Inflate(r, Theme.Sf(2), Theme.Sf(2));
            using (var p = new Pen(Theme.Text, Theme.Sf(1.5f)))
            using (var path = Theme.Rounded(rr, radius + Theme.Sf(2))) g.DrawPath(p, path);
        }
    }

    // ───────────────────────── buttons ─────────────────────────

    enum ButtonKind { Standard, Primary, Subtle, Danger }

    sealed class FluentButton : Button
    {
        public ButtonKind Kind;
        public string Glyph;
        bool hover, pressed;

        public FluentButton(string text, ButtonKind kind = ButtonKind.Standard, string glyph = null, EventHandler click = null)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Kind = kind; Glyph = glyph;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Margin = Theme.P(0, 0, 8, 0);
            Text = text ?? "";
            Size = Measure();
            if (click != null) Click += click;
        }

        int minWidth;
        public int MinWidth { get => minWidth; set { minWidth = value; Size = Measure(); } }

        Size Measure()
        {
            int tw = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
            if (tw == 0) return new Size(Theme.S(34), Theme.S(32));
            int gw = Glyph != null ? Theme.S(16) + Theme.S(8) : 0;
            return new Size(Math.Max(MinWidth, tw + gw + Theme.S(28)), Theme.S(32));
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Size = Measure(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        Color Surface
        {
            get
            {
                for (Control c = Parent; c != null; c = c.Parent) if (c.BackColor.A == 255) return c.BackColor;
                return Theme.CardBg;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Surface); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Surface);
            Color fill, text; Color? border = null;
            switch (Kind)
            {
                case ButtonKind.Primary:
                    fill = !Enabled ? Theme.Blend(Theme.Accent, Surface, 0.6f) : pressed ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent;
                    text = Theme.OnAccent; break;
                case ButtonKind.Subtle:
                    fill = pressed ? Theme.SubtlePressed : hover ? Theme.SubtleHover : Surface;
                    text = Enabled ? Theme.Text : Theme.Faint; break;
                default:
                    fill = pressed ? Theme.ControlPressed : hover ? Theme.ControlHover : Theme.ControlBg;
                    border = Theme.ControlBorder;
                    text = !Enabled ? Theme.Faint : Kind == ButtonKind.Danger ? Theme.Bad : Theme.Text; break;
            }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRounded(g, fill, r, Theme.Sf(4), border);

            int tw = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
            int gw = Glyph != null ? Theme.S(16) : 0;
            int gap = Glyph != null && tw > 0 ? Theme.S(8) : 0;
            int x = (Width - (tw + gw + gap)) / 2;
            if (Glyph != null) Theme.Glyph(g, Glyph, 10f, text, new Rectangle(x, 0, gw, Height));
            if (tw > 0)
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x + gw + gap, 0, tw + 2, Height), text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.Text, Theme.Sf(1.5f)))
                using (var path = Theme.Rounded(RectangleF.Inflate(r, -Theme.Sf(1.5f), -Theme.Sf(1.5f)), Theme.Sf(3))) g.DrawPath(p, path);
        }
    }

    // ───────────────────────── toggle switch ─────────────────────────

    sealed class ToggleSwitch : FluentControl
    {
        bool on;
        public event EventHandler CheckedChanged;
        public string OnText = "On", OffText = "Off";

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardDoubleClick, false); // two quick clicks = two toggles
            TabStop = true;
            Cursor = Cursors.Hand;
            int tw = Math.Max(TextRenderer.MeasureText(OnText, Theme.Body).Width, TextRenderer.MeasureText(OffText, Theme.Body).Width);
            Size = new Size(tw + Theme.S(12) + Theme.S(40), Theme.S(32));
        }

        public bool Checked
        {
            get => on;
            set { if (on == value) return; on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { if (Enabled) { Focus(); Checked = !Checked; } base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked; base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(Width - Theme.S(40) - 1, (Height - Theme.S(20)) / 2f, Theme.S(40), Theme.S(20));
            Color txt = Enabled ? Theme.Text : Theme.Faint;
            TextRenderer.DrawText(g, on ? OnText : OffText, Theme.Body, new Rectangle(0, 0, (int)track.X - Theme.S(12), Height), txt,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
            float knob = Hover && Enabled ? Theme.Sf(14) : Theme.Sf(12);
            float cy = track.Y + track.Height / 2;
            if (on)
            {
                var fill = !Enabled ? Theme.Faint : Pressed ? Theme.AccentPressed : Hover ? Theme.AccentHover : Theme.Accent;
                Theme.FillRounded(g, fill, track, track.Height / 2);
                float cx = track.Right - Theme.Sf(10);
                using (var b = new SolidBrush(Theme.OnAccent)) g.FillEllipse(b, cx - knob / 2, cy - knob / 2, knob, knob);
            }
            else
            {
                var border = Enabled ? Theme.TrackOff : Theme.Faint;
                Theme.FillRounded(g, Hover ? Theme.ControlHover : Theme.ControlBg, track, track.Height / 2, border);
                float cx = track.X + Theme.Sf(10);
                using (var b = new SolidBrush(border)) g.FillEllipse(b, cx - knob / 2, cy - knob / 2, knob, knob);
            }
            FocusRing(g, track, track.Height / 2);
        }
    }

    // ───────────────────────── slider ─────────────────────────

    sealed class Slider : FluentControl
    {
        int min, max = 100, val;
        bool dragging;
        public int SmallChange = 1, LargeChange = 5;
        public event EventHandler ValueChanged, ValueCommitted;

        public Slider()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(Theme.S(220), Theme.S(32));
        }

        public int Minimum { get => min; set { min = value; if (max < min) max = min; val = Clamp(val); Invalidate(); } }
        public int Maximum { get => max; set { max = Math.Max(value, min); val = Clamp(val); Invalidate(); } }
        /// <summary>Setting Value from code doesn't raise events.</summary>
        public int Value { get => val; set { val = Clamp(value); Invalidate(); } }
        public bool Dragging => dragging;

        int Clamp(int v) => Math.Max(min, Math.Min(max, v));
        float R => Theme.Sf(10);
        RectangleF Track => new RectangleF(R, Height / 2f - Theme.Sf(2), Width - 2 * R, Theme.Sf(4));
        float XOf(int v) => Track.X + (max == min ? 0 : (v - min) / (float)(max - min)) * Track.Width;
        int ValueAt(int x) => Clamp((int)Math.Round(min + (x - Track.X) / Math.Max(1, Track.Width) * (max - min)));

        void SetUser(int v)
        {
            v = Clamp(v);
            if (v == val) return;
            val = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            Focus();
            dragging = true;
            Capture = true;
            SetUser(ValueAt(e.X));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) SetUser(ValueAt(e.X));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys k) =>
            k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int v = val;
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: v -= SmallChange; break;
                case Keys.Right: case Keys.Up: v += SmallChange; break;
                case Keys.PageDown: v -= LargeChange; break;
                case Keys.PageUp: v += LargeChange; break;
                case Keys.Home: v = min; break;
                case Keys.End: v = max; break;
                default: return;
            }
            SetUser(v);
            ValueCommitted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = Track;
            Theme.FillRounded(g, Theme.Blend(Theme.TrackOff, Surface, 0.45f), t, t.Height / 2);
            float x = XOf(val);
            var fill = Enabled ? Theme.Accent : Theme.Faint;
            Theme.FillRounded(g, fill, new RectangleF(t.X, t.Y, Math.Max(t.Height, x - t.X), t.Height), t.Height / 2);
            float cy = Height / 2f;
            using (var b = new SolidBrush(Theme.ThumbBg)) g.FillEllipse(b, x - R, cy - R, 2 * R, 2 * R);
            using (var p = new Pen(Theme.ControlBorder)) g.DrawEllipse(p, x - R, cy - R, 2 * R, 2 * R);
            float inner = Pressed || dragging ? Theme.Sf(4.5f) : Hover ? Theme.Sf(7) : Theme.Sf(6);
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, x - inner, cy - inner, 2 * inner, 2 * inner);
            FocusRing(g, new RectangleF(x - R, cy - R, 2 * R, 2 * R), R);
        }
    }

    // ───────────────────────── segmented control ─────────────────────────

    sealed class Segmented : FluentControl
    {
        string[] items = new string[0];
        int[] widths = new int[0];
        int sel = -1, hoverIdx = -1;
        public event EventHandler SelectedIndexChanged;

        public Segmented()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public void SetItems(params string[] labels)
        {
            items = labels;
            widths = labels.Select(l => TextRenderer.MeasureText(l, Theme.BodyStrong).Width + Theme.S(28)).ToArray();
            Size = new Size(widths.Sum() + Theme.S(6), Theme.S(36));
            if (sel >= items.Length) sel = -1;
            Invalidate();
        }

        public int Count => items.Length;
        public int SelectedIndex { get => sel; set { sel = value; Invalidate(); } }

        Rectangle Seg(int i)
        {
            int x = Theme.S(3);
            for (int k = 0; k < i; k++) x += widths[k];
            return new Rectangle(x, Theme.S(3), widths[i], Height - Theme.S(6));
        }

        int IndexAt(Point p)
        {
            for (int i = 0; i < items.Length; i++) if (Seg(i).Contains(p)) return i;
            return -1;
        }

        void Choose(int i)
        {
            if (i < 0 || i >= items.Length || i == sel) return;
            sel = i;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e) { int h = IndexAt(e.Location); if (h != hoverIdx) { hoverIdx = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverIdx = -1; base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (Enabled && e.Button == MouseButtons.Left) { Focus(); Choose(IndexAt(e.Location)); } }
        protected override bool IsInputKey(Keys k) => k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) Choose(Math.Max(0, sel - 1));
            if (e.KeyCode == Keys.Right) Choose(Math.Min(items.Length - 1, sel + 1));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRounded(g, Theme.ControlBg, outer, Theme.Sf(6), Theme.ControlBorder);
            for (int i = 0; i < items.Length; i++)
            {
                var r = Seg(i);
                bool selected = i == sel;
                if (selected) Theme.FillRounded(g, Enabled ? Theme.Accent : Theme.Faint, r, Theme.Sf(4));
                else if (i == hoverIdx && Enabled) Theme.FillRounded(g, Theme.SubtleHover, r, Theme.Sf(4));
                var color = selected ? Theme.OnAccent : Enabled ? Theme.Text : Theme.Faint;
                TextRenderer.DrawText(g, items[i], selected ? Theme.BodyStrong : Theme.Body, r, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            FocusRing(g, outer, Theme.Sf(6));
        }
    }

    // ───────────────────────── drop-down + themed menus ─────────────────────────

    class DropDown : FluentControl
    {
        readonly List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>();
        public object SelectedValue { get; private set; }
        public event Action<object> Picked;
        public Func<ContextMenuStrip> MenuBuilder;

        public DropDown()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(Theme.S(280), Theme.S(34));
        }

        public void SetItems(IEnumerable<KeyValuePair<string, object>> list)
        {
            items.Clear();
            items.AddRange(list);
        }

        public void Select(object value)
        {
            SelectedValue = value;
            var hit = items.FirstOrDefault(i => Equals(i.Value, value));
            Text = hit.Key ?? "";
            Invalidate();
        }

        protected void Choose(object value) { Select(value); Picked?.Invoke(value); }

        protected virtual ContextMenuStrip BuildMenu()
        {
            if (MenuBuilder != null) return MenuBuilder();
            var m = Menus.Create();
            foreach (var it in items)
            {
                var v = it.Value;
                m.Items.Add(Menus.Item(it.Key, () => Choose(v), Equals(v, SelectedValue)));
            }
            return m;
        }

        public void Open()
        {
            if (!Enabled) return;
            var m = BuildMenu();
            if (m == null) return;
            m.MinimumSize = new Size(Width, 0);
            m.Closed += (s, e) => BeginInvoke((Action)(() => m.Dispose()));
            m.Show(this, new Point(0, Height + Theme.S(2)));
        }

        protected override void OnClick(EventArgs e) { Focus(); Open(); base.OnClick(e); }
        protected override bool IsInputKey(Keys k) => k == Keys.Down || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down || e.KeyCode == Keys.F4) { Open(); e.Handled = true; }
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            var fill = !Enabled ? Theme.ControlBg : Pressed ? Theme.ControlPressed : Hover ? Theme.ControlHover : Theme.ControlBg;
            Theme.FillRounded(g, fill, r, Theme.Sf(4), Theme.ControlBorder);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.S(11), 0, Width - Theme.S(44), Height), Enabled ? Theme.Text : Theme.Faint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            Theme.Glyph(g, Theme.IcChevronDown, 8f, Theme.Muted, new Rectangle(Width - Theme.S(32), 0, Theme.S(24), Height));
            FocusRing(g, r, Theme.Sf(4));
        }
    }

    static class Menus
    {
        public static ContextMenuStrip Create()
        {
            var m = new ContextMenuStrip();
            Style(m);
            return m;
        }

        static void Style(ToolStripDropDownMenu m)
        {
            m.Renderer = new ThemedRenderer();
            m.ShowImageMargin = false;
            m.ShowCheckMargin = false;
            m.Font = Theme.Body;
            m.BackColor = Theme.MenuBg;
            m.ForeColor = Theme.Text;
            m.Padding = new Padding(Theme.S(4), Theme.S(4), Theme.S(4), Theme.S(4));
            m.HandleCreated += (s, e) => Theme.RoundCorners(m.Handle);
        }

        public static ToolStripMenuItem Item(string text, Action onClick, bool check = false)
        {
            var it = new ToolStripMenuItem(text) { Checked = check, ForeColor = Theme.Text, Padding = new Padding(Theme.S(10), Theme.S(5), Theme.S(18), Theme.S(5)) };
            if (onClick != null) it.Click += (s, e) => onClick();
            return it;
        }

        public static ToolStripMenuItem Submenu(string text, IEnumerable<ToolStripItem> children, bool containsCurrent = false)
        {
            var it = Item(text, null, containsCurrent);
            var dd = new ToolStripDropDownMenu();
            Style(dd);
            dd.Items.AddRange(children.ToArray());
            it.DropDown = dd;
            return it;
        }

        public static ToolStripSeparator Separator() => new ToolStripSeparator { Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
    }

    sealed class ThemedRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.MenuBg)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(Theme.Dark ? Theme.ControlBorder : Theme.CardBorder))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            var r = new RectangleF(Theme.Sf(1), Theme.Sf(1), e.Item.Width - Theme.Sf(2), e.Item.Height - Theme.Sf(2));
            if (e.Item.Selected && e.Item.Enabled) Theme.FillRounded(g, Theme.SubtleHover, r, Theme.Sf(4));
            if (e.Item is ToolStripMenuItem mi && mi.Checked)
                Theme.FillRounded(g, Theme.Accent, new RectangleF(r.X + Theme.Sf(1), r.Y + r.Height / 2 - Theme.Sf(8), Theme.Sf(3), Theme.Sf(16)), Theme.Sf(1.5f));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? (e.Item.ForeColor == Theme.Faint ? Theme.Faint : Theme.Text) : Theme.Faint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Theme.Dark ? Theme.ControlBorder : Theme.CardBorder))
                e.Graphics.DrawLine(p, Theme.S(6), y, e.Item.Width - Theme.S(6), y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    }

    // ───────────────────────── navigation ─────────────────────────

    sealed class NavItem : FluentControl
    {
        bool selected;
        readonly string glyph;
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }

        public NavItem(string text, string glyph)
        {
            Text = text;
            this.glyph = glyph;
            Height = Theme.S(40);
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new RectangleF(Theme.Sf(4), Theme.Sf(2), Width - Theme.Sf(8), Height - Theme.Sf(4));
            if (selected || Hover) Theme.FillRounded(g, selected ? Theme.NavSelected : Theme.SubtleHover, r, Theme.Sf(5));
            if (selected) Theme.FillRounded(g, Theme.Accent, new RectangleF(r.X, r.Y + r.Height / 2 - Theme.Sf(8), Theme.Sf(3), Theme.Sf(16)), Theme.Sf(1.5f));
            Theme.Glyph(g, glyph, 11.5f, Theme.Text, new Rectangle((int)r.X + Theme.S(12), 0, Theme.S(22), Height));
            TextRenderer.DrawText(g, Text, Theme.Body, new Rectangle((int)r.X + Theme.S(46), 0, Width - Theme.S(56), Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            FocusRing(g, RectangleF.Inflate(r, -Theme.Sf(2), -Theme.Sf(2)), Theme.Sf(5));
        }
    }

    // ───────────────────────── cards and rows ─────────────────────────

    /// <summary>Rounded card holding a column of rows, with dividers between them.</summary>
    sealed class CardPanel : TableLayoutPanel
    {
        public CardPanel()
        {
            ColumnCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
            Padding = new Padding(1);
            Margin = Padding.Empty;
            BackColor = Theme.CardBg;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public T Add<T>(T c) where T : Control
        {
            int row = RowCount++;
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
            c.Margin = Padding.Empty;
            Controls.Add(c, 0, row);
            c.VisibleChanged += (s, e) => Invalidate();
            return c;
        }

        bool flash;
        Timer flashTimer;

        /// <summary>Briefly outlines the card in the accent colour (used when jumping here from the device picture).</summary>
        public void Flash()
        {
            flash = true;
            Invalidate();
            flashTimer?.Dispose();
            flashTimer = new Timer { Interval = 1100 };
            flashTimer.Tick += (s, e) => { flash = false; Invalidate(); flashTimer.Stop(); };
            flashTimer.Start();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color outside = UIx.SolidBack(this);
            g.Clear(outside);
            Theme.FillRounded(g, Theme.CardBg, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Sf(7), Theme.CardBorder);
            if (flash)
                using (var p = new Pen(Theme.Accent, Theme.Sf(2)))
                using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), Theme.Sf(7))) g.DrawPath(p, path);
            bool first = true;
            using (var p = new Pen(Theme.Divider))
                foreach (Control c in Controls.Cast<Control>().Where(x => x.Visible).OrderBy(x => x.Top))
                {
                    if (!first) g.DrawLine(p, 1, c.Top, Width - 2, c.Top);
                    first = false;
                }
        }
    }

    sealed class GlyphBox : Control
    {
        public string Glyph;
        public GlyphBox(string glyph)
        {
            Glyph = glyph;
            Size = new Size(Theme.S(24), Theme.S(24));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Margin = Padding.Empty;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(UIx.SolidBack(this));
            if (!string.IsNullOrEmpty(Glyph)) Theme.Glyph(e.Graphics, Glyph, 12.5f, Theme.Text, ClientRectangle);
        }
    }

    /// <summary>Windows 11 settings row: icon · title + description · control on the right.</summary>
    sealed class SettingRow : TableLayoutPanel
    {
        public readonly Label Title, Desc;
        public readonly Control RightControl;
        readonly TableLayoutPanel text;

        public SettingRow(string glyph, string title, string desc, Control right)
        {
            ColumnCount = 3; RowCount = 1;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = Theme.P(16, 12, 16, 12);
            Margin = Padding.Empty;
            BackColor = Color.Transparent;
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(40)));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var icon = new GlyphBox(glyph) { Anchor = AnchorStyles.Left };
            Controls.Add(icon, 0, 0);

            text = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Theme.P(0, 0, 16, 0), Anchor = AnchorStyles.Left, BackColor = Color.Transparent };
            Title = UIx.Label(title, Theme.Body);
            Desc = UIx.Label(desc ?? "", Theme.Caption, Theme.Muted);
            Desc.Margin = Theme.P(0, 2, 0, 0);
            Desc.Visible = !string.IsNullOrEmpty(desc);
            text.Controls.Add(Title, 0, 0);
            text.Controls.Add(Desc, 0, 1);
            Controls.Add(text, 1, 0);

            RightControl = right;
            if (right != null)
            {
                right.Anchor = AnchorStyles.Right;
                right.Margin = Padding.Empty;
                Controls.Add(right, 2, 0);
                right.SizeChanged += (s, e) => Rewrap();
            }
        }

        public string Description
        {
            set { Desc.Text = value ?? ""; Desc.Visible = !string.IsNullOrEmpty(value); Rewrap(); }
        }

        bool rewrapping;
        void Rewrap()
        {
            if (rewrapping || Width <= 0) return;
            rewrapping = true;
            int rightW = RightControl != null && RightControl.Visible ? RightControl.Width : 0;
            int w = Math.Max(Theme.S(140), Width - Padding.Horizontal - Theme.S(40) - rightW - Theme.S(20));
            var max = new Size(w, 0);
            if (Title.MaximumSize != max) Title.MaximumSize = max;
            if (Desc.MaximumSize != max) Desc.MaximumSize = max;
            rewrapping = false;
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Rewrap(); }
    }

    /// <summary>A row inside a card that shows wrapped, muted text. Painted directly so it always sizes correctly.</summary>
    sealed class NoteRow : Control
    {
        string text;
        int lastHeight;
        public NoteRow(string text, string glyph = null)
        {
            this.text = text ?? "";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            Height = Theme.S(40);
        }

        public string Text2
        {
            set { text = value ?? ""; Remeasure(); Invalidate(); }
        }

        int TextWidth(int width) => Math.Max(Theme.S(160), width - Theme.S(16 + 40 + 16));

        int Measure(int width) =>
            TextRenderer.MeasureText(text, Theme.Caption, new Size(TextWidth(width), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + Theme.S(22);

        public override Size GetPreferredSize(Size proposed)
        {
            int w = proposed.Width > 0 && proposed.Width < 10000 ? proposed.Width : Width;
            return new Size(w, Measure(w));
        }

        void Remeasure()
        {
            int h = Measure(Width);
            if (h != lastHeight) { lastHeight = h; Height = h; Parent?.PerformLayout(); }
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (Width > 0) Remeasure(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, text, Theme.Caption, new Rectangle(Theme.S(56), Theme.S(10), TextWidth(Width), Height - Theme.S(12)), Theme.Muted,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
        }
    }

    // ───────────────────────── text input ─────────────────────────

    sealed class TextField : Panel
    {
        public readonly TextBox Box;

        public TextField(bool multiline = false, int height = 34)
        {
            Height = Theme.S(height);
            Padding = multiline ? Theme.P(10, 8, 4, 8) : Theme.P(10, 8, 10, 4);
            BackColor = Theme.ControlBg;
            Margin = Padding.Empty;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None, BackColor = Theme.ControlBg, ForeColor = Theme.Text, Font = Theme.Body,
                Dock = DockStyle.Fill, Multiline = multiline
            };
            if (multiline) { Box.ScrollBars = ScrollBars.Vertical; Box.AcceptsReturn = true; }
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Theme.DarkScrollbars(Box);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UIx.SolidBack(this));
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRounded(g, Theme.ControlBg, r, Theme.Sf(4), Theme.ControlBorder);
            if (Box.Focused)
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, Theme.S(2), Height - Theme.S(2), Width - Theme.S(4), Theme.S(2));
        }
    }

    // ───────────────────────── device panel ─────────────────────────

    sealed class DeviceHero : Control
    {
        public bool IsMouse;
        public string Title = "", Hint = "", Connection = "", Firmware = "";
        public bool Found, Online;
        public Hid.BatteryInfo Battery;
        public bool BatteryLow;

        sealed class Hotspot { public string Id, Label; public RectangleF Area; public PointF Dot; }
        readonly List<Hotspot> hotspots = new List<Hotspot>();
        Hotspot hot;
        public event Action<string> HotspotClicked;

        public DeviceHero(bool mouse)
        {
            IsMouse = mouse;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(128);
            Margin = Padding.Empty;
        }

        Hotspot HitTest(Point p)
        {
            // smallest area wins (the wheel sits inside the mouse body)
            return hotspots.Where(h => h.Area.Contains(p)).OrderBy(h => h.Area.Width * h.Area.Height).FirstOrDefault();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitTest(e.Location);
            if (h != hot) { hot = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hot != null) { hot = null; Invalidate(); } }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var h = HitTest(e.Location);
            if (h != null) HotspotClicked?.Invoke(h.Id);
        }

        void DrawHotspots(Graphics g, Rectangle art)
        {
            foreach (var h in hotspots)
            {
                bool isHot = h == hot;
                float r = isHot ? Theme.Sf(7) : Theme.Sf(5);
                using (var b = new SolidBrush(Color.FromArgb(isHot ? 255 : 210, Theme.Accent))) g.FillEllipse(b, h.Dot.X - r, h.Dot.Y - r, r * 2, r * 2);
                using (var p = new Pen(Theme.CardBg, Theme.Sf(1.5f))) g.DrawEllipse(p, h.Dot.X - r, h.Dot.Y - r, r * 2, r * 2);
            }
            if (hot == null) return;
            // label pill under the picture
            var size = TextRenderer.MeasureText(hot.Label, Theme.CaptionStrong);
            var pill = new RectangleF(art.X + (art.Width - size.Width - Theme.S(20)) / 2f, Height - Theme.S(26), size.Width + Theme.S(20), Theme.S(20));
            Theme.FillRounded(g, Theme.Accent, pill, pill.Height / 2);
            TextRenderer.DrawText(g, hot.Label, Theme.CaptionStrong, Rectangle.Round(pill), Theme.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        public override Size GetPreferredSize(Size proposed) => new Size(proposed.Width > 0 && proposed.Width < 10000 ? proposed.Width : Width, Theme.S(128));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UIx.SolidBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRounded(g, Theme.CardBg, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Sf(7), Theme.CardBorder);

            var art = new Rectangle(Theme.S(18), Theme.S(14), Theme.S(150), Height - Theme.S(34));
            hotspots.Clear();
            if (IsMouse) DrawMouse(g, art); else DrawKeyboard(g, art);
            DrawHotspots(g, art);

            int x = art.Right + Theme.S(18);
            int w = Width - x - Theme.S(20);
            if (!Found)
            {
                TextRenderer.DrawText(g, Title, Theme.Subtitle, new Rectangle(x, Theme.S(24), w, Theme.S(28)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, Hint, Theme.Caption, new Rectangle(x, Theme.S(56), w, Height - Theme.S(62)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
                return;
            }
            TextRenderer.DrawText(g, Title, Theme.Subtitle, new Rectangle(x, Theme.S(18), w, Theme.S(28)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            // status: coloured dot + text
            var dot = Online ? Theme.Good : Theme.Faint;
            int y = Theme.S(54);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, x, y + Theme.S(5), Theme.S(8), Theme.S(8));
            string status = Online ? "Connected · " + Connection : "Asleep or switched off";
            TextRenderer.DrawText(g, status, Theme.Body, new Point(x + Theme.S(14), y), Theme.Muted, TextFormatFlags.NoPadding);

            // battery gauge
            y = Theme.S(80);
            if (Battery != null)
            {
                var col = Theme.BatteryColor(Battery.Percent, BatteryLow);
                Theme.DrawBattery(g, new Rectangle(x, y + Theme.S(3), Theme.S(26), Theme.S(13)), Theme.BatteryFraction(Battery), col, Theme.Muted);
                string bt = Battery.Percent.HasValue ? Battery.Percent + "%" : (string.IsNullOrEmpty(Battery.Level) ? "Battery" : Battery.Level);
                TextRenderer.DrawText(g, bt, Theme.BodyStrong, new Point(x + Theme.S(34), y), Theme.Text, TextFormatFlags.NoPadding);
                int bw = TextRenderer.MeasureText(bt, Theme.BodyStrong).Width;
                if (!string.IsNullOrEmpty(Firmware))
                    TextRenderer.DrawText(g, "  ·  Firmware " + Firmware, Theme.Caption, new Point(x + Theme.S(34) + bw, y + Theme.S(2)), Theme.Faint, TextFormatFlags.NoPadding);
            }
            else if (!string.IsNullOrEmpty(Firmware))
                TextRenderer.DrawText(g, "Firmware " + Firmware, Theme.Caption, new Point(x, y + Theme.S(2)), Theme.Faint, TextFormatFlags.NoPadding);
        }

        void DrawMouse(Graphics g, Rectangle area)
        {
            float h = area.Height, w = h * 0.64f;
            var body = new RectangleF(area.X + (area.Width - w) / 2, area.Y + Theme.Sf(2), w, h - Theme.Sf(6));
            // soft shadow
            using (var sh = new SolidBrush(Color.FromArgb(Theme.Dark ? 70 : 28, 0, 0, 0)))
                g.FillEllipse(sh, body.X + Theme.Sf(3), body.Y + Theme.Sf(5), body.Width, body.Height);
            Color top = Theme.Dark ? Color.FromArgb(92, 92, 96) : Color.FromArgb(236, 236, 238);
            Color bot = Theme.Dark ? Color.FromArgb(58, 58, 62) : Color.FromArgb(200, 201, 205);
            using (var lg = new LinearGradientBrush(body, top, bot, 90f)) g.FillEllipse(lg, body);
            using (var p = new Pen(Theme.Dark ? Color.FromArgb(110, 110, 115) : Color.FromArgb(185, 186, 190), Theme.Sf(1))) g.DrawEllipse(p, body);
            float cx = body.X + body.Width / 2;
            using (var p = new Pen(Theme.Dark ? Color.FromArgb(48, 48, 52) : Color.FromArgb(175, 176, 180), Theme.Sf(1.4f)))
                g.DrawLine(p, cx, body.Y + Theme.Sf(3), cx, body.Y + body.Height * 0.36f);
            var wheel = new RectangleF(cx - Theme.Sf(4), body.Y + body.Height * 0.11f, Theme.Sf(8), body.Height * 0.17f);
            Theme.FillRounded(g, Theme.Dark ? Color.FromArgb(40, 40, 44) : Color.FromArgb(120, 122, 128), wheel, Theme.Sf(4));
            hotspots.Add(new Hotspot { Id = "middle", Label = "Middle button", Area = RectangleF.Inflate(wheel, Theme.Sf(10), Theme.Sf(8)), Dot = new PointF(wheel.Right + Theme.Sf(14), wheel.Y + wheel.Height / 2) });
            hotspots.Add(new Hotspot { Id = "pointer", Label = "Pointer & scrolling", Area = body, Dot = new PointF(cx, body.Y + body.Height * 0.68f) });
            using (var hl = new SolidBrush(Color.FromArgb(Theme.Dark ? 30 : 90, 255, 255, 255)))
                g.FillEllipse(hl, body.X + body.Width * 0.14f, body.Y + body.Height * 0.08f, body.Width * 0.32f, body.Height * 0.3f);
        }

        void DrawKeyboard(Graphics g, Rectangle area)
        {
            float w = area.Width, h = w * 0.42f;
            var body = new RectangleF(area.X, area.Y + (area.Height - h) / 2, w, h);
            using (var sh = new SolidBrush(Color.FromArgb(Theme.Dark ? 70 : 28, 0, 0, 0)))
            using (var path = Theme.Rounded(new RectangleF(body.X + Theme.Sf(2), body.Y + Theme.Sf(5), body.Width, body.Height), Theme.Sf(10)))
                g.FillPath(sh, path);
            Color top = Theme.Dark ? Color.FromArgb(84, 84, 88) : Color.FromArgb(234, 234, 236);
            Color bot = Theme.Dark ? Color.FromArgb(56, 56, 60) : Color.FromArgb(206, 207, 211);
            using (var path = Theme.Rounded(body, Theme.Sf(10)))
            {
                using (var lg = new LinearGradientBrush(body, top, bot, 90f)) g.FillPath(lg, path);
                using (var p = new Pen(Theme.Dark ? Color.FromArgb(110, 110, 115) : Color.FromArgb(185, 186, 190), Theme.Sf(1))) g.DrawPath(p, path);
            }
            hotspots.Add(new Hotspot { Id = "remap", Label = "Remap keys", Area = body, Dot = new PointF(body.X + body.Width * 0.5f, body.Y + body.Height * 0.62f) });
            // the K380's round keys
            Color key = Theme.Dark ? Color.FromArgb(36, 36, 40) : Color.FromArgb(250, 250, 251);
            Color keyEdge = Theme.Dark ? Color.FromArgb(28, 28, 30) : Color.FromArgb(190, 191, 196);
            int cols = 12, rows = 4;
            float pad = Theme.Sf(7), gap = Theme.Sf(2.2f);
            float kw = (body.Width - pad * 2 - gap * (cols - 1)) / cols;
            float kh = (body.Height - pad * 2 - gap * (rows - 1)) / rows;
            float d = Math.Min(kw, kh);
            using (var kb = new SolidBrush(key))
            using (var kp = new Pen(keyEdge, Theme.Sf(0.8f)))
            {
                for (int r = 0; r < rows; r++)
                {
                    float y = body.Y + pad + r * (kh + gap) + (kh - d) / 2;
                    if (r == rows - 1)
                    {
                        // modifiers, space bar, arrows
                        float x0 = body.X + pad + (kw - d) / 2;
                        for (int c = 0; c < 3; c++) { var rr = new RectangleF(x0 + c * (kw + gap), y, d, d); g.FillEllipse(kb, rr); g.DrawEllipse(kp, rr); }
                        var space = new RectangleF(x0 + 3 * (kw + gap), y, 5 * (kw + gap) - gap, d);
                        using (var sp = Theme.Rounded(space, d / 2)) { g.FillPath(kb, sp); g.DrawPath(kp, sp); }
                        for (int c = 8; c < cols; c++) { var rr = new RectangleF(x0 + c * (kw + gap), y, d, d); g.FillEllipse(kb, rr); g.DrawEllipse(kp, rr); }
                        continue;
                    }
                    float offset = r == 1 ? kw * 0.25f : r == 2 ? kw * 0.5f : 0;
                    int n = r == 0 ? cols : cols - 1;
                    if (r == 0)
                        hotspots.Add(new Hotspot { Id = "toprow", Label = "Top row (F1–F12)", Area = new RectangleF(body.X, body.Y, body.Width, pad + kh + gap),
                            Dot = new PointF(body.Right - Theme.Sf(2), body.Y + pad + kh / 2) });
                    for (int c = 0; c < n; c++)
                    {
                        var rr = new RectangleF(body.X + pad + offset + c * (kw + gap) + (kw - d) / 2, y, d, d);
                        g.FillEllipse(kb, rr); g.DrawEllipse(kp, rr);
                    }
                }
            }
        }
    }

    // ───────────────────────── key remap list ─────────────────────────

    static class KeyCaps
    {
        public static List<string> Parts(int mods, int vk)
        {
            var parts = new List<string>();
            if ((mods & (int)Mods.Ctrl) != 0) parts.Add("Ctrl");
            if ((mods & (int)Mods.Shift) != 0) parts.Add("Shift");
            if ((mods & (int)Mods.Alt) != 0) parts.Add("Alt");
            if ((mods & (int)Mods.Win) != 0) parts.Add("Win");
            parts.Add(KeyNames.Key(vk));
            return parts;
        }

        /// <summary>Draws key caps left to right; returns the total width.</summary>
        public static int Draw(Graphics g, IList<string> parts, int x, int centerY, Font font = null)
        {
            font = font ?? Theme.Body;
            int start = x, h = Theme.S(28);
            foreach (var p in parts)
            {
                int w = Math.Max(Theme.S(30), TextRenderer.MeasureText(p, font).Width + Theme.S(16));
                var r = new RectangleF(x, centerY - h / 2f, w, h - Theme.Sf(2));
                Theme.FillRounded(g, Theme.KeyCapShadow, new RectangleF(r.X, r.Y + Theme.Sf(2), r.Width, r.Height), Theme.Sf(5));
                Theme.FillRounded(g, Theme.KeyCapBg, r, Theme.Sf(5), Theme.KeyCapBorder);
                TextRenderer.DrawText(g, p, font, Rectangle.Round(r), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                x += w + Theme.S(5);
            }
            return x - start - Theme.S(5);
        }

        public static int Width(IList<string> parts, Font font = null)
        {
            font = font ?? Theme.Body;
            return parts.Sum(p => Math.Max(Theme.S(30), TextRenderer.MeasureText(p, font).Width + Theme.S(16))) + Theme.S(5) * (parts.Count - 1);
        }
    }

    /// <summary>Shows a captured key combination as key caps (used in dialogs).</summary>
    sealed class KeyComboView : Control
    {
        List<string> parts;
        string placeholder = "";
        public KeyComboView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(52);
            Margin = Padding.Empty;
        }
        public void Show(int mods, int vk) { parts = KeyCaps.Parts(mods, vk); Invalidate(); }
        public void Placeholder(string text) { parts = null; placeholder = text; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UIx.SolidBack(this));
            if (parts == null)
            {
                TextRenderer.DrawText(g, placeholder, Theme.Body, ClientRectangle, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                return;
            }
            KeyCaps.Draw(g, parts, 0, Height / 2, Theme.BodyStrong);
        }
    }

    sealed class RemapRow : Control
    {
        public readonly KeyBinding Binding;
        readonly List<string> parts;
        readonly string action;
        bool hover;
        public bool First;
        public event EventHandler EditClicked, DeleteClicked;
        readonly FluentButton edit, del;

        public RemapRow(KeyBinding b, string actionText)
        {
            Binding = b;
            parts = KeyCaps.Parts(b.Mods, b.Vk);
            action = actionText;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.CardBg;
            Cursor = Cursors.Hand;
            edit = new FluentButton("", ButtonKind.Subtle, Theme.IcEdit, (s, e) => EditClicked?.Invoke(this, EventArgs.Empty));
            del = new FluentButton("", ButtonKind.Subtle, Theme.IcDelete, (s, e) => DeleteClicked?.Invoke(this, EventArgs.Empty));
            Controls.Add(edit);
            Controls.Add(del);
            edit.MouseLeave += (s, e) => SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
            del.MouseLeave += (s, e) => SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
            Height = Theme.S(56);
            Click += (s, e) => EditClicked?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (del == null || edit == null) return; // layout can run from the constructor before the buttons exist
            int y = (Height - del.Height) / 2;
            del.Location = new Point(Width - Theme.S(16) - del.Width, y);
            edit.Location = new Point(del.Left - Theme.S(4) - edit.Width, y);
        }

        void SetHover(bool h)
        {
            if (hover == h) return;
            hover = h;
            BackColor = h ? Theme.Blend(Theme.CardBg, Theme.SubtleHover, 0.6f) : Theme.CardBg;
            Invalidate(true);
        }

        protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e)
        {
            // moving onto the edit/delete buttons still counts as being over the row
            SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (!First) using (var p = new Pen(Theme.Divider)) g.DrawLine(p, 0, 0, Width, 0);
            int x = Theme.S(56);
            int w = KeyCaps.Draw(g, parts, x, Height / 2);
            x += Math.Max(w, Theme.S(150)) + Theme.S(16);
            Theme.Glyph(g, Theme.IcRight, 9f, Theme.Muted, new Rectangle(x, 0, Theme.S(16), Height));
            x += Theme.S(30);
            TextRenderer.DrawText(g, action, Theme.Body, new Rectangle(x, 0, Math.Max(0, edit.Left - x - Theme.S(8)), Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    sealed class RemapList : Panel
    {
        public event Action<KeyBinding> Edit, Delete;
        int rows;

        public RemapList()
        {
            Margin = Padding.Empty;
            BackColor = Theme.CardBg;
            DoubleBuffered = true;
            Height = Theme.S(64);
        }

        public void SetItems(IEnumerable<KeyValuePair<KeyBinding, string>> items)
        {
            SuspendLayout();
            foreach (Control c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); c.Dispose(); }
            var list = items.ToList();
            rows = list.Count;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var row = new RemapRow(list[i].Key, list[i].Value) { Dock = DockStyle.Top, First = false };
                row.EditClicked += (s, e) => Edit?.Invoke(((RemapRow)s).Binding);
                row.DeleteClicked += (s, e) => Delete?.Invoke(((RemapRow)s).Binding);
                Controls.Add(row);
            }
            Height = rows == 0 ? Theme.S(64) : rows * Theme.S(56) + 1;
            ResumeLayout();
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposed) =>
            new Size(proposed.Width > 0 && proposed.Width < 10000 ? proposed.Width : Width, rows == 0 ? Theme.S(64) : rows * Theme.S(56) + 1);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (rows > 0) return;
            using (var p = new Pen(Theme.Divider)) e.Graphics.DrawLine(p, 0, 0, Width, 0);
            TextRenderer.DrawText(e.Graphics, "No keys remapped yet. Click Add key, then press the key you want to change.", Theme.Caption,
                new Rectangle(Theme.S(56), 0, Width - Theme.S(72), Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
    }
}
