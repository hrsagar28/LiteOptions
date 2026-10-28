using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace LiteOptions
{
    /// <summary>Windows-level pointer/scroll settings, start-up registration and the Options+ check.</summary>
    public static class SystemTweaks
    {
        // ── pointer speed (Windows "pointer speed" slider, 1..20, default 10) ──
        public static int GetPointerSpeed()
        {
            int v = 10;
            Native.SystemParametersInfo(Native.SPI_GETMOUSESPEED, 0, ref v, 0);
            return Math.Max(1, Math.Min(20, v));
        }

        public static void SetPointerSpeed(int speed)
        {
            speed = Math.Max(1, Math.Min(20, speed));
            Native.SystemParametersInfoSet(Native.SPI_SETMOUSESPEED, 0, new IntPtr(speed), Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
        }

        // ── "Enhance pointer precision" (acceleration) ──
        public static bool GetPrecision()
        {
            var p = new int[3];
            Native.SystemParametersInfo(Native.SPI_GETMOUSE, 0, p, 0);
            return p[2] != 0;
        }

        public static void SetPrecision(bool on)
        {
            var p = new int[3];
            Native.SystemParametersInfo(Native.SPI_GETMOUSE, 0, p, 0);
            if (on) { if (p[0] == 0 && p[1] == 0) { p[0] = 6; p[1] = 10; } p[2] = 1; }
            else p[2] = 0;
            Native.SystemParametersInfo(Native.SPI_SETMOUSE, 0, p, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
        }

        // ── lines per wheel notch ──
        public static int GetWheelLines()
        {
            int v = 3;
            Native.SystemParametersInfo(Native.SPI_GETWHEELSCROLLLINES, 0, ref v, 0);
            return v;
        }

        public static void SetWheelLines(int lines)
        {
            lines = Math.Max(1, Math.Min(30, lines));
            Native.SystemParametersInfoSet(Native.SPI_SETWHEELSCROLLLINES, (uint)lines, IntPtr.Zero, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
        }

        // ── reverse scrolling for one mouse (Windows' own per-device FlipFlopWheel flag; needs admin once) ──
        const string HidEnum = @"SYSTEM\CurrentControlSet\Enum\HID";

        static IEnumerable<string> MatchingDeviceParamKeys(ushort pid)
        {
            string pidHex = pid.ToString("X4");
            using (var hid = Registry.LocalMachine.OpenSubKey(HidEnum))
            {
                if (hid == null) yield break;
                foreach (var dev in hid.GetSubKeyNames())
                {
                    string up = dev.ToUpperInvariant();
                    if (!up.Contains("046D")) continue;
                    if (!up.Contains("PID&" + pidHex) && !up.Contains("PID_" + pidHex)) continue;
                    RegistryKey devKey = null;
                    try { devKey = hid.OpenSubKey(dev); } catch { }
                    if (devKey == null) continue;
                    using (devKey)
                        foreach (var inst in devKey.GetSubKeyNames())
                            yield return HidEnum + "\\" + dev + "\\" + inst + "\\Device Parameters";
                }
            }
        }

        /// <summary>null = unknown/not found, otherwise whether reverse scrolling is set for this mouse.</summary>
        public static bool? GetReverseScroll(ushort pid)
        {
            try
            {
                foreach (var path in MatchingDeviceParamKeys(pid))
                    using (var k = Registry.LocalMachine.OpenSubKey(path))
                    {
                        var v = k?.GetValue("FlipFlopWheel");
                        if (v is int i) return i != 0;
                    }
            }
            catch { }
            return null;
        }

        /// <summary>Runs elevated (via --set-reverse-scroll). Returns the number of entries changed.</summary>
        public static int SetReverseScrollElevated(ushort pid, bool on)
        {
            int changed = 0;
            foreach (var path in MatchingDeviceParamKeys(pid).ToList())
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(path, true))
                    {
                        if (k == null || k.GetValue("FlipFlopWheel") == null) continue; // only mouse collections have this value
                        k.SetValue("FlipFlopWheel", on ? 1 : 0, RegistryValueKind.DWord);
                        changed++;
                    }
                }
                catch { }
            }
            return changed;
        }

        /// <summary>Starts an elevated copy of this exe to flip the flag. Returns its exit code, or -1 if cancelled.</summary>
        public static int RequestReverseScroll(ushort pid, bool on)
        {
            try
            {
                var psi = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,
                    $"--set-reverse-scroll {pid:X4} {(on ? 1 : 0)}")
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(15000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return -1; } // user said no to UAC
        }

        // ── start with Windows ──
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "LiteOptions";

        public static bool GetStartup()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                var v = k?.GetValue(RunName) as string;
                return v != null && v.IndexOf(System.Windows.Forms.Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>If start-up is on but points at an old location (exe was moved), point it here.</summary>
        public static void FixStartupPath()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                if (k?.GetValue(RunName) is string v && v.IndexOf(System.Windows.Forms.Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) < 0)
                    SetStartup(true);
        }

        public static bool StartupValueExists()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue(RunName) != null;
        }

        public static void SetStartupPath(string exe)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(RunName, "\"" + exe + "\" --tray");
        }

        public static void SetStartup(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunName, "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --tray");
                else if (k.GetValue(RunName) != null) k.DeleteValue(RunName, false);
            }
        }

        // ── Logi Options+ check ──
        public static List<string> RunningLogiProcesses()
        {
            var found = new List<string>();
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    string n;
                    try { n = p.ProcessName.ToLowerInvariant(); } catch { continue; } finally { p.Dispose(); }
                    if (n.StartsWith("logioptionsplus") || n == "logioptions" || n.StartsWith("logi_crashpad") || n == "logioverlay")
                        if (!found.Contains(n)) found.Add(n);
                }
            }
            catch { }
            return found;
        }
    }
}
