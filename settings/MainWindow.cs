using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiteOptions.Ipc;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
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
        readonly Grid root, titleRow;
        readonly NavigationView nav;
        readonly NavigationViewItem navMouse, navKeys, navApps, navAlerts, navSettings;
        readonly TextBlock pageTitle;
        readonly DropDownButton profileButton;
        readonly TextBlock profileText;
        readonly ScrollViewer scroller;
        readonly Border pageHost;
        readonly InfoBar engineBar;
        readonly StackPanel devicePanel;
        readonly ProgressRing loadingRing;
        readonly Button paneButton;
        readonly AutoSuggestBox search;
        readonly ColumnDefinition captionInset;
        readonly Image logo;
        bool revealed, narrow, paneModeSet;
        /// <summary>Window widths (in effective pixels) below this hide the menu behind ☰, as Windows Settings does.</summary>
        internal const double NarrowWidth = 1000;
        /// <summary>
        /// The page column stops growing here and sits in the middle of the area beside the menu, with equal space
        /// either side (as in Windows Settings). The title and the "All apps" button line up with its edges.
        /// </summary>
        const double ContentMaxWidth = 1000;
        internal readonly MousePage MousePage;
        internal readonly KeyboardPage KeysPage;
        internal readonly AppsPage AppsPage;
        internal readonly AlertsPage AlertsPage;
        internal readonly SettingsPage SettingsPage;
        readonly (string tag, PageBase page, NavigationViewItem item)[] pages;
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

            // ── title bar: [☰ when narrow] [logo  LiteOptions] [ search, centred ] [caption buttons] ──
            paneButton = new Button
            {
                Content = new FontIcon { Glyph = Glyph.Menu, FontSize = 16 },
                Width = 40, Height = 36, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0),
                Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed
            };
            ToolTipService.SetToolTip(paneButton, "Show or hide the menu");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(paneButton, "Menu");
            paneButton.Click += (s, e) => nav.IsPaneOpen = !nav.IsPaneOpen;
            logo = new Image { Width = 18, Height = 18, Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            try { logo.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "logo.png"))); } catch { }
            var appName = new TextBlock { Text = "LiteOptions", Style = Ui.Style("CaptionTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
            var brand = Ui.Row(0, logo, appName);
            brand.Margin = new Thickness(0, 0, 16, 0);

            search = new AutoSuggestBox
            {
                PlaceholderText = "Search settings",
                QueryIcon = new SymbolIcon(Symbol.Find),
                MaxWidth = 360, MinWidth = 120,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                UpdateTextOnSelect = false,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, "Search settings");
            search.TextChanged += OnSearchText;
            search.QuerySubmitted += OnSearchSubmitted;
            search.SizeChanged += (s, e) => UpdateDragRegions();

            titleRow = new Grid { Height = 48, Background = new SolidColorBrush(Colors.Transparent) };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            captionInset = new ColumnDefinition { Width = new GridLength(140) };
            titleRow.ColumnDefinitions.Add(captionInset);
            Grid.SetColumn(brand, 1);
            Grid.SetColumn(search, 2);
            // keeps the search box centred in the window, not in what is left of it
            var balance = new Border { Width = 0 };
            Grid.SetColumn(balance, 3);
            titleRow.Children.Add(paneButton);
            titleRow.Children.Add(brand);
            titleRow.Children.Add(search);
            titleRow.Children.Add(balance);
            root.Children.Add(titleRow);

            // ── pages ──
            MousePage = new MousePage(store);
            KeysPage = new KeyboardPage(store);
            AppsPage = new AppsPage(store);
            AlertsPage = new AlertsPage(store);
            SettingsPage = new SettingsPage(store) { RunSetup = () => SetupFlow.RunAsync(store) };

            // ── content: header, engine warning, scrolling page ──
            pageTitle = new TextBlock { Style = Ui.Style("TitleTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
            profileText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 };
            var profileBadge = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                Background = Ui.Brush("AccentFillColorDefaultBrush"),
                Child = new FontIcon { Glyph = Glyph.Apps, FontSize = 12, Foreground = Ui.Brush("TextOnAccentFillColorPrimaryBrush") },
            };
            profileButton = new DropDownButton
            {
                Content = Ui.Row(10, profileBadge, profileText),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(4, 4, 10, 4),
                MinHeight = 32,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(profileButton, "Which apps these settings apply to");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(profileButton, "Settings apply to");
            var profileMenu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
            profileMenu.Opening += (s, e) => BuildProfileMenu(profileMenu);
            profileButton.Flyout = profileMenu;
            var header = new Grid { Margin = new Thickness(40, 24, 40, 6), ColumnSpacing = 16, MinHeight = 40, MaxWidth = ContentMaxWidth };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(profileButton, 1);
            header.Children.Add(pageTitle);
            header.Children.Add(profileButton);

            engineBar = new InfoBar
            {
                Severity = InfoBarSeverity.Warning, IsClosable = false, IsOpen = false, Margin = new Thickness(40, 4, 40, 4), MaxWidth = ContentMaxWidth,
                Title = "LiteOptions isn't running in the background",
                Message = "Your mouse and keyboard aren't being customised, and changes can't be made until it's running."
            };
            var start = Ui.Button("Start it", null, accent: true, click: (s, e) => StartEngine());
            engineBar.ActionButton = start;

            pageHost = new Border { Margin = new Thickness(40, 12, 40, 0), MaxWidth = ContentMaxWidth };
            // no sideways scrolling: pages are laid out at the window's width (text wraps) instead of their widest line,
            // so the capped column centres correctly instead of being pushed off to the right
            scroller = new ScrollViewer
            {
                Content = pageHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
            };

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

            // ── navigation: the devices with their battery rings, then the pages ──
            navMouse = Item("Mouse", Glyph.Mouse, "mouse");
            navKeys = Item("Keyboard", Glyph.Keyboard, "keys");
            navApps = Item("App profiles", Glyph.Apps, "apps");
            navAlerts = Item("Pop-ups & alerts", Glyph.Alert, "alerts");
            navSettings = Item("Settings", Glyph.Settings, "settings");
            devicePanel = new StackPanel { Spacing = 2, Margin = new Thickness(4, 4, 4, 12), Opacity = 0 };
            nav = new NavigationView
            {
                PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
                IsSettingsVisible = false,
                IsPaneToggleButtonVisible = false,
                IsTitleBarAutoPaddingEnabled = false,
                OpenPaneLength = 280,
                PaneHeader = devicePanel,
                Content = content
            };
            nav.MenuItems.Add(navMouse);
            nav.MenuItems.Add(navKeys);
            nav.MenuItems.Add(navApps);
            nav.MenuItems.Add(navAlerts);
            nav.FooterMenuItems.Add(navSettings);
            // on a wide window the menu always shows in full (otherwise, after the window has been narrow, it can come back as icons only)
            nav.PaneClosing += (s, e) => { if (!narrow) e.Cancel = true; };
            nav.SelectionChanged += (s, e) =>
            {
                ShowPage((e.SelectedItem as NavigationViewItem)?.Tag as string);
                if (narrow) nav.IsPaneOpen = false; // the pane floats over the page when narrow: get it out of the way
            };
            Grid.SetRow(nav, 1);
            root.Children.Add(nav);

            pages = new (string, PageBase, NavigationViewItem)[]
            {
                ("mouse", MousePage, navMouse), ("keys", KeysPage, navKeys), ("apps", AppsPage, navApps),
                ("alerts", AlertsPage, navAlerts), ("settings", SettingsPage, navSettings),
            };
            foreach (var p in pages) p.page.Navigate = Navigate;

            Content = root;
            SetTitleBar(titleRow);
            root.Loaded += (s, e) => { Ui.Root = root.XamlRoot; UpdateCaptionInset(); UpdateDragRegions(); ApplyCaptionColors(); };
            root.ActualThemeChanged += (s, e) => ApplyCaptionColors();
            // Like Windows Settings: below a certain width the menu folds away behind a ☰ button in the title bar
            root.SizeChanged += (s, e) => { UpdatePaneMode(e.NewSize.Width); UpdateCaptionInset(); UpdateDragRegions(); };

            try
            {
                AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "app.ico"));
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                double scale = Math.Max(1.0, W32.GetDpiForWindow(Hwnd) / 96.0);
                var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                int w = Math.Min((int)(1160 * scale), area.Width - (int)(24 * scale));
                int h = Math.Min((int)(820 * scale), area.Height - (int)(24 * scale));
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

        static NavigationViewItem Item(string text, string glyph, string tag) =>
            new NavigationViewItem { Content = text, Icon = new FontIcon { Glyph = glyph }, Tag = tag };

        internal FrameworkElement RootElement => root;

        // ───────────── pages ─────────────

        internal void ShowPage(string tag)
        {
            var entry = pages.FirstOrDefault(x => x.tag == tag);
            if (entry.page == null) entry = pages[0];
            var p = entry.page;
            if (!ReferenceEquals(nav.SelectedItem, entry.item)) nav.SelectedItem = entry.item;
            if (p == current) return;
            current = p;
            pageHost.Child = p;
            pageTitle.Text = p.Title;
            profileButton.Visibility = p.UsesProfiles ? Visibility.Visible : Visibility.Collapsed;
            scroller.ChangeView(null, 0, null, true);
            if (p == SettingsPage) _ = store.RefreshAsync(); // Options+ status, startup state
        }

        /// <summary>A page asked to go to another page, maybe after picking an app profile: show the profile everywhere.</summary>
        void Navigate(string tag)
        {
            ReloadPages();
            ShowPage(tag);
        }

        internal string CurrentTag => pages.FirstOrDefault(x => x.page == current).tag;

        internal void ScrollToEnd() => scroller.ChangeView(null, scroller.ScrollableHeight, null, true);

        internal void ScrollTo(double offset) => scroller.ChangeView(null, Math.Min(offset, scroller.ScrollableHeight), null, true);

        internal void ReloadPages()
        {
            profileText.Text = store.Current.IsGlobal ? "All apps" : store.Current.App;
            foreach (var p in pages) p.page.LoadSettings();
        }

        /// <summary>Show the pages once real data has arrived (or once it's clear the background app isn't running).</summary>
        void Reveal()
        {
            if (revealed) return;
            revealed = true;
            loadingRing.IsActive = false;
            loadingRing.Visibility = Visibility.Collapsed;
            scroller.Opacity = 1;
            devicePanel.Opacity = 1;
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
            devicePanel.Children.Clear();
            devicePanel.Children.Add(DeviceRow(store.Mouse, true));
            devicePanel.Children.Add(DeviceRow(store.Keyboard, false));
        }

        /// <summary>A device in the menu: its photo in a battery ring, its name and state. Clicking it opens its page.</summary>
        FrameworkElement DeviceRow(DeviceDto d, bool mouse)
        {
            var ring = Ring.Device(d, mouse, 44, store.IsLow(d));
            string name = d?.Name ?? (mouse ? "Mouse" : "Keyboard");
            string state = d == null ? "Not found" : !d.Online ? "Asleep" : d.HasBattery ? d.BatteryText + " · Connected" : "Connected";
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
            texts.Children.Add(new TextBlock { Text = name, Style = Ui.Style("BodyStrongTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis });
            texts.Children.Add(new TextBlock { Text = state, Style = Ui.Style("Secondary"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            var g = new Grid { ColumnSpacing = 12 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(texts, 1);
            g.Children.Add(ring);
            g.Children.Add(texts);
            var b = new Button
            {
                Content = g,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, name + ", " + state);
            b.Click += (s, e) => { ShowPage(mouse ? "mouse" : "keys"); if (narrow) nav.IsPaneOpen = false; };
            return b;
        }

        // ───────────── search ─────────────

        sealed class SearchHit
        {
            public string Tag, Text, Page;
            public FrameworkElement Target;
            public override string ToString() => Text + "  ·  " + Page;
        }

        List<SearchHit> Search(string query)
        {
            var words = (query ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = new List<SearchHit>();
            if (words.Length == 0) return hits;
            foreach (var (tag, page, _) in pages)
                foreach (var (title, keys, target) in page.Searchable)
                {
                    if (target == null || target.Visibility != Visibility.Visible) continue;
                    string text = title ?? (target as SettingCard)?.Title ?? "";
                    if (string.IsNullOrEmpty(text)) continue;
                    string hay = (text + " " + keys + " " + page.Title).ToLowerInvariant();
                    if (!words.All(w => hay.Contains(w))) continue;
                    // titles that start with what was typed come first
                    hits.Add(new SearchHit { Tag = tag, Text = text, Page = page.Title, Target = target });
                }
            string first = words[0];
            return hits.OrderBy(h => h.Text.ToLowerInvariant().StartsWith(first) ? 0 : h.Text.ToLowerInvariant().Contains(first) ? 1 : 2)
                       .GroupBy(h => h.Text + "|" + h.Page).Select(g => g.First()).Take(10).ToList();
        }

        void OnSearchText(AutoSuggestBox box, AutoSuggestBoxTextChangedEventArgs e)
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            if (string.IsNullOrWhiteSpace(box.Text)) { box.ItemsSource = null; return; }
            var hits = Search(box.Text);
            box.ItemsSource = hits.Count > 0 ? (object)hits : new List<string> { "No results" };
        }

        void OnSearchSubmitted(AutoSuggestBox box, AutoSuggestBoxQuerySubmittedEventArgs e)
        {
            var hit = e.ChosenSuggestion as SearchHit ?? Search(e.QueryText).FirstOrDefault();
            if (hit == null) return;
            box.Text = "";
            box.ItemsSource = null;
            GoTo(hit.Tag, hit.Target);
        }

        /// <summary>Open a page and scroll to a setting on it, flashing it so it's easy to spot.</summary>
        internal void GoTo(string tag, FrameworkElement target)
        {
            ShowPage(tag);
            if (narrow) nav.IsPaneOpen = false;
            // wait for the page to be laid out before scrolling
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => PageBase.Jump(target));
        }

        // ───────────── profiles (per-app settings) ─────────────

        void BuildProfileMenu(MenuFlyout m)
        {
            m.Items.Clear();
            foreach (var p in store.Settings.Profiles)
            {
                var app = p.App;
                var item = new ToggleMenuFlyoutItem { Text = p.IsGlobal ? "All apps" : p.App, IsChecked = app == (store.Current.App ?? "") };
                item.Click += (s, e) => { store.CurrentApp = app; ReloadPages(); };
                m.Items.Add(item);
            }
            m.Items.Add(new MenuFlyoutSeparator());
            var add = new MenuFlyoutItem { Text = "Add an app…", Icon = new FontIcon { Glyph = Glyph.Add } };
            add.Click += async (s, e) => await AppsPage.AddApp();
            m.Items.Add(add);
            var manage = new MenuFlyoutItem { Text = "Manage app profiles", Icon = new FontIcon { Glyph = Glyph.Apps } };
            manage.Click += (s, e) => ShowPage("apps");
            m.Items.Add(manage);
            if (!store.Current.IsGlobal)
            {
                var app = store.Current.App;
                var remove = new MenuFlyoutItem { Text = "Remove " + app, Icon = new FontIcon { Glyph = Glyph.Delete } };
                remove.Click += async (s, e) => { await AppsPage.RemoveApp(app); ReloadPages(); };
                m.Items.Add(remove);
            }
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

        /// <summary>Wide: the menu sits on the left. Narrow: it folds away and ☰ in the title bar slides it over the page.</summary>
        void UpdatePaneMode(double width)
        {
            bool now = width < NarrowWidth;
            if (paneModeSet && now == narrow) return;
            paneModeSet = true;
            narrow = now;
            nav.PaneDisplayMode = narrow ? NavigationViewPaneDisplayMode.LeftMinimal : NavigationViewPaneDisplayMode.Left;
            nav.IsPaneOpen = !narrow;
            // the menu settles into its new mode a moment later and may close itself; open it again then
            if (!narrow) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { if (!narrow) nav.IsPaneOpen = true; });
            paneButton.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
            logo.Margin = new Thickness(narrow ? 8 : 16, 0, 12, 0);
            DispatcherQueue.TryEnqueue(UpdateDragRegions);
        }

        /// <summary>Minimise / maximise / close in the colours of the theme (they don't follow it by themselves in a custom title bar).</summary>
        void ApplyCaptionColors()
        {
            try
            {
                bool dark = root.ActualTheme == ElementTheme.Dark;
                var tb = AppWindow.TitleBar;
                tb.ButtonBackgroundColor = Colors.Transparent;
                tb.ButtonInactiveBackgroundColor = Colors.Transparent;
                tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
                tb.ButtonInactiveForegroundColor = dark ? Windows.UI.Color.FromArgb(255, 150, 150, 150) : Windows.UI.Color.FromArgb(255, 120, 120, 120);
                tb.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(24, 255, 255, 255) : Windows.UI.Color.FromArgb(18, 0, 0, 0);
                tb.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
                tb.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(16, 255, 255, 255) : Windows.UI.Color.FromArgb(10, 0, 0, 0);
                tb.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
            }
            catch { }
        }

        /// <summary>Leave room on the right for the minimise / maximise / close buttons.</summary>
        void UpdateCaptionInset()
        {
            try
            {
                double scale = root.XamlRoot?.RasterizationScale ?? 1.0;
                double inset = AppWindow.TitleBar.RightInset / scale;
                if (inset > 0) captionInset.Width = new GridLength(inset + 8);
                // the left part (☰, logo and name) and the right part get the same width, so the search box sits in the middle
                if (titleRow.Children[3] is Border balance)
                {
                    double left = (paneButton.Visibility == Visibility.Visible ? 44 : 0) + ((FrameworkElement)titleRow.Children[1]).ActualWidth;
                    balance.Width = Math.Max(0, left - captionInset.Width.Value);
                }
            }
            catch { }
        }

        /// <summary>The whole top row drags the window, except where the ☰ button and the search box are.</summary>
        void UpdateDragRegions()
        {
            try
            {
                if (root.XamlRoot == null) return;
                double s = root.XamlRoot.RasterizationScale;
                var rects = new List<RectInt32>();
                foreach (var e in new FrameworkElement[] { paneButton, search })
                {
                    if (e.Visibility != Visibility.Visible || e.ActualWidth <= 0) continue;
                    var r = e.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, e.ActualWidth, e.ActualHeight));
                    rects.Add(new RectInt32((int)Math.Round(r.X * s), (int)Math.Round(r.Y * s), (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s)));
                }
                InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
            }
            catch (Exception ex) { try { Logger.Write("title bar regions: " + ex.Message); } catch { } }
        }

        internal bool IsNarrow => narrow;

        internal bool PaneOpen => nav.IsPaneOpen;

        /// <summary>For screenshots: open or close the menu while narrow.</summary>
        internal void SetPaneOpen(bool open) { if (narrow) nav.IsPaneOpen = open; }

        /// <summary>For screenshots: type into the search box and show its suggestions.</summary>
        internal void ShowSearch(string text)
        {
            search.Text = text;
            search.ItemsSource = Search(text);
            search.Focus(FocusState.Programmatic);
            search.IsSuggestionListOpen = true;
        }

        /// <summary>For the self-test: the titles the search finds.</summary>
        internal List<string> SearchResults(string text) => Search(text).Select(h => h.Text).ToList();

        /// <summary>For the self-test: search and go to the first result, as pressing Enter does.</summary>
        internal void GoToFirst(string text)
        {
            var hit = Search(text).FirstOrDefault();
            if (hit != null) GoTo(hit.Tag, hit.Target);
        }

        internal void ClearSearch()
        {
            search.IsSuggestionListOpen = false;
            search.Text = "";
            search.ItemsSource = null;
        }

        /// <summary>For screenshots: resize the window to a size in effective pixels.</summary>
        internal void ResizeTo(double width, double height)
        {
            double scale = Math.Max(1.0, W32.GetDpiForWindow(Hwnd) / 96.0);
            var pos = AppWindow.Position;
            AppWindow.MoveAndResize(new RectInt32(pos.X, pos.Y, (int)(width * scale), (int)(height * scale)));
        }

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

        internal static T FindFirst<T>(DependencyObject parent) where T : DependencyObject
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
