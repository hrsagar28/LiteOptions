using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LiteOptions.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;

namespace LiteOptions.SettingsApp
{
    /// <summary>
    /// The action chooser. Shows as a drop-down button, or as a tile (the middle-button gestures). Either way it opens
    /// a panel with a search box, category chips, every action with its icon and shortcut, and the custom actions.
    /// </summary>
    sealed partial class ActionPicker : UserControl
    {
        readonly Store store;
        readonly string defaultLabel;
        readonly bool allowNothing;
        readonly bool tile;
        readonly Button button;
        readonly TextBlock label, hint;
        readonly Flyout flyout;
        ActionSpec spec = ActionSpec.Default();

        public event Action<ActionSpec> Changed;
        /// <summary>Set inside dialogs: custom actions are then edited in the dialog itself (WinUI shows one dialog at a time).</summary>
        public Action<string> InlineCustom;

        public ActionPicker(Store store, string defaultLabel, bool allowNothing = true) : this(store, defaultLabel, allowNothing, null, null, false) { }

        /// <summary>A tile for a gesture: the direction and its arrow at the top, then the action and its shortcut.</summary>
        public static ActionPicker Tile(Store store, string defaultLabel, bool allowNothing, string direction, string glyph, bool accent) =>
            new ActionPicker(store, defaultLabel, allowNothing, direction, glyph, accent);

        ActionPicker(Store store, string defaultLabel, bool allowNothing, string direction, string glyph, bool accent)
        {
            this.store = store;
            this.defaultLabel = defaultLabel;
            this.allowNothing = allowNothing;
            tile = direction != null;
            IsTabStop = false;
            flyout = new Flyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft, FlyoutPresenterStyle = PresenterStyle() };
            flyout.Opening += (s, e) => flyout.Content = BuildPanel();

            if (!tile)
            {
                label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230, TextAlignment = TextAlignment.Left };
                button = new DropDownButton { Content = label, MinWidth = 230, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Flyout = flyout };
            }
            else
            {
                var top = new Grid();
                var dir = new TextBlock { Text = direction, Style = Ui.Style("CaptionTextBlockStyle") };
                var arrow = new FontIcon { Glyph = glyph ?? "", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right };
                top.Children.Add(dir);
                top.Children.Add(arrow);
                label = new TextBlock { Style = Ui.Style("BodyStrongTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
                hint = new TextBlock { Style = Ui.Style("CaptionTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
                var faint = Ui.Brush(accent ? "TextOnAccentFillColorSecondaryBrush" : "TextFillColorSecondaryBrush");
                dir.Foreground = faint; arrow.Foreground = faint; hint.Foreground = faint;
                button = new Button
                {
                    Content = Ui.Column(4, top, label, hint),
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                    Padding = new Thickness(14, 10, 14, 10), MinHeight = 80, CornerRadius = new CornerRadius(6),
                    Flyout = flyout,
                };
                if (accent) button.Style = Ui.Style("AccentButtonStyle");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, direction);
            }
            Content = button;
            Value = ActionSpec.Default();
        }

        static Style PresenterStyle()
        {
            var st = new Style(typeof(FlyoutPresenter));
            st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            st.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
            st.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 460.0));
            st.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, 680.0));
            st.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
            return st;
        }

        /// <summary>Open the panel (used by the screenshot tour).</summary>
        public FlyoutBase OpenMenu()
        {
            flyout.ShowAt(button);
            return flyout;
        }

        public ActionSpec Value
        {
            get => spec.Clone();
            set
            {
                spec = value?.Clone() ?? ActionSpec.Default();
                label.Text = tile ? Short(spec) : Presets.Describe(spec, defaultLabel);
                if (hint != null) { hint.Text = HintOf(spec); hint.Visibility = hint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }
            }
        }

        /// <summary>The action's name without the "Shortcut:" / "Open:" prefixes (tiles are small).</summary>
        string Short(ActionSpec a)
        {
            if (a == null || a.IsDefault) return defaultLabel;
            if (a.Kind == "keys") return "Shortcut";
            if (a.Kind == "launch") return "Open " + (Presets.Describe(a).Replace("Open: ", ""));
            if (a.Kind == "text") return "Type text";
            return Presets.Describe(a, defaultLabel);
        }

        static string HintOf(ActionSpec a)
        {
            if (a == null || a.IsDefault || a.Kind == "none") return "";
            if (a.Kind == "keys") return KeyNames.Combo(a.Mods, a.Vk);
            if (a.Kind == "preset") return Hint(Presets.Get(a.Preset));
            if (a.Kind == "text") return "“" + (a.Text != null && a.Text.Length > 20 ? a.Text.Substring(0, 20) + "…" : a.Text) + "”";
            return "";
        }

        /// <summary>Show a label without changing the value (used while a custom action is being edited inline).</summary>
        public void ShowLabel(string text) => label.Text = text;

        void Set(ActionSpec a)
        {
            Value = a;
            Changed?.Invoke(Value);
        }

        // ───────────── the panel ─────────────

        static readonly Dictionary<string, string> glyphs = new Dictionary<string, string>
        {
            { "middle_click", "" }, { "back", "" }, { "forward", "" }, { "double_click", "" }, { "right_click", "" },
            { "ctrl_click", "" }, { "shift_click", "" },
            { "task_view", "" }, { "show_desktop", "" }, { "desktop_left", "" }, { "desktop_right", "" },
            { "switch_app", "" }, { "snip", "" }, { "emoji", "" }, { "dictation", "" }, { "clipboard", "" },
            { "search", "" }, { "explorer", "" }, { "notifications", "" }, { "minimize", "" }, { "maximize", "" },
            { "close_window", "" }, { "settings", "" }, { "quick_settings", "" }, { "input_language", "" },
            { "print_screen", "" }, { "lock", "" }, { "sleep", "" }, { "calc", "" },
            { "brightness_up", "" }, { "brightness_down", "" },
            { "fn_toggle", "" }, { "pause_toggle", "" }, { "open_app", "" },
            { "copy", "" }, { "cut", "" }, { "paste", "" }, { "paste_plain", "" }, { "undo", "" }, { "redo", "" },
            { "select_all", "" }, { "save", "" }, { "find", "" }, { "print", "" },
            { "new_tab", "" }, { "close_tab", "" }, { "reopen_tab", "" }, { "next_tab", "" }, { "prev_tab", "" },
            { "refresh", "" }, { "zoom_in", "" }, { "zoom_out", "" }, { "zoom_reset", "" },
            { "play_pause", "" }, { "next_track", "" }, { "prev_track", "" }, { "stop", "" },
            { "mute", "" }, { "vol_up", "" }, { "vol_down", "" }, { "mic_mute", "" },
        };

        static string GroupGlyph(string group)
        {
            switch (group)
            {
                case "Mouse": return Glyph.Mouse;
                case "Windows": return "";
                case "Screen": return "";
                case "LiteOptions": return Glyph.Keyboard;
                case "Editing": return Glyph.Edit;
                case "Browser & tabs": return "";
                case "Media": return "";
            }
            return "";
        }

        static string GlyphOf(Preset p) => p != null && glyphs.TryGetValue(p.Id, out var g) ? g : GroupGlyph(p?.Group);

        /// <summary>What a preset sends, shown in small type next to it.</summary>
        static string Hint(Preset p)
        {
            if (p == null) return "";
            if (p.Vk != 0) return KeyNames.Combo(p.Mods, p.Vk).Replace("Equals", "=");
            switch (p.Special)
            {
                case "lock": return "Win + L";
                case "app:sleep": return "Screen off";
                case "app:brightness_up":
                case "app:brightness_down": return "Laptop screen";
                case "app:fn_toggle": return "Media ↔ F1–F12";
            }
            return "";
        }

        FrameworkElement BuildPanel()
        {
            string group = "All";
            var search = new TextBox { PlaceholderText = "Search actions", Margin = new Thickness(12, 12, 12, 8) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, "Search actions");
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 0, 12, 10) };
            var chipScroll = new ScrollViewer
            {
                Content = chipRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Enabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Disabled,
            };
            var list = new StackPanel { Padding = new Thickness(6, 4, 6, 6) };
            var listScroll = new ScrollViewer { Content = list, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            var chips = new List<ToggleButton>();
            void refill()
            {
                list.Children.Clear();
                string q = (search.Text ?? "").Trim();
                bool any = false;
                if (q.Length == 0 && group == "All" && !spec.IsDefault && spec.Kind != "none")
                {
                    list.Children.Add(Header("Current"));
                    var cur = spec.Kind == "preset" ? Presets.Get(spec.Preset) : null;
                    list.Children.Add(Item(cur != null ? GlyphOf(cur) : CustomGlyph(spec.Kind), Presets.Describe(spec), HintOf(spec), true,
                        () => { if (spec.Kind == "preset") Pick(spec); else Custom(spec.Kind); }));
                }
                foreach (var g in Presets.All.GroupBy(p => p.Group))
                {
                    if (group != "All" && g.Key != group) continue;
                    var matches = g.Where(p => q.Length == 0
                        || p.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || g.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || Hint(p).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    if (matches.Count == 0) continue;
                    any = true;
                    list.Children.Add(Header(g.Key));
                    foreach (var p in matches)
                    {
                        var id = p.Id;
                        bool current = spec.Kind == "preset" && spec.Preset == id;
                        list.Children.Add(Item(GlyphOf(p), p.Label, Hint(p), current, () => Pick(ActionSpec.Of(id))));
                    }
                }
                if (!any && q.Length > 0)
                {
                    var none = Ui.Text("No actions match “" + q + "”. Try a keyboard shortcut instead.", "SecondaryBody");
                    none.Margin = new Thickness(10, 12, 10, 12);
                    list.Children.Add(none);
                }
            }

            foreach (var name in new[] { "All" }.Concat(Presets.All.Select(p => p.Group).Distinct()))
            {
                var g = name;
                var chip = new ToggleButton { Content = name == "Browser & tabs" ? "Browser" : name, IsChecked = name == "All", CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 3, 12, 4), MinHeight = 28, FontSize = 12 };
                chip.Click += (s, e) =>
                {
                    group = g;
                    foreach (var c in chips) c.IsChecked = ReferenceEquals(c, s);
                    refill();
                };
                chips.Add(chip);
                chipRow.Children.Add(chip);
            }
            search.TextChanged += (s, e) => refill();
            refill();

            var footer = new StackPanel { Padding = new Thickness(6) };
            footer.Children.Add(Item("", spec.Kind == "keys" ? "Keyboard shortcut: " + KeyNames.Combo(spec.Mods, spec.Vk) + "…" : "Keyboard shortcut…", "", false, () => Custom("keys"), chevron: true));
            footer.Children.Add(Item("", spec.Kind == "launch" ? Presets.Describe(spec) + "…" : "Open an app, file or website…", "", false, () => Custom("launch"), chevron: true));
            footer.Children.Add(Item("", spec.Kind == "text" ? Presets.Describe(spec) + "…" : "Type text…", "", false, () => Custom("text"), chevron: true));
            var bottom = new Grid { ColumnSpacing = 6, Margin = new Thickness(4, 6, 4, 2) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (allowNothing) bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var def = new Button { Content = (spec.IsDefault ? "✓ " : "") + defaultLabel, HorizontalAlignment = HorizontalAlignment.Stretch };
            def.Click += (s, e) => Pick(ActionSpec.Default());
            bottom.Children.Add(def);
            if (allowNothing)
            {
                var nothing = new Button { Content = (spec.Kind == "none" ? "✓ " : "") + "Do nothing", HorizontalAlignment = HorizontalAlignment.Stretch };
                nothing.Click += (s, e) => Pick(ActionSpec.Nothing());
                Grid.SetColumn(nothing, 1);
                bottom.Children.Add(nothing);
            }
            footer.Children.Add(bottom);

            var root = new StackPanel { Width = 400 };
            root.Children.Add(search);
            root.Children.Add(chipScroll);
            root.Children.Add(Divider());
            root.Children.Add(listScroll);
            root.Children.Add(Divider());
            root.Children.Add(footer);
            return root;
        }

        static string CustomGlyph(string kind) => kind == "keys" ? "" : kind == "launch" ? "" : kind == "text" ? "" : "";

        static Border Divider() => new Border { Height = 1, Background = Ui.Brush("DividerStrokeColorDefaultBrush") };

        static TextBlock Header(string text) => new TextBlock
        {
            Text = text, Style = Ui.Style("CaptionTextBlockStyle"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Ui.Brush("TextFillColorSecondaryBrush"), Margin = new Thickness(10, 10, 10, 4),
        };

        static Button Item(string glyph, string text, string keys, bool current, Action click, bool chevron = false)
        {
            var g = new Grid { ColumnSpacing = 12 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new FontIcon { Glyph = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(t, 1);
            g.Children.Add(icon);
            g.Children.Add(t);
            if (!string.IsNullOrEmpty(keys))
            {
                var k = new Border
                {
                    Background = Ui.Brush("SubtleFillColorSecondaryBrush"), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = keys, FontSize = 11, Foreground = Ui.Brush("TextFillColorSecondaryBrush") },
                };
                Grid.SetColumn(k, 2);
                g.Children.Add(k);
            }
            if (current || chevron)
            {
                var mark = new FontIcon { Glyph = current ? Glyph.Check : "", FontSize = current ? 14 : 12, VerticalAlignment = VerticalAlignment.Center };
                if (current) mark.Foreground = Ui.Brush("AccentTextFillColorPrimaryBrush");
                Grid.SetColumn(mark, 3);
                g.Children.Add(mark);
            }
            if (current)
            {
                icon.Foreground = Ui.Brush("AccentTextFillColorPrimaryBrush");
                t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }
            var b = new Button
            {
                Content = g, Style = Ui.Style("SubtleButton"),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 7, 10, 7), MinHeight = 36,
            };
            if (current) b.Background = Ui.Brush("SubtleFillColorSecondaryBrush");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, text + (string.IsNullOrEmpty(keys) ? "" : ", " + keys) + (current ? ", current" : ""));
            b.Click += (s, e) => click();
            return b;
        }

        void Pick(ActionSpec a)
        {
            flyout.Hide();
            Set(a);
        }

        async void Custom(string kind)
        {
            flyout.Hide();
            if (InlineCustom != null) { InlineCustom(kind); return; }
            var result = await Dialogs.EditCustomAsync(store, kind, spec.Kind == kind ? spec : null);
            if (result != null) Set(result);
            else Value = spec; // restore the label
        }
    }

    /// <summary>Records a key or shortcut through the engine's keyboard hook, so Win-combos and media keys work too.</summary>
    sealed partial class KeyRecorder : StackPanel
    {
        readonly Store store;
        readonly Border box;
        readonly Button again;
        bool recording;
        public int Mods { get; private set; }
        public int Vk { get; private set; }
        public bool HasValue => Vk != 0;
        public event Action Changed;

        public KeyRecorder(Store store)
        {
            this.store = store;
            Spacing = 8;
            box = new Border
            {
                Background = Ui.Brush("SubtleFillColorSecondaryBrush"), CornerRadius = new CornerRadius(6), MinHeight = 72,
                Padding = new Thickness(16, 12, 16, 12), BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1)
            };
            again = Ui.Button("Record again", Glyph.Refresh, false, (s, e) => Start());
            again.Visibility = Visibility.Collapsed;
            Children.Add(box);
            Children.Add(Ui.Text("Esc cancels recording.", "Secondary"));
            Children.Add(again);
            Placeholder("Click Record, then press the keys.");
            store.Captured += OnCaptured;
            Unloaded += (s, e) => Stop();
        }

        public void SetInitial(int mods, int vk)
        {
            Mods = mods; Vk = vk;
            Show();
            again.Visibility = Visibility.Visible;
        }

        void Placeholder(string text)
        {
            var t = Ui.Text(text, "SecondaryBody");
            t.VerticalAlignment = VerticalAlignment.Center;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            box.Child = t;
        }

        void Show()
        {
            var caps = Ui.KeyCaps(Mods, Vk, large: true);
            caps.HorizontalAlignment = HorizontalAlignment.Center;
            box.Child = caps;
        }

        public async void Start()
        {
            again.Visibility = Visibility.Collapsed;
            Placeholder("Waiting for a key…");
            var r = await store.Send("capture");
            if (r.Error != null)
            {
                Placeholder(r.Error == "not connected" || r.Error == "disconnected"
                    ? "Recording needs LiteOptions running in the background."
                    : r.Error);
                again.Visibility = Visibility.Visible;
                return;
            }
            recording = true;
        }

        public void Stop()
        {
            if (!recording) return;
            recording = false;
            _ = store.Send("cancelCapture");
        }

        void OnCaptured(int mods, int vk)
        {
            if (!recording) return;
            recording = false;
            again.Visibility = Visibility.Visible;
            if (vk == 0)
            {
                if (Vk != 0) Show(); else Placeholder("Cancelled. Click Record again.");
                return;
            }
            Mods = mods; Vk = vk;
            Show();
            Changed?.Invoke();
        }

        public void Detach() { Stop(); store.Captured -= OnCaptured; }
    }

    /// <summary>Editor for the three custom kinds of action: a shortcut to send, something to open, or text to type.</summary>
    sealed partial class CustomEditor : StackPanel
    {
        readonly Store store;
        string kind;
        KeyRecorder recorder;
        TextBox target, args, text;
        public event Action Changed;

        public CustomEditor(Store store)
        {
            this.store = store;
            Spacing = 8;
        }

        public string Kind => kind;

        public static string TitleOf(string kind) =>
            kind == "keys" ? "Keyboard shortcut" : kind == "launch" ? "Open an app, file, folder or website" : "Type text";

        public void Load(string kind, ActionSpec start)
        {
            recorder?.Detach();
            recorder = null;
            Children.Clear();
            this.kind = kind;
            if (kind == "keys")
            {
                Children.Add(Ui.Text("Press the shortcut this should send, for example Ctrl + Shift + V."));
                recorder = new KeyRecorder(store);
                if (start != null && start.Vk != 0) recorder.SetInitial(start.Mods, start.Vk);
                recorder.Changed += () => Changed?.Invoke();
                Children.Add(recorder);
                recorder.Loaded += (s, e) => { if (!recorder.HasValue) recorder.Start(); };
            }
            else if (kind == "launch")
            {
                Children.Add(Ui.Text("A program, document or folder on this PC, or a web address starting with https://"));
                target = new TextBox { PlaceholderText = @"C:\…  or  https://…", Text = start?.Target ?? "" };
                target.TextChanged += (s, e) => Changed?.Invoke();
                Children.Add(target);
                Children.Add(Ui.Row(8,
                    Ui.Button("Choose a file…", Glyph.OpenFile, false, async (s, e) =>
                    {
                        var p = new FileOpenPicker();
                        WinRT.Interop.InitializeWithWindow.Initialize(p, MainWindow.Hwnd);
                        p.FileTypeFilter.Add("*");
                        var f = await p.PickSingleFileAsync();
                        if (f != null) target.Text = f.Path;
                    }),
                    Ui.Button("Choose a folder…", Glyph.Folder, false, async (s, e) =>
                    {
                        var p = new FolderPicker();
                        WinRT.Interop.InitializeWithWindow.Initialize(p, MainWindow.Hwnd);
                        p.FileTypeFilter.Add("*");
                        var f = await p.PickSingleFolderAsync();
                        if (f != null) target.Text = f.Path;
                    })));
                args = new TextBox { Header = "Arguments (optional)", Text = start?.Args ?? "", Margin = new Thickness(0, 8, 0, 0) };
                Children.Add(args);
            }
            else
            {
                Children.Add(Ui.Text("Typed wherever your cursor is, for example your email signature or a standard reply."));
                text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 130, Text = start?.Text ?? "" };
                text.TextChanged += (s, e) => Changed?.Invoke();
                Children.Add(text);
            }
            Changed?.Invoke();
        }

        /// <summary>The action, or null while it isn't complete yet.</summary>
        public ActionSpec Result
        {
            get
            {
                if (kind == "keys") return recorder != null && recorder.HasValue ? ActionSpec.Keys(recorder.Mods, recorder.Vk) : null;
                if (kind == "launch") return target != null && target.Text.Trim().Length > 0 ? new ActionSpec { Kind = "launch", Target = target.Text.Trim(), Args = args.Text.Trim() } : null;
                if (kind == "text") return text != null && text.Text.Length > 0 ? new ActionSpec { Kind = "text", Text = text.Text.Replace("\r\n", "\n").Replace("\r", "\n") } : null;
                return null;
            }
        }

        public void Stop() => recorder?.Detach();
    }

    static class Dialogs
    {
        static readonly HashSet<int> typingKeys = new HashSet<int> { Vk.Space, Vk.Enter, Vk.Back, Vk.Tab, Vk.Delete, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE };

        public static bool IsTypingKey(int mods, int vk) => mods == 0 && ((vk >= 0x30 && vk <= 0x5A) || typingKeys.Contains(vk));

        /// <summary>Edit a shortcut / launch / text action in its own dialog.</summary>
        public static async Task<ActionSpec> EditCustomAsync(Store store, string kind, ActionSpec start)
        {
            var editor = new CustomEditor(store);
            var d = Ui.Dialog(CustomEditor.TitleOf(kind), editor, "Save");
            editor.Changed += () => d.IsPrimaryButtonEnabled = editor.Result != null;
            editor.Load(kind, start);
            var r = await Ui.ShowAsync(d);
            editor.Stop();
            return r == ContentDialogResult.Primary ? editor.Result : null;
        }

        /// <summary>Step one of adding a remap: which key? Returns null when cancelled.</summary>
        public static async Task<(int mods, int vk)?> CaptureKeyAsync(Store store)
        {
            var rec = new KeyRecorder(store);
            var panel = Ui.Column(12, Ui.Text("Press the key, or key combination, you want to change."), rec);
            var d = Ui.Dialog("Choose a key", panel, "Next");
            d.IsPrimaryButtonEnabled = false;
            rec.Changed += () => d.IsPrimaryButtonEnabled = rec.HasValue;
            d.Opened += (s, e) => rec.Start();
            var r = await Ui.ShowAsync(d);
            rec.Detach();
            if (r != ContentDialogResult.Primary || !rec.HasValue) return null;
            return (rec.Mods, rec.Vk);
        }

        /// <summary>What should this key do? The picker and any custom editor live in the same dialog.</summary>
        public static async Task<ActionSpec> ChooseActionAsync(Store store, int mods, int vk, ActionSpec current)
        {
            var picker = new ActionPicker(store, "Leave the key as it is", true) { HorizontalAlignment = HorizontalAlignment.Stretch };
            picker.Value = current ?? ActionSpec.Of("play_pause");
            var editor = new CustomEditor(store) { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            var panel = Ui.Column(8,
                Ui.Text("When you press", "Secondary"),
                Ui.KeyCaps(mods, vk),
                new Border { Height = 6 },
                Ui.Text("LiteOptions will", "Secondary"),
                picker,
                editor);
            var d = Ui.Dialog("What should this key do?", panel, "Save");

            void validate() => d.IsPrimaryButtonEnabled = editor.Visibility == Visibility.Collapsed || editor.Result != null;
            picker.Changed += a => { editor.Stop(); editor.Visibility = Visibility.Collapsed; validate(); };
            picker.InlineCustom = kind =>
            {
                var v = picker.Value;
                editor.Load(kind, v.Kind == kind ? v : null);
                editor.Visibility = Visibility.Visible;
                picker.ShowLabel(CustomEditor.TitleOf(kind) + "…");
                validate();
            };
            editor.Changed += validate;
            if (current != null && (current.Kind == "keys" || current.Kind == "launch" || current.Kind == "text")) picker.InlineCustom(current.Kind);

            var r = await Ui.ShowAsync(d);
            editor.Stop();
            if (r != ContentDialogResult.Primary) return null;
            return editor.Visibility == Visibility.Visible ? editor.Result : picker.Value;
        }

        sealed class AppItem { public string Exe, Title, Path; }

        /// <summary>Pick an app that's open now, or browse for its .exe. Returns the lower-case exe name.</summary>
        public static async Task<string> AddAppAsync(IEnumerable<string> existing)
        {
            var skip = new HashSet<string>(existing ?? Enumerable.Empty<string>())
                { "liteoptions.exe", "liteoptions.settings.exe", "explorer.exe", "applicationframehost.exe", "textinputhost.exe", "searchhost.exe", "shellexperiencehost.exe", "systemsettings.exe" };
            var apps = await Task.Run(() =>
            {
                var found = new SortedDictionary<string, AppItem>();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                        string path = W32.ExePath(p.Id);
                        string exe = path != null ? System.IO.Path.GetFileName(path).ToLowerInvariant() : p.ProcessName.ToLowerInvariant() + ".exe";
                        if (skip.Contains(exe) || found.ContainsKey(exe)) continue;
                        found[exe] = new AppItem { Exe = exe, Title = p.MainWindowTitle, Path = path };
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                return found.Values.ToList();
            });

            string chosen = null;
            var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 300 };
            foreach (var a in apps)
            {
                var img = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
                if (a.Path != null) LoadIcon(img, a.Path);
                var texts = Ui.Column(0, Ui.Text(a.Exe, "BodyStrongTextBlockStyle", wrap: false), Ui.Text(a.Title, "Secondary", wrap: false));
                var row = new Grid { ColumnSpacing = 14, Padding = new Thickness(0, 6, 0, 6), Tag = a.Exe };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(texts, 1);
                row.Children.Add(img);
                row.Children.Add(texts);
                list.Items.Add(row);
            }
            var browse = Ui.Button("Browse for an .exe…", Glyph.Folder);
            var panel = Ui.Column(10,
                Ui.Text("Pick an app that's open now, or browse for its .exe. Settings you make for it only apply while it's in front."),
                apps.Count > 0 ? (UIElement)list : Ui.Text("No other apps are open right now.", "SecondaryBody"),
                browse);
            var d = Ui.Dialog("Add an app", panel, "Add");
            d.IsPrimaryButtonEnabled = false;
            list.SelectionChanged += (s, e) => d.IsPrimaryButtonEnabled = list.SelectedItem != null;
            list.DoubleTapped += (s, e) => { if (list.SelectedItem != null) { chosen = (string)((Grid)list.SelectedItem).Tag; d.Hide(); } };
            browse.Click += async (s, e) =>
            {
                var p = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(p, MainWindow.Hwnd);
                p.FileTypeFilter.Add(".exe");
                var f = await p.PickSingleFileAsync();
                if (f != null) { chosen = System.IO.Path.GetFileName(f.Path).ToLowerInvariant(); d.Hide(); }
            };
            var r = await Ui.ShowAsync(d);
            if (r == ContentDialogResult.Primary && list.SelectedItem is Grid g) chosen = (string)g.Tag;
            return chosen;
        }

        static async void LoadIcon(Image img, string path)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using (var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 32))
                {
                    var bmp = new BitmapImage();
                    await bmp.SetSourceAsync(thumb);
                    img.Source = bmp;
                }
            }
            catch { }
        }
    }
}
