using System;
using System.Diagnostics;
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
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace LiteOptions.SettingsApp
{
    public sealed partial class MainWindow : Window
    {
        public static IntPtr Hwnd;
        readonly Store store;
        readonly Grid root;
        readonly NavigationView nav;
        readonly NavigationViewItem navMouse, navKeys, navSettings;
        readonly TextBlock pageTitle;
        readonly DropDownButton profileButton;
        readonly StackPanel profileBar;
        readonly ScrollViewer scroller;
        readonly Border pageHost;
        readonly InfoBar engineBar;
        readonly StackPanel deviceStatus;
        readonly ProgressRing loadingRing;
        bool revealed;
        internal readonly MousePage MousePage;
        internal readonly KeyboardPage KeysPage;
        internal readonly SettingsPage SettingsPage;
        PageBase current;
        bool setupStarted, everConnected;

        public MainWindow(Store store)
        {
            this.store = store;
            Title = "LiteOptions";
            Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ExtendsContentIntoTitleBar = true;

            root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            if (MicaController.IsSupported()) SystemBackdrop = new MicaBackdrop();
            else if (DesktopAcrylicController.IsSupported()) SystemBackdrop = new DesktopAcrylicBackdrop();
            else root.Background = Ui.Brush("SolidBackgroundFillColorBaseBrush");

            // ── title bar ──
            var titleBar = new Grid { Height = 48, Background = new SolidColorBrush(Colors.Transparent) };
            var logo = new Image { Width = 18, Height = 18, Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            try { logo.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "logo.png"))); } catch { }
            var appName = new TextBlock { Text = "LiteOptions", Style = Ui.Style("CaptionTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
            titleBar.Children.Add(Ui.Row(0, logo, appName));
            root.Children.Add(titleBar);

            // ── pages ──
            MousePage = new MousePage(store);
            KeysPage = new KeyboardPage(store);
            SettingsPage = new SettingsPage(store) { RunSetup = () => SetupFlow.RunAsync(store) };

            // ── content: header, engine warning, scrolling page ──
            pageTitle = new TextBlock { Style = Ui.Style("TitleTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
            profileButton = new DropDownButton { MinWidth = 200, HorizontalContentAlignment = HorizontalAlignment.Left };
            var profileMenu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
            profileMenu.Opening += (s, e) => BuildProfileMenu(profileMenu);
            profileButton.Flyout = profileMenu;
            profileBar = Ui.Row(10, Ui.Text("Applies to", "SecondaryBody", wrap: false), profileButton);
            var header = new Grid { Margin = new Thickness(36, 22, 36, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(profileBar, 1);
            header.Children.Add(pageTitle);
            header.Children.Add(profileBar);

            engineBar = new InfoBar
            {
                Severity = InfoBarSeverity.Warning, IsClosable = false, IsOpen = false, Margin = new Thickness(36, 4, 36, 4),
                Title = "LiteOptions isn't running in the background",
                Message = "Your mouse and keyboard aren't being customised, and changes can't be made until it's running."
            };
            var start = Ui.Button("Start it", null, accent: true, click: (s, e) => StartEngine());
            engineBar.ActionButton = start;

            pageHost = new Border { MaxWidth = 1000, Margin = new Thickness(36, 8, 36, 0) };
            scroller = new ScrollViewer { Content = pageHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(engineBar, 1);
            Grid.SetRow(scroller, 2);
            // Until the background app has answered, show a spinner rather than "No mouse found" and default values
            loadingRing = new ProgressRing { IsActive = true, Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(loadingRing, 2);
            scroller.Opacity = 0;
            content.Children.Add(header);
            content.Children.Add(engineBar);
            content.Children.Add(scroller);
            content.Children.Add(loadingRing);

            // ── navigation ──
            navMouse = new NavigationViewItem { Content = "Mouse", Icon = new FontIcon { Glyph = Glyph.Mouse }, Tag = "mouse" };
            navKeys = new NavigationViewItem { Content = "Keyboard", Icon = new FontIcon { Glyph = Glyph.Keyboard }, Tag = "keys" };
            navSettings = new NavigationViewItem { Content = "Settings", Icon = new FontIcon { Glyph = Glyph.Settings }, Tag = "settings" };
            deviceStatus = new StackPanel { Spacing = 6, Margin = new Thickness(16, 8, 16, 16), Opacity = 0 };
            nav = new NavigationView
            {
                PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
                IsSettingsVisible = false,
                IsPaneToggleButtonVisible = false,
                IsTitleBarAutoPaddingEnabled = false,
                OpenPaneLength = 240,
                PaneFooter = deviceStatus,
                Content = content
            };
            nav.MenuItems.Add(navMouse);
            nav.MenuItems.Add(navKeys);
            nav.FooterMenuItems.Add(navSettings);
            nav.SelectionChanged += (s, e) => ShowPage((e.SelectedItem as NavigationViewItem)?.Tag as string);
            Grid.SetRow(nav, 1);
            root.Children.Add(nav);

            Content = root;
            SetTitleBar(titleBar);
            root.Loaded += (s, e) => Ui.Root = root.XamlRoot;

            try
            {
                AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "app.ico"));
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                double scale = Math.Max(1.0, W32.GetDpiForWindow(Hwnd) / 96.0);
                var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                int w = Math.Min((int)(1100 * scale), area.Width - (int)(24 * scale));
                int h = Math.Min((int)(800 * scale), area.Height - (int)(24 * scale));
                AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
            }
            catch (Exception ex) { try { Logger.Write("settings window size: " + ex.Message); } catch { } }

            store.SettingsChanged += OnSettings;
            store.DevicesChanged += OnDevices;
            store.SystemChanged += () => { MousePage.LoadSystem(); SettingsPage.LoadSystem(); };
            store.ConnectionChanged += OnConnection;

            nav.SelectedItem = navMouse;
            ShowPage("mouse");
            OnSettings();
            OnDevices();
            // only complain about the engine if it doesn't answer quickly
            _ = Task.Delay(1500).ContinueWith(_ => DispatcherQueue.TryEnqueue(UpdateEngineBar));
        }

        internal FrameworkElement RootElement => root;

        // ───────────── pages ─────────────

        internal void ShowPage(string tag)
        {
            PageBase p = tag == "keys" ? KeysPage : tag == "settings" ? (PageBase)SettingsPage : MousePage;
            if (p == current) return;
            current = p;
            pageHost.Child = p;
            pageTitle.Text = p.Title;
            profileBar.Visibility = p.UsesProfiles ? Visibility.Visible : Visibility.Collapsed;
            scroller.ChangeView(null, 0, null, true);
            var item = tag == "keys" ? navKeys : tag == "settings" ? navSettings : navMouse;
            if (!ReferenceEquals(nav.SelectedItem, item)) nav.SelectedItem = item;
            if (p == SettingsPage) _ = store.RefreshAsync(); // Options+ status, startup state
        }

        internal void ScrollToEnd() => scroller.ChangeView(null, scroller.ScrollableHeight, null, true);

        internal void ReloadPages()
        {
            profileButton.Content = store.Current.IsGlobal ? "All applications" : store.Current.App;
            MousePage.LoadSettings();
            KeysPage.LoadSettings();
            SettingsPage.LoadSettings();
        }

        /// <summary>Show the pages once real data has arrived (or once it's clear the background app isn't running).</summary>
        void Reveal()
        {
            if (revealed) return;
            revealed = true;
            loadingRing.IsActive = false;
            loadingRing.Visibility = Visibility.Collapsed;
            scroller.Opacity = 1;
            deviceStatus.Opacity = 1;
        }

        void OnSettings()
        {
            ReloadPages();
            if (store.Loaded) Reveal();
            if (store.Loaded && !store.Settings.SetupDone && !setupStarted && !Entry.SelfTest && Entry.ShotsDir == null)
            {
                setupStarted = true;
                DispatcherQueue.TryEnqueue(async () => { await SetupFlow.RunAsync(store); ReloadPages(); });
            }
        }

        void OnDevices()
        {
            if (store.Loaded) Reveal();
            MousePage.LoadDevices();
            KeysPage.LoadDevices();
            deviceStatus.Children.Clear();
            deviceStatus.Children.Add(Ui.Text("DEVICES", "StepLabel"));
            deviceStatus.Children.Add(StatusLine(Glyph.Mouse, "Mouse", store.Mouse));
            deviceStatus.Children.Add(StatusLine(Glyph.Keyboard, "Keyboard", store.Keyboard));
        }

        UIElement StatusLine(string glyph, string label, DeviceDto d)
        {
            var g = new Grid { ColumnSpacing = 10 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Ui.Icon(glyph, 14);
            icon.Foreground = Ui.Brush("TextFillColorSecondaryBrush");
            var name = Ui.Text(label, "BodyTextBlockStyle", wrap: false);
            string state = d == null ? "Not found" : !d.Online ? "Asleep" : !d.HasBattery ? "Connected" : d.BatteryText;
            var right = Ui.Row(6);
            if (d != null && d.Online && d.HasBattery)
            {
                int pct = d.BatteryPercent >= 0 ? d.BatteryPercent : DeviceHero.LevelPercent(d.BatteryLevel);
                var b = Ui.Icon(Glyph.Battery(pct, d.Charging), 14);
                b.Foreground = store.IsLow(d) ? Ui.Brush("SystemFillColorCriticalBrush") : Ui.Brush("TextFillColorSecondaryBrush");
                right.Children.Add(b);
            }
            right.Children.Add(Ui.Text(state, "Secondary", wrap: false));
            Grid.SetColumn(name, 1);
            Grid.SetColumn(right, 2);
            g.Children.Add(icon); g.Children.Add(name); g.Children.Add(right);
            ToolTipService.SetToolTip(g, d?.Name ?? label + " not found");
            return g;
        }

        // ───────────── profiles (per-app settings) ─────────────

        void BuildProfileMenu(MenuFlyout m)
        {
            m.Items.Clear();
            foreach (var p in store.Settings.Profiles)
            {
                var app = p.App;
                var item = new ToggleMenuFlyoutItem { Text = p.IsGlobal ? "All applications" : p.App, IsChecked = app == (store.Current.App ?? "") };
                item.Click += (s, e) => { store.CurrentApp = app; ReloadPages(); };
                m.Items.Add(item);
            }
            m.Items.Add(new MenuFlyoutSeparator());
            var add = new MenuFlyoutItem { Text = "Add an app…", Icon = new FontIcon { Glyph = Glyph.Add } };
            add.Click += async (s, e) => await AddApp();
            m.Items.Add(add);
            if (!store.Current.IsGlobal)
            {
                var app = store.Current.App;
                var remove = new MenuFlyoutItem { Text = "Remove " + app, Icon = new FontIcon { Glyph = Glyph.Delete } };
                remove.Click += async (s, e) => await RemoveApp(app);
                m.Items.Add(remove);
            }
        }

        async Task AddApp()
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
            ReloadPages();
        }

        async Task RemoveApp(string app)
        {
            if (!await Ui.Confirm("Remove " + app + "?", "Its own middle-button and key settings are deleted. All applications settings stay as they are.", "Remove", danger: true)) return;
            store.Settings.Profiles.RemoveAll(p => p.App == app);
            store.Save();
            store.CurrentApp = "";
            ReloadPages();
        }

        // ───────────── the engine ─────────────

        void OnConnection()
        {
            if (store.Connected) everConnected = true;
            UpdateEngineBar();
        }

        void UpdateEngineBar()
        {
            bool down = !store.Connected;
            if (down || store.Loaded) Reveal();
            engineBar.IsOpen = down;
            scroller.IsEnabled = !down;
            profileButton.IsEnabled = !down;
        }

        void StartEngine()
        {
            try
            {
                string exe = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('\\', '/')) ?? "", "LiteOptions.exe");
                if (!File.Exists(exe)) exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LiteOptions", "LiteOptions.exe");
                Process.Start(new ProcessStartInfo(exe, "--tray") { UseShellExecute = false })?.Dispose();
                engineBar.Message = "Starting…";
            }
            catch (Exception ex) { engineBar.Message = "Couldn't start it: " + ex.Message; }
        }

        // ───────────── window ─────────────

        public void BringToFront()
        {
            if (W32.IsIconic(Hwnd)) W32.ShowWindow(Hwnd, 9 /* SW_RESTORE */);
            Activate();
            W32.SetForegroundWindow(Hwnd);
        }

        internal void SetTheme(ElementTheme theme)
        {
            root.RequestedTheme = theme;
            Ui.Theme = theme;
            OnDevices(); // redraws the device pictures in the new colours
            try
            {
                var tb = AppWindow.TitleBar;
                tb.ButtonForegroundColor = theme == ElementTheme.Dark ? Colors.White : Colors.Black;
            }
            catch { }
        }

        /// <summary>For screenshots: open the first action menu on the current page.</summary>
        internal FlyoutBase OpenFirstMenu()
        {
            return FindFirst<ActionPicker>(current)?.OpenMenu();
        }

        static T FindFirst<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int n = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(parent, i);
                if (c is T t && (!(c is UIElement u) || u.Visibility == Visibility.Visible)) return t;
                var deeper = FindFirst<T>(c);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
