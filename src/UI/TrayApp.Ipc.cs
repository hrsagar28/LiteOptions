using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using LiteOptions.Hid;
using LiteOptions.Ipc;

namespace LiteOptions.UI
{
    /// <summary>The engine's side of the link to the WinUI settings window.</summary>
    sealed partial class TrayApp
    {
        EngineServer server;

        /// <summary>The WinUI settings window, shipped next to the engine in a "Settings" folder.</summary>
        public static string SettingsUiExe =>
            Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? "", "Settings", "LiteOptions.Settings.exe");

        public static bool HasSettingsUi => Native.IsWindows && File.Exists(SettingsUiExe);

        void StartIpc()
        {
            server = new EngineServer(ui, HandleIpc);
            server.Start();
        }

        /// <summary>Open the WinUI window (it keeps a single instance itself). Falls back to the classic window if it can't start.</summary>
        bool LaunchSettingsUi(string args = "")
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(SettingsUiExe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(SettingsUiExe) });
                if (p == null) return false;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        if (p.WaitForExit(5000) && p.ExitCode != 0)
                        {
                            Logger.Write("Settings window exited with code " + p.ExitCode + "; opening the classic window");
                            ui.Post(__ => ShowClassicWindow(), null);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                });
                return true;
            }
            catch (Exception ex)
            {
                Logger.Write("Settings window could not start: " + ex.Message);
                return false;
            }
        }

        void BroadcastSettings() => server?.Broadcast(new IpcMessage { Event = "settings", Settings = Settings });
        void BroadcastDevices() => server?.Broadcast(new IpcMessage { Event = "devices", Devices = DeviceDtos() });

        System.Collections.Generic.List<DeviceDto> DeviceDtos() => Devices.Snapshot().Select(d => new DeviceDto
        {
            Key = d.Key, Name = d.Name ?? "", Kind = d.Kind.ToString(), Connection = d.Connection ?? "", Pid = d.Pid, Online = d.Online,
            Firmware = d.Firmware ?? "", HasBattery = d.Battery != null, BatteryPercent = d.Battery?.Percent ?? -1, BatteryLevel = d.Battery?.Level ?? "",
            BatteryLow = IsLow(d.Battery), Charging = d.Battery?.Charging ?? false,
            DpiMin = d.Dpi?.Min ?? 0, DpiMax = d.Dpi?.Max ?? 0, DpiStep = d.Dpi?.Step ?? 0, DpiCurrent = d.Dpi?.Current ?? 0, DpiDefault = d.Dpi?.Default ?? 0,
            FnSupported = d.FnSupported, FnMedia = d.FnMedia == null ? -1 : d.FnMedia.Value ? 1 : 0, LastError = d.LastError ?? ""
        }).ToList();

        SystemDto SystemState()
        {
            var s = new SystemDto { Version = Version, Installed = Installer.RunningInstalled, Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() };
            try { s.PointerSpeed = SystemTweaks.GetPointerSpeed(); s.Precision = SystemTweaks.GetPrecision(); s.WheelLines = SystemTweaks.GetWheelLines(); } catch { }
            try { s.Startup = SystemTweaks.GetStartup(); } catch { }
            var m = Devices.Mouse;
            if (m != null) { var r = SystemTweaks.GetReverseScroll(m.Pid); s.ReverseScroll = r == null ? -1 : r.Value ? 1 : 0; }
            s.OptionsPlus = string.Join(", ", SystemTweaks.RunningLogiProcesses());
            return s;
        }

        IpcMessage HandleIpc(IpcMessage m, EngineServer.Client c)
        {
            var ok = new IpcMessage { Ok = true };
            switch (m.Cmd)
            {
                case "hello":
                    c.IsUi = m.Text == "ui";
                    return new IpcMessage { Ok = true, Settings = Settings, Devices = DeviceDtos(), System = SystemState() };

                case "state":
                    return new IpcMessage { Ok = true, Settings = Settings, Devices = DeviceDtos(), System = SystemState() };

                case "saveSettings":
                    if (m.Settings == null) return new IpcMessage { Error = "no settings" };
                    var incoming = m.Settings;
                    incoming.Normalize();
                    incoming.FirstRunDone = Settings.FirstRunDone || incoming.FirstRunDone;
                    incoming.ReverseScrollPid = Settings.ReverseScrollPid; // owned by the engine
                    Settings = incoming;
                    Commit();
                    Devices.ApplySettingsNow();
                    form?.ReloadAll();
                    return ok;

                case "setDpi":
                    if (m.Value > 0)
                    {
                        if (Settings.MouseDpi != m.Value) { Settings.MouseDpi = m.Value; Settings.Save(); }
                        Devices.SetDpi(m.Value);
                        if (m.On)
                        {
                            var mouse = Devices.Mouse;
                            double frac = mouse?.Dpi != null && mouse.Dpi.Max > mouse.Dpi.Min ? (m.Value - mouse.Dpi.Min) / (double)(mouse.Dpi.Max - mouse.Dpi.Min) : 0;
                            Osd.Show(Theme.IcMouse, m.Value + " DPI", mouse == null ? "Mouse not connected" : "Mouse sensitivity", frac);
                        }
                    }
                    return ok;

                case "setFnMode":
                    if (m.Text != "media" && m.Text != "standard" && m.Text != "unchanged") return new IpcMessage { Error = "bad mode" };
                    Settings.FnMode = m.Text;
                    Commit();
                    if (m.Text != "unchanged") Devices.SetFnMode(m.Text == "media");
                    return ok;

                case "rescan":
                    Devices.RequestScan(10);
                    Devices.RequestPoll();
                    return ok;

                case "capture":
                    if (Hooks == null) return new IpcMessage { Error = "Key recording needs the background app" };
                    Hooks.BeginCapture((mods, vk) => c.Send(new IpcMessage { Event = "captured", Mods = mods, Vk = vk }));
                    return ok;

                case "cancelCapture":
                    Hooks?.CancelCapture();
                    return ok;

                case "setPointerSpeed": SafeSys(() => SystemTweaks.SetPointerSpeed(m.Value)); return ok;
                case "setPrecision": SafeSys(() => SystemTweaks.SetPrecision(m.On)); return ok;
                case "setWheelLines": SafeSys(() => SystemTweaks.SetWheelLines(m.Value)); return ok;
                case "setStartup": SafeSys(() => SystemTweaks.SetStartup(m.On)); return ok;

                case "setReverse":
                    {
                        var mouse = Devices.Mouse;
                        if (mouse == null) return new IpcMessage { Error = "nomouse" };
                        int code = SystemTweaks.RequestReverseScroll(mouse.Pid, m.On);
                        if (code == 0)
                        {
                            Settings.ReverseScrollPid = m.On ? mouse.Pid : 0;
                            Settings.Save();
                            return ok;
                        }
                        return new IpcMessage { Error = code == -1 ? "cancelled" : "notfound" };
                    }

                case "diagnostics":
                    return new IpcMessage { Ok = true, Text = DiagnosticsText() };

                case "toggleFn": ToggleFn(); return ok;

                case "uninstall":
                    try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--uninstall --confirmed") { UseShellExecute = false })?.Dispose(); }
                    catch (Exception ex) { return new IpcMessage { Error = ex.Message }; }
                    return ok;

                case "classic":
                    ShowClassicWindow();
                    return ok;
            }
            return new IpcMessage { Error = "unknown command " + m.Cmd };
        }

        static void SafeSys(Action a) { try { a(); } catch (Exception ex) { Logger.Write("system setting failed: " + ex.Message); } }
    }
}
