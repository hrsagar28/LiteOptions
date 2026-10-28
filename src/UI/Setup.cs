using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using LiteOptions.Input;

namespace LiteOptions.UI
{
    /// <summary>Tray icon with a small battery gauge in the corner.</summary>
    static class TrayBadge
    {
        public static Bitmap Compose(Icon baseIcon, double fraction, int bucket)
        {
            int size = Math.Max(16, baseIcon.Width);
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawIcon(baseIcon, new Rectangle(0, 0, size, size));
                float w = size * 0.70f, h = size * 0.42f;
                var r = new RectangleF(size - w, size - h, w - 0.5f, h - 0.5f);
                using (var path = Theme.Rounded(r, h * 0.28f))
                using (var b = new SolidBrush(Color.FromArgb(235, 28, 28, 30)))
                    g.FillPath(b, path);
                Color fill = bucket == 2 ? Color.FromArgb(255, 255, 77, 77) : bucket == 1 ? Color.FromArgb(255, 255, 185, 0) : Color.FromArgb(255, 108, 203, 95);
                float inset = Math.Max(1.5f, size * 0.085f);
                float nub = Math.Max(1f, size * 0.05f);
                var inner = new RectangleF(r.X + inset, r.Y + inset, r.Width - inset * 2 - nub, r.Height - inset * 2);
                using (var b = new SolidBrush(Color.FromArgb(255, 70, 70, 74))) g.FillRectangle(b, inner);
                var level = new RectangleF(inner.X, inner.Y, inner.Width * (float)Math.Max(0.12, Math.Min(1, fraction)), inner.Height);
                using (var b = new SolidBrush(fill)) g.FillRectangle(b, level);
                using (var b = new SolidBrush(Color.FromArgb(255, 200, 200, 204)))
                    g.FillRectangle(b, inner.Right + nub * 0.3f, inner.Y + inner.Height * 0.25f, nub, inner.Height * 0.5f);
            }
            return bmp;
        }
    }

    /// <summary>Large selectable option with icon, title and description.</summary>
    sealed class ChoiceCard : FluentControl
    {
        readonly string glyph, title, desc;
        bool selected;
        public event EventHandler Chosen;
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }

        public ChoiceCard(string glyph, string title, string desc)
        {
            this.glyph = glyph; this.title = title; this.desc = desc;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Height = Theme.S(84);
            Margin = Theme.P(0, 0, 0, 8);
        }

        protected override void OnClick(EventArgs e) { Focus(); Chosen?.Invoke(this, EventArgs.Empty); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Chosen?.Invoke(this, EventArgs.Empty); base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            var fill = selected ? Theme.Blend(Theme.ControlBg, Theme.Accent, Theme.Dark ? 0.12f : 0.06f) : Hover ? Theme.ControlHover : Theme.ControlBg;
            Theme.FillRounded(g, fill, r, Theme.Sf(7), selected ? (Color?)null : Theme.ControlBorder);
            if (selected)
                using (var p = new Pen(Theme.Accent, Theme.Sf(2)))
                using (var path = Theme.Rounded(r, Theme.Sf(7))) g.DrawPath(p, path);
            Theme.Glyph(g, glyph, 16f, selected ? Theme.Accent : Theme.Text, new Rectangle(Theme.S(14), 0, Theme.S(40), Height));
            int x = Theme.S(66);
            TextRenderer.DrawText(g, title, Theme.BodyStrong, new Rectangle(x, Theme.S(14), Width - x - Theme.S(44), Theme.S(22)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, desc, Theme.Caption, new Rectangle(x, Theme.S(38), Width - x - Theme.S(44), Height - Theme.S(42)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
            // radio mark
            float cx = Width - Theme.Sf(26), cy = Height / 2f, rr = Theme.Sf(9);
            using (var p = new Pen(selected ? Theme.Accent : Theme.TrackOff, Theme.Sf(1.4f))) g.DrawEllipse(p, cx - rr, cy - rr, rr * 2, rr * 2);
            if (selected) using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, cx - rr * 0.5f, cy - rr * 0.5f, rr, rr);
            FocusRing(g, r, Theme.Sf(7));
        }
    }

    /// <summary>Three short steps shown once: Options+ check, top-row mode, middle button.</summary>
    sealed class SetupDialog : Form
    {
        readonly TrayApp app;
        readonly List<Panel> pages = new List<Panel>();
        int page;
        readonly Label stepLabel, title;
        readonly FluentButton next, back, skip;
        readonly Panel host;
        string fnChoice, middleChoice;
        ActionPicker middlePicker;
        SettingRow optionsRow;

        public SetupDialog(TrayApp app)
        {
            this.app = app;
            Theme.Init();
            Text = "Set up LiteOptions";
            Font = Theme.Body;
            BackColor = Theme.CardBg;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(600), Theme.S(560));
            Icon = TrayApp.AppIcon;
            HandleCreated += (s, e) => Theme.ApplyWindowChrome(this);

            fnChoice = app.Settings.FnMode == "unchanged" ? "standard" : app.Settings.FnMode;
            middleChoice = app.Settings.Global.MiddleMode == "default" ? "gestures" : app.Settings.Global.MiddleMode;

            var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(72), BackColor = Theme.WindowBg, Padding = Theme.P(24, 18, 24, 18) };
            skip = new FluentButton("Skip setup", ButtonKind.Subtle, null, (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            back = new FluentButton("Back", ButtonKind.Standard, null, (s, e) => Go(page - 1)) { MinWidth = Theme.S(100) };
            next = new FluentButton("Next", ButtonKind.Primary, null, (s, e) => { if (page == 2) Finish(); else Go(page + 1); }) { MinWidth = Theme.S(110) };
            skip.Location = new Point(Theme.S(24), Theme.S(20));
            footer.Controls.Add(skip);
            footer.Controls.Add(back);
            footer.Controls.Add(next);
            footer.Resize += (s, e) =>
            {
                next.Location = new Point(footer.Width - Theme.S(24) - next.Width, Theme.S(20));
                back.Location = new Point(next.Left - Theme.S(8) - back.Width, Theme.S(20));
            };

            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(96), BackColor = Theme.CardBg, Padding = Theme.P(28, 22, 28, 0) };
            stepLabel = UIx.Label("", Theme.CaptionStrong, Theme.Accent);
            stepLabel.Location = new Point(Theme.S(28), Theme.S(22));
            title = UIx.Label("", Theme.Subtitle);
            title.Location = new Point(Theme.S(28), Theme.S(44));
            head.Controls.Add(stepLabel);
            head.Controls.Add(title);

            host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.CardBg, Padding = Theme.P(28, 4, 28, 12) };
            Controls.Add(host);
            Controls.Add(head);
            Controls.Add(footer);

            BuildPages();
            Go(0);
        }

        Panel NewPage()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.CardBg, Visible = false };
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Theme.CardBg };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.Controls.Add(stack);
            p.Tag = stack;
            host.Controls.Add(p);
            pages.Add(p);
            return p;
        }

        static T Put<T>(Panel page, T c, Padding? margin = null) where T : Control
        {
            var stack = (TableLayoutPanel)page.Tag;
            if (margin.HasValue) c.Margin = margin.Value;
            if (!(c is Label)) c.Dock = DockStyle.Fill;
            stack.Controls.Add(c);
            return c;
        }

        static Label Para(string text)
        {
            var l = UIx.Label(text, Theme.Body, Theme.Muted);
            l.MaximumSize = new Size(Theme.S(540), 0);
            return l;
        }

        void BuildPages()
        {
            // 1 — welcome + Options+ check
            var p1 = NewPage();
            Put(p1, Para("LiteOptions replaces Logi Options+ for your Pebble mouse and K380 keyboard. It works offline, needs no account and stays light. Three quick choices and you're done."), Theme.P(0, 0, 0, 16));
            var card = new CardPanel();
            optionsRow = card.Add(new SettingRow(Theme.IcCheck, "", "", new FluentButton("Installed apps", ButtonKind.Standard, null, (s, e) => { try { System.Diagnostics.Process.Start("ms-settings:appsfeatures"); } catch { } })));
            Put(p1, card);
            Put(p1, new FluentButton("Check again", ButtonKind.Subtle, Theme.IcRefresh, (s, e) => CheckOptions()), Theme.P(0, 8, 0, 0)).Dock = DockStyle.None;
            CheckOptions();

            // 2 — top row
            var p2 = NewPage();
            Put(p2, Para("What should the K380's top row do when you press a key on its own? The other set is always one Fn press away."), Theme.P(0, 0, 0, 14));
            var media = Put(p2, new ChoiceCard(Theme.IcKeyboard, "Media keys first", "Volume, play/pause and the like. Hold Fn for F1–F12. This is how the keyboard comes."));
            var fkeys = Put(p2, new ChoiceCard(Theme.IcKeyboard, "F1–F12 first", "Best for Excel and Tally: F2, F4 and the rest work directly. Hold Fn for the media keys."));
            void pickFn(string v) { fnChoice = v; media.Selected = v == "media"; fkeys.Selected = v == "standard"; }
            media.Chosen += (s, e) => pickFn("media");
            fkeys.Chosen += (s, e) => pickFn("standard");
            pickFn(fnChoice);
            Put(p2, Para("You can switch any time by pressing Ctrl + Esc."), Theme.P(0, 8, 0, 0));

            // 3 — middle button
            var p3 = NewPage();
            Put(p3, Para("What should pressing the scroll wheel do?"), Theme.P(0, 0, 0, 14));
            var normal = Put(p3, new ChoiceCard(Theme.IcMouse, "Normal middle click", "Opens links in a new tab, closes tabs, and so on. Same as without LiteOptions."));
            var gest = Put(p3, new ChoiceCard(Theme.IcTouch, "Gestures", "Hold the wheel and nudge the mouse: up for Task View, down for the desktop, left and right to switch desktops. A plain click still middle-clicks."));
            var one = Put(p3, new ChoiceCard(Theme.IcEdit, "One action of my choice", "For example Back, Copy, or a keyboard shortcut."));
            middlePicker = Put(p3, new ActionPicker("Middle click", true, () => app.Hooks), Theme.P(0, 4, 0, 0));
            middlePicker.Dock = DockStyle.None;
            middlePicker.Width = Theme.S(320);
            middlePicker.Value = app.Settings.Global.MiddleAction;
            void pickMid(string v)
            {
                middleChoice = v;
                normal.Selected = v == "default"; gest.Selected = v == "gestures"; one.Selected = v == "action";
                middlePicker.Visible = v == "action";
            }
            normal.Chosen += (s, e) => pickMid("default");
            gest.Chosen += (s, e) => pickMid("gestures");
            one.Chosen += (s, e) => pickMid("action");
            pickMid(middleChoice);
        }

        void CheckOptions()
        {
            var logi = SystemTweaks.RunningLogiProcesses();
            var icon = (GlyphBox)optionsRow.Controls[0];
            if (logi.Count == 0)
            {
                optionsRow.Title.Text = "Logi Options+ isn't running";
                optionsRow.Description = "Good. Nothing will compete with LiteOptions for your devices.";
                icon.Glyph = Theme.IcCheck;
            }
            else
            {
                optionsRow.Title.Text = "Logi Options+ is still running";
                optionsRow.Description = "Uninstall it from Installed apps, otherwise the two will fight over your mouse and keyboard.";
                icon.Glyph = Theme.IcWarning;
            }
            icon.Invalidate();
        }

        void Go(int i)
        {
            page = Math.Max(0, Math.Min(2, i));
            for (int k = 0; k < pages.Count; k++) pages[k].Visible = k == page;
            stepLabel.Text = $"STEP {page + 1} OF 3";
            title.Text = page == 0 ? "Welcome to LiteOptions" : page == 1 ? "The top row of your K380" : "The middle button";
            back.Visible = page > 0;
            next.Text = page == 2 ? "Finish" : "Next";
            next.Location = new Point(next.Parent.Width - Theme.S(24) - next.Width, Theme.S(20));
            back.Location = new Point(next.Left - Theme.S(8) - back.Width, Theme.S(20));
        }

        void Finish()
        {
            var s = app.Settings;
            s.FnMode = fnChoice;
            s.Global.MiddleMode = middleChoice;
            if (middleChoice == "action") s.Global.MiddleAction = middlePicker.Value;
            s.SetupDone = true;
            app.Commit();
            app.Devices.SetFnMode(fnChoice == "media");
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
