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
        public static bool Demo, SelfTest;
        public static string ShotsDir, SelfTestFile;
        static EventWaitHandle showEvent;

        [STAThread]
        static int Main(string[] args)
        {
            Args = args ?? new string[0];
            SelfTest = Has("--selftest");
            SelfTestFile = After("--selftest");
            ShotsDir = After("--shots");
            Demo = Has("--demo") || ShotsDir != null;

            if (!SelfTest && ShotsDir == null)
            {
                // one window only: a second start just brings the first one forward
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LiteOptions.SettingsUI.Show", out bool createdNew);
                if (!createdNew)
                {
                    W32.AllowSetForegroundWindow(-1);
                    showEvent.Set();
                    return 0;
                }
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
        internal static void ListenForShow(DispatcherQueue dq, Action show)
        {
            if (showEvent == null) return;
            new Thread(() =>
            {
                try { while (showEvent.WaitOne()) dq.TryEnqueue(() => show()); }
                catch { }
            }) { IsBackground = true, Name = "show" }.Start();
        }
    }

    public partial class App : Application
    {
        MainWindow window;

        public App()
        {
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
            IEngine engine = Entry.Demo ? new DemoEngine() : new EngineClient();
            var store = new Store(engine, DispatcherQueue.GetForCurrentThread());
            window = new MainWindow(store);
            window.Activate();
            Entry.ListenForShow(DispatcherQueue.GetForCurrentThread(), () => window.BringToFront());
            store.Start();

            if (Entry.SelfTest) SelfTestRunner.Run(window, store);
            else if (Entry.ShotsDir != null) _ = Shots.RunAsync(window, store, Entry.ShotsDir);
        }
    }
}
