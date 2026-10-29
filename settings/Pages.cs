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
    abstract partial class PageBase : StackPanel
    {
        protected readonly Store store;
        protected bool loading;

        /// <summary>Settings the title-bar search can find: a title (null = the card's own), words to match, and where to jump.</summary>
        public readonly List<(string title, string words, FrameworkElement target)> Searchable = new List<(string, string, FrameworkElement)>();

        /// <summary>Set by the window: go to another page ("mouse", "keys", "apps", "alerts", "settings").</summary>
        public Action<string> Navigate;

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

        protected T Find<T>(T e, string words, string title = null) where T : FrameworkElement { Searchable.Add((title, words, e)); return e; }

        protected SettingsGroup Group() => Add(new SettingsGroup());

        protected TextBlock Section(string text)
        {
            var h = Ui.Header(text);
            Children.Add(h);
            return h;
        }

        /// <summary>A section heading with something on its right: a short note or a button.</summary>
        protected Grid Section(string text, FrameworkElement right)
        {
            var g = new Grid { ColumnSpacing = 12 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var h = Ui.Header(text);
            g.Children.Add(h);
            if (right is FrameworkElement fe)
            {
                fe.HorizontalAlignment = HorizontalAlignment.Right;
                fe.VerticalAlignment = VerticalAlignment.Bottom;
                fe.Margin = new Thickness(0, 0, 0, fe is TextBlock ? 9 : 4);
                if (fe is TextBlock tb) { tb.TextAlignment = TextAlignment.Right; tb.TextWrapping = TextWrapping.Wrap; }
            }
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            Children.Add(g);
            return g;
        }

        protected Profile Current => store.Current;

        protected static void Show(UIElement e, bool on) => e.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        public static void Jump(FrameworkElement target)
        {
            if (target == null) return;
            target.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.0, VerticalOffset = -16, AnimationDesired = true });
            if (target is SettingCard c) c.Flash();
        }

        protected static TextBlock Note(string text) => new TextBlock { Text = text, Style = Ui.Style("Secondary") };
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

    sealed partial class MousePage : PageBase
    {
        readonly DeviceHero hero;
        readonly Segmented mode;
        readonly SettingCard modeCard, actionCard, dpiCard, speedCard, precisionCard, linesCard, smoothCard, reverseCard;
        readonly Border gestureBox;
        readonly ActionPicker middleAction, gClick, gUp, gDown, gLeft, gRight;
        readonly NoteCard appNote;
        readonly TextBlock pointerHeader, scrollHeader;
        readonly SettingsGroup pointerGroup, scrollGroup;
        readonly Grid hostsHeader, channels;
        readonly Slider dpi, speed, lines;
        readonly TextBlock dpiValue, speedValue, linesValue;
        readonly Button dpiReset;
        readonly ToggleSwitch precision, reverse, smooth;
        readonly Debouncer dpiSend = new Debouncer(180), speedSend = new Debouncer(120), linesSend = new Debouncer(120);
        List<string> modes = new List<string>();

        public override string Title => "Mouse";

        public MousePage(Store store) : base(store)
        {
            hero = Add(new DeviceHero(true));
            hero.HotspotClicked += JumpTo;

            Section("Buttons");
            var buttons = Group();
            mode = new Segmented("Pressing the scroll wheel", "Normal click", "One action", "Gestures");
            mode.SelectionChanged += i =>
            {
                if (loading || i < 0 || i >= modes.Count) return;
                Current.MiddleMode = modes[i];
                UpdateMiddle();
                store.Save();
            };
            modeCard = Find(buttons.Add(new SettingCard(Glyph.Mouse, "Pressing the scroll wheel", "", mode)), "middle button wheel click gestures scroll wheel press");

            middleAction = Picker("Middle click", a => Current.MiddleAction = a);
            actionCard = buttons.Add(new SettingCard(null, "Action", "Runs each time you press the wheel", middleAction).Indent());

            gClick = Tile("Middle click", a => Current.GestureClick = a, "Click", "", true, true);
            gUp = Tile("Nothing", a => Current.GestureUp = a, "Up", Glyph.Up, false, false);
            gDown = Tile("Nothing", a => Current.GestureDown = a, "Down", Glyph.Down, false, false);
            gLeft = Tile("Nothing", a => Current.GestureLeft = a, "Left", Glyph.Left, false, false);
            gRight = Tile("Nothing", a => Current.GestureRight = a, "Right", Glyph.Right, false, false);
            var cross = new Grid { ColumnSpacing = 8, RowSpacing = 8, MaxWidth = 660, HorizontalAlignment = HorizontalAlignment.Left };
            for (int i = 0; i < 3; i++) cross.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++) cross.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            void place(FrameworkElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); cross.Children.Add(e); }
            place(gUp, 0, 1); place(gLeft, 1, 0); place(gClick, 1, 1); place(gRight, 1, 2); place(gDown, 2, 1);
            gestureBox = buttons.Add(new Border
            {
                Padding = new Thickness(56, 14, 16, 16),
                Background = Ui.Brush("SubtleFillColorTertiaryBrush"),
                Child = Ui.Column(10, cross, Note("Pick a tile to change it. Each gesture shows a pop-up so you know it worked.")),
            });
            Find(gestureBox, "gestures up down left right task view desktop", "Middle-button gestures");

            appNote = Add(new NoteCard("Pointer and scrolling settings are shared by every app. Switch to All apps to change them."));
            appNote.Margin = new Thickness(0, 22, 0, 0);

            pointerHeader = Section("Pointer");
            pointerGroup = Group();
            dpi = new Slider { Width = 200, VerticalAlignment = VerticalAlignment.Center, SnapsTo = SliderSnapsTo.StepValues, IsThumbToolTipEnabled = true };
            dpi.ThumbToolTipValueConverter = new FuncConverter(v => (int)v + " DPI");
            dpiValue = Value(80);
            dpiReset = Ui.IconButton(Glyph.Refresh, "Back to the mouse's default", (s, e) =>
            {
                var m = store.Mouse;
                if (m != null && m.DpiMax > 0) dpi.Value = m.DpiDefault > 0 ? m.DpiDefault : 1000;
            });
            dpi.ValueChanged += (s, e) =>
            {
                dpiValue.Text = (int)dpi.Value + " DPI";
                hero.SetCallout("pointer", dpiValue.Text);
                if (loading) return;
                int v = (int)dpi.Value;
                dpiSend.Run(() =>
                {
                    store.Settings.MouseDpi = v; // the engine saves this itself
                    _ = store.Send("setDpi", v);
                });
            };
            dpiCard = Find(pointerGroup.Add(new SettingCard(Glyph.Speed, "Mouse sensitivity", "", Ui.Row(10, dpi, dpiValue, dpiReset))), "dpi sensitivity speed sensor");

            speed = new Slider { Minimum = 1, Maximum = 20, StepFrequency = 1, Width = 200, VerticalAlignment = VerticalAlignment.Center };
            speedValue = Value(80);
            speed.ValueChanged += (s, e) =>
            {
                speedValue.Text = ((int)speed.Value).ToString();
                if (loading) return;
                int v = (int)speed.Value;
                speedSend.Run(() => _ = store.Send("setPointerSpeed", v));
            };
            speedCard = Find(pointerGroup.Add(new SettingCard(Glyph.Touch, "Windows pointer speed", "Also changes the touchpad. Windows' default is 10.", Ui.Row(10, speed, speedValue, Spacer()))), "pointer speed cursor touchpad");

            precision = Ui.Toggle();
            precision.Toggled += (s, e) => { if (!loading) _ = store.Send("setPrecision", on: precision.IsOn); };
            precisionCard = Find(pointerGroup.Add(new SettingCard(null, "Enhance pointer precision", "Faster hand movements carry the pointer further", precision).Indent()), "enhance pointer precision acceleration");

            scrollHeader = Section("Scrolling");
            scrollGroup = Group();
            lines = new Slider { Minimum = 1, Maximum = 20, StepFrequency = 1, Width = 200, VerticalAlignment = VerticalAlignment.Center };
            linesValue = Value(80);
            lines.ValueChanged += (s, e) =>
            {
                int v = (int)lines.Value;
                linesValue.Text = v + (v == 1 ? " line" : " lines");
                UpdateScrollCallout();
                if (loading) return;
                linesSend.Run(() => _ = store.Send("setWheelLines", v));
            };
            linesCard = Find(scrollGroup.Add(new SettingCard(Glyph.Scroll, "Scroll distance", "Lines moved by one click of the wheel. Windows' default is 3.", Ui.Row(10, lines, linesValue, Spacer()))), "scroll distance lines wheel");

            smooth = Ui.Toggle();
            smooth.Toggled += (s, e) => { UpdateScrollCallout(); if (!loading) { store.Settings.SmoothScroll = smooth.IsOn; store.Save(); } };
            smoothCard = Find(scrollGroup.Add(new SettingCard(null, "Smooth scrolling",
                "Each click of the wheel glides instead of jumping. Works in browsers, Office and most apps; if an older app stops scrolling, switch this off.", smooth).Indent()), "smooth scrolling glide wheel");

            reverse = Ui.Toggle();
            reverse.Toggled += (s, e) => { if (!loading) ToggleReverse(); };
            reverseCard = Find(scrollGroup.Add(new SettingCard(Glyph.Swap, "Reverse scroll direction", "This mouse only. Windows asks for permission once; then switch the mouse off and on.", reverse)), "reverse scroll direction natural");

            hostsHeader = Section("Easy-Switch", Note("The button underneath the mouse changes channel"));
            channels = Add(Channels.Make());
            Find(channels, "easy-switch channels paired computers phone bluetooth", "Easy-Switch channels (mouse)");
        }

        static TextBlock Value(double width) => new TextBlock { Width = width, VerticalAlignment = VerticalAlignment.Center, Style = Ui.Style("BodyStrongTextBlockStyle") };
        static FrameworkElement Spacer() => new Border { Width = 36 };

        ActionPicker Picker(string defaultLabel, Action<ActionSpec> set, bool allowNothing = true)
        {
            var p = new ActionPicker(store, defaultLabel, allowNothing);
            p.Changed += a => { if (loading) return; set(a); store.Save(); };
            return p;
        }

        ActionPicker Tile(string defaultLabel, Action<ActionSpec> set, string direction, string glyph, bool accent, bool allowNothing)
        {
            var p = ActionPicker.Tile(store, defaultLabel, allowNothing, direction, glyph, accent);
            p.Changed += a => { if (loading) return; set(a); store.Save(); };
            return p;
        }

        public override void LoadSettings()
        {
            loading = true;
            var p = Current;
            bool global = p.IsGlobal;
            modes = global ? new List<string> { "default", "action", "gestures" } : new List<string> { "inherit", "default", "action", "gestures" };
            if (global) mode.SetOptions("Normal click", "One action", "Gestures");
            else mode.SetOptions("Same as all apps", "Normal click", "One action", "Gestures");
            mode.SelectedIndex = Math.Max(0, modes.IndexOf(p.MiddleMode));
            middleAction.Value = p.MiddleAction;
            gClick.Value = p.GestureClick; gUp.Value = p.GestureUp; gDown.Value = p.GestureDown; gLeft.Value = p.GestureLeft; gRight.Value = p.GestureRight;
            UpdateMiddle();

            foreach (var e in new UIElement[] { pointerHeader, pointerGroup, scrollHeader, scrollGroup }) Show(e, global);
            smooth.IsOn = store.Settings.SmoothScroll;
            appNote.Shown = !global;
            loading = false;
            UpdateScrollCallout();
            LoadDevices();
        }

        void UpdateMiddle()
        {
            string m = Current.MiddleMode ?? "default";
            actionCard.Shown = m == "action";
            gestureBox.Visibility = m == "gestures" ? Visibility.Visible : Visibility.Collapsed;
            modeCard.Description =
                m == "inherit" ? "Uses the All apps setting" :
                m == "default" ? "Works as a normal middle click" :
                m == "action" ? "Does one thing of your choice" :
                "Hold the wheel, nudge the mouse in a direction, let go. The pointer stays still while you hold it.";
            var eff = m == "inherit" ? store.Settings.Global.MiddleMode : m;
            hero.SetCallout("middle", eff == "gestures" ? "Gestures" : eff == "action" ? Presets.Describe(m == "inherit" ? store.Settings.Global.MiddleAction : Current.MiddleAction, "Middle click") : "Normal click");
        }

        void UpdateScrollCallout() =>
            hero.SetCallout("scroll", (smooth.IsOn ? "Smooth · " : "") + (int)lines.Value + ((int)lines.Value == 1 ? " line" : " lines"));

        public override void LoadDevices()
        {
            var m = store.Mouse;
            hero.Fill(m, "No mouse found", "Turn the mouse on, or pair it in Settings › Bluetooth & devices. Button remapping works either way.", store.IsLow(m));
            Channels.Fill(channels, m, true);
            bool hosts = Current.IsGlobal && channels.Children.Count > 0;
            Show(channels, hosts);
            Show(hostsHeader, hosts);
            loading = true;
            if (m != null && m.DpiMax > m.DpiMin)
            {
                int step = Math.Max(1, m.DpiStep);
                dpi.Minimum = m.DpiMin;
                dpi.Maximum = m.DpiMax;
                dpi.StepFrequency = step;
                dpi.SmallChange = step;      // arrow keys: one step (the default of 1 snaps straight back)
                dpi.LargeChange = step * 5;  // Page Up / Page Down
                dpi.IsEnabled = dpiReset.IsEnabled = m.Online;
                int want = store.Settings.MouseDpi > 0 ? store.Settings.MouseDpi : m.DpiCurrent;
                want = Math.Max(m.DpiMin, Math.Min(m.DpiMax, m.DpiMin + (int)Math.Round((want - m.DpiMin) / (double)step) * step));
                if (!dpiSend.Waiting) dpi.Value = want;
                dpiValue.Text = (int)dpi.Value + " DPI";
                dpiCard.Description = $"Saved on the mouse itself · {m.DpiMin}–{m.DpiMax} DPI (default {m.DpiDefault})";
            }
            else
            {
                dpi.IsEnabled = dpiReset.IsEnabled = false;
                dpiValue.Text = store.Settings.MouseDpi > 0 ? store.Settings.MouseDpi + " DPI" : "—";
                dpiCard.Description = m == null ? "Turn the mouse on to change its sensitivity" : "This mouse didn't report an adjustable sensor";
            }
            hero.SetCallout("pointer", dpiValue.Text);
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
            UpdateScrollCallout();
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
            bool global = Current.IsGlobal;
            if (id == "middle") Jump(modeCard);
            else if (id == "scroll") Jump(global ? linesCard : modeCard);
            else Jump(global ? dpiCard : modeCard);
        }
    }

    // ───────────────────────────── Keyboard ─────────────────────────────

    sealed partial class KeyboardPage : PageBase
    {
        readonly DeviceHero hero;
        readonly TextBlock fnHeader;
        readonly SettingsGroup fnGroup, remapGroup;
        readonly SettingCard fnCard;
        readonly Segmented fn;
        readonly NoteCard appNote;
        readonly Grid offHeader, remapHeader, hostsHeader, channels;
        readonly KeysOff keysOff;

        public override string Title => "Keyboard";

        public KeyboardPage(Store store) : base(store)
        {
            hero = Add(new DeviceHero(false));
            hero.HotspotClicked += JumpTo;

            fnHeader = Section("Top row");
            fnGroup = Group();
            fn = new Segmented("What the top row does", "Media keys", "F1–F12", "Leave as is");
            fn.SelectionChanged += async i =>
            {
                if (loading || i < 0) return;
                string m = i == 0 ? "media" : i == 1 ? "standard" : "unchanged";
                store.Settings.FnMode = m;
                UpdateTopRow();
                await store.Send("setFnMode", text: m);
                await store.RefreshAsync();
            };
            fnCard = Find(fnGroup.Add(new SettingCard(Glyph.Keyboard, "What the top row does", "", fn)), "top row media keys f1 f12 function keys fn");

            appNote = Add(new NoteCard("The top row and switched-off keys are shared by every app. Switch to All apps to change them."));
            appNote.Margin = new Thickness(0, 22, 0, 0);

            offHeader = Section("Turn off keys", Note("Switched off inside the keyboard. The laptop's own keys keep working."));
            keysOff = Add(new KeysOff());
            keysOff.Changed += mask =>
            {
                store.Settings.DisabledKeys = mask;
                store.Save();
                hero.SetCallout("keysoff", keysOff.Summary(mask));
                PointKeysOff(mask);
            };
            Find(keysOff, "turn off disable keys caps lock insert windows key scroll lock", "Turn off keys");

            var add = Ui.Button("Add key", Glyph.Add, accent: true, click: (s, e) => AddKey());
            remapHeader = Section("Remapped keys", add);
            remapGroup = Group();
            Find(remapHeader, "remap keys shortcut change key add key", "Remapped keys");

            hostsHeader = Section("Easy-Switch", Note("The F1, F2 and F3 keys change channel"));
            channels = Add(Channels.Make());
            Find(channels, "easy-switch channels paired computers phone bluetooth", "Easy-Switch channels (keyboard)");
        }

        public override void LoadSettings()
        {
            loading = true;
            var p = Current;
            bool global = p.IsGlobal;
            Show(fnHeader, global); Show(fnGroup, global); appNote.Shown = !global;
            string m = store.Settings.FnMode;
            fn.SelectedIndex = m == "media" ? 0 : m == "standard" ? 1 : 2;
            loading = false;
            RefreshList();
            LoadDevices();
        }

        void RefreshList()
        {
            remapGroup.Clear();
            var p = Current;
            var keys = p.Keys.OrderBy(k => k.Vk).ThenBy(k => k.Mods).ToList();
            if (keys.Count == 0)
            {
                var none = Ui.Text(p.IsGlobal ? "No keys remapped yet." : "No keys remapped for " + p.App + " yet.", "SecondaryBody");
                remapGroup.Add(new Border { Padding = new Thickness(56, 14, 16, 14), Child = none });
            }
            foreach (var b in keys) remapGroup.Add(Row(b));
            var tip = p.IsGlobal
                ? "Remaps work in every app and on the laptop's own keyboard too. The K380's Easy-Switch keys and Fn work inside the keyboard and can't be changed. To change a media key, press it as you normally would after Add key."
                : "Used only while " + p.App + " is in front, on top of the All apps list. Choose “Leave the key as it is” to switch a global remap off here.";
            var tipRow = new Grid { ColumnSpacing = 16, Padding = new Thickness(16, 10, 16, 12) };
            tipRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            tipRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var i = new FontIcon { Glyph = Glyph.Info, FontSize = 14, Foreground = Ui.Brush("TextFillColorSecondaryBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
            var t = Note(tip);
            Grid.SetColumn(t, 1);
            tipRow.Children.Add(i);
            tipRow.Children.Add(t);
            remapGroup.Add(tipRow);
            int count = store.Settings.Global.Keys.Count;
            hero.SetCallout("remap", count == 0 ? "None" : count == 1 ? KeyNames.Combo(store.Settings.Global.Keys[0].Mods, store.Settings.Global.Keys[0].Vk) : count + " keys");
        }

        UIElement Row(KeyBinding b)
        {
            var g = new Grid { ColumnSpacing = 14, Padding = new Thickness(16, 10, 12, 10), MinHeight = 56 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var caps = Ui.KeyCaps(b.Mods, b.Vk);
            caps.Margin = new Thickness(40, 0, 0, 0);
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
            return g;
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

        void UpdateTopRow()
        {
            var k = store.Keyboard;
            string m = store.Settings.FnMode;
            bool? media = k != null && k.FnSupported && k.FnMedia >= 0 ? k.FnMedia == 1 : m == "media" ? true : m == "standard" ? false : (bool?)null;
            hero.SetCallout("toprow", media == true ? "Media keys first" : media == false ? "F1–F12 first" : "Left as it is");
            string state = k == null ? "The keyboard isn't connected; your choice is applied when it connects."
                         : !k.FnSupported ? "This keyboard didn't offer the top-row setting."
                         : "Without holding Fn. Right now: " + (k.FnMedia == 1 ? "media keys first." : k.FnMedia == 0 ? "F1–F12 first." : "unknown.");
            var sc = store.Settings.Global.Keys.FirstOrDefault(b => b.Action?.Kind == "preset" && b.Action.Preset == "fn_toggle");
            fnCard.Description = state + (sc != null ? " Press " + KeyNames.Combo(sc.Mods, sc.Vk) + " to switch any time." : "");
        }

        /// <summary>Point the "Turned off" label at the Windows key when that's the only key off, otherwise at Caps Lock.</summary>
        void PointKeysOff(int mask)
        {
            if ((mask & ~16) == 0 && (mask & 16) != 0) hero.PointCallout("keysoff", 258, 249);
            else hero.PointCallout("keysoff", 204, 185);
        }

        public override void LoadDevices()
        {
            var k = store.Keyboard;
            bool global = Current.IsGlobal;
            hero.Fill(k, "No keyboard found", "Turn the keyboard on, or pair it in Settings › Bluetooth & devices. Key remapping works either way.", store.IsLow(k));
            UpdateTopRow();

            bool canOff = k != null && k.DisableCaps != 0;
            Show(offHeader, canOff && global);
            Show(keysOff, canOff && global);
            if (canOff)
            {
                int shown = store.Settings.DisabledKeys >= 0 ? store.Settings.DisabledKeys & k.DisableCaps : Math.Max(0, k.DisabledKeys);
                keysOff.Load(k.DisableCaps, shown);
                hero.SetCallout("keysoff", keysOff.Summary(shown));
                PointKeysOff(shown);
            }
            else hero.SetCallout("keysoff", null);

            Channels.Fill(channels, k, false);
            bool hosts = global && channels.Children.Count > 0;
            Show(channels, hosts);
            Show(hostsHeader, hosts);
        }

        public override void JumpTo(string id)
        {
            if (id == "toprow" && fnGroup.Visibility == Visibility.Visible) Jump(fnCard);
            else if (id == "keysoff" && keysOff.Visibility == Visibility.Visible) Jump(keysOff);
            else Jump(remapHeader);
        }
    }

    // ───────────────────────────── App profiles ─────────────────────────────

    sealed partial class AppsPage : PageBase
    {
        readonly SettingsGroup list;

        public override string Title => "App profiles";
        public override bool UsesProfiles => false;

        public AppsPage(Store store) : base(store)
        {
            Add(new TextBlock
            {
                Text = "Give an app its own middle button and keys. They apply only while that app is in front; everything else follows All apps.",
                Style = Ui.Style("SecondaryBody"), Margin = new Thickness(0, 4, 0, 0),
            });
            var add = Ui.Button("Add an app", Glyph.Add, accent: true, click: async (s, e) => await AddApp());
            Find(Section("Apps", add), "app profiles per-app add an app", "Add an app");
            list = Group();
        }

        public override void LoadSettings()
        {
            list.Clear();
            foreach (var p in store.Settings.Profiles) list.Add(Row(p));
        }

        UIElement Row(Profile p)
        {
            string middle = p.MiddleMode == "inherit" ? "middle button as All apps"
                          : p.MiddleMode == "gestures" ? "middle button: gestures"
                          : p.MiddleMode == "action" ? "middle button: " + Presets.Describe(p.MiddleAction, "Middle click")
                          : "middle button: normal click";
            string keys = p.Keys.Count == 0 ? "no keys remapped" : p.Keys.Count == 1 ? "1 key remapped" : p.Keys.Count + " keys remapped";
            string summary = char.ToUpper(middle[0]) + middle.Substring(1) + " · " + keys;
            var app = p.App;
            var edit = Ui.Button("Edit", Glyph.Edit, false, (s, e) => { store.CurrentApp = app; Navigate?.Invoke("mouse"); });
            UIElement right = edit;
            if (!p.IsGlobal) right = Ui.Row(4, edit, Ui.IconButton(Glyph.Delete, "Remove " + app, async (s, e) => await RemoveApp(app)));
            return new SettingCard(p.IsGlobal ? Glyph.Apps : Glyph.App, p.IsGlobal ? "All apps" : app, summary, right);
        }

        internal async System.Threading.Tasks.Task AddApp()
        {
            var exe = await Dialogs.AddAppAsync(store.Settings.Profiles.Select(p => p.App));
            if (string.IsNullOrEmpty(exe)) return;
            if (store.Settings.ForApp(exe) == null)
            {
                var p = new Profile { App = exe, MiddleMode = "inherit" };
                p.Normalize();
                store.Settings.Profiles.Add(p);
                store.Settings.Normalize();
                store.Save();
            }
            store.CurrentApp = exe;
            Navigate?.Invoke("mouse");
        }

        internal async System.Threading.Tasks.Task RemoveApp(string app)
        {
            if (!await Ui.Confirm("Remove " + app + "?", "Its own middle-button and key settings are deleted. All apps settings stay as they are.", "Remove", danger: true)) return;
            store.Settings.Profiles.RemoveAll(p => p.App == app);
            store.Save();
            if (store.CurrentApp == app) store.CurrentApp = "";
            LoadSettings();
        }
    }

    // ───────────────────────────── Pop-ups & alerts ─────────────────────────────

    sealed partial class AlertsPage : PageBase
    {
        readonly ToggleSwitch popups, lockPopups, notify;
        readonly SettingCard lockCard;
        readonly ComboBox lowBatt;
        static readonly int[] levels = { 10, 15, 20, 25, 30 };

        public override string Title => "Pop-ups & alerts";
        public override bool UsesProfiles => false;

        public AlertsPage(Store store) : base(store)
        {
            Section("On-screen pop-ups");
            var g = Group();
            popups = Ui.Toggle();
            popups.Toggled += (s, e) => { if (!loading) { store.Settings.ShowPopups = popups.IsOn; store.Save(); } lockCard.Shown = popups.IsOn; };
            Find(g.Add(new SettingCard(Glyph.Message, "Pop-ups", "A small note at the bottom of every screen when you switch the top row, change DPI, mute the microphone or use a gesture", popups)), "pop-ups on-screen notes osd");
            lockPopups = Ui.Toggle();
            lockPopups.Toggled += (s, e) => { if (!loading) { store.Settings.LockKeyPopups = lockPopups.IsOn; store.Save(); } };
            lockCard = Find(g.Add(new SettingCard(null, "Caps Lock and Num Lock", "Show a pop-up when Caps Lock, Num Lock or Scroll Lock is switched on or off", lockPopups).Indent()), "caps lock num lock scroll lock pop-up");

            Section("Battery");
            var b = Group();
            notify = Ui.Toggle();
            lowBatt = new ComboBox { MinWidth = 110 };
            foreach (var v in levels) lowBatt.Items.Add("At " + v + "%");
            notify.Toggled += (s, e) => { if (!loading) { store.Settings.Notifications = notify.IsOn; lowBatt.IsEnabled = notify.IsOn; store.Save(); } };
            lowBatt.SelectionChanged += (s, e) => { if (!loading && lowBatt.SelectedIndex >= 0) { store.Settings.LowBatteryPercent = levels[lowBatt.SelectedIndex]; store.Save(); } };
            Find(b.Add(new SettingCard(Glyph.Battery(30, false), "Low battery warning", "A Windows notification when the mouse or keyboard battery runs low", Ui.Row(16, lowBatt, notify))), "low battery warning notification");
        }

        public override void LoadSettings()
        {
            loading = true;
            var s = store.Settings;
            popups.IsOn = s.ShowPopups;
            lockPopups.IsOn = s.LockKeyPopups;
            lockCard.Shown = s.ShowPopups;
            notify.IsOn = s.Notifications;
            lowBatt.IsEnabled = s.Notifications;
            int near = levels.OrderBy(v => Math.Abs(v - s.LowBatteryPercent)).First();
            lowBatt.SelectedIndex = Array.IndexOf(levels, near);
            loading = false;
        }
    }

    // ───────────────────────────── Settings ─────────────────────────────

    sealed partial class SettingsPage : PageBase
    {
        readonly ToggleSwitch startup, paused;
        readonly SettingCard optionsCard, aboutCard;
        readonly TextBox diag;
        readonly Button diagToggle;
        public Func<System.Threading.Tasks.Task> RunSetup;

        public override string Title => "Settings";
        public override bool UsesProfiles => false;

        public SettingsPage(Store store) : base(store)
        {
            Section("General");
            var g = Group();
            startup = Ui.Toggle();
            startup.Toggled += (s, e) => { if (!loading) _ = store.Send("setStartup", on: startup.IsOn); };
            Find(g.Add(new SettingCard(Glyph.Power, "Start with Windows", "LiteOptions waits quietly in the tray after you sign in", startup)), "start with windows startup login");
            paused = Ui.Toggle();
            paused.Toggled += (s, e) => { if (!loading) { store.Settings.Paused = paused.IsOn; store.Save(); } };
            Find(g.Add(new SettingCard(Glyph.Pause, "Pause remapping", "Buttons and keys work as normal until you switch this off", paused)), "pause remapping off disable");
            Find(g.Add(new SettingCard(Glyph.Setup, "Setup", "Go through the three first-run choices again",
                Ui.Button("Run setup", null, false, async (s, e) => { if (RunSetup != null) await RunSetup(); }))), "setup first run wizard");

            Section("Logi Options+");
            var o = Group();
            optionsCard = Find(o.Add(new SettingCard(Glyph.Check, "", "", Ui.Button("Installed apps", null, false, (s, e) => Open("ms-settings:appsfeatures")))), "logi options+ logitech firmware");
            var fw = new SettingCard(null, "Firmware updates", "They only come through Options+, and they're rare and optional. If you ever want one: install Options+, let it update, then uninstall it. The firmware stays on the device.");
            o.Add(fw.Indent());

            Section("About");
            var a = Group();
            aboutCard = Find(a.Add(new SettingCard(Glyph.Info, "LiteOptions", "To update, download the new LiteOptions installer and double-click it. Your settings are kept. No account, no internet, no telemetry.",
                Ui.Button("Settings folder", Glyph.Folder, false, (s, e) =>
                {
                    try { System.IO.Directory.CreateDirectory(AppSettings.Folder); Process.Start("explorer.exe", "\"" + AppSettings.Folder + "\"")?.Dispose(); } catch { }
                }))), "about version update");
            Find(a.Add(new SettingCard(Glyph.Delete, "Uninstall LiteOptions", "Removes the app, its settings and its startup entry", Ui.DangerButton("Uninstall", (s, e) => Uninstall()))), "uninstall remove");

            Section("Troubleshooting");
            var t = Group();
            diag = new TextBox { Style = Ui.Style("Mono"), Height = 260, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(diag, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(diag, ScrollBarVisibility.Auto);
            diagToggle = Ui.Button("Show details", null, false, async (s, e) =>
            {
                bool show = diag.Visibility != Visibility.Visible;
                diag.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                ((Button)s).Content = show ? "Hide details" : "Show details";
                if (show) diag.Text = await Diagnostics();
            });
            Find(t.Add(new SettingCard(Glyph.Diagnostic, "Diagnostics", "What LiteOptions found and did. If something isn't working, copy this and send it to Claude.",
                Ui.Row(8,
                    Ui.Button("Copy", Glyph.Copy, false, async (s, e) =>
                    {
                        var dp = new DataPackage();
                        dp.SetText(await Diagnostics());
                        Clipboard.SetContent(dp);
                        ((Button)s).Content = Ui.Row(8, Ui.Icon(Glyph.Check, 14), new TextBlock { Text = "Copied" });
                    }),
                    Ui.Button("Re-scan", Glyph.Refresh, false, async (s, e) => { await store.Send("rescan"); }),
                    diagToggle))), "diagnostics log troubleshooting rescan");
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
            paused.IsOn = store.Settings.Paused;
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
