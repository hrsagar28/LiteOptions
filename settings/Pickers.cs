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
    /// <summary>The action chooser: a drop-down button whose menu groups the presets, plus custom actions.</summary>
    /// <remarks>Wraps a DropDownButton rather than deriving from it: a subclass loses the button's ▾ look.</remarks>
    sealed partial class ActionPicker : UserControl
    {
        readonly Store store;
        readonly string defaultLabel;
        readonly bool allowNothing;
        readonly TextBlock label;
        readonly DropDownButton button;
        ActionSpec spec = ActionSpec.Default();

        public event Action<ActionSpec> Changed;
        /// <summary>Set inside dialogs: custom actions are then edited in the dialog itself (WinUI shows one dialog at a time).</summary>
        public Action<string> InlineCustom;

        public ActionPicker(Store store, string defaultLabel, bool allowNothing = true)
        {
            this.store = store;
            this.defaultLabel = defaultLabel;
            this.allowNothing = allowNothing;
            IsTabStop = false;
            label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 250, TextAlignment = TextAlignment.Left };
            button = new DropDownButton { Content = label, MinWidth = 250, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            var f = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
            f.Opening += (s, e) => Build(f);
            button.Flyout = f;
            Content = button;
            Value = ActionSpec.Default();
        }

        /// <summary>Open the menu (used by the screenshot tour).</summary>
        public FlyoutBase OpenMenu()
        {
            button.Flyout.ShowAt(button);
            return button.Flyout;
        }

        public ActionSpec Value
        {
            get => spec.Clone();
            set { spec = value?.Clone() ?? ActionSpec.Default(); label.Text = Presets.Describe(spec, defaultLabel); }
        }

        /// <summary>Show a label without changing the value (used while a custom action is being edited inline).</summary>
        public void ShowLabel(string text) => label.Text = text;

        void Set(ActionSpec a)
        {
            Value = a;
            Changed?.Invoke(Value);
        }

        static string GroupGlyph(string group)
        {
            switch (group)
            {
                case "Mouse": return Glyph.Mouse;
                case "Windows": return "\uE782";
                case "LiteOptions": return Glyph.Keyboard;
                case "Editing": return Glyph.Edit;
                case "Browser & tabs": return "\uE774";
                case "Media": return "\uE8D6";
            }
            return null;
        }

        void Build(MenuFlyout f)
        {
            f.Items.Clear();
            f.Items.Add(Item(defaultLabel, spec.IsDefault, () => Set(ActionSpec.Default())));
            if (allowNothing) f.Items.Add(Item("Do nothing", spec.Kind == "none", () => Set(ActionSpec.Nothing())));
            f.Items.Add(new MenuFlyoutSeparator());
            foreach (var group in Presets.All.GroupBy(p => p.Group))
            {
                var sub = new MenuFlyoutSubItem { Text = group.Key };
                var g = GroupGlyph(group.Key);
                if (g != null) sub.Icon = new FontIcon { Glyph = g };
                foreach (var p in group)
                {
                    var id = p.Id;
                    sub.Items.Add(Item(p.Label, spec.Kind == "preset" && spec.Preset == id, () => Set(ActionSpec.Of(id))));
                }
                f.Items.Add(sub);
            }
            f.Items.Add(new MenuFlyoutSeparator());
            f.Items.Add(Item(spec.Kind == "keys" ? "Keyboard shortcut: " + KeyNames.Combo(spec.Mods, spec.Vk) + "…" : "Keyboard shortcut…", spec.Kind == "keys", () => Custom("keys")));
            f.Items.Add(Item(spec.Kind == "launch" ? Presets.Describe(spec) + "…" : "Open an app, file, folder or website…", spec.Kind == "launch", () => Custom("launch")));
            f.Items.Add(Item(spec.Kind == "text" ? Presets.Describe(spec) + "…" : "Type text…", spec.Kind == "text", () => Custom("text")));
        }

        static MenuFlyoutItemBase Item(string text, bool check, Action click)
        {
            var i = new ToggleMenuFlyoutItem { Text = text, IsChecked = check };
            i.Click += (s, e) => click();
            return i;
        }

        async void Custom(string kind)
        {
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
            var list = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = 300 };
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
