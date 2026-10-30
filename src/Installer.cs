using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiteOptions
{
    /// <summary>
    /// Per-user install, update and uninstall. No admin rights, no installer framework:
    /// the exe copies itself to %LOCALAPPDATA%\Programs\LiteOptions and registers itself like a normal app.
    /// </summary>
    static class Installer
    {
        public const string ExitEventName = @"Local\LiteOptions.Exit";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LiteOptions";

        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LiteOptions");
        public static string InstalledExe => Path.Combine(InstallDir, "LiteOptions.exe");
        static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LiteOptions.lnk");
        public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version;
        public static string VersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

        public static bool RunningInstalled =>
            string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        public static Version InstalledVersion()
        {
            try
            {
                if (!File.Exists(InstalledExe)) return null;
                var fv = FileVersionInfo.GetVersionInfo(InstalledExe);
                return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart, fv.FilePrivatePart);
            }
            catch { return null; }
        }

        /// <summary>Old portable copy that "Start with Windows" points at (v1.0 ran from wherever it was saved).</summary>
        public static string OldStartupCopy()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (!(k?.GetValue("LiteOptions") is string v)) return null;
                    string path = v.Trim();
                    if (path.StartsWith("\"")) path = path.Substring(1, path.IndexOf('"', 1) - 1);
                    else if (path.Contains(" --")) path = path.Substring(0, path.IndexOf(" --"));
                    path = Path.GetFullPath(path);
                    if (string.Equals(path, Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase)) return null;
                    if (string.Equals(path, Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase)) return null;
                    if (!Path.GetFileName(path).Equals("LiteOptions.exe", StringComparison.OrdinalIgnoreCase)) return null;
                    return File.Exists(path) ? path : null;
                }
            }
            catch { return null; }
        }

        /// <summary>The WinUI settings window, zipped inside the engine exe by the build (absent in plain builds).</summary>
        static System.IO.Stream Payload() => Assembly.GetExecutingAssembly().GetManifestResourceStream("LiteOptions.settings.zip");
        public static bool HasPayload { get { using (var p = Payload()) return p != null; } }

        static void ExtractPayload()
        {
            string dir = Path.Combine(InstallDir, "Settings");
            using (var stream = Payload())
            {
                if (stream == null) return;
                for (int i = 0; i < 15 && Directory.Exists(dir); i++)
                {
                    try { Directory.Delete(dir, true); }
                    catch (IOException) { Thread.Sleep(300); }
                    catch (UnauthorizedAccessException) { Thread.Sleep(300); }
                }
                Directory.CreateDirectory(dir);
                using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read))
                {
                    string root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
                    foreach (var entry in zip.Entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // ignore odd paths
                        if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var src = entry.Open())
                        using (var dst = File.Create(target)) src.CopyTo(dst);
                    }
                }
            }
        }

        /// <summary>Ask any running copy (old or new) to quit, and wait until it has.</summary>
        public static void StopOtherInstances()
        {
            // the settings window holds files in the Settings folder; it saves as it goes, so just close it
            foreach (var p in Process.GetProcessesByName("LiteOptions.Settings"))
            {
                try { if (!p.CloseMainWindow() || !p.WaitForExit(2000)) { p.Kill(); p.WaitForExit(3000); } } catch { }
                finally { p.Dispose(); }
            }
            try { using (var ev = EventWaitHandle.OpenExisting(ExitEventName)) ev.Set(); } catch { }
            int me = Process.GetCurrentProcess().Id;
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < deadline)
            {
                var others = Process.GetProcessesByName("LiteOptions").Where(p => p.Id != me).ToList();
                if (others.Count == 0) return;
                foreach (var p in others) p.Dispose();
                Thread.Sleep(200);
            }
            // version 1.0 has no exit signal: end it (it holds no files open and its hooks die with it)
            foreach (var p in Process.GetProcessesByName("LiteOptions"))
            {
                try { if (p.Id != me) { p.Kill(); p.WaitForExit(3000); } } catch { }
                finally { p.Dispose(); }
            }
        }

        /// <summary>Copy this exe into place, register it, and start the installed copy. Returns false on failure.</summary>
        public static bool Install(out string error, bool startInstalled = true)
        {
            error = null;
            try
            {
                StopOtherInstances();
                Directory.CreateDirectory(InstallDir);
                string self = Application.ExecutablePath;
                Exception last = null;
                for (int i = 0; i < 15; i++)
                {
                    try { File.Copy(self, InstalledExe, true); last = null; break; }
                    catch (IOException ex) { last = ex; Thread.Sleep(300); }
                    catch (UnauthorizedAccessException ex) { last = ex; Thread.Sleep(300); }
                }
                if (last != null) throw last;
                // the copy inherits the installer's "downloaded" mark; installed programs shouldn't trip SmartScreen on every start
                Native.ClearDownloadMark(InstalledExe);
                ExtractPayload();

                string old = OldStartupCopy();
                bool startup = SystemTweaks.StartupValueExists() || !File.Exists(AppSettings.FilePath);
                Register();
                CreateShortcut();
                if (startup) SystemTweaks.SetStartupPath(InstalledExe); // keep start-up on, now pointing at the installed copy
                if (old != null) try { File.Delete(old); Logger.Write("Removed the old copy at " + old); } catch { }

                if (startInstalled)
                    Process.Start(new ProcessStartInfo(InstalledExe, "--installed") { UseShellExecute = false, WorkingDirectory = InstallDir })?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.Write("install failed: " + ex);
                return false;
            }
        }

        static void Register()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "LiteOptions");
                k.SetValue("DisplayVersion", VersionText);
                k.SetValue("Publisher", "LiteOptions");
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
                k.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
                k.SetValue("Comments", "Lightweight settings for Logitech Pebble Mouse 2 M350s and K380");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try { k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord); } catch { }
            }
        }

        static void CreateShortcut()
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath });
                var t = lnk.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { InstallDir });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Mouse and keyboard settings" });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe + ",0" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(lnk);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
            catch (Exception ex) { Logger.Write("Start menu shortcut not created: " + ex.Message); }
        }

        /// <summary>Remove everything. Called from Windows' Installed apps or the in-app button (as a separate process).</summary>
        public static void Uninstall()
        {
            StopOtherInstances();

            // undo the one setting that lives in Windows rather than in our files
            try
            {
                var s = AppSettings.Load();
                if (s.ReverseScrollPid != 0) SystemTweaks.RequestReverseScroll((ushort)s.ReverseScrollPid, false);
            }
            catch { }

            try { SystemTweaks.SetStartup(false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); } catch { }
            try { if (Directory.Exists(AppSettings.Folder)) Directory.Delete(AppSettings.Folder, true); } catch { }

        }

        /// <summary>The running exe can't delete itself, so a hidden shell removes the folder a few seconds after we exit.</summary>
        public static void ScheduleFolderRemoval()
        {
            if (!Directory.Exists(InstallDir)) return;
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 5 > NUL & rd /s /q \"{InstallDir}\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })?.Dispose();
            }
            catch { }
        }
    }
}
