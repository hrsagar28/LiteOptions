using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LiteOptions.SettingsApp
{
    /// <summary>
    /// The settings window. It starts only when you open it from the tray, talks to the background engine
    /// (LiteOptions.exe) over a private named pipe, and exits completely when closed.
    /// </summary>
    public static class Entry
    {
        public static string[] Args = new string[0];
        public static bool Demo, SelfTest, Tray, Dark;
        public static string ShotsDir, SelfTestFile;
        static EventWaitHandle showEvent, trayEvent;
        const string ShowName = @"Local\LiteOptions.SettingsUI.Show", TrayName = @"Local\LiteOptions.SettingsUI.Tray";

        [STAThread]
        static int Main(string[] args)
        {
            Args = args ?? new string[0];
            SelfTest = Has("--selftest");
            SelfTestFile = After("--selftest");
            ShotsDir = After("--shots");
            Demo = Has("--demo") || ShotsDir != null;
            Tray = Has("--tray") && !SelfTest && ShotsDir == null;
            Dark = Has("--dark"); // screenshots in the dark theme

            if (!SelfTest && ShotsDir == null)
            {
                // one copy only: a second start hands over to the first (bring the window forward, or open/close the tray panel)
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName, out bool createdNew);
                if (!createdNew)
                {
                    W32.AllowSetForegroundWindow(-1);
                    if (Tray && EventWaitHandle.TryOpenExisting(TrayName, out var t)) { t.Set(); t.Dispose(); }
                    else showEvent.Set();
                    return 0;
                }
                trayEvent = new EventWaitHandle(false, EventResetMode.AutoReset, TrayName);
            }

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
            return 0;
        }

        static bool Has(string flag) => Args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        static string After(string flag)
        {
            for (int i = 0; i < Args.Length; i++)
                if (string.Equals(Args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return i + 1 < Args.Length && !Args[i + 1].StartsWith("--") ? Args[i + 1] : "";
            return null;
        }

        /// <summary>Wakes the open window when another copy is started.</summary>
        internal static void ListenForShow(DispatcherQueue dq, Action show) => Listen(showEvent, dq, show, "show");

        /// <summary>Opens or closes the tray panel when the tray icon is clicked again.</summary>
        internal static void ListenForTray(DispatcherQueue dq, Action toggle) => Listen(trayEvent, dq, toggle, "tray");

        static void Listen(EventWaitHandle ev, DispatcherQueue dq, Action a, string name)
        {
            if (ev == null) return;
            new Thread(() =>
            {
                try { while (ev.WaitOne()) dq.TryEnqueue(() => a()); }
                catch { }
            }) { IsBackground = true, Name = name }.Start();
        }
    }

    public partial class App : Application
    {
        MainWindow window;
        TrayFlyout flyout;
        Store store;
        DispatcherQueueTimer quitTimer;

        public App()
        {
            if (Entry.Dark) RequestedTheme = ApplicationTheme.Dark;
            InitializeComponent();
            UnhandledException += (s, e) =>
            {
                try { Logger.Write("settings window error: " + e.Exception); } catch { }
                if (Entry.SelfTest) { SelfTestRunner.Fail("unhandled: " + e.Exception?.Message); }
                e.Handled = true;
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var dq = DispatcherQueue.GetForCurrentThread();
            IEngine engine = Entry.Demo ? new DemoEngine() : new EngineClient();
            store = new Store(engine, dq);
            quitTimer = dq.CreateTimer();
            quitTimer.Interval = TimeSpan.FromMilliseconds(900);
            quitTimer.IsRepeating = false;
            quitTimer.Tick += (s, e) => { quitTimer.Stop(); if (window == null && (flyout == null || !flyout.Showing)) Exit(); };

            if (Entry.Tray) ToggleFlyout(); else OpenMain(null);
            Entry.ListenForShow(dq, () => OpenMain(null));
            Entry.ListenForTray(dq, ToggleFlyout);
            store.Start();

            if (Entry.SelfTest) SelfTestRunner.Run(window, store);
            else if (Entry.ShotsDir != null) _ = Shots.RunAsync(window, store, Entry.ShotsDir);
        }

        /// <summary>The settings window: open it, or bring it forward, on a page.</summary>
        void OpenMain(string tag)
        {
            quitTimer.Stop();
            if (window == null)
            {
                window = new MainWindow(store);
                // the process ends with the window, unless the tray panel is open at that moment
                window.Closed += (s, e) => { window = null; if (flyout == null || !flyout.Showing) Exit(); };
                window.Activate();
            }
            else window.BringToFront();
            if (tag != null) window.ShowPage(tag);
        }

        /// <summary>A click on the tray icon: open the panel, or close it if it's open.</summary>
        void ToggleFlyout()
        {
            if (flyout != null && flyout.Showing) { flyout.HideFlyout(); return; }
            // clicking the tray icon while the panel is open first takes focus away, which closes it;
            // the click itself then arrives here a moment later and should not open it again
            if (flyout != null && (DateTime.UtcNow - flyout.HiddenAt).TotalMilliseconds < 600) return;
            quitTimer.Stop();
            if (flyout == null)
            {
                flyout = new TrayFlyout(store, tag => { flyout.HideFlyout(); OpenMain(tag); });
                flyout.Hidden += () => { if (window == null) { quitTimer.Stop(); quitTimer.Start(); } };
            }
            flyout.ShowFlyout();
        }
    }
}
