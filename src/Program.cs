using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using LiteOptions.UI;

namespace LiteOptions
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Elevated helper used by "Reverse scroll direction" (and by uninstall to undo it)
            if (args.Length >= 3 && args[0] == "--set-reverse-scroll")
            {
                try
                {
                    ushort pid = Convert.ToUInt16(args[1], 16);
                    return SystemTweaks.SetReverseScrollElevated(pid, args[2] == "1") > 0 ? 0 : 2;
                }
                catch { return 3; }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();
            Theme.ApplyDpi(Theme.DpiAtCursor()); // installer/uninstaller dialogs open on the monitor you're using

            if (args.Contains("--uninstall"))
            {
                bool quiet = args.Contains("--quiet");
                if (!quiet && !args.Contains("--confirmed") &&
                    !Dialogs.Confirm(null, "Uninstall LiteOptions?",
                        "This removes LiteOptions, its settings and its startup entry. Your mouse and keyboard keep working; they go back to their defaults the next time they're switched off and on.",
                        "Uninstall", danger: true))
                    return 0;
                Installer.Uninstall();
                if (!quiet) Dialogs.Info(null, "LiteOptions has been removed", "Everything it added has been taken out again.");
                Installer.ScheduleFolderRemoval(); // after the dialog, once nothing is using the exe
                return 0;
            }

            // Unattended install (used by the automated Windows tests): no questions, then start it.
            if (args.Contains("--install"))
                return Installer.Install(out string err, startInstalled: !args.Contains("--no-start")) ? 0 : 1;

            // Started from anywhere other than the install folder: offer to install or update.
            if (!Installer.RunningInstalled && !args.Contains("--portable"))
            {
                var installed = Installer.InstalledVersion();
                using (var dlg = new InstallDialog(installed, Installer.OldStartupCopy()))
                {
                    if (dlg.ShowDialog() != DialogResult.OK) return 0;
                }
                if (!Installer.Install(out string error))
                {
                    Dialogs.Info(null, "LiteOptions couldn't be installed", error);
                    return 1;
                }
                return 0; // the installed copy has been started
            }

            using (var mutex = new Mutex(true, @"Local\LiteOptions.Instance", out bool first))
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LiteOptions.Show"))
            using (var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.ExitEventName))
            using (var classicSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LiteOptions.Classic"))
            {
                // --classic opens the built-in classic window (the fallback when the new window can't start); used to check it still works
                if (!first) { (args.Contains("--classic") ? classicSignal : showSignal).Set(); return 0; }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Logger.Write("UI error: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Logger.Write("Fatal: " + e.ExceptionObject);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

                string welcome = null;
                if (args.Contains("--installed"))
                    welcome = "Installed and running. Find it in the Start menu, or here in the tray.";
                var app = new TrayApp(!args.Contains("--tray") || args.Contains("--installed"), welcome);
                var ui = SynchronizationContext.Current;

                new Thread(() => { try { while (showSignal.WaitOne()) app.ShowFromOtherInstance(); } catch { } }) { IsBackground = true }.Start();
                new Thread(() => { try { while (classicSignal.WaitOne()) ui.Post(_ => app.ShowClassicWindow(), null); } catch { } }) { IsBackground = true }.Start();
                if (args.Contains("--classic")) ui.Post(_ => app.ShowClassicWindow(), null);
                new Thread(() => { try { if (exitSignal.WaitOne()) ui.Post(_ => app.ExitThread(), null); } catch { } }) { IsBackground = true }.Start();

                Application.Run(app);
                GC.KeepAlive(mutex);
                return 0;
            }
        }
    }
}
