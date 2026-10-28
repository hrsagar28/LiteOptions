using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LiteOptions.Input;
using LiteOptions.Ipc;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;

namespace LiteOptions.SettingsApp
{
    abstract class PageBase : StackPanel
    {
        protected readonly Store store;
        protected bool loading;

        protected PageBase(Store store)
        {
            this.store = store;
            Spacing = 4;
            Padding = new Thickness(0, 0, 0, 36);
        }

        public abstract string Title { get; }
        public virtual bool UsesProfiles => true;
        public virtual void LoadSettings() { }
        public virtual void LoadDevices() { }
        public virtual void LoadSystem() { }
        public virtual void JumpTo(string id) { }

        protected T Add<T>(T e) where T : UIElement { Children.Add(e); return e; }

        protected TextBlock Section(string text)
        {
            var h = Ui.Header(text);
            Children.Add(h);
            return h;
        }

        protected Profile Current => store.Current;

        protected static void Show(UIElement e, bool on) => e.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        protected static void Jump(FrameworkElement target)
        {
            target.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.0, VerticalOffset = -16, AnimationDesired = true });
            if (target is SettingCard c) c.Flash();
        }
    }

    /// <summary>Calls an action once things have been quiet for a moment (slider drags, typing).</summary>
    sealed class Debouncer
    {
        readonly DispatcherQueueTimer timer;
        Action pending;
        public Debouncer(int ms)
        {
            timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(ms);
            timer.IsRepeating = false;
            timer.Tick += (s, e) => { timer.Stop(); var a = pending; pending = null; a?.Invoke(); };
        }
        public void Run(Action a) { pending = a; timer.Stop(); timer.Start(); }
        public bool Waiting => timer.IsRunning;
    }

    // ───────────────────────────── Mouse ─────────────────────────────

    sealed class MousePage : PageBase
    {
        readonly DeviceHero hero;
        readonly SettingCard modeCard, actionCard, dpiCard, speedCard, precisionCard, linesCard, reverseCard;
        readonly SettingCard[] gestureCards;
        readonly ComboBox mode;
        readonly ActionPicker middleAction, gClick, gUp, gDown, gLeft, gRight;
        readonly NoteCard appNote;
        readonly TextBlock pointerHeader, scrollHeader;
        readonly Slider dpi, speed, lines;
        readonly TextBlock dpiValue, speedValue, linesValue;
        readonly Button dpiReset;
        readonly ToggleSwitch precision, reverse;
        readonly Debouncer dpiSend = new Debouncer(180), speedSend = new Debouncer(120), linesSend = new Debouncer(120);
        List<string> modes = new List<string>();

        public override string Title => "Mouse";

        public MousePage(Store store) : base(store)
        {
            hero = Add(new DeviceHero(true));
            hero.HotspotClicked += JumpTo;

            Section("Middle button");
            mode = new ComboBox { MinWidth = 200 };
            mode.SelectionChanged += (s, e) =>
            {
                if (loading || mode.SelectedIndex < 0) return;
                Current.MiddleMode = modes[mode.SelectedIndex];
                UpdateMiddle();
                store.Save();
            };
            modeCard = Add(new SettingCard(Glyph.Mouse, "Pressing the scroll wheel", "", mode));

            middleAction = Picker("Middle click", a => Current.MiddleAction = a);
            actionCard = Add(new SettingCard(null, "Action", "Runs each time you press the wheel", middleAction).Indent());

            gClick = Picker("Middle click", a => Current.GestureClick = a);
            gUp = Picker("Nothing", a => Current.GestureUp = a, false);
            gDown = Picker("Nothing", a => Current.GestureDown = a, false);
            gLeft = Picker("Nothing", a => Current.GestureLeft = a, false);
            gRight = Picker("Nothing", a => Current.GestureRight = a, false);
            gestureCards = new[]
            {
                Add(new SettingCard(Glyph.Touch, "Click without moving", null, gClick).Indent()),
                Add(new SettingCard(Glyph.Up, "Hold and move up", null, gUp).Indent()),
                Add(new SettingCard(Glyph.Down, "Hold and move down", null, gDown).Indent()),
                Add(new SettingCard(Glyph.Left, "Hold and move left", null, gLeft).Indent()),
                Add(new SettingCard(Glyph.Right, "Hold and move right", null, gRight).Indent()),
            };

            appNote = Add(new NoteCard("Pointer and scrolling settings are shared by every app. Switch to All applications to change them."));
            appNote.Margin = new Thickness(0, 22, 0, 0);

            pointerHeader = Section("Pointer");
            dpi = new Slider { Width = 220, VerticalAlignment = VerticalAlignment.Center, SnapsTo = SliderSnapsTo.StepValues, IsThumbToolTipEnabled = true };
            dpi.ThumbToolTipValueConverter = new FuncConverter(v => (int)v + " DPI");
            dpiValue = Value(84);
            dpiReset = Ui.IconButton(Glyph.Refresh, "Back to the mouse's default", (s, e) =>
            {
                var m = store.Mouse;
                if (m != null && m.DpiMax > 0) dpi.Value = m.DpiDefault > 0 ? m.DpiDefault : 1000;
            });
            dpi.ValueChanged += (s, e) =>
            {
                dpiValue.Text = (int)dpi.Value + " DPI";
                if (loading) return;
                int v = (int)dpi.Value;
                dpiSend.Run(() =>
                {
                    store.Settings.MouseDpi = v; // the engine saves this itself
                    _ = store.Send("setDpi", v);
                });
            };
            dpiCard = Add(new SettingCard(Glyph.Speed, "Mouse sensitivity", "", Ui.Row(10, dpi, dpiValue, dpiReset)));

            speed = new Slider { Minimum = 1, Maximum = 20, StepFrequency = 1, Width = 220, VerticalAlignment = VerticalAlignment.Center };
            speedValue = Value(84);
            speed.ValueChanged += (s, e) =>
            {
                speedValue.Text = ((int)speed.Value).ToString();
                if (loading) return;
                int v = (int)speed.Value;
                speedSend.Run(() => _ = store.Send("setPointerSpeed", v));
            };
            speedCard = Add(new SettingCard(Glyph.Touch, "Windows pointer speed", "Also changes the touchpad. Windows' default is 10.", Ui.Row(10, speed, speedValue, Spacer())));

            precision = Ui.Toggle();
            precision.Toggled += (s, e) => { if (!loading) _ = store.Send("setPrecision", on: precision.IsOn); };
            precisionCard = Add(new SettingCard(null, "Enhance pointer precision", "Faster hand movements carry the pointer further", precision));

            scrollHeader = Section("Scrolling");
            lines = new Slider { Minimum = 1, Maximum = 20, StepFrequency = 1, Width = 220, VerticalAlignment = VerticalAlignment.Center };
            linesValue = Value(84);
            lines.ValueChanged += (s, e) =>
            {
                int v = (int)lines.Value;
                linesValue.Text = v + (v == 1 ? " line" : " lines");
                if (loading) return;
                linesSend.Run(() => _ = store.Send("setWheelLines", v));
            };
            linesCard = Add(new SettingCard(Glyph.Scroll, "Scroll distance", "Lines moved by one click of the wheel. Windows' default is 3.", Ui.Row(10, lines, linesValue, Spacer())));

            reverse = Ui.Toggle();
            reverse.Toggled += (s, e) => { if (!loading) ToggleReverse(); };
            reverseCard = Add(new SettingCard(Glyph.Swap, "Reverse scroll direction", "This mouse only. Windows asks for permission once; then switch the mouse off and on.", reverse));
        }

        static TextBlock Value(double width) => new TextBlock { Width = width, VerticalAlignment = VerticalAlignment.Center, Style = Ui.Style("BodyStrongTextBlockStyle") };
        static FrameworkElement Spacer() => new Border { Width = 36 };

        ActionPicker Picker(string defaultLabel, Action<ActionSpec> set, bool allowNothing = true)
        {
            var p = new ActionPicker(store, defaultLabel, allowNothing);
            p.Changed += a => { if (loading) return; set(a); store.Save(); };
            return p;
        }

        public override void LoadSettings()
        {
            loading = true;
            var p = Current;
            bool global = p.IsGlobal;
            modes = global ? new List<string> { "default", "action", "gestures" } : new List<string> { "inherit", "default", "action", "gestures" };
            mode.Items.Clear();
            if (!global) mode.Items.Add("Same as all apps");
            mode.Items.Add("Normal click");
            mode.Items.Add("One action");
            mode.Items.Add("Gestures");
            mode.SelectedIndex = Math.Max(0, modes.IndexOf(p.MiddleMode));
            middleAction.Value = p.MiddleAction;
            gClick.Value = p.GestureClick; gUp.Value = p.GestureUp; gDown.Value = p.GestureDown; gLeft.Value = p.GestureLeft; gRight.Value = p.GestureRight;
            UpdateMiddle();

            foreach (var e in new UIElement[] { pointerHeader, dpiCard, speedCard, precisionCard, scrollHeader, linesCard, reverseCard }) Show(e, global);
            appNote.Shown = !global;
            loading = false;
            LoadDevices();
        }

        void UpdateMiddle()
        {
            string m = Current.MiddleMode ?? "default";
            actionCard.Shown = m == "action";
            foreach (var c in gestureCards) c.Shown = m == "gestures";
            modeCard.Description =
                m == "inherit" ? "Uses the All applications setting" :
                m == "default" ? "Works as a normal middle click" :
                m == "action" ? "Does one thing of your choice" :
                "Hold the wheel down, nudge the mouse in a direction, then let go. The pointer stays still while you hold it.";
        }

        public override void LoadDevices()
        {
            var m = store.Mouse;
            hero.Fill(m, "No mouse found", "Turn the mouse on, or pair it in Settings › Bluetooth & devices. Button remapping works either way.", store.IsLow(m));
            loading = true;
            if (m != null && m.DpiMax > m.DpiMin)
            {
                int step = Math.Max(1, m.DpiStep);
                dpi.Minimum = m.DpiMin;
                dpi.Maximum = m.DpiMax;
                dpi.StepFrequency = step;
                dpi.IsEnabled = dpiReset.IsEnabled = m.Online;
                int want = store.Settings.MouseDpi > 0 ? store.Settings.MouseDpi : m.DpiCurrent;
                want = Math.Max(m.DpiMin, Math.Min(m.DpiMax, m.DpiMin + (int)Math.Round((want - m.DpiMin) / (double)step) * step));
                if (!dpiSend.Waiting) dpi.Value = want;
                dpiValue.Text = (int)dpi.Value + " DPI";
                dpiCard.Description = $"{m.DpiMin}–{m.DpiMax} DPI, stored on the mouse itself (default {m.DpiDefault}). Higher moves the pointer further.";
            }
            else
            {
                dpi.IsEnabled = dpiReset.IsEnabled = false;
                dpiValue.Text = store.Settings.MouseDpi > 0 ? store.Settings.MouseDpi + " DPI" : "—";
                dpiCard.Description = m == null ? "Turn the mouse on to change its sensitivity" : "This mouse didn't report an adjustable sensor";
            }
            loading = false;
        }

        public override void LoadSystem()
        {
            var s = store.System;
            loading = true;
            if (!speedSend.Waiting) speed.Value = Math.Max(1, Math.Min(20, s.PointerSpeed));
            speedValue.Text = ((int)speed.Value).ToString();
            precision.IsOn = s.Precision;
            if (!linesSend.Waiting) lines.Value = Math.Max(1, Math.Min(20, s.WheelLines));
            int lv = (int)lines.Value;
            linesValue.Text = lv + (lv == 1 ? " line" : " lines");
            reverse.IsOn = s.ReverseScroll == 1;
            loading = false;
        }

        async void ToggleReverse()
        {
            bool want = reverse.IsOn;
            void revert() { loading = true; reverse.IsOn = !want; loading = false; }
            var r = await store.Send("setReverse", on: want, timeoutMs: 120000);
            if (r.Error == null)
            {
                await Ui.Info("Done", "Switch the mouse off and on (the switch underneath) and the new direction takes effect.");
                return;
            }
            revert();
            if (r.Error == "nomouse") await Ui.Info("Turn the mouse on first", "LiteOptions needs to see the mouse to find its entry in Windows.");
            else if (r.Error == "notfound") await Ui.Info("Couldn't change it", "Windows doesn't have a scroll setting for this mouse yet. Try again after it has been connected for a minute.");
            else if (r.Error != "cancelled") await Ui.Info("Couldn't change it", "LiteOptions isn't running in the background (" + r.Error + ").");
        }

        public override void JumpTo(string id)
        {
            if (id == "middle") Jump(modeCard);
            else Jump(dpiCard.Visibility == Visibility.Visible ? dpiCard : modeCard);
        }
    }

    // ───────────────────────────── Keyboard ─────────────────────────────

    sealed class KeyboardPage : PageBase
    {
        readonly DeviceHero hero;
        readonly TextBlock fnHeader;
        readonly SettingCard fnCard, remapCard;
        readonly NoteCard fnNote, appNote;
        readonly ComboBox fn;
        readonly StackPanel list;

        public override string Title => "Keyboard";

        public KeyboardPage(Store store) : base(store)
        {
            hero = Add(new DeviceHero(false));
            hero.HotspotClicked += JumpTo;

            fnHeader = Section("Top row");
            fn = new ComboBox { MinWidth = 200 };
            foreach (var t in new[] { "Media keys", "F1–F12", "Leave as is" }) fn.Items.Add(t);
            fn.SelectionChanged += async (s, e) =>
            {
                if (loading || fn.SelectedIndex < 0) return;
                string m = fn.SelectedIndex == 0 ? "media" : fn.SelectedIndex == 1 ? "standard" : "unchanged";
                store.Settings.FnMode = m;
                await store.Send("setFnMode", text: m);
                await store.RefreshAsync();
            };
            fnCard = Add(new SettingCard(Glyph.Keyboard, "What the top row does", "Without holding Fn. The other set is always one Fn press away.", fn));
            fnNote = Add(new NoteCard(""));
            appNote = Add(new NoteCard("The top-row setting is shared by every app. Switch to All applications to change it."));

            Section("Key remapping");
            remapCard = Add(new SettingCard(Glyph.Edit, "Remapped keys", "", Ui.Button("Add key", Glyph.Add, accent: true, click: (s, e) => AddKey())));
            list = Add(new StackPanel { Spacing = 4 });
            Add(new NoteCard("Tip: to change a K380 media key, click Add key and press it exactly as you normally would."));
        }

        public override void LoadSettings()
        {
            loading = true;
            var p = Current;
            bool global = p.IsGlobal;
            Show(fnHeader, global); fnCard.Shown = global; fnNote.Shown = global; appNote.Shown = !global;
            remapCard.Title = global ? "Remapped keys" : "Remapped keys in " + p.App;
            remapCard.Description = global
                ? "Apply in every app, and to the laptop's own keyboard too. The K380's Easy-Switch keys and Fn work inside the keyboard and can't be changed."
                : "Used only while " + p.App + " is in front, on top of the All applications list. Choose \"Leave the key as it is\" to switch a global remap off here.";
            string m = store.Settings.FnMode;
            fn.SelectedIndex = m == "media" ? 0 : m == "standard" ? 1 : 2;
            loading = false;
            RefreshList();
            LoadDevices();
        }

        void RefreshList()
        {
            list.Children.Clear();
            var keys = Current.Keys.OrderBy(k => k.Vk).ThenBy(k => k.Mods).ToList();
            if (keys.Count == 0)
            {
                var none = new Border { Style = Ui.Style("Card"), MinHeight = 0, Padding = new Thickness(56, 14, 16, 14) };
                none.Child = Ui.Text("No keys remapped yet.", "SecondaryBody");
                list.Children.Add(none);
                return;
            }
            foreach (var b in keys) list.Children.Add(Row(b));
        }

        UIElement Row(KeyBinding b)
        {
            var card = new Border { Style = Ui.Style("Card"), MinHeight = 56, Padding = new Thickness(56, 8, 12, 8) };
            var g = new Grid { ColumnSpacing = 14 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var caps = Ui.KeyCaps(b.Mods, b.Vk);
            var arrow = Ui.Icon(Glyph.Forward, 12);
            arrow.Foreground = Ui.Brush("TextFillColorSecondaryBrush");
            arrow.VerticalAlignment = VerticalAlignment.Center;
            var what = Ui.Text(Presets.Describe(b.Action, "Leave the key as it is"), wrap: false);
            what.VerticalAlignment = VerticalAlignment.Center;
            var buttons = Ui.Row(2,
                Ui.IconButton(Glyph.Edit, "Change", async (s, e) =>
                {
                    var a = await Dialogs.ChooseActionAsync(store, b.Mods, b.Vk, b.Action);
                    if (a == null) return;
                    b.Action = a;
                    store.Save();
                    RefreshList();
                }),
                Ui.IconButton(Glyph.Delete, "Remove", (s, e) =>
                {
                    Current.Keys.Remove(b);
                    store.Save();
                    RefreshList();
                }));
            Grid.SetColumn(arrow, 1); Grid.SetColumn(what, 2); Grid.SetColumn(buttons, 3);
            g.Children.Add(caps); g.Children.Add(arrow); g.Children.Add(what); g.Children.Add(buttons);
            card.Child = g;
            return card;
        }

        async void AddKey()
        {
            var key = await Dialogs.CaptureKeyAsync(store);
            if (key == null) return;
            var (mods, vk) = key.Value;
            if (Dialogs.IsTypingKey(mods, vk) &&
                !await Ui.Confirm(KeyNames.Key(vk) + " is a typing key", "If you remap it, it will stop typing " + KeyNames.Key(vk) + ". Continue?", "Continue")) return;
            var existing = Current.Keys.FirstOrDefault(k => k.Mods == mods && k.Vk == vk);
            var a = await Dialogs.ChooseActionAsync(store, mods, vk, existing?.Action);
            if (a == null) return;
            if (existing != null) existing.Action = a;
            else Current.Keys.Add(new KeyBinding { Mods = mods, Vk = vk, Action = a });
            store.Save();
            RefreshList();
        }

        public override void LoadDevices()
        {
            var k = store.Keyboard;
            hero.Fill(k, "No keyboard found", "Turn the keyboard on, or pair it in Settings › Bluetooth & devices. Key remapping works either way.", store.IsLow(k));
            string state = k == null ? "The keyboard isn't connected; your choice is applied when it connects."
                         : !k.FnSupported ? "This keyboard didn't offer the top-row setting."
                         : "Right now: " + (k.FnMedia == 1 ? "media keys first." : k.FnMedia == 0 ? "F1–F12 first." : "unknown.") + " Re-applied whenever the keyboard reconnects.";
            var sc = store.Settings.Global.Keys.FirstOrDefault(b => b.Action?.Kind == "preset" && b.Action.Preset == "fn_toggle");
            fnNote.Message = state + (sc != null ? " Press " + KeyNames.Combo(sc.Mods, sc.Vk) + " any time to switch." : "");
        }

        public override void JumpTo(string id)
        {
            if (id == "toprow" && fnCard.Visibility == Visibility.Visible) Jump(fnCard);
            else Jump(remapCard);
        }
    }

    // ───────────────────────────── Settings ─────────────────────────────

    sealed class SettingsPage : PageBase
    {
        readonly ToggleSwitch startup, paused, notify, popups;
        readonly ComboBox lowBatt;
        readonly SettingCard optionsCard, aboutCard;
        readonly TextBox diag;
        readonly Button diagToggle;
        static readonly int[] levels = { 10, 15, 20, 25, 30 };
        public Func<System.Threading.Tasks.Task> RunSetup;

        public override string Title => "Settings";
        public override bool UsesProfiles => false;

        public SettingsPage(Store store) : base(store)
        {
            Section("General");
            startup = Ui.Toggle();
            startup.Toggled += (s, e) => { if (!loading) _ = store.Send("setStartup", on: startup.IsOn); };
            Add(new SettingCard(Glyph.Power, "Start with Windows", "LiteOptions waits quietly in the tray after you sign in", startup));

            paused = Ui.Toggle();
            paused.Toggled += (s, e) => { if (!loading) { store.Settings.Paused = paused.IsOn; store.Save(); } };
            Add(new SettingCard(Glyph.Pause, "Pause remapping", "Buttons and keys work as normal until you switch this off", paused));

            notify = Ui.Toggle();
            lowBatt = new ComboBox { MinWidth = 110 };
            foreach (var v in levels) lowBatt.Items.Add("At " + v + "%");
            notify.Toggled += (s, e) => { if (!loading) { store.Settings.Notifications = notify.IsOn; lowBatt.IsEnabled = notify.IsOn; store.Save(); } };
            lowBatt.SelectionChanged += (s, e) => { if (!loading && lowBatt.SelectedIndex >= 0) { store.Settings.LowBatteryPercent = levels[lowBatt.SelectedIndex]; store.Save(); } };
            Add(new SettingCard(Glyph.Battery(30, false), "Low battery warning", "A notification when the mouse or keyboard battery runs low", Ui.Row(16, lowBatt, notify)));

            popups = Ui.Toggle();
            popups.Toggled += (s, e) => { if (!loading) { store.Settings.ShowPopups = popups.IsOn; store.Save(); } };
            Add(new SettingCard(Glyph.Message, "On-screen pop-ups", "A small note at the bottom of the screen when you switch the top row, change DPI or use a gesture", popups));

            Add(new SettingCard(Glyph.Setup, "Setup", "Go through the three first-run choices again",
                Ui.Button("Run setup", null, false, async (s, e) => { if (RunSetup != null) await RunSetup(); })));

            Section("Logi Options+");
            optionsCard = Add(new SettingCard(Glyph.Check, "", "", Ui.Button("Installed apps", null, false, (s, e) => Open("ms-settings:appsfeatures"))));
            Add(new NoteCard("Firmware updates for Logitech devices only come through Options+. They're rare and optional. If you ever want one: install Options+, let it update, then uninstall it — the firmware stays on the device."));

            Section("About");
            aboutCard = Add(new SettingCard(Glyph.Info, "LiteOptions", "To update, download the new LiteOptions installer and double-click it. Your settings are kept. No account, no internet, no telemetry.",
                Ui.Button("Settings folder", Glyph.Folder, false, (s, e) =>
                {
                    try { System.IO.Directory.CreateDirectory(AppSettings.Folder); Process.Start("explorer.exe", "\"" + AppSettings.Folder + "\"")?.Dispose(); } catch { }
                })));
            Add(new SettingCard(Glyph.Delete, "Uninstall LiteOptions", "Removes the app, its settings and its startup entry", Ui.DangerButton("Uninstall", (s, e) => Uninstall())));

            Section("Troubleshooting");
            diag = new TextBox { Style = Ui.Style("Mono"), Height = 260, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(diag, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(diag, ScrollBarVisibility.Auto);
            diagToggle = Ui.Button("Show details", null, false, async (s, e) =>
            {
                bool show = diag.Visibility != Visibility.Visible;
                diag.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                ((Button)s).Content = show ? "Hide details" : "Show details";
                if (show) diag.Text = await Diagnostics();
            });
            Add(new SettingCard(Glyph.Diagnostic, "Diagnostics", "What LiteOptions found and did. If something isn't working, copy this and send it to Claude.",
                Ui.Row(8,
                    Ui.Button("Copy", Glyph.Copy, false, async (s, e) =>
                    {
                        var dp = new DataPackage();
                        dp.SetText(await Diagnostics());
                        Clipboard.SetContent(dp);
                        ((Button)s).Content = Ui.Row(8, Ui.Icon(Glyph.Check, 14), new TextBlock { Text = "Copied" });
                    }),
                    Ui.Button("Re-scan", Glyph.Refresh, false, async (s, e) => { await store.Send("rescan"); }),
                    diagToggle)));
            Add(diag);
            var classic = new HyperlinkButton { Content = "Open the classic settings window instead", Margin = new Thickness(0, 6, 0, 0) };
            classic.Click += (s, e) => _ = store.Send("classic");
            Add(classic);
        }

        async System.Threading.Tasks.Task<string> Diagnostics()
        {
            var r = await store.Send("diagnostics");
            string head = $"Settings window {typeof(SettingsPage).Assembly.GetName().Version} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, {Environment.OSVersion.VersionString})";
            return r.Error == null ? head + Environment.NewLine + r.Text : head + Environment.NewLine + "The background app didn't answer: " + r.Error;
        }

        static void Open(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose(); } catch { }
        }

        async void Uninstall()
        {
            if (!await Ui.Confirm("Uninstall LiteOptions?",
                    "This removes LiteOptions, its settings and its startup entry. Your mouse and keyboard keep working; they go back to their defaults the next time they're switched off and on.",
                    "Uninstall", danger: true)) return;
            var r = await store.Send("uninstall");
            if (r.Error != null) await Ui.Info("Couldn't start the uninstaller", r.Error);
        }

        public override void LoadSettings()
        {
            loading = true;
            var s = store.Settings;
            paused.IsOn = s.Paused;
            notify.IsOn = s.Notifications;
            lowBatt.IsEnabled = s.Notifications;
            popups.IsOn = s.ShowPopups;
            int near = levels.OrderBy(v => Math.Abs(v - s.LowBatteryPercent)).First();
            lowBatt.SelectedIndex = Array.IndexOf(levels, near);
            loading = false;
        }

        public override void LoadSystem()
        {
            var s = store.System;
            loading = true;
            startup.IsOn = s.Startup;
            loading = false;
            aboutCard.Title = "LiteOptions " + (string.IsNullOrEmpty(s.Version) ? "" : s.Version) + (string.IsNullOrEmpty(s.Architecture) ? "" : " (" + s.Architecture + ")");
            if (string.IsNullOrEmpty(s.OptionsPlus))
            {
                optionsCard.Title = "Not running";
                optionsCard.Description = "Good — nothing is competing with LiteOptions for your mouse and keyboard.";
                optionsCard.IconGlyph = Glyph.Check;
                optionsCard.IconBrush = Ui.Brush("SystemFillColorSuccessBrush");
            }
            else
            {
                optionsCard.Title = "Options+ is running";
                optionsCard.Description = "It will fight LiteOptions over buttons and device settings. Uninstall it from Installed apps. (" + s.OptionsPlus + ")";
                optionsCard.IconGlyph = Glyph.Warning;
                optionsCard.IconBrush = Ui.Brush("SystemFillColorCautionBrush");
            }
        }
    }
}
