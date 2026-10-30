using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LiteOptions.Hid;
using LiteOptions.Input;

namespace LiteOptions.UI
{
    sealed class MainForm : Form
    {
        readonly TrayApp app;
        bool loading;
        Profile current;
        public bool AllowClose;

        // shell
        NavItem navMouse, navKeys, navSettings;
        Label pageTitle;
        // added in 2.2 so the classic window (the fallback when the new one can't start) has the same settings
        ToggleSwitch smooth, lockPopups;
        Control mouseHostsHeader, kbHostsHeader, keysOffHeader;
        CardPanel mouseHostsCard, kbHostsCard, keysOffCard;
        SettingRow[] mouseHostRows, kbHostRows;
        readonly List<(int bit, SettingRow row, ToggleSwitch sw)> offRows = new List<(int, SettingRow, ToggleSwitch)>();
        DropDown profileBox;
        FlowLayoutPanel profileBar;
        Panel pageMouse, pageKeys, pageSettings;
        SidebarStatus sideStatus;

        // mouse page
        DeviceHero mouseHero;
        Segmented middleMode;
        SettingRow rowMode, rowAction, rowGClick, rowGUp, rowGDown, rowGLeft, rowGRight, rowDpi;
        ActionPicker middleAction, gClick, gUp, gDown, gLeft, gRight;
        Control pointerHeader, scrollHeader, appNoteMouse;
        CardPanel middleCard, pointerCard, scrollCard;
        Slider dpiSlider, speedSlider, linesSlider;
        Label dpiValue, speedValue, linesValue;
        FluentButton dpiReset;
        ToggleSwitch precision, reverse;
        Timer dpiDebounce;
        int dpiMin = 400, dpiStep = 100;

        // keyboard page
        DeviceHero kbHero;
        Segmented fnMode;
        NoteRow fnNote;
        Control fnHeader, appNoteKeys;
        CardPanel fnCard, remapCard;
        SettingRow rowRemap;
        RemapList remapList;

        // settings page
        ToggleSwitch startup, paused, notify, popups;
        DropDown lowBatt;
        SettingRow rowOptions;
        TextField diag;
        Panel diagWrap;
        FluentButton diagToggle;

        public MainForm(TrayApp app)
        {
            this.app = app;
            Theme.Init();
            Theme.ApplyDpi(Theme.DpiAtCursor());
            Text = "LiteOptions";
            Font = Theme.Body;
            BackColor = Theme.WindowBg;
            ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(Theme.S(860), Theme.S(560));
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int w = Math.Min(Theme.S(1040), wa.Width - Theme.S(40)), h = Math.Min(Theme.S(740), wa.Height - Theme.S(40));
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, w, h);
            Icon = TrayApp.AppIcon;
            DoubleBuffered = true;

            BuildAll();
            ReloadProfiles("");
            ShowPage(pageMouse);
            RefreshDevices();
            HandleCreated += (s, e) => Theme.ApplyWindowChrome(this);
        }

        void BuildAll()
        {
            SuspendLayout();
            offRows.Clear(); // rebuilt on a DPI change
            BuildShell();
            BuildMousePage();
            BuildKeysPage();
            BuildSettingsPage();
            ResumeLayout();
        }

        /// <summary>Moved to a monitor with a different scale: rebuild the whole window at the new size, keeping its state.</summary>
        void Rebuild(int dpi)
        {
            string pageName = pageKeys.Visible ? "keys" : pageSettings.Visible ? "settings" : "mouse";
            string profile = current?.App ?? "";
            SuspendLayout();
            dpiDebounce?.Dispose();
            var old = Controls.Cast<Control>().ToList();
            Controls.Clear();
            foreach (var c in old) c.Dispose();
            Theme.ApplyDpi(dpi);
            MinimumSize = new Size(Theme.S(860), Theme.S(560));
            BuildAll();
            ResumeLayout();
            ReloadProfiles(profile);
            ShowPageByName(pageName);
            RefreshDevices();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED)
            {
                int dpi = (int)((long)m.WParam & 0xFFFF);
                var r = (Native.RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                Bounds = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                if (dpi != Theme.CurrentDpi) Rebuild(dpi);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Jump from a spot on the device picture to the matching settings card.</summary>
        void JumpTo(string id)
        {
            Panel page; CardPanel card;
            switch (id)
            {
                case "middle": page = pageMouse; card = middleCard; break;
                case "pointer": page = pageMouse; card = pointerCard.Visible ? pointerCard : middleCard; break;
                case "toprow": page = pageKeys; card = fnCard.Visible ? fnCard : remapCard; break;
                default: page = pageKeys; card = remapCard; break;
            }
            ShowPage(page);
            page.AutoScrollPosition = new Point(0, Math.Max(0, card.Top - Theme.S(44)));
            card.Flash();
        }

        // ───────────────────────── shell ─────────────────────────

        void BuildShell()
        {
            var sidebar = new Panel { Dock = DockStyle.Left, Width = Theme.S(264), BackColor = Theme.WindowBg, Padding = Theme.P(10, 10, 10, 14) };
            var brand = new Panel { Dock = DockStyle.Top, Height = Theme.S(70), BackColor = Theme.WindowBg };
            var logo = new PictureBox { Image = TrayApp.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(Theme.S(30), Theme.S(30)), Location = new Point(Theme.S(14), Theme.S(14)), BackColor = Theme.WindowBg };
            var title = UIx.Label("LiteOptions", Theme.Subtitle);
            title.Location = new Point(Theme.S(54), Theme.S(17));
            brand.Controls.Add(logo);
            brand.Controls.Add(title);

            navSettings = new NavItem("Settings", Theme.IcSettings);
            navKeys = new NavItem("Keyboard", Theme.IcKeyboard);
            navMouse = new NavItem("Mouse", Theme.IcMouse);
            navMouse.Click += (s, e) => ShowPage(pageMouse);
            navKeys.Click += (s, e) => ShowPage(pageKeys);
            navSettings.Click += (s, e) => ShowPage(pageSettings);
            sideStatus = new SidebarStatus(app) { Dock = DockStyle.Bottom };

            sidebar.Controls.Add(navSettings);
            sidebar.Controls.Add(navKeys);
            sidebar.Controls.Add(navMouse);
            sidebar.Controls.Add(brand);
            sidebar.Controls.Add(sideStatus);

            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.WindowBg, Padding = Theme.P(28, 12, 18, 0) };
            var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(74), BackColor = Theme.WindowBg };
            pageTitle = UIx.Label("Mouse", Theme.PageTitle);
            pageTitle.Location = new Point(0, Theme.S(10));

            var applies = UIx.Label("Applies to", Theme.Body, Theme.Muted);
            applies.Margin = Theme.P(0, 8, 10, 0);
            profileBox = new DropDown { Width = Theme.S(240), MenuBuilder = BuildProfileMenu };
            profileBar = UIx.Row(applies, profileBox);
            header.Controls.Add(pageTitle);
            header.Controls.Add(profileBar);
            void place() => profileBar.Location = new Point(header.Width - profileBar.Width - Theme.S(14), Theme.S(18));
            header.Resize += (s, e) => place();
            profileBar.SizeChanged += (s, e) => place();

            pageMouse = Page(); pageKeys = Page(); pageSettings = Page();
            content.Controls.Add(pageMouse);
            content.Controls.Add(pageKeys);
            content.Controls.Add(pageSettings);
            content.Controls.Add(header);

            Controls.Add(content);
            Controls.Add(sidebar);
        }

        static Panel Page()
        {
            var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.WindowBg, Visible = false, Padding = Theme.P(0, 0, 14, 28) };
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Theme.WindowBg, Margin = Padding.Empty };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.Controls.Add(stack);
            p.Tag = stack;
            Theme.DarkScrollbars(p);
            return p;
        }

        static T Add<T>(Panel page, T c) where T : Control
        {
            var stack = (TableLayoutPanel)page.Tag;
            int row = stack.RowCount++;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (!(c is Label)) { c.Dock = DockStyle.Top; c.Margin = Padding.Empty; }
            stack.Controls.Add(c, 0, row);
            return c;
        }

        static CardPanel NoteCard(string text, string glyph = null)
        {
            var c = new CardPanel();
            c.Margin = Theme.P(0, 14, 0, 0);
            c.Add(new NoteRow(text, glyph));
            return c;
        }

        void ShowPage(Panel page)
        {
            SuspendLayout();
            foreach (var p in new[] { pageMouse, pageKeys, pageSettings }) p.Visible = p == page;
            ResumeLayout();
            navMouse.Selected = page == pageMouse;
            navKeys.Selected = page == pageKeys;
            navSettings.Selected = page == pageSettings;
            pageTitle.Text = page == pageMouse ? "Mouse" : page == pageKeys ? "Keyboard" : "Settings";
            profileBar.Visible = page != pageSettings;
            if (page == pageSettings) RefreshSettingsPage();
        }

        internal void ShowPageByName(string name) => ShowPage(name == "keys" ? pageKeys : name == "settings" ? pageSettings : pageMouse);
        internal void ReloadAll() { ReloadProfiles(current?.App ?? ""); RefreshDevices(); }

        // ───────────────────────── profiles ─────────────────────────

        ContextMenuStrip BuildProfileMenu()
        {
            var m = Menus.Create();
            foreach (var p in app.Settings.Profiles)
            {
                var app0 = p.App;
                m.Items.Add(Menus.Item(p.IsGlobal ? "All applications" : p.App, () => ReloadProfiles(app0), p == current));
            }
            m.Items.Add(Menus.Separator());
            m.Items.Add(Menus.Item("Add an app…", () => BeginInvoke((Action)AddApp)));
            if (current != null && !current.IsGlobal)
                m.Items.Add(Menus.Item("Remove " + current.App, () => BeginInvoke((Action)RemoveApp)));
            return m;
        }

        void ReloadProfiles(string select)
        {
            current = app.Settings.Profiles.FirstOrDefault(p => p.App == (select ?? "")) ?? app.Settings.Global;
            profileBox.Text = current.IsGlobal ? "All applications" : current.App;
            LoadProfile();
        }

        void AddApp()
        {
            using (var d = new AddAppDialog(app.Settings.Profiles.Select(p => p.App)))
            {
                if (d.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(d.Exe)) return;
                if (app.Settings.ForApp(d.Exe) == null)
                {
                    var p = new Profile { App = d.Exe, MiddleMode = "inherit" };
                    p.Normalize();
                    app.Settings.Profiles.Add(p);
                    app.Settings.Normalize();
                    app.Commit();
                }
                ReloadProfiles(d.Exe);
            }
        }

        void RemoveApp()
        {
            if (current == null || current.IsGlobal) return;
            if (!Dialogs.Confirm(this, "Remove " + current.App + "?", "Its own middle-button and key settings are deleted. All applications settings stay as they are.", "Remove", danger: true)) return;
            app.Settings.Profiles.Remove(current);
            app.Commit();
            ReloadProfiles("");
        }

        List<string> ModeList() => current != null && !current.IsGlobal
            ? new List<string> { "inherit", "default", "action", "gestures" }
            : new List<string> { "default", "action", "gestures" };

        void LoadProfile()
        {
            loading = true;
            SuspendLayout();
            bool global = current.IsGlobal;
            if (global) middleMode.SetItems("Normal click", "One action", "Gestures");
            else middleMode.SetItems("Same as all apps", "Normal click", "One action", "Gestures");
            middleMode.SelectedIndex = Math.Max(0, ModeList().IndexOf(current.MiddleMode));
            middleAction.Value = current.MiddleAction;
            gClick.Value = current.GestureClick; gUp.Value = current.GestureUp; gDown.Value = current.GestureDown;
            gLeft.Value = current.GestureLeft; gRight.Value = current.GestureRight;
            UpdateMiddleVisibility();

            pointerHeader.Visible = pointerCard.Visible = scrollHeader.Visible = scrollCard.Visible = global;
            fnHeader.Visible = fnCard.Visible = global;
            appNoteMouse.Visible = appNoteKeys.Visible = !global;
            rowRemap.Title.Text = global ? "Remapped keys" : "Remapped keys in " + current.App;
            rowRemap.Description = global
                ? "Apply in every app, and to the laptop's own keyboard too. The K380's Easy-Switch keys and Fn work inside the keyboard and can't be changed."
                : "Used only while " + current.App + " is in front, on top of the All applications list. Choose \"Leave the key as it is\" to switch a global remap off here.";
            RefreshKeyList();
            UpdateDeviceSections();
            ResumeLayout();
            loading = false;
        }

        // ───────────────────────── mouse page ─────────────────────────

        void BuildMousePage()
        {
            mouseHero = Add(pageMouse, new DeviceHero(true));
            mouseHero.HotspotClicked += JumpTo;

            Add(pageMouse, UIx.Section("Middle button"));
            var card = middleCard = new CardPanel();
            middleMode = new Segmented();
            middleMode.SelectedIndexChanged += (s, e) =>
            {
                if (loading || middleMode.SelectedIndex < 0) return;
                current.MiddleMode = ModeList()[middleMode.SelectedIndex];
                UpdateMiddleVisibility();
                app.Commit();
            };
            rowMode = card.Add(new SettingRow(Theme.IcMouse, "Pressing the scroll wheel", "", middleMode));
            middleAction = Picker("Middle click", v => current.MiddleAction = v);
            rowAction = card.Add(new SettingRow(null, "Action", "Runs each time you press the wheel", middleAction));
            gClick = Picker("Middle click", v => current.GestureClick = v);
            gUp = Picker("Nothing", v => current.GestureUp = v, allowNothing: false);
            gDown = Picker("Nothing", v => current.GestureDown = v, allowNothing: false);
            gLeft = Picker("Nothing", v => current.GestureLeft = v, allowNothing: false);
            gRight = Picker("Nothing", v => current.GestureRight = v, allowNothing: false);
            rowGClick = card.Add(new SettingRow(Theme.IcTouch, "Click without moving", null, gClick));
            rowGUp = card.Add(new SettingRow(Theme.IcUp, "Hold and move up", null, gUp));
            rowGDown = card.Add(new SettingRow(Theme.IcDown, "Hold and move down", null, gDown));
            rowGLeft = card.Add(new SettingRow(Theme.IcLeft, "Hold and move left", null, gLeft));
            rowGRight = card.Add(new SettingRow(Theme.IcRight, "Hold and move right", null, gRight));
            Add(pageMouse, card);

            appNoteMouse = Add(pageMouse, NoteCard("Pointer and scrolling settings are shared by every app. Switch to All applications to change them."));

            pointerHeader = Add(pageMouse, UIx.Section("Pointer"));
            pointerCard = new CardPanel();
            dpiSlider = new Slider { Width = Theme.S(220), SmallChange = 1, LargeChange = 5 };
            dpiValue = ValueLabel(Theme.S(86));
            dpiReset = new FluentButton("", ButtonKind.Subtle, Theme.IcRefresh, (s, e) =>
            {
                var m = app.Devices.Mouse;
                SetDpiFromUi(m?.Dpi?.Default > 0 ? m.Dpi.Default : 1000);
            });
            dpiDebounce = new Timer { Interval = 150 };
            dpiDebounce.Tick += (s, e) => { dpiDebounce.Stop(); PushDpi(); };
            dpiSlider.ValueChanged += (s, e) => { dpiValue.Text = SliderDpi() + " DPI"; dpiDebounce.Stop(); dpiDebounce.Start(); };
            dpiSlider.ValueCommitted += (s, e) => { dpiDebounce.Stop(); PushDpi(); ShowDpiPopup(); };
            rowDpi = pointerCard.Add(new SettingRow(Theme.IcMouse, "Mouse sensitivity", "", UIx.Row(dpiSlider, dpiValue, dpiReset)));

            speedSlider = new Slider { Minimum = 1, Maximum = 20, Width = Theme.S(220) };
            speedValue = ValueLabel(Theme.S(86));
            speedSlider.ValueChanged += (s, e) => { speedValue.Text = speedSlider.Value.ToString(); SafeWin(() => SystemTweaks.SetPointerSpeed(speedSlider.Value)); };
            pointerCard.Add(new SettingRow(Theme.IcTouch, "Windows pointer speed", "Also changes the touchpad. Windows' default is 10.",
                UIx.Row(speedSlider, speedValue, new FluentButton("", ButtonKind.Subtle, Theme.IcRefresh, (s, e) => speedSlider.Value = 10))));

            precision = new ToggleSwitch();
            precision.CheckedChanged += (s, e) => { if (!loading) SafeWin(() => SystemTweaks.SetPrecision(precision.Checked)); };
            pointerCard.Add(new SettingRow(null, "Enhance pointer precision", "Faster hand movements carry the pointer further", precision));
            Add(pageMouse, pointerCard);

            scrollHeader = Add(pageMouse, UIx.Section("Scrolling"));
            scrollCard = new CardPanel();
            linesSlider = new Slider { Minimum = 1, Maximum = 20, Width = Theme.S(220) };
            linesValue = ValueLabel(Theme.S(86));
            linesSlider.ValueChanged += (s, e) => { linesValue.Text = linesSlider.Value + (linesSlider.Value == 1 ? " line" : " lines"); SafeWin(() => SystemTweaks.SetWheelLines(linesSlider.Value)); };
            scrollCard.Add(new SettingRow(null, "Scroll distance", "Lines moved by one click of the wheel. Windows' default is 3.",
                UIx.Row(linesSlider, linesValue, new FluentButton("", ButtonKind.Subtle, Theme.IcRefresh, (s, e) => linesSlider.Value = 3))));
            smooth = new ToggleSwitch();
            smooth.CheckedChanged += (s, e) => { if (!loading) { app.Settings.SmoothScroll = smooth.Checked; app.Commit(); } };
            scrollCard.Add(new SettingRow(null, "Smooth scrolling", "Each click of the wheel glides instead of jumping. Works in browsers, Office and most apps; if an older app stops scrolling, switch this off.", smooth));
            reverse = new ToggleSwitch();
            reverse.CheckedChanged += (s, e) => { if (!loading) BeginInvoke((Action)ToggleReverse); };
            scrollCard.Add(new SettingRow(null, "Reverse scroll direction", "This mouse only. Windows asks for permission once; then switch the mouse off and on.", reverse));
            Add(pageMouse, scrollCard);

            mouseHostsHeader = Add(pageMouse, UIx.Section("Easy-Switch"));
            mouseHostsCard = new CardPanel();
            mouseHostRows = HostRows(mouseHostsCard, "The button underneath the mouse changes channel.");
            Add(pageMouse, mouseHostsCard);
        }

        /// <summary>Three rows for the Easy-Switch channels and a note on how to switch.</summary>
        static SettingRow[] HostRows(CardPanel card, string note)
        {
            var rows = new SettingRow[3];
            for (int i = 0; i < 3; i++) rows[i] = card.Add(new SettingRow(Theme.IcSync, "Channel " + (i + 1), "", null));
            card.Add(new NoteRow(note));
            return rows;
        }

        void FillHosts(Control header, CardPanel card, SettingRow[] rows, LogiDevice d, bool mouse)
        {
            var h = d?.Hosts;
            bool show = h != null && h.Hosts.Count > 0 && (current == null || current.IsGlobal);
            header.Visible = card.Visible = show;
            if (!show) return;
            for (int i = 0; i < rows.Length; i++)
            {
                var info = h.Hosts.FirstOrDefault(x => x.Index == i);
                rows[i].Visible = info != null;
                if (info == null) continue;
                bool here = i == h.Current;
                string name = info.Name ?? "";
                rows[i].Title.Text = (i + 1) + "   " + (name.Length > 0 ? name : here ? "This computer" : info.Unknown ? "Channel " + (i + 1) : info.Paired ? "Paired device" : "Nothing paired");
                rows[i].Description = here ? "This computer"
                    : info.Unknown ? "This device doesn't say what's paired here"
                    : !info.Paired ? (mouse ? "Hold the button underneath to pair" : "Hold F" + (i + 1) + " to pair")
                    : "Paired over Bluetooth";
            }
        }

        /// <summary>Easy-Switch and "turn off keys" follow the devices and the profile shown (they belong to All applications).</summary>
        void UpdateDeviceSections()
        {
            var m = app.Devices.Mouse;
            var k = app.Devices.Keyboard;
            FillHosts(mouseHostsHeader, mouseHostsCard, mouseHostRows, m, true);
            FillHosts(kbHostsHeader, kbHostsCard, kbHostRows, k, false);
            int caps = k?.DisableCaps ?? 0;
            bool showOff = caps != 0 && (current == null || current.IsGlobal);
            keysOffHeader.Visible = keysOffCard.Visible = showOff;
            int shown = app.Settings.DisabledKeys >= 0 ? app.Settings.DisabledKeys & caps : Math.Max(0, k?.DisabledKeys ?? 0);
            foreach (var o in offRows)
            {
                o.row.Visible = (caps & o.bit) != 0;
                o.sw.Checked = (shown & o.bit) != 0;
            }
            if (smooth != null) smooth.Checked = app.Settings.SmoothScroll;
        }

        void SetKeyOff(int bit, bool off)
        {
            var k = app.Devices.Keyboard;
            int mask = app.Settings.DisabledKeys >= 0 ? app.Settings.DisabledKeys : Math.Max(0, k?.DisabledKeys ?? 0);
            mask = off ? mask | bit : mask & ~bit;
            app.Settings.DisabledKeys = mask;
            app.Commit();
            app.Devices.ApplySettingsNow();
        }

        static Label ValueLabel(int width)
        {
            var l = UIx.Label("", Theme.BodyStrong);
            l.AutoSize = false;
            l.Size = new Size(width, Theme.S(32));
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = Theme.P(10, 0, 0, 0);
            return l;
        }


        ActionPicker Picker(string defaultLabel, Action<ActionSpec> setter, bool allowNothing = true)
        {
            var p = new ActionPicker(defaultLabel, allowNothing, () => app.Hooks);
            p.ActionChanged += (s, e) => { if (loading) return; setter(p.Value); app.Commit(); };
            return p;
        }

        void UpdateMiddleVisibility()
        {
            string mode = current?.MiddleMode ?? "default";
            rowAction.Visible = mode == "action";
            foreach (var r in new[] { rowGClick, rowGUp, rowGDown, rowGLeft, rowGRight }) r.Visible = mode == "gestures";
            rowMode.Description =
                mode == "inherit" ? "Uses the All applications setting" :
                mode == "default" ? "Works as a normal middle click" :
                mode == "action" ? "Does one thing of your choice" :
                "Hold the wheel down, nudge the mouse in a direction, then let go. The pointer stays still while you hold it.";
        }

        int SliderDpi() => dpiMin + dpiSlider.Value * dpiStep;

        void SetDpiFromUi(int dpi)
        {
            dpiSlider.Value = (dpi - dpiMin) / Math.Max(1, dpiStep);
            dpiValue.Text = SliderDpi() + " DPI";
            PushDpi();
            ShowDpiPopup();
        }

        void ShowDpiPopup()
        {
            var m = app.Devices.Mouse;
            double frac = dpiSlider.Maximum > 0 ? dpiSlider.Value / (double)dpiSlider.Maximum : 0;
            Osd.Show(Theme.IcMouse, SliderDpi() + " DPI", m == null ? "Mouse not connected" : "Mouse sensitivity", frac);
        }

        void PushDpi()
        {
            int dpi = SliderDpi();
            if (app.Settings.MouseDpi != dpi) { app.Settings.MouseDpi = dpi; app.Commit(); }
            app.Devices.SetDpi(dpi);
        }

        void ToggleReverse()
        {
            var m = app.Devices.Mouse;
            bool want = reverse.Checked;
            void revert() { loading = true; reverse.Checked = !want; loading = false; }
            if (m == null)
            {
                Dialogs.Info(this, "Turn the mouse on first", "LiteOptions needs to see the mouse to find its entry in Windows.");
                revert();
                return;
            }
            int code = SystemTweaks.RequestReverseScroll(m.Pid, want);
            if (code == 0)
            {
                app.Settings.ReverseScrollPid = want ? m.Pid : 0;
                app.Commit();
                Dialogs.Info(this, "Done", "Switch the mouse off and on (the switch underneath) and the new direction takes effect.");
            }
            else
            {
                if (code != -1) Dialogs.Info(this, "Couldn't change it", "Windows doesn't have a scroll setting for this mouse yet. Try again after it has been connected for a minute.");
                revert();
            }
        }

        static void SafeWin(Action a) { try { a(); } catch (Exception ex) { Logger.Write("system setting failed: " + ex.Message); } }

        // ───────────────────────── keyboard page ─────────────────────────

        void BuildKeysPage()
        {
            kbHero = Add(pageKeys, new DeviceHero(false));
            kbHero.HotspotClicked += JumpTo;

            fnHeader = Add(pageKeys, UIx.Section("Top row"));
            fnCard = new CardPanel();
            fnMode = new Segmented();
            fnMode.SetItems("Media keys", "F1–F12", "Leave as is");
            fnMode.SelectedIndexChanged += (s, e) =>
            {
                if (loading) return;
                string mode = fnMode.SelectedIndex == 0 ? "media" : fnMode.SelectedIndex == 1 ? "standard" : "unchanged";
                app.Settings.FnMode = mode;
                app.Commit();
                if (mode != "unchanged") app.Devices.SetFnMode(mode == "media");
                RefreshDevices();
            };
            fnCard.Add(new SettingRow(Theme.IcKeyboard, "What the top row does", "Without holding Fn. The other set is always one Fn press away.", fnMode));
            fnNote = fnCard.Add(new NoteRow(""));
            Add(pageKeys, fnCard);
            appNoteKeys = Add(pageKeys, NoteCard("The top-row setting is shared by every app. Switch to All applications to change it."));

            keysOffHeader = Add(pageKeys, UIx.Section("Turn off keys"));
            keysOffCard = new CardPanel();
            foreach (var (bit, name) in new[] { (0x01, "Caps Lock"), (0x08, "Insert"), (0x10, "Windows key"), (0x04, "Scroll Lock"), (0x02, "Num Lock") })
            {
                var sw = new ToggleSwitch();
                int b = bit;
                sw.CheckedChanged += (s, e) => { if (!loading) SetKeyOff(b, sw.Checked); };
                offRows.Add((bit, keysOffCard.Add(new SettingRow(Theme.IcKeyboard, "Turn off " + name, null, sw)), sw));
            }
            keysOffCard.Add(new NoteRow("Switched off inside the keyboard, so it applies everywhere. The laptop's own keys keep working."));
            Add(pageKeys, keysOffCard);

            Add(pageKeys, UIx.Section("Key remapping"));
            var card = remapCard = new CardPanel();
            rowRemap = card.Add(new SettingRow(Theme.IcEdit, "Remapped keys", "", new FluentButton("Add key", ButtonKind.Primary, Theme.IcAdd, (s, e) => AddKey())));
            remapList = card.Add(new RemapList());
            remapList.Edit += b => ChangeKey(b);
            remapList.Delete += b => { current.Keys.Remove(b); app.Commit(); RefreshKeyList(); };
            card.Add(new NoteRow("Tip: to change a K380 media key, click Add key and press it exactly as you normally would."));
            Add(pageKeys, card);

            kbHostsHeader = Add(pageKeys, UIx.Section("Easy-Switch"));
            kbHostsCard = new CardPanel();
            kbHostRows = HostRows(kbHostsCard, "The F1, F2 and F3 keys change channel.");
            Add(pageKeys, kbHostsCard);
        }

        void RefreshKeyList()
        {
            remapList.SetItems(current.Keys.OrderBy(k => k.Vk).ThenBy(k => k.Mods)
                .Select(b => new KeyValuePair<KeyBinding, string>(b, Presets.Describe(b.Action, "Leave the key as it is"))));
            ((Control)remapList.Parent)?.PerformLayout();
        }

        static readonly HashSet<int> typingKeys = new HashSet<int> { Vk.Space, Vk.Enter, Vk.Back, Vk.Tab, Vk.Delete, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE };

        void AddKey()
        {
            int mods, vk;
            using (var cap = new CaptureDialog("Choose a key", "Press the key, or key combination, you want to change.", app.Hooks))
            {
                if (cap.ShowDialog(this) != DialogResult.OK) return;
                mods = cap.Mods; vk = cap.Vk;
            }
            bool typing = mods == 0 && ((vk >= 0x30 && vk <= 0x5A) || typingKeys.Contains(vk));
            if (typing && !Dialogs.Confirm(this, KeyNames.Key(vk) + " is a typing key", "If you remap it, it will stop typing " + KeyNames.Key(vk) + ". Continue?", "Continue")) return;

            var existing = current.Keys.FirstOrDefault(k => k.Mods == mods && k.Vk == vk);
            using (var d = new ChooseActionDialog(mods, vk, existing?.Action, () => app.Hooks))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (existing != null) existing.Action = d.Value;
                else current.Keys.Add(new KeyBinding { Mods = mods, Vk = vk, Action = d.Value });
            }
            app.Commit();
            RefreshKeyList();
        }

        void ChangeKey(KeyBinding b)
        {
            using (var d = new ChooseActionDialog(b.Mods, b.Vk, b.Action, () => app.Hooks))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                b.Action = d.Value;
            }
            app.Commit();
            RefreshKeyList();
        }

        // ───────────────────────── settings page ─────────────────────────

        void BuildSettingsPage()
        {
            Add(pageSettings, UIx.Section("General"));
            var gen = new CardPanel();
            startup = new ToggleSwitch();
            startup.CheckedChanged += (s, e) => { if (!loading) SafeWin(() => SystemTweaks.SetStartup(startup.Checked)); };
            gen.Add(new SettingRow(Theme.IcPower, "Start with Windows", "LiteOptions waits quietly in the tray after you sign in", startup));
            paused = new ToggleSwitch();
            paused.CheckedChanged += (s, e) => { if (!loading) { app.Settings.Paused = paused.Checked; app.Commit(); } };
            gen.Add(new SettingRow(Theme.IcPause, "Pause remapping", "Buttons and keys work as normal until you switch this off", paused));
            notify = new ToggleSwitch();
            notify.CheckedChanged += (s, e) => { if (!loading) { app.Settings.Notifications = notify.Checked; app.Commit(); lowBatt.Enabled = notify.Checked; } };
            lowBatt = new DropDown { Width = Theme.S(120) };
            lowBatt.SetItems(new[] { 10, 15, 20, 25, 30 }.Select(v => new KeyValuePair<string, object>("At " + v + "%", v)));
            lowBatt.Picked += v => { app.Settings.LowBatteryPercent = (int)v; app.Commit(); };
            var lb = UIx.Row(lowBatt, notify);
            lowBatt.Margin = Theme.P(0, 0, 16, 0);
            gen.Add(new SettingRow(Theme.IcBattery, "Low battery warning", "A notification when the mouse or keyboard battery runs low", lb));
            popups = new ToggleSwitch();
            popups.CheckedChanged += (s, e) =>
            {
                if (loading) return;
                app.Settings.ShowPopups = popups.Checked;
                app.Commit();
                if (popups.Checked) Osd.Show(Theme.IcCheck, "Pop-ups on", "Shown when you switch the top row, change DPI or use a gesture");
            };
            gen.Add(new SettingRow(Theme.IcInfo, "On-screen pop-ups", "A small note at the bottom of the screen when you switch the top row, change DPI or use a gesture", popups));
            lockPopups = new ToggleSwitch();
            lockPopups.CheckedChanged += (s, e) => { if (!loading) { app.Settings.LockKeyPopups = lockPopups.Checked; app.Commit(); } };
            gen.Add(new SettingRow(null, "Caps Lock and Num Lock pop-ups", "Show a pop-up when Caps Lock, Num Lock or Scroll Lock is switched on or off", lockPopups));
            gen.Add(new SettingRow(Theme.IcSettings, "Setup", "Go through the three first-run choices again",
                new FluentButton("Run setup", ButtonKind.Standard, null, (s, e) =>
                {
                    using (var d = new SetupDialog(app)) d.ShowDialog(this);
                    ReloadAll();
                })));
            Add(pageSettings, gen);

            Add(pageSettings, UIx.Section("Logi Options+"));
            var opt = new CardPanel();
            rowOptions = opt.Add(new SettingRow(Theme.IcCheck, "", "", new FluentButton("Installed apps", ButtonKind.Standard, null, (s, e) => { try { Process.Start("ms-settings:appsfeatures"); } catch { } })));
            opt.Add(new NoteRow("Firmware updates for Logitech devices only come through Options+. They're rare and optional. If you ever want one: install Options+, let it update, then uninstall it — the firmware stays on the device."));
            Add(pageSettings, opt);

            Add(pageSettings, UIx.Section("About"));
            var about = new CardPanel();
            about.Add(new SettingRow(Theme.IcInfo, "LiteOptions " + Installer.VersionText,
                "To update, download the new LiteOptions.exe and double-click it. Your settings are kept. No account, no internet, no telemetry.",
                new FluentButton("Settings folder", ButtonKind.Standard, Theme.IcFolder, (s, e) =>
                {
                    try { System.IO.Directory.CreateDirectory(AppSettings.Folder); Process.Start("explorer.exe", AppSettings.Folder); } catch { }
                })));
            about.Add(new SettingRow(Theme.IcDelete, "Uninstall LiteOptions", "Removes the app, its settings and its startup entry",
                new FluentButton("Uninstall", ButtonKind.Danger, null, (s, e) => Uninstall())));
            Add(pageSettings, about);

            Add(pageSettings, UIx.Section("Troubleshooting"));
            var dg = new CardPanel();
            diagToggle = new FluentButton("Show details", ButtonKind.Standard, null, (s, e) =>
            {
                diagWrap.Visible = !diagWrap.Visible;
                diagToggle.Text = diagWrap.Visible ? "Hide details" : "Show details";
                if (diagWrap.Visible) RefreshSettingsPage();
            });
            dg.Add(new SettingRow(Theme.IcDiagnostic, "Diagnostics", "What LiteOptions found and did. If something isn't working, copy this and send it to Claude.",
                UIx.Row(
                    new FluentButton("Copy", ButtonKind.Standard, Theme.IcCopy, (s, e) => { RefreshSettingsPage(); try { Clipboard.SetText(DiagnosticsText()); } catch { } }),
                    new FluentButton("Re-scan", ButtonKind.Standard, Theme.IcRefresh, (s, e) => { app.Devices.RequestScan(10); app.Devices.RequestPoll(); }),
                    diagToggle)));
            diagWrap = new Panel { Height = Theme.S(260), Padding = Theme.P(16, 4, 16, 16), Visible = false, BackColor = Color.Transparent };
            diag = new TextField(multiline: true, height: 240) { Dock = DockStyle.Fill };
            diag.Box.ReadOnly = true;
            diag.Box.Font = Theme.Mono;
            diag.Box.WordWrap = false;
            diag.Box.ScrollBars = ScrollBars.Both;
            diagWrap.Controls.Add(diag);
            dg.Add(diagWrap);
            Add(pageSettings, dg);
        }

        void Uninstall()
        {
            if (!Dialogs.Confirm(this, "Uninstall LiteOptions?",
                    "This removes LiteOptions, its settings and its startup entry. Your mouse and keyboard keep working; they go back to their defaults the next time they're switched off and on.",
                    "Uninstall", danger: true)) return;
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--uninstall --confirmed") { UseShellExecute = false })?.Dispose(); }
            catch (Exception ex) { Dialogs.Info(this, "Couldn't start the uninstaller", ex.Message); }
        }

        void RefreshSettingsPage()
        {
            loading = true;
            try { startup.Checked = SystemTweaks.GetStartup(); } catch { }
            paused.Checked = app.Settings.Paused;
            notify.Checked = app.Settings.Notifications;
            popups.Checked = app.Settings.ShowPopups;
            lockPopups.Checked = app.Settings.LockKeyPopups;
            lowBatt.Enabled = app.Settings.Notifications;
            int lv = new[] { 10, 15, 20, 25, 30 }.OrderBy(v => Math.Abs(v - app.Settings.LowBatteryPercent)).First();
            lowBatt.Select(lv);
            loading = false;

            var logi = SystemTweaks.RunningLogiProcesses();
            var icon = (GlyphBox)rowOptions.Controls[0];
            if (logi.Count == 0)
            {
                rowOptions.Title.Text = "Not running";
                rowOptions.Description = "Good — nothing is competing with LiteOptions for your mouse and keyboard.";
                icon.Glyph = Theme.IcCheck;
            }
            else
            {
                rowOptions.Title.Text = "Options+ is running";
                rowOptions.Description = "It will fight LiteOptions over buttons and device settings. Uninstall it from Installed apps. (" + string.Join(", ", logi) + ")";
                icon.Glyph = Theme.IcWarning;
            }
            icon.Invalidate();
            if (diagWrap.Visible) diag.Box.Text = DiagnosticsText();
        }

        string DiagnosticsText() => app.DiagnosticsText();

        // ───────────────────────── device refresh ─────────────────────────

        public void RefreshDevices()
        {
            var m = app.Devices.Mouse;
            var k = app.Devices.Keyboard;
            loading = true;

            Fill(mouseHero, m, "No mouse found", "Turn the mouse on, or pair it in Settings › Bluetooth & devices. Button remapping works either way.");
            Fill(kbHero, k, "No keyboard found", "Turn the keyboard on, or pair it in Settings › Bluetooth & devices. Key remapping works either way.");

            if (m?.Dpi != null)
            {
                dpiMin = m.Dpi.Min; dpiStep = Math.Max(1, m.Dpi.Step);
                dpiSlider.Minimum = 0;
                dpiSlider.Maximum = Math.Max(1, (m.Dpi.Max - dpiMin) / dpiStep);
                dpiSlider.Enabled = dpiReset.Enabled = m.Online;
                int shown = app.Settings.MouseDpi > 0 ? m.Dpi.Nearest(app.Settings.MouseDpi) : m.Dpi.Current;
                if (!dpiDebounce.Enabled && !dpiSlider.Dragging) dpiSlider.Value = (shown - dpiMin) / dpiStep;
                dpiValue.Text = SliderDpi() + " DPI";
                rowDpi.Description = $"{m.Dpi.Min}–{m.Dpi.Max} DPI, stored on the mouse itself (default {m.Dpi.Default}). Higher moves the pointer further.";
            }
            else
            {
                dpiSlider.Enabled = dpiReset.Enabled = false;
                dpiValue.Text = app.Settings.MouseDpi > 0 ? app.Settings.MouseDpi + " DPI" : "—";
                rowDpi.Description = m == null ? "Turn the mouse on to change its sensitivity" : "This mouse didn't report an adjustable sensor";
            }

            try
            {
                speedSlider.Value = SystemTweaks.GetPointerSpeed(); speedValue.Text = speedSlider.Value.ToString();
                precision.Checked = SystemTweaks.GetPrecision();
                linesSlider.Value = Math.Max(1, Math.Min(20, SystemTweaks.GetWheelLines()));
                linesValue.Text = linesSlider.Value + (linesSlider.Value == 1 ? " line" : " lines");
                reverse.Checked = m != null && SystemTweaks.GetReverseScroll(m.Pid) == true;
            }
            catch { }

            string mode = app.Settings.FnMode;
            fnMode.SelectedIndex = mode == "media" ? 0 : mode == "standard" ? 1 : 2;
            string state = k == null ? "The keyboard isn't connected; your choice is applied when it connects."
                         : !k.FnSupported ? "This keyboard didn't offer the top-row setting."
                         : fnNote2(k) + " Re-applied whenever the keyboard reconnects.";
            var sc = app.Settings.Global.Keys.FirstOrDefault(b => b.Action?.Kind == "preset" && b.Action.Preset == "fn_toggle");
            fnNote.Text2 = state + (sc != null ? " Press " + KeyNames.Combo(sc.Mods, sc.Vk) + " any time to switch." : "");
            fnNote.Parent?.PerformLayout();

            UpdateDeviceSections();
            sideStatus.Invalidate();
            loading = false;
            if (pageSettings.Visible) RefreshSettingsPage();
        }

        static string fnNote2(LogiDevice k) => "Right now: " + (k.FnMedia == true ? "media keys first." : k.FnMedia == false ? "F1–F12 first." : "unknown.");

        void Fill(DeviceHero hero, LogiDevice d, string missing, string hint)
        {
            hero.Found = d != null;
            hero.Title = d?.Name ?? missing;
            hero.Hint = hint;
            hero.Online = d?.Online ?? false;
            hero.Connection = d?.Connection ?? "";
            hero.Battery = d?.Battery;
            hero.BatteryLow = d != null && app.IsLow(d.Battery);
            hero.Firmware = d?.Firmware ?? "";
            hero.Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                app.OnWindowHidden();
                return;
            }
            base.OnFormClosing(e);
        }
    }

    /// <summary>Bottom of the sidebar: both devices with a small battery gauge.</summary>
    sealed class SidebarStatus : Control
    {
        readonly TrayApp app;
        public SidebarStatus(TrayApp app)
        {
            this.app = app;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(96);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.WindowBg);
            TextRenderer.DrawText(g, "DEVICES", Theme.CaptionStrong, new Point(Theme.S(16), 0), Theme.Faint, TextFormatFlags.NoPadding);
            Line(g, Theme.S(26), Theme.IcMouse, "Mouse", app.Devices.Mouse);
            Line(g, Theme.S(58), Theme.IcKeyboard, "Keyboard", app.Devices.Keyboard);
        }

        void Line(Graphics g, int y, string glyph, string label, LogiDevice d)
        {
            Theme.Glyph(g, glyph, 10.5f, Theme.Muted, new Rectangle(Theme.S(12), y, Theme.S(24), Theme.S(26)));
            TextRenderer.DrawText(g, label, Theme.Body, new Rectangle(Theme.S(42), y, Theme.S(100), Theme.S(26)), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            string state;
            if (d == null) state = "Not found";
            else if (!d.Online) state = "Asleep";
            else if (d.Battery == null) state = "Connected";
            else
            {
                var col = Theme.BatteryColor(d.Battery.Percent, app.IsLow(d.Battery));
                Theme.DrawBattery(g, new Rectangle(Width - Theme.S(108), y + Theme.S(7), Theme.S(22), Theme.S(12)), Theme.BatteryFraction(d.Battery), col, Theme.Muted);
                state = d.Battery.Percent.HasValue ? d.Battery.Percent + "%" : d.Battery.Level;
            }
            TextRenderer.DrawText(g, state, Theme.Caption, new Rectangle(Width - Theme.S(80), y, Theme.S(76), Theme.S(26)), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }
}
