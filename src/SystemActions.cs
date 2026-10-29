using System;
using System.Management;
using System.Runtime.InteropServices;

namespace LiteOptions
{
    /// <summary>
    /// Actions that need Windows APIs rather than a key press: microphone mute, laptop-screen brightness and sleep.
    /// Called on the action thread; each returns a short result for the pop-up.
    /// </summary>
    public static class SystemActions
    {
        public sealed class Result
        {
            public string Title, Subtitle;
            public double? Level;     // 0..1 for a bar in the pop-up
            public bool Muted;
        }

        // ───────────── microphone ─────────────

        /// <summary>Mute or unmute the default microphone (and the default communications microphone, if it's a different one).</summary>
        public static Result ToggleMic()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                var console = Endpoint(enumerator, 0);
                var comms = Endpoint(enumerator, 2);
                var main = console ?? comms;
                if (main == null) return new Result { Title = "No microphone found", Subtitle = "Connect or enable a microphone in Sound settings" };
                Check(main.GetMute(out bool muted));
                bool want = !muted;
                var ctx = Guid.Empty;
                Check(main.SetMute(want, ref ctx));
                if (comms != null && !ReferenceEquals(comms, console)) try { comms.SetMute(want, ref ctx); } catch { }
                Release(console); if (!ReferenceEquals(comms, console)) Release(comms);
                return new Result { Title = want ? "Microphone muted" : "Microphone on", Muted = want };
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }

        static IAudioEndpointVolume Endpoint(IMMDeviceEnumerator e, int role)
        {
            if (e.GetDefaultAudioEndpoint(1 /* eCapture */, role, out IMMDevice dev) != 0 || dev == null) return null;
            try
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                Check(dev.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out object o));
                return (IAudioEndpointVolume)o;
            }
            finally { Marshal.ReleaseComObject(dev); }
        }

        static void Release(object o) { if (o != null) try { Marshal.ReleaseComObject(o); } catch { } }
        static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        // ───────────── brightness (built-in screen, through WMI) ─────────────

        /// <summary>Step the built-in screen's brightness up or down by 10%.</summary>
        public static Result StepBrightness(bool up)
        {
            var scope = new ManagementScope(@"root\WMI");
            int? current = null;
            using (var s = new ManagementObjectSearcher(scope, new SelectQuery("WmiMonitorBrightness")))
            using (var all = s.Get())
                foreach (ManagementObject o in all)
                    using (o) if (current == null) current = Convert.ToInt32(o["CurrentBrightness"]);
            if (current == null) return new Result { Title = "Brightness can't be changed", Subtitle = "Only a laptop's built-in screen can be dimmed this way" };

            int now = current.Value;
            int target = up ? Math.Min(100, (now / 10 + 1) * 10) : Math.Max(0, (now + 9) / 10 * 10 - 10);
            using (var s = new ManagementObjectSearcher(scope, new SelectQuery("WmiMonitorBrightnessMethods")))
            using (var all = s.Get())
                foreach (ManagementObject o in all)
                    using (o) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)target });
            return new Result { Title = target + "%", Level = target / 100.0 }; // the bar pop-up shows the title as its value
        }

        // ───────────── sleep ─────────────

        /// <summary>
        /// Put the PC to sleep. On Modern Standby PCs (most recent laptops, including Surface) that means switching the
        /// screen off, which is what Start › Power › Sleep does there; older PCs get a classic S3 sleep.
        /// </summary>
        public static void Sleep()
        {
            if (IsModernStandby())
                PostMessage(new IntPtr(0xFFFF) /* HWND_BROADCAST */, 0x0112 /* WM_SYSCOMMAND */, new IntPtr(0xF170) /* SC_MONITORPOWER */, new IntPtr(2) /* off */);
            else if (!SetSuspendState(false, false, false))
                Logger.Write("Sleep failed (error " + Marshal.GetLastWin32Error() + ")");
        }

        static bool IsModernStandby()
        {
            try
            {
                var caps = new byte[256];
                return GetPwrCapabilities(caps) && caps[20] != 0; // SYSTEM_POWER_CAPABILITIES.AoAc
            }
            catch { return false; }
        }

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("powrprof.dll", SetLastError = true)]
        static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        [DllImport("powrprof.dll")]
        static extern bool GetPwrCapabilities([Out] byte[] capabilities);
    }
}
