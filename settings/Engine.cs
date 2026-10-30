using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteOptions.Ipc;
using Microsoft.UI.Dispatching;
using IpcCodec = LiteOptions.Ipc.Ipc;

namespace LiteOptions.SettingsApp
{
    /// <summary>The background engine as seen from the window: requests with replies, plus pushed events.</summary>
    public interface IEngine
    {
        bool Connected { get; }
        /// <summary>Raised on the window's thread.</summary>
        event Action<bool> ConnectionChanged;
        /// <summary>Raised on the window's thread.</summary>
        event Action<IpcMessage> Pushed;
        void Start(DispatcherQueue dq);
        Task<IpcMessage> RequestAsync(IpcMessage m, int timeoutMs = 8000);
    }

    /// <summary>Talks to LiteOptions.exe over the per-user named pipe and reconnects by itself.</summary>
    public sealed class EngineClient : IEngine
    {
        DispatcherQueue dq;
        readonly object writeGate = new object();
        StreamWriter writer;
        readonly ConcurrentDictionary<int, TaskCompletionSource<IpcMessage>> pending = new ConcurrentDictionary<int, TaskCompletionSource<IpcMessage>>();
        int nextId;

        public bool Connected { get; private set; }
        public event Action<bool> ConnectionChanged;
        public event Action<IpcMessage> Pushed;

        public void Start(DispatcherQueue dq)
        {
            this.dq = dq;
            new Thread(Loop) { IsBackground = true, Name = "engine-link" }.Start();
        }

        void Loop()
        {
            string name = IpcCodec.PipeName;
            while (true)
            {
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
                    {
                        pipe.Connect(1500);
                        var reader = new StreamReader(pipe, new UTF8Encoding(false));
                        lock (writeGate) writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                        SetConnected(true);
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.Length == 0) continue;
                            IpcMessage m;
                            try { m = IpcCodec.FromLine(line); } catch { continue; }
                            if (!string.IsNullOrEmpty(m.Event)) dq.TryEnqueue(() => Pushed?.Invoke(m));
                            else if (m.Id != 0 && pending.TryRemove(m.Id, out var tcs)) tcs.TrySetResult(m);
                        }
                    }
                }
                catch (TimeoutException) { }
                catch (IOException) { }
                catch (Exception ex) { try { Logger.Write("settings link: " + ex.Message); } catch { } }

                lock (writeGate) writer = null;
                foreach (var id in pending.Keys.ToList())
                    if (pending.TryRemove(id, out var t)) t.TrySetResult(new IpcMessage { Error = "disconnected" });
                SetConnected(false);
                Thread.Sleep(1200);
            }
        }

        void SetConnected(bool on)
        {
            if (Connected == on) return;
            Connected = on;
            dq.TryEnqueue(() => ConnectionChanged?.Invoke(on));
        }

        public async Task<IpcMessage> RequestAsync(IpcMessage m, int timeoutMs = 8000)
        {
            int id = Interlocked.Increment(ref nextId);
            m.Id = id;
            var tcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = tcs;
            string line = IpcCodec.ToLine(m);
            bool sent = await Task.Run(() =>
            {
                lock (writeGate)
                {
                    if (writer == null) return false;
                    try { writer.WriteLine(line); return true; } catch { return false; }
                }
            });
            if (!sent)
            {
                pending.TryRemove(id, out _);
                return new IpcMessage { Error = "not connected" };
            }
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (done != tcs.Task)
            {
                pending.TryRemove(id, out _);
                return new IpcMessage { Error = "timeout" };
            }
            return tcs.Task.Result;
        }
    }

    /// <summary>A pretend engine with a Pebble and a K380, for screenshots and for trying the window without devices.</summary>
    public sealed class DemoEngine : IEngine
    {
        DispatcherQueue dq;
        AppSettings settings;
        readonly List<DeviceDto> devices = new List<DeviceDto>
        {
            new DeviceDto { Key = "demo-mouse", Name = "Pebble Mouse 2 M350s", Kind = "Mouse", Connection = "Bluetooth", Pid = 0xB03A, Online = true,
                Firmware = "RQM 40.02.B0017", HasBattery = true, BatteryPercent = 80, BatteryLevel = "Good",
                DpiMin = 400, DpiMax = 4000, DpiStep = 100, DpiCurrent = 1000, DpiDefault = 1000,
                CurrentHost = 2, Hosts = new List<HostDto> { new HostDto { Number = 1, Paired = true, Name = "Pixel 8" }, new HostDto { Number = 2, Paired = true, Name = "DESKTOP-OFFICE" }, new HostDto { Number = 3, Paired = true, Name = "WORK-LAPTOP" } } },
            new DeviceDto { Key = "demo-kb", Name = "Keyboard K380", Kind = "Keyboard", Connection = "Bluetooth", Pid = 0xB342, Online = true,
                HasBattery = true, BatteryPercent = -1, BatteryLevel = "Good", FnSupported = true, FnMedia = 0, DisableCaps = 0x1D, DisabledKeys = 0x10,
                CurrentHost = 1, Hosts = new List<HostDto> { new HostDto { Number = 1 }, new HostDto { Number = 2, Paired = true, Name = "WORK-LAPTOP" }, new HostDto { Number = 3, Paired = true, Name = "iPhone" } } },
        };
        readonly SystemDto system = new SystemDto { Version = "2.3.3", Installed = true, PointerSpeed = 10, Precision = true, WheelLines = 3, Startup = true, ReverseScroll = 0, Architecture = RuntimeInformation.ProcessArchitecture.ToString() };

        public bool Connected { get; private set; }
        public event Action<bool> ConnectionChanged;
        public event Action<IpcMessage> Pushed;

        public DemoEngine()
        {
            settings = new AppSettings();
            settings.Normalize();
            var g = settings.Global;
            g.MiddleMode = "gestures";
            g.Keys.Add(new KeyBinding { Mods = 0, Vk = 0xB3, Action = ActionSpec.Of("mute") });
            g.Keys.Add(new KeyBinding { Mods = (int)Mods.Ctrl | (int)Mods.Shift, Vk = 0x56, Action = ActionSpec.Of("paste_plain") });
            g.Keys.Add(new KeyBinding { Mods = 0, Vk = 0x2C, Action = ActionSpec.Of("snip") });
            settings.FnMode = "standard";
            settings.MouseDpi = 1000;
            settings.SetupDone = true;
            settings.FirstRunDone = true;
            var excel = new Profile { App = "excel.exe", MiddleMode = "action", MiddleAction = ActionSpec.Keys((int)Mods.Ctrl, 0x22) };
            excel.Normalize();
            settings.Profiles.Add(excel);
            settings.Normalize();
        }

        public void Start(DispatcherQueue dq)
        {
            this.dq = dq;
            dq.TryEnqueue(() => { Connected = true; ConnectionChanged?.Invoke(true); });
        }

        public void SetSetupDone(bool done) => settings.SetupDone = done;

        public Task<IpcMessage> RequestAsync(IpcMessage m, int timeoutMs = 8000)
        {
            var ok = new IpcMessage { Ok = true };
            switch (m.Cmd)
            {
                case "hello":
                case "state":
                    ok.Settings = settings.Clone(); ok.Devices = devices; ok.System = system;
                    break;
                case "saveSettings":
                    settings = m.Settings.Clone();
                    break;
                case "setDpi":
                    devices[0].DpiCurrent = m.Value;
                    break;
                case "setFnMode":
                    settings.FnMode = m.Text;
                    devices[1].FnMedia = m.Text == "media" ? 1 : m.Text == "standard" ? 0 : devices[1].FnMedia;
                    break;
                case "capture":
                    Task.Delay(1100).ContinueWith(_ => dq.TryEnqueue(() =>
                        Pushed?.Invoke(new IpcMessage { Event = "captured", Mods = (int)Mods.Ctrl | (int)Mods.Shift, Vk = 0x56 })));
                    break;
                case "setPointerSpeed": system.PointerSpeed = m.Value; break;
                case "setPrecision": system.Precision = m.On; break;
                case "setWheelLines": system.WheelLines = m.Value; break;
                case "setStartup": system.Startup = m.On; break;
                case "setReverse": system.ReverseScroll = m.On ? 1 : 0; break;
                case "diagnostics":
                    ok.Text = "LiteOptions 2.0.0 (demo)\r\nMouse: Pebble Mouse 2 M350s | Bluetooth | battery 80%\r\nKeyboard: Keyboard K380 | Bluetooth | battery Good";
                    break;
            }
            return Task.FromResult(ok);
        }
    }

    static class W32
    {
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        /// <summary>The DPI of the display at a point on the screen (96 if Windows won't say).</summary>
        public static uint DpiAt(int x, int y)
        {
            try
            {
                var m = MonitorFromPoint(new POINT { X = x, Y = y }, 2 /* MONITOR_DEFAULTTONEAREST */);
                if (m != IntPtr.Zero && GetDpiForMonitor(m, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out _) == 0 && dx > 0) return dx;
            }
            catch { }
            return 96;
        }

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        public static string ExePath(int pid)
        {
            IntPtr h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, (uint)pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint n = (uint)sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
    }
}
