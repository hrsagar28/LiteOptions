using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteOptions
{
    /// <summary>All Win32 interop in one place. Layouts are valid for x86, x64 and ARM64.</summary>
    internal static class Native
    {
        // ───────────── hooks ─────────────
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        public const int WM_MOUSEMOVE = 0x0200, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MBUTTONDBLCLK = 0x0209, WM_MOUSEWHEEL = 0x020A;
        public const int WM_TIMER = 0x0113, WM_QUIT = 0x0012, WM_APP = 0x8000;

        public const uint LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80;
        public const uint LLMHF_INJECTED = 0x01;

        public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; public uint lPrivate; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        // ───────────── foreground window tracking ─────────────
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
        public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);

        // ───────────── SendInput ─────────────
        public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004, KEYEVENTF_SCANCODE = 0x0008;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004, MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010,
            MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100,
            MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000;
        public const uint XBUTTON1 = 1, XBUTTON2 = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion U; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool LockWorkStation();

        // ───────────── system parameters ─────────────
        public const uint SPI_GETMOUSE = 0x0003, SPI_SETMOUSE = 0x0004;
        public const uint SPI_GETWHEELSCROLLLINES = 0x0068, SPI_SETWHEELSCROLLLINES = 0x0069;
        public const uint SPI_GETMOUSESPEED = 0x0070, SPI_SETMOUSESPEED = 0x0071;
        public const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfo")]
        public static extern bool SystemParametersInfoSet(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        // ───────────── device change notifications ─────────────
        public const int WM_DEVICECHANGE = 0x0219;
        public const int DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVNODES_CHANGED = 0x0007;
        public const int DBT_DEVTYP_DEVICEINTERFACE = 5;
        public const int WM_POWERBROADCAST = 0x0218, PBT_APMRESUMEAUTOMATIC = 0x0012, PBT_APMRESUMESUSPEND = 0x0007;

        [StructLayout(LayoutKind.Sequential)]
        public struct DEV_BROADCAST_DEVICEINTERFACE { public int dbcc_size; public int dbcc_devicetype; public int dbcc_reserved; public Guid dbcc_classguid; public short dbcc_name; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, ref DEV_BROADCAST_DEVICEINTERFACE filter, int flags);
        [DllImport("user32.dll")]
        public static extern bool UnregisterDeviceNotification(IntPtr handle);

        // ───────────── HID / SetupAPI / CfgMgr ─────────────
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        public const int DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
        public const int ERROR_IO_PENDING = 997, ERROR_NO_MORE_ITEMS = 259, ERROR_INSUFFICIENT_BUFFER = 122;
        public const uint WAIT_OBJECT_0 = 0, WAIT_TIMEOUT = 0x102;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid hidGuid);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetAttributes(IntPtr device, ref HIDD_ATTRIBUTES attributes);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsed);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetProductString(IntPtr device, byte[] buffer, int bufferLength);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr devs, IntPtr devInfo, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA did);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devs, ref SP_DEVICE_INTERFACE_DATA did, IntPtr detail, int detailSize, out int requiredSize, ref SP_DEVINFO_DATA devInfo);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr devs);

        [DllImport("cfgmgr32.dll")]
        public static extern int CM_Get_Parent(out uint parent, uint devInst, int flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_IDW")]
        public static extern int CM_Get_Device_ID(uint devInst, StringBuilder buffer, int bufferLen, int flags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr h, IntPtr buffer, int toRead, IntPtr read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr h, IntPtr buffer, int toWrite, IntPtr written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(IntPtr h, IntPtr overlapped, out int transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(IntPtr h, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateEvent(IntPtr security, bool manualReset, bool initialState, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetEvent(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForMultipleObjects(int count, IntPtr[] handles, bool waitAll, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr h, uint milliseconds);

        /// <summary>OVERLAPPED is 32 bytes on 64-bit, 20 on 32-bit; we allocate it unmanaged.</summary>
        public static IntPtr AllocOverlapped(IntPtr evt)
        {
            int size = IntPtr.Size == 8 ? 32 : 20;
            IntPtr p = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
            // hEvent is the last pointer-sized field
            Marshal.WriteIntPtr(p, size - IntPtr.Size, evt);
            return p;
        }

        public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        // ───────────── per-monitor DPI ─────────────
        public const int WM_DPICHANGED = 0x02E0;
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        /// <summary>Effective DPI of the monitor containing a screen point (falls back to system DPI).</summary>
        public static int DpiAt(System.Drawing.Point p, int fallback)
        {
            try
            {
                IntPtr mon = MonitorFromPoint(new POINT { X = p.X, Y = p.Y }, 2 /*MONITOR_DEFAULTTONEAREST*/);
                if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0, out uint x, out _) == 0 && x > 0) return (int)x;
            }
            catch { }
            return fallback;
        }

        // ───────────── layered (per-pixel alpha) windows ─────────────
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_EX_LAYERED = 0x00080000, WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOOLWINDOW = 0x00000080,
            WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x00000008;
        public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;

        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

        // ── acrylic pop-ups (Windows 11 22H2+): a DWM backdrop behind a window we draw on with per-pixel alpha ──
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
        [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int Left, Right, Top, Bottom; }
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMWCP_ROUND = 2, DWMSBT_TRANSIENTWINDOW = 3;
        [StructLayout(LayoutKind.Sequential)]
        public struct PAINTSTRUCT
        {
            public IntPtr hdc; public int fErase; public int left, top, right, bottom; public int fRestore, fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
        }
        [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        public const int WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014, WM_MOUSEACTIVATE = 0x0021, WM_NCHITTEST = 0x0084;

        /// <summary>Windows build number (22621 = Windows 11 22H2), or 0 if unknown.</summary>
        public static int WindowsBuild
        {
            get
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        return int.TryParse(k?.GetValue("CurrentBuildNumber") as string, out int b) ? b : 0;
                }
                catch { return 0; }
            }
        }
    }
}
