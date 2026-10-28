using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LiteOptions.Input;

namespace LiteOptions.UI
{
    /// <summary>Action chooser: a drop-down whose menu groups the presets into sub-menus, plus custom actions.</summary>
    sealed class ActionPicker : DropDown
    {
        readonly string defaultLabel;
        readonly bool allowNothing;
        readonly Func<InputHooks> hooks;
        ActionSpec spec = ActionSpec.Default();
        public event EventHandler ActionChanged;

        public ActionPicker(string defaultLabel, bool allowNothing, Func<InputHooks> hooks)
        {
            this.defaultLabel = defaultLabel;
            this.allowNothing = allowNothing;
            this.hooks = hooks;
            Width = Theme.S(300);
            Text = defaultLabel;
        }

        public ActionSpec Value
        {
            get => spec.Clone();
            set { spec = value?.Clone() ?? ActionSpec.Default(); Text = Presets.Describe(spec, defaultLabel); }
        }

        void Set(ActionSpec a)
        {
            Value = a;
            ActionChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override ContextMenuStrip BuildMenu()
        {
            var m = Menus.Create();
            m.Items.Add(Menus.Item(defaultLabel, () => Set(ActionSpec.Default()), spec.IsDefault));
            if (allowNothing) m.Items.Add(Menus.Item("Do nothing", () => Set(ActionSpec.Nothing()), spec.Kind == "none"));
            m.Items.Add(Menus.Separator());
            foreach (var group in Presets.All.GroupBy(p => p.Group))
            {
                bool current = spec.Kind == "preset" && group.Any(p => p.Id == spec.Preset);
                var children = group.Select(p =>
                {
                    var id = p.Id;
                    return (ToolStripItem)Menus.Item(p.Label, () => Set(ActionSpec.Of(id)), spec.Kind == "preset" && spec.Preset == id);
                });
                m.Items.Add(Menus.Submenu(group.Key, children, current));
            }
            m.Items.Add(Menus.Separator());
            m.Items.Add(Menus.Item(spec.Kind == "keys" ? "Keyboard shortcut: " + KeyNames.Combo(spec.Mods, spec.Vk) + "…" : "Keyboard shortcut…", () => Later("keys"), spec.Kind == "keys"));
            m.Items.Add(Menus.Item(spec.Kind == "launch" ? Presets.Describe(spec) + "…" : "Open an app, file, folder or website…", () => Later("launch"), spec.Kind == "launch"));
            m.Items.Add(Menus.Item(spec.Kind == "text" ? Presets.Describe(spec) + "…" : "Type text…", () => Later("text"), spec.Kind == "text"));
            return m;
        }

        void Later(string kind) => BeginInvoke((Action)(() => EditCustom(kind)));

        void EditCustom(string kind)
        {
            var owner = FindForm();
            var start = spec.Kind == kind ? spec : null;
            ActionSpec result = null;
            if (kind == "keys")
            {
                using (var d = new CaptureDialog("Keyboard shortcut", "Press the shortcut this should send, for example Ctrl + Shift + V.", hooks()))
                {
                    if (start != null) d.SetInitial(start.Mods, start.Vk);
                    if (d.ShowDialog(owner) == DialogResult.OK) result = ActionSpec.Keys(d.Mods, d.Vk);
                }
            }
            else if (kind == "launch")
            {
                using (var d = new LaunchDialog(start?.Target, start?.Args))
                    if (d.ShowDialog(owner) == DialogResult.OK) result = new ActionSpec { Kind = "launch", Target = d.Target, Args = d.Arguments };
            }
            else if (kind == "text")
            {
                using (var d = new TextDialog(start?.Text))
                    if (d.ShowDialog(owner) == DialogResult.OK) result = new ActionSpec { Kind = "text", Text = d.Value };
            }
            if (result != null) Set(result);
        }
    }

    // ───────────────────────── dialogs ─────────────────────────

    class DialogBase : Form
    {
        protected readonly TableLayoutPanel Body;
        protected readonly FluentButton Ok, Cancel;
        protected readonly FlowLayoutPanel Footer;

        public DialogBase(string title, string okText = "OK", string cancelText = "Cancel", ButtonKind okKind = ButtonKind.Primary)
        {
            Text = title;
            Font = Theme.Body;
            BackColor = Theme.CardBg;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Icon = TrayApp.AppIcon;

            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.CardBg };
            Body = new TableLayoutPanel
            {
                ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                Padding = Theme.P(24, 20, 24, 22), Margin = Padding.Empty, BackColor = Theme.CardBg, MinimumSize = new Size(Theme.S(480), 0)
            };
            var t = UIx.Label(title, Theme.Subtitle);
            t.Margin = Theme.P(0, 0, 0, 12);
            Body.Controls.Add(t);

            Footer = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                Padding = Theme.P(24, 18, 16, 18), Margin = Padding.Empty, BackColor = Theme.WindowBg, WrapContents = false
            };
            Cancel = new FluentButton(cancelText) { DialogResult = DialogResult.Cancel, MinWidth = Theme.S(110) };
            Ok = new FluentButton(okText, okKind) { DialogResult = DialogResult.OK, MinWidth = Theme.S(110) };
            Cancel.Text = cancelText; Ok.Text = okText; // re-measure with MinWidth
            if (cancelText != null) Footer.Controls.Add(Cancel);
            Footer.Controls.Add(Ok);

            root.Controls.Add(Body, 0, 0);
            root.Controls.Add(Footer, 0, 1);
            Controls.Add(root);
            AcceptButton = Ok;
            CancelButton = Cancel;
            HandleCreated += (s, e) => Theme.ApplyWindowChrome(this);
        }

        protected T Add<T>(T c, Padding? margin = null) where T : Control
        {
            if (margin.HasValue) c.Margin = margin.Value;
            if (!(c is Label) && !(c is FlowLayoutPanel) && !(c is FluentButton)) c.Dock = DockStyle.Fill;
            Body.Controls.Add(c);
            return c;
        }

        protected Label Text2(string text, bool muted = true)
        {
            var l = UIx.Label(text, muted ? Theme.Caption : Theme.Body, muted ? Theme.Muted : Theme.Text);
            l.MaximumSize = new Size(Theme.S(460), 0);
            return Add(l, Theme.P(0, 0, 0, 8));
        }
    }

    static class Dialogs
    {
        public static bool Confirm(IWin32Window owner, string title, string message, string okText, bool danger = false)
        {
            using (var d = new MessageDialog(title, message, okText, "Cancel", danger ? ButtonKind.Danger : ButtonKind.Primary))
            {
                Standalone(d, owner);
                return d.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public static void Info(IWin32Window owner, string title, string message)
        {
            using (var d = new MessageDialog(title, message, "OK", null, ButtonKind.Primary)) { Standalone(d, owner); d.ShowDialog(owner); }
        }

        public static void Standalone(Form d, IWin32Window owner)
        {
            if (owner != null) return;
            d.StartPosition = FormStartPosition.CenterScreen;
            d.ShowInTaskbar = true;
            d.TopMost = true;
        }

        sealed class MessageDialog : DialogBase
        {
            public MessageDialog(string title, string message, string ok, string cancel, ButtonKind kind) : base(title, ok, cancel ?? "Close", kind)
            {
                Text2(message, muted: false);
                if (cancel == null) Footer.Controls.Remove(Cancel);
            }
        }
    }

    /// <summary>Records a key or shortcut through the global hook, so Win-key combos and media keys work too.</summary>
    sealed class CaptureDialog : DialogBase
    {
        readonly InputHooks hooks;
        readonly KeyComboView view;
        public int Mods { get; private set; }
        public int Vk { get; private set; }

        public CaptureDialog(string title, string prompt, InputHooks hooks) : base(title)
        {
            this.hooks = hooks;
            Text2(prompt, muted: false);
            var box = new Panel { Height = Theme.S(76), BackColor = Theme.WindowBg, Padding = Theme.P(16, 12, 16, 12), Margin = Theme.P(0, 6, 0, 8) };
            view = new KeyComboView { Dock = DockStyle.Fill, BackColor = Theme.WindowBg };
            box.Controls.Add(view);
            Add(box);
            Text2("Esc cancels recording.");
            var again = new FluentButton("Record again", ButtonKind.Subtle, Theme.IcRefresh, (s, e) => Start());
            Footer.Controls.Add(again);
            Ok.Enabled = false;
            Shown += (s, e) => Start();
            FormClosed += (s, e) => hooks?.CancelCapture();
        }

        public void SetInitial(int mods, int vk)
        {
            Mods = mods; Vk = vk;
            view.Show(mods, vk);
            Ok.Enabled = true;
        }

        void Start()
        {
            if (hooks == null) { view.Placeholder("Recording needs Windows."); return; }
            view.Placeholder("Waiting for a key…");
            hooks.BeginCapture((mods, vk) =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (vk == 0)
                        {
                            if (Vk != 0) view.Show(Mods, Vk); else view.Placeholder("Cancelled. Click Record again.");
                            return;
                        }
                        Mods = mods; Vk = vk;
                        view.Show(mods, vk);
                        Ok.Enabled = true;
                        Ok.Focus();
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }
    }

    sealed class LaunchDialog : DialogBase
    {
        readonly TextField target, args;
        public string Target => target.Box.Text.Trim();
        public string Arguments => args.Box.Text.Trim();

        public LaunchDialog(string t, string a) : base("Open an app, file, folder or website")
        {
            Text2("A program, document or folder on this PC, or a web address starting with https://");
            target = Add(new TextField(), Theme.P(0, 2, 0, 8));
            target.Box.Text = t ?? "";
            Add(UIx.Row(
                new FluentButton("Choose a file…", ButtonKind.Standard, Theme.IcFolder, (s, e) =>
                {
                    using (var d = new OpenFileDialog { Filter = "Programs and documents|*.*", Title = "Choose what to open" })
                        if (d.ShowDialog(this) == DialogResult.OK) target.Box.Text = d.FileName;
                }),
                new FluentButton("Choose a folder…", ButtonKind.Standard, null, (s, e) =>
                {
                    using (var d = new FolderBrowserDialog())
                        if (d.ShowDialog(this) == DialogResult.OK) target.Box.Text = d.SelectedPath;
                })), Theme.P(0, 0, 0, 14));
            Text2("Arguments (optional)");
            args = Add(new TextField(), Theme.P(0, 2, 0, 0));
            args.Box.Text = a ?? "";
            Ok.Click += (s, e) => { if (Target.Length == 0) DialogResult = DialogResult.None; };
            Shown += (s, e) => target.Box.Focus();
        }
    }

    sealed class TextDialog : DialogBase
    {
        readonly TextField box;
        public string Value => box.Box.Text;

        public TextDialog(string text) : base("Type text")
        {
            Text2("Typed wherever your cursor is, for example your email signature or a standard reply.");
            box = Add(new TextField(multiline: true, height: 130), Theme.P(0, 2, 0, 0));
            box.Box.Text = text ?? "";
            Ok.Click += (s, e) => { if (box.Box.Text.Length == 0) DialogResult = DialogResult.None; };
            AcceptButton = null;
            Shown += (s, e) => box.Box.Focus();
        }
    }

    sealed class ChooseActionDialog : DialogBase
    {
        readonly ActionPicker picker;
        public ActionSpec Value => picker.Value;

        public ChooseActionDialog(int mods, int vk, ActionSpec current, Func<InputHooks> hooks) : base("What should this key do?", "Save")
        {
            Text2("When you press");
            var keys = new KeyComboView { BackColor = Theme.CardBg, Margin = Theme.P(0, 0, 0, 8) };
            keys.Show(mods, vk);
            Add(keys);
            Text2("LiteOptions will");
            picker = Add(new ActionPicker("Leave the key as it is", true, hooks), Theme.P(0, 2, 0, 0));
            picker.Value = current ?? ActionSpec.Of("play_pause");
        }
    }

    sealed class AddAppDialog : DialogBase
    {
        sealed class AppItem { public string Exe, Title, Path; public Bitmap Icon; }
        readonly ListBox list;
        public string Exe { get; private set; }

        public AddAppDialog(IEnumerable<string> existing) : base("Add an app", "Add")
        {
            Text2("Pick an app that's open now, or browse for its .exe. Settings you make for it only apply while it's in front.");
            var skip = new HashSet<string>(existing ?? Enumerable.Empty<string>())
                { "liteoptions.exe", "explorer.exe", "applicationframehost.exe", "textinputhost.exe", "searchhost.exe", "shellexperiencehost.exe", "systemsettings.exe" };
            var apps = new SortedDictionary<string, AppItem>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                    string path = InputHooks.ExePath((uint)p.Id);
                    string exe = path != null ? System.IO.Path.GetFileName(path).ToLowerInvariant() : p.ProcessName.ToLowerInvariant() + ".exe";
                    if (skip.Contains(exe) || apps.ContainsKey(exe)) continue;
                    Bitmap icon = null;
                    try { if (path != null) using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(path)) icon = new Bitmap(ic.ToBitmap(), Theme.S(24), Theme.S(24)); } catch { }
                    apps[exe] = new AppItem { Exe = exe, Title = p.MainWindowTitle, Path = path, Icon = icon };
                }
                catch { }
                finally { p.Dispose(); }
            }

            var frame = new Panel { Height = Theme.S(280), Padding = new Padding(1), BackColor = Theme.ControlBorder, Margin = Theme.P(0, 4, 0, 12) };
            list = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(46),
                BackColor = Theme.ControlBg, ForeColor = Theme.Text, IntegralHeight = false
            };
            foreach (var a in apps.Values) list.Items.Add(a);
            list.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                var a = (AppItem)list.Items[e.Index];
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(sel ? Theme.SubtleHover : Theme.ControlBg)) e.Graphics.FillRectangle(b, e.Bounds);
                if (sel) Theme.FillRounded(e.Graphics, Theme.Accent, new RectangleF(e.Bounds.X + Theme.Sf(3), e.Bounds.Y + e.Bounds.Height / 2f - Theme.Sf(8), Theme.Sf(3), Theme.Sf(16)), Theme.Sf(1.5f));
                if (a.Icon != null) e.Graphics.DrawImage(a.Icon, e.Bounds.X + Theme.S(14), e.Bounds.Y + (e.Bounds.Height - a.Icon.Height) / 2);
                int x = e.Bounds.X + Theme.S(50);
                TextRenderer.DrawText(e.Graphics, a.Exe, Theme.BodyStrong, new Rectangle(x, e.Bounds.Y + Theme.S(4), e.Bounds.Width - x - Theme.S(8), Theme.S(20)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics, a.Title, Theme.Caption, new Rectangle(x, e.Bounds.Y + Theme.S(24), e.Bounds.Width - x - Theme.S(8), Theme.S(18)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            list.DoubleClick += (s, e) => { if (list.SelectedItem != null) { DialogResult = DialogResult.OK; Close(); } };
            Theme.DarkScrollbars(list);
            frame.Controls.Add(list);
            Add(frame);
            Add(UIx.Row(new FluentButton("Browse for an .exe…", ButtonKind.Standard, Theme.IcFolder, (s, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe" })
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        Exe = System.IO.Path.GetFileName(d.FileName).ToLowerInvariant();
                        DialogResult = DialogResult.OK;
                        Close();
                    }
            })));
            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK && Exe == null)
                {
                    if (list.SelectedItem == null) { e.Cancel = true; return; }
                    Exe = ((AppItem)list.SelectedItem).Exe;
                }
            };
        }
    }

    /// <summary>Shown when the exe is started from outside the install folder.</summary>
    sealed class InstallDialog : DialogBase
    {
        public InstallDialog(Version installed, string oldCopy)
            : base(installed == null ? "Install LiteOptions " + Installer.VersionText
                   : installed < Installer.CurrentVersion ? "Update LiteOptions to " + Installer.VersionText
                   : "LiteOptions is already installed",
                   installed == null ? "Install" : installed < Installer.CurrentVersion ? "Update" : "Reinstall")
        {
            var hero = new PictureBox { Image = TrayApp.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(Theme.S(48), Theme.S(48)), Margin = Theme.P(0, 0, 0, 12) };
            Body.Controls.Add(hero);
            Body.Controls.SetChildIndex(hero, 0);
            if (installed == null)
                Text2("Installs for your Windows account only (no administrator needed), adds LiteOptions to the Start menu and Installed apps, and starts it with Windows.", muted: false);
            else if (installed < Installer.CurrentVersion)
                Text2($"Replaces version {installed.Major}.{installed.Minor}.{installed.Build}. Your settings are kept. LiteOptions restarts by itself.", muted: false);
            else
                Text2($"Version {installed.Major}.{installed.Minor}.{installed.Build} is installed. Reinstalling keeps your settings.", muted: false);
            if (oldCopy != null)
                Text2("Your earlier copy at " + oldCopy + " will be removed; the installed one takes over.");
            Dialogs.Standalone(this, null);
        }
    }
}
