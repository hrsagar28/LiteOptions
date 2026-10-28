using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using LiteOptions.Hid;
using LiteOptions.Input;

namespace LiteOptions.UI
{
    /// <summary>The running app: tray icon, settings, input hooks and device manager.</summary>
    sealed partial class TrayApp : ApplicationContext
    {
        public static string Version => Installer.VersionText;
        public static readonly Icon AppIcon = LoadIcon();
        public static readonly Image Logo = LoadLogo();

        static Image LoadLogo()
        {
            try
            {
                using (var s = typeof(TrayApp).Assembly.GetManifestResourceStream("LiteOptions.logo.png"))
                    if (s != null) return new Bitmap(Image.FromStream(s));
            }
            catch { }
            return AppIcon.ToBitmap();
        }

        public AppSettings Settings;
        public readonly DeviceManager Devices;
        public readonly InputHooks Hooks;
        readonly SynchronizationContext ui;
        readonly NotifyIcon tray;
        readonly MessageWindow messages;
        MainForm form;
        readonly Dictionary<string, int> warnedAt = new Dictionary<string, int>();
        ToolStripMenuItem miMouse, miKeys, miFn, miPause;
        bool optionsWarned;

        public TrayApp(bool showWindow, string welcome = null) : this(showWindow, new WinDeviceSource(), true, welcome) { }

        readonly bool live;

        public TrayApp(bool showWindow, IDeviceSource source, bool live, string welcome = null)
        {
            this.live = live;
            Theme.Init();
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            Settings = AppSettings.Load();
            Logger.Write($"LiteOptions {Version} starting ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");

            Osd.Enabled = Settings.ShowPopups;
            if (live)
            {
                Hooks = new InputHooks();
                Hooks.Update(Settings);
                Hooks.Gesture += (dir, a) => ui.Post(_ => Osd.Show(
                    dir == "up" ? Theme.IcUp : dir == "down" ? Theme.IcDown : dir == "left" ? Theme.IcLeft : Theme.IcRight,
                    Presets.Describe(a)), null);
                ActionRunner.AppCommand = cmd => ui.Post(_ => AppCommand(cmd), null);
                ActionRunner.Start();
                Hooks.Start();
            }

            Devices = new DeviceManager(source, () => Settings);
            Devices.Changed += () => ui.Post(_ => OnDevicesChanged(), null);

            if (live)
            {
                messages = new MessageWindow(this);
                tray = BuildTray();
                StartIpc();
            }
            Devices.Start();

            if (live) try { SystemTweaks.FixStartupPath(); } catch { }
            bool firstRun = !Settings.FirstRunDone;
            if (firstRun && live)
            {
                Settings.FirstRunDone = true;
                try { SystemTweaks.SetStartup(true); } catch { }
                Settings.Save();
            }
            if (showWindow || firstRun || (!Settings.SetupDone && live)) ShowWindow();
            if (welcome != null && tray != null) tray.ShowBalloonTip(6000, "LiteOptions " + Version, welcome, ToolTipIcon.Info);
            if (!Settings.SetupDone && live && !HasSettingsUi) ui.Post(_ => RunSetup(), null); // the WinUI window runs its own setup

            if (live)
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(4000);
                    var logi = SystemTweaks.RunningLogiProcesses();
                    if (logi.Count > 0) ui.Post(__ => WarnOptionsPlus(), null);
                });
        }

        static Icon LoadIcon()
        {
            try
            {
                using (var s = typeof(TrayApp).Assembly.GetManifestResourceStream("LiteOptions.app.ico"))
                    if (s != null) return new Icon(s);
            }
            catch { }
            return SystemIcons.Application;
        }

        public void Commit()
        {
            Settings.Save();
            Hooks?.Update(Settings);
            Osd.Enabled = Settings.ShowPopups;
            UpdateTray();
        }

        // ───────────────────────── app commands (from keys, gestures, tray) ─────────────────────────

        void AppCommand(string cmd)
        {
            switch (cmd)
            {
                case "fn_toggle": ToggleFn(); break;
                case "pause_toggle": TogglePause(); break;
                case "open": ShowWindow(); break;
            }
        }

        public void ToggleFn()
        {
            Settings.FnMode = Settings.FnMode == "standard" ? "media" : "standard";
            bool media = Settings.FnMode == "media";
            Commit();
            Devices.SetFnMode(media);
            form?.RefreshDevices();
            BroadcastSettings();
            var k = Devices.Keyboard;
            string sub = k == null ? "Keyboard not connected; applied when it connects"
                       : !k.FnSupported ? "This keyboard doesn't support switching"
                       : media ? "Hold Fn for F1–F12" : "Hold Fn for media keys";
            Osd.Show(Theme.IcKeyboard, media ? "Media keys first" : "F1–F12 keys first", sub);
        }

        public void TogglePause()
        {
            Settings.Paused = !Settings.Paused;
            Commit();
            form?.RefreshDevices();
            BroadcastSettings();
            Osd.Show(Settings.Paused ? Theme.IcPause : Theme.IcCheck, Settings.Paused ? "Remapping paused" : "Remapping on",
                Settings.Paused ? "Buttons and keys work as normal" : null);
        }

        void RunSetup()
        {
            if (form == null || form.IsDisposed) ShowWindow();
            using (var d = new SetupDialog(this))
            {
                d.ShowDialog(form);
            }
            Settings.SetupDone = true;
            Commit();
            form?.ReloadAll();
        }

        public string DiagnosticsText()
        {
            var lines = new List<string> { $"LiteOptions {Installer.VersionText} on {Environment.OSVersion.VersionString}, {(Environment.Is64BitProcess ? "64" : "32")}-bit process, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, installed={Installer.RunningInstalled}", "" };
            foreach (var d in Devices.Snapshot())
                lines.Add($"{d.Kind}: {d.Name} | {d.Connection} | PID {d.Pid:X4} | online {d.Online} | battery {d.Battery?.Text ?? "?"}" +
                          $" | DPI {(d.Dpi != null ? d.Dpi.Current + $" ({d.Dpi.Min}-{d.Dpi.Max} step {d.Dpi.Step})" : "n/a")}" +
                          $" | Fn swap {(d.FnSupported ? (d.FnMedia == true ? "media-first" : d.FnMedia == false ? "F-keys-first" : "?") : "n/a")}" +
                          $"{(d.Firmware != null ? " | fw " + d.Firmware : "")}{(d.LastError != null ? " | last error: " + d.LastError : "")}");
            lines.Add("");
            lines.Add("Logitech HID collections:");
            lines.AddRange(Devices.LastEnumeration.Select(l => "  " + l));
            lines.Add("");
            lines.Add("Log:");
            lines.Add(Logger.Snapshot());
            return string.Join(Environment.NewLine, lines);
        }

        public bool IsLow(BatteryInfo b)
        {
            if (b == null) return false;
            if (b.Percent.HasValue) return b.Percent.Value <= Settings.LowBatteryPercent;
            return b.IsLow;
        }

        // ───────────────────────── window ─────────────────────────

        public MainForm Window => form;

        public void ShowWindow()
        {
            if (live && HasSettingsUi && LaunchSettingsUi()) return;
            ShowClassicWindow();
        }

        /// <summary>The built-in WinForms window, used when the WinUI window isn't installed or can't start.</summary>
        public void ShowClassicWindow()
        {
            if (form == null || form.IsDisposed) form = new MainForm(this);
            if (!form.Visible) form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
            form.BringToFront();
        }

        public void ShowFromOtherInstance() => ui.Post(_ => ShowWindow(), null);

        public void OnWindowHidden()
        {
            // free the window's memory while it's in the tray; it's rebuilt on demand
            var f = form;
            form = null;
            ui.Post(_ => { if (f != null) { f.AllowClose = true; f.Close(); f.Dispose(); } GC.Collect(); }, null);
        }

        // ───────────────────────── tray ─────────────────────────

        NotifyIcon BuildTray()
        {
            var menu = Menus.Create();
            var open = Menus.Item("Open LiteOptions", ShowWindow);
            open.Font = Theme.BodyStrong;
            miMouse = Menus.Item("Mouse: —", null); miMouse.Enabled = false;
            miKeys = Menus.Item("Keyboard: —", null); miKeys.Enabled = false;
            miFn = Menus.Item("F1–F12 keys first", ToggleFn);
            miPause = Menus.Item("Pause remapping", TogglePause);
            menu.Items.AddRange(new ToolStripItem[]
            {
                open, Menus.Separator(), miMouse, miKeys, Menus.Separator(), miFn, miPause, Menus.Separator(),
                Menus.Item("Exit", ExitThread)
            });
            menu.Opening += (s, e) =>
            {
                Theme.Init();
                if (form == null) Theme.ApplyDpi(Theme.SystemDpi);
                menu.BackColor = Theme.MenuBg;
                UpdateTray();
            };

            Icon small;
            try { small = new Icon(AppIcon, SystemInformation.SmallIconSize); } catch { small = AppIcon; }
            baseTrayIcon = small;
            var icon = new NotifyIcon { Icon = small, Text = "LiteOptions", ContextMenuStrip = menu, Visible = true };
            icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            return icon;
        }

        Icon baseTrayIcon, badgeIcon;
        string badgeKey = "";

        /// <summary>Lowest battery among connected devices, as (fraction, colour bucket), or null.</summary>
        (double frac, int bucket)? LowestBattery()
        {
            (double, int)? best = null;
            foreach (var d in Devices.Snapshot())
            {
                if (d.Battery == null || !d.Online) continue;
                double f = Theme.BatteryFraction(d.Battery);
                int bucket = IsLow(d.Battery) ? 2 : (d.Battery.Percent.HasValue ? (d.Battery.Percent <= 30 ? 1 : 0) : (d.Battery.Level == "Low" ? 1 : 0));
                if (best == null || f < best.Value.Item1) best = (f, bucket);
            }
            return best;
        }

        void UpdateTrayIcon()
        {
            if (tray == null || baseTrayIcon == null) return;
            var low = LowestBattery();
            string key = low == null ? "none" : $"{low.Value.bucket}:{Math.Round(low.Value.frac * 5)}";
            if (key == badgeKey) return;
            badgeKey = key;
            var old = badgeIcon;
            if (low == null) { tray.Icon = baseTrayIcon; badgeIcon = null; }
            else
            {
                try
                {
                    using (var bmp = TrayBadge.Compose(baseTrayIcon, low.Value.frac, low.Value.bucket))
                    {
                        IntPtr h = bmp.GetHicon();
                        badgeIcon = Icon.FromHandle(h);
                        tray.Icon = badgeIcon;
                    }
                }
                catch (Exception ex) { Logger.Write("tray icon: " + ex.Message); tray.Icon = baseTrayIcon; badgeIcon = null; }
            }
            if (old != null) { Native.DestroyIcon(old.Handle); old.Dispose(); }
        }

        void UpdateTray()
        {
            if (tray == null) return;
            UpdateTrayIcon();
            var m = Devices.Mouse;
            var k = Devices.Keyboard;
            string ms = m == null ? "not found" : !m.Online ? "asleep" : m.Battery?.Text ?? "connected";
            string ks = k == null ? "not found" : !k.Online ? "asleep" : k.Battery?.Text ?? "connected";
            miMouse.Text = "Mouse: " + ms;
            miKeys.Text = "Keyboard: " + ks;
            miFn.Checked = Settings.FnMode == "standard";
            miFn.Enabled = k == null || k.FnSupported;
            miPause.Checked = Settings.Paused;
            string tip = $"LiteOptions{(Settings.Paused ? " (paused)" : "")}\nMouse {ms}\nKeyboard {ks}";
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void OnDevicesChanged()
        {
            BroadcastDevices();
            UpdateTray();
            form?.RefreshDevices();
            if (tray == null || !Settings.Notifications) return;
            foreach (var d in Devices.Snapshot())
            {
                var b = d.Battery;
                if (b == null) continue;
                if (!IsLow(b)) { warnedAt.Remove(d.Key); continue; }
                int level = b.Percent ?? (b.Level == "Critical" ? 5 : 15);
                if (warnedAt.TryGetValue(d.Key, out int last) && level > last - 5) continue;
                warnedAt[d.Key] = level;
                tray.ShowBalloonTip(8000, "Battery low", $"{d.Name}: {b.Text}. Time for a fresh {(d.Kind == DeviceKind.Mouse ? "AA" : "AAA")} battery soon.", ToolTipIcon.Warning);
            }
        }

        void WarnOptionsPlus()
        {
            if (optionsWarned || tray == null) return;
            optionsWarned = true;
            tray.ShowBalloonTip(10000, "Logi Options+ is running",
                "It will fight LiteOptions over your mouse and keyboard. Uninstall it from Settings › Apps › Installed apps.", ToolTipIcon.Warning);
        }

        protected override void ExitThreadCore()
        {
            if (form != null) { form.AllowClose = true; form.Close(); }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (badgeIcon != null) { Native.DestroyIcon(badgeIcon.Handle); badgeIcon = null; }
            Osd.Shutdown();
            server?.Dispose();
            messages?.DestroyHandle();
            Hooks?.Dispose();
            Devices.Dispose();
            base.ExitThreadCore();
        }

        /// <summary>Hidden top-level window that receives device-arrival and resume-from-sleep broadcasts.</summary>
        sealed class MessageWindow : NativeWindow
        {
            readonly TrayApp app;
            readonly IntPtr notify;

            public MessageWindow(TrayApp app)
            {
                this.app = app;
                CreateHandle(new CreateParams { Caption = "LiteOptions.Messages" });
                Native.HidD_GetHidGuid(out Guid hid);
                var filter = new Native.DEV_BROADCAST_DEVICEINTERFACE
                {
                    dbcc_size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.DEV_BROADCAST_DEVICEINTERFACE)),
                    dbcc_devicetype = Native.DBT_DEVTYP_DEVICEINTERFACE,
                    dbcc_classguid = hid
                };
                notify = Native.RegisterDeviceNotification(Handle, ref filter, 0);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_DEVICECHANGE)
                {
                    int ev = (int)m.WParam;
                    if (ev == Native.DBT_DEVICEARRIVAL || ev == Native.DBT_DEVICEREMOVECOMPLETE)
                        app.Devices.RequestScan(1500);
                }
                else if (m.Msg == Native.WM_POWERBROADCAST)
                {
                    int ev = (int)m.WParam;
                    if (ev == Native.PBT_APMRESUMEAUTOMATIC || ev == Native.PBT_APMRESUMESUSPEND)
                    {
                        Logger.Write("Resumed from sleep");
                        app.Hooks?.Rehook();
                        app.Devices.RequestScan(5000);
                    }
                }
                base.WndProc(ref m);
            }

            public override void DestroyHandle()
            {
                if (notify != IntPtr.Zero) Native.UnregisterDeviceNotification(notify);
                base.DestroyHandle();
            }
        }
    }
}
