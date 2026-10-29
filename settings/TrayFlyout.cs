using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiteOptions.Ipc;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace LiteOptions.SettingsApp
{
    /// <summary>
    /// What a left click on the tray icon opens: a small panel above the taskbar, like Quick Settings.
    /// Both devices with their battery rings, four quick switches, the mouse sensitivity, and a way to quit.
    /// It hides as soon as you click anywhere else.
    /// </summary>
    public sealed partial class TrayFlyout : Window
    {
        const double PanelWidth = 360;
        readonly Store store;
        readonly Action<string> openMain;
        readonly Grid root;
        readonly Grid devices;
        readonly ToggleButton fnTile, smoothTile, popupsTile, pauseTile;
        readonly TextBlock fnTitle, fnState, smoothState, popupsState, pauseState, status, dpiValue;
        readonly Grid dpiRow;
        readonly Slider dpi;
        readonly Debouncer dpiSend = new Debouncer(180);
        readonly IntPtr hwnd;
        W32.POINT anchor;
        bool loading;

        public bool Showing { get; private set; }
        public DateTime HiddenAt { get; private set; } = DateTime.MinValue;
        public event Action Hidden;

        internal FrameworkElement RootElement => root;
        internal IntPtr Handle => hwnd;

        public TrayFlyout(Store store, Action<string> openMain)
        {
            this.store = store;
            this.openMain = openMain;
            Title = "LiteOptions";
            hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (DesktopAcrylicController.IsSupported()) SystemBackdrop = new DesktopAcrylicBackdrop();

            root = new Grid { Width = PanelWidth };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (!DesktopAcrylicController.IsSupported()) root.Background = Ui.Brush("SolidBackgroundFillColorBaseBrush");

            var body = new StackPanel { Spacing = 12, Padding = new Thickness(16, 14, 16, 16) };

            // ── header: logo, name, open the settings ──
            var logo = new Image { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            try { logo.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "logo.png"))); } catch { }
            var name = new TextBlock { Text = "LiteOptions", Style = Ui.Style("BodyStrongTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
            var open = Ui.IconButton(Glyph.Settings, "Open LiteOptions settings", (s, e) => openMain("mouse"));
            var head = new Grid { ColumnSpacing = 10 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(name, 1);
            Grid.SetColumn(open, 2);
            head.Children.Add(logo); head.Children.Add(name); head.Children.Add(open);
            body.Children.Add(head);

            // ── the two devices ──
            devices = new Grid { ColumnSpacing = 8 };
            devices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            devices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.Children.Add(devices);

            // ── quick switches ──
            var tiles = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fnTile = Tile(Glyph.Keyboard, "Media keys first", out fnTitle, out fnState);
            smoothTile = Tile(Glyph.Scroll, "Smooth scrolling", out _, out smoothState);
            popupsTile = Tile(Glyph.Message, "Pop-ups", out _, out popupsState);
            pauseTile = Tile(Glyph.Pause, "Pause remapping", out _, out pauseState);
            Place(tiles, fnTile, 0, 0); Place(tiles, smoothTile, 0, 1); Place(tiles, popupsTile, 1, 0); Place(tiles, pauseTile, 1, 1);
            body.Children.Add(tiles);

            fnTile.Click += async (s, e) =>
            {
                if (loading) return;
                string m = fnTile.IsChecked == true ? "media" : "standard";
                store.Settings.FnMode = m;
                Load();
                await store.Send("setFnMode", text: m);
                await store.RefreshAsync();
            };
            smoothTile.Click += (s, e) => { if (loading) return; store.Settings.SmoothScroll = smoothTile.IsChecked == true; store.Save(); Load(); };
            popupsTile.Click += (s, e) => { if (loading) return; store.Settings.ShowPopups = popupsTile.IsChecked == true; store.Save(); Load(); };
            pauseTile.Click += (s, e) => { if (loading) return; store.Settings.Paused = pauseTile.IsChecked == true; store.Save(); Load(); };

            // ── mouse sensitivity ──
            dpi = new Slider { VerticalAlignment = VerticalAlignment.Center, SnapsTo = SliderSnapsTo.StepValues, IsThumbToolTipEnabled = false };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dpi, "Mouse sensitivity");
            dpiValue = new TextBlock { Style = Ui.Style("CaptionTextBlockStyle"), Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            dpi.ValueChanged += (s, e) =>
            {
                dpiValue.Text = (int)dpi.Value + " DPI";
                if (loading) return;
                int v = (int)dpi.Value;
                dpiSend.Run(() => { store.Settings.MouseDpi = v; _ = store.Send("setDpi", v); });
            };
            dpiRow = new Grid { ColumnSpacing = 12, Padding = new Thickness(4, 0, 0, 0) };
            dpiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dpiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dpiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dpiIcon = Ui.Icon(Glyph.Speed, 16);
            ToolTipService.SetToolTip(dpiIcon, "Mouse sensitivity");
            Grid.SetColumn(dpi, 1);
            Grid.SetColumn(dpiValue, 2);
            dpiRow.Children.Add(dpiIcon); dpiRow.Children.Add(dpi); dpiRow.Children.Add(dpiValue);
            body.Children.Add(dpiRow);
            root.Children.Add(body);

            // ── footer: what's going on, and quit ──
            status = new TextBlock { Style = Ui.Style("Secondary"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            var quit = Ui.IconButton(Glyph.Power, "Quit LiteOptions", async (s, e) =>
            {
                HideFlyout();
                await store.Send("quit", timeoutMs: 2000);
                Application.Current.Exit();
            });
            var foot = new Grid
            {
                ColumnSpacing = 8, Padding = new Thickness(16, 6, 10, 6),
                Background = Ui.Brush("LayerOnAcrylicFillColorDefaultBrush"),
                BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(0, 1, 0, 0),
            };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(quit, 1);
            foot.Children.Add(status); foot.Children.Add(quit);
            Grid.SetRow(foot, 1);
            root.Children.Add(foot);

            root.KeyDown += (s, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; HideFlyout(); } };
            Content = root;

            // a borderless, always-on-top panel that stays out of Alt+Tab and the taskbar
            try
            {
                var pr = OverlappedPresenter.Create();
                pr.IsResizable = false; pr.IsMaximizable = false; pr.IsMinimizable = false; pr.IsAlwaysOnTop = true;
                pr.SetBorderAndTitleBar(true, false);
                AppWindow.SetPresenter(pr);
                AppWindow.IsShownInSwitchers = false;
                AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "app.ico"));
                int round = 2; // DWMWCP_ROUND
                W32.DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
            }
            catch (Exception ex) { try { Logger.Write("tray panel: " + ex.Message); } catch { } }

            Activated += (s, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated && Showing && Entry.ShotsDir == null) HideFlyout(); };

            store.SettingsChanged += () => { if (Showing) Load(); };
            store.DevicesChanged += () => { if (Showing) Load(); };
            store.SystemChanged += () => { if (Showing) Load(); };
        }

        static void Place(Grid g, FrameworkElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); g.Children.Add(e); }

        /// <summary>A quick-settings switch: icon on top, the name and its state underneath; filled with the accent colour when on.</summary>
        static ToggleButton Tile(string glyph, string title, out TextBlock titleText, out TextBlock state)
        {
            var icon = new FontIcon { Glyph = glyph, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Left };
            titleText = new TextBlock { Text = title, Style = Ui.Style("CaptionTextBlockStyle"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            state = new TextBlock { Style = Ui.Style("CaptionTextBlockStyle"), Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            texts.Children.Add(titleText);
            texts.Children.Add(state);
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(texts, 1);
            g.Children.Add(icon);
            g.Children.Add(texts);
            var b = new ToggleButton
            {
                Content = g,
                Height = 76,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(12, 10, 12, 10),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, title);
            return b;
        }

        /// <summary>A device: its photo in a battery ring, its name, and one line about it. Clicking it opens its page.</summary>
        UIElement DeviceTile(DeviceDto d, bool mouse, string detail)
        {
            var stack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            var ring = Ring.Device(d, mouse, 64, store.IsLow(d));
            ring.HorizontalAlignment = HorizontalAlignment.Center;
            ring.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(ring);
            string name = d?.Name ?? (mouse ? "Mouse" : "Keyboard");
            stack.Children.Add(new TextBlock { Text = ShortName(name), Style = Ui.Style("BodyStrongTextBlockStyle"), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            stack.Children.Add(new TextBlock { Text = detail, Style = Ui.Style("Secondary"), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            var b = new Button
            {
                Content = stack,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 12, 8, 10),
            };
            ToolTipService.SetToolTip(b, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, name + ", " + detail);
            b.Click += (s, e) => openMain(mouse ? "mouse" : "keys");
            return b;
        }

        /// <summary>"Pebble Mouse 2 M350s" → "Pebble Mouse 2"; "Keyboard K380" → "K380": room for two side by side.</summary>
        static string ShortName(string n)
        {
            if (string.IsNullOrEmpty(n)) return n;
            if (n.StartsWith("Keyboard ", StringComparison.OrdinalIgnoreCase)) n = n.Substring(9);
            var parts = n.Split(' ');
            if (parts.Length > 3 && parts[parts.Length - 1].Any(char.IsDigit) && parts[parts.Length - 1].Any(char.IsLetter)) n = string.Join(" ", parts.Take(parts.Length - 1));
            return n;
        }

        static string Battery(DeviceDto d) => d == null ? "Not found" : !d.Online ? "Asleep" : d.HasBattery ? d.BatteryText : "Connected";

        void Load()
        {
            loading = true;
            var s = store.Settings;
            var m = store.Mouse;
            var k = store.Keyboard;

            // devices
            devices.Children.Clear();
            int want = s.MouseDpi > 0 ? s.MouseDpi : m?.DpiCurrent ?? 0;
            string mouseDetail = Battery(m) + (m != null && m.Online && want > 0 && m.DpiMax > 0 ? " · " + want + " DPI" : "");
            bool? media = k != null && k.FnSupported && k.FnMedia >= 0 ? k.FnMedia == 1 : s.FnMode == "media" ? true : s.FnMode == "standard" ? false : (bool?)null;
            string keysDetail = Battery(k) + (k != null && k.Online && media != null ? (media == true ? " · media keys" : " · F1–F12") : "");
            var mt = DeviceTile(m, true, mouseDetail);
            var kt = DeviceTile(k, false, keysDetail);
            Grid.SetColumn(kt, 1);
            devices.Children.Add(mt);
            devices.Children.Add(kt);

            // top row
            bool fnOk = k != null && k.FnSupported;
            fnTile.IsEnabled = fnOk;
            fnTile.IsChecked = media == true;
            fnTitle.Text = media == false ? "F1–F12 first" : "Media keys first";
            var sc = s.Global.Keys.FirstOrDefault(b => b.Action?.Kind == "preset" && b.Action.Preset == "fn_toggle");
            fnState.Text = !fnOk ? (k == null ? "No keyboard" : "Not offered") : sc != null ? KeyNames.Combo(sc.Mods, sc.Vk) + " to switch" : media == null ? "Left as is" : "Top row";

            smoothTile.IsChecked = s.SmoothScroll;
            smoothState.Text = s.SmoothScroll ? "On" : "Off";
            popupsTile.IsChecked = s.ShowPopups;
            popupsState.Text = s.ShowPopups ? "On" : "Off";
            pauseTile.IsChecked = s.Paused;
            pauseState.Text = s.Paused ? "Buttons and keys as normal" : "Off";

            // sensitivity
            bool dpiOk = m != null && m.DpiMax > m.DpiMin;
            dpiRow.Visibility = dpiOk ? Visibility.Visible : Visibility.Collapsed;
            if (dpiOk)
            {
                int step = Math.Max(1, m.DpiStep);
                dpi.Minimum = m.DpiMin;
                dpi.Maximum = m.DpiMax;
                dpi.StepFrequency = step;
                dpi.SmallChange = step;
                dpi.LargeChange = step * 5;
                dpi.IsEnabled = m.Online;
                int v = want > 0 ? want : m.DpiDefault > 0 ? m.DpiDefault : m.DpiMin;
                v = Math.Max(m.DpiMin, Math.Min(m.DpiMax, m.DpiMin + (int)Math.Round((v - m.DpiMin) / (double)step) * step));
                if (!dpiSend.Waiting) dpi.Value = v;
                dpiValue.Text = (int)dpi.Value + " DPI";
            }

            string logi = store.System.OptionsPlus;
            status.Text = !store.Connected ? "Not running in the background"
                        : (s.Paused ? "Remapping paused" : "Remapping on") + " · " + (string.IsNullOrEmpty(logi) ? "Options+ not running" : "Options+ is running");
            loading = false;
        }

        /// <summary>Open above the tray: bottom right on a bottom taskbar, and the matching corner for other taskbar positions.</summary>
        public async void ShowFlyout()
        {
            Showing = true;
            // wait briefly for the first answer from the background app, so the panel opens complete
            var start = DateTime.UtcNow;
            while (!store.Loaded && store.Connected == false && (DateTime.UtcNow - start).TotalMilliseconds < 300) await Task.Delay(30);
            while (!store.Loaded && (DateTime.UtcNow - start).TotalMilliseconds < 900) await Task.Delay(30);
            if (!Showing) return;
            Load();
            Fit(true);
            AppWindow.Show();
            Activate();
            W32.SetForegroundWindow(hwnd);
            fnTile.Focus(FocusState.Programmatic);
            // once laid out at the real scale, fit the height exactly
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Fit(false));
        }

        /// <summary>Size the window to the panel and put it in the corner by the tray (on the display the pointer is on).</summary>
        void Fit(bool first)
        {
            try
            {
                if (first) W32.GetCursorPos(out anchor);
                var area = DisplayArea.GetFromPoint(new PointInt32(anchor.X, anchor.Y), DisplayAreaFallback.Primary);
                double scale = first ? Math.Max(1.0, W32.DpiAt(anchor.X, anchor.Y) / 96.0) : Math.Max(1.0, root.XamlRoot?.RasterizationScale ?? W32.GetDpiForWindow(hwnd) / 96.0);
                double height;
                if (first) { root.Measure(new Windows.Foundation.Size(PanelWidth, double.PositiveInfinity)); height = root.DesiredSize.Height; }
                else height = root.ActualHeight;
                if (height < 100) height = 420; // not measured yet: a sensible guess, corrected on the next pass
                var work = area.WorkArea;
                var outer = area.OuterBounds;
                int w = (int)Math.Ceiling(PanelWidth * scale), h = (int)Math.Ceiling(height * scale);
                int gap = (int)(12 * scale);
                int x = work.X + work.Width - w - gap, y = work.Y + work.Height - h - gap;    // taskbar at the bottom
                if (work.Y > outer.Y) y = work.Y + gap;                                      // at the top
                if (work.X > outer.X) x = work.X + gap;                                      // on the left
                var size = AppWindow.ClientSize;
                if (!first && Math.Abs(size.Height - h) <= 1 && Math.Abs(size.Width - w) <= 1) return;
                AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
                // MoveAndResize sets the outer size; make the inside exactly the panel
                var client = AppWindow.ClientSize;
                if (client.Width != w || client.Height != h)
                {
                    int dw = w - client.Width, dh = h - client.Height;
                    AppWindow.MoveAndResize(new RectInt32(x - dw, y - (y > work.Y + work.Height / 2 ? dh : 0), w + dw, h + dh));
                }
            }
            catch (Exception ex) { try { Logger.Write("tray panel position: " + ex.Message); } catch { } }
        }

        internal void SetTheme(ElementTheme theme) => root.RequestedTheme = theme;

        public void HideFlyout()
        {
            if (!Showing) return;
            Showing = false;
            HiddenAt = DateTime.UtcNow;
            try { AppWindow.Hide(); } catch { }
            Hidden?.Invoke();
        }
    }
}
