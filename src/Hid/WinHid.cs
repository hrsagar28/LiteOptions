using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LiteOptions.Hid
{
    /// <summary>A physical HID device as seen by the HID++ layer: send reports, receive reports.</summary>
    public interface IHidLink : IDisposable
    {
        bool CanLong { get; }
        bool CanShort { get; }
        bool Alive { get; }
        bool Write(byte[] report, int timeoutMs);
        event Action<byte[]> Report;
    }

    public sealed class HidCollectionInfo
    {
        public string Path;
        public ushort Vid, Pid;
        public ushort UsagePage, Usage;
        public int InLen, OutLen;
        public string Product;
        public string ParentId;

        public bool IsHidppCandidate => Vid == 0x046D && UsagePage >= 0xFF00 && OutLen >= 7 && InLen >= 7;
        public string GroupKey => string.IsNullOrEmpty(ParentId) ? Path : ParentId;

        public override string ToString() =>
            $"PID {Pid:X4} page {UsagePage:X4} usage {Usage:X4} in {InLen} out {OutLen} \"{Product}\" parent {ParentId}";
    }

    /// <summary>Enumerates HID collections with SetupAPI.</summary>
    public static class HidEnumerator
    {
        public static List<HidCollectionInfo> Enumerate(ushort vendorId)
        {
            var list = new List<HidCollectionInfo>();
            Native.HidD_GetHidGuid(out Guid hidGuid);
            IntPtr devs = Native.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (devs == Native.INVALID_HANDLE_VALUE) return list;
            try
            {
                for (int i = 0; ; i++)
                {
                    var did = new Native.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(Native.SP_DEVICE_INTERFACE_DATA)) };
                    if (!Native.SetupDiEnumDeviceInterfaces(devs, IntPtr.Zero, ref hidGuid, i, ref did)) break;

                    var devInfo = new Native.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(Native.SP_DEVINFO_DATA)) };
                    Native.SetupDiGetDeviceInterfaceDetail(devs, ref did, IntPtr.Zero, 0, out int required, ref devInfo);
                    if (required <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(required);
                    string path;
                    try
                    {
                        // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on 64-bit, 6 on 32-bit
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(devs, ref did, detail, required, out _, ref devInfo)) continue;
                        path = Marshal.PtrToStringUni(detail + 4);
                    }
                    finally { Marshal.FreeHGlobal(detail); }

                    if (string.IsNullOrEmpty(path)) continue;
                    // Quick filter on the path so we don't open every HID device on the system.
                    string up = path.ToUpperInvariant();
                    string vidHex = vendorId.ToString("X4");
                    if (!up.Contains("VID_" + vidHex) && !up.Contains("VID&0002" + vidHex) && !up.Contains("VID&02" + vidHex) && !up.Contains(vidHex))
                        continue;

                    var info = Query(path);
                    if (info == null || info.Vid != vendorId) continue;
                    info.ParentId = ParentOf(devInfo.DevInst);
                    list.Add(info);
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(devs); }
            return list;
        }

        static string ParentOf(uint devInst)
        {
            try
            {
                if (Native.CM_Get_Parent(out uint parent, devInst, 0) != 0) return null;
                var sb = new StringBuilder(512);
                if (Native.CM_Get_Device_ID(parent, sb, sb.Capacity, 0) != 0) return null;
                return sb.ToString();
            }
            catch { return null; }
        }

        static HidCollectionInfo Query(string path)
        {
            // Access 0 = query only; works even for keyboards/mice that Windows holds exclusively.
            IntPtr h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE_VALUE) return null;
            try
            {
                var attr = new Native.HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES)) };
                if (!Native.HidD_GetAttributes(h, ref attr)) return null;
                var info = new HidCollectionInfo { Path = path, Vid = attr.VendorID, Pid = attr.ProductID };
                if (Native.HidD_GetPreparsedData(h, out IntPtr pp))
                {
                    try
                    {
                        if (Native.HidP_GetCaps(pp, out Native.HIDP_CAPS caps) == 0x00110000) // HIDP_STATUS_SUCCESS
                        {
                            info.UsagePage = caps.UsagePage; info.Usage = caps.Usage;
                            info.InLen = caps.InputReportByteLength; info.OutLen = caps.OutputReportByteLength;
                        }
                    }
                    finally { Native.HidD_FreePreparsedData(pp); }
                }
                var buf = new byte[256];
                if (Native.HidD_GetProductString(h, buf, buf.Length))
                    info.Product = Encoding.Unicode.GetString(buf).TrimEnd('\0');
                return info;
            }
            finally { Native.CloseHandle(h); }
        }
    }

    /// <summary>One opened HID collection with a background reader thread (overlapped I/O).</summary>
    sealed class WinHidCollection : IDisposable
    {
        public readonly HidCollectionInfo Info;
        readonly IntPtr handle;
        readonly IntPtr stopEvent;
        readonly Thread reader;
        readonly object writeLock = new object();
        readonly IntPtr writeEvent, writeOv, writeBuf;
        volatile bool stopping;
        public volatile bool Dead;
        public event Action<byte[]> Report;

        WinHidCollection(HidCollectionInfo info, IntPtr h)
        {
            Info = info; handle = h;
            stopEvent = Native.CreateEvent(IntPtr.Zero, true, false, null);
            writeEvent = Native.CreateEvent(IntPtr.Zero, true, false, null);
            writeOv = Native.AllocOverlapped(writeEvent);
            writeBuf = Marshal.AllocHGlobal(Math.Max(info.OutLen, 64));
            reader = new Thread(ReadLoop) { IsBackground = true, Name = "hid-read" };
            reader.Start();
        }

        public static WinHidCollection Open(HidCollectionInfo info)
        {
            IntPtr h = Native.CreateFile(info.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE_VALUE) return null;
            return new WinHidCollection(info, h);
        }

        void ReadLoop()
        {
            int len = Math.Max(Info.InLen, 7);
            IntPtr evt = Native.CreateEvent(IntPtr.Zero, true, false, null);
            IntPtr ov = Native.AllocOverlapped(evt);
            IntPtr buf = Marshal.AllocHGlobal(len);
            var waitHandles = new[] { evt, stopEvent };
            bool ioOutstanding = false;
            try
            {
                while (!stopping)
                {
                    Native.ResetEvent(evt);
                    bool ok = Native.ReadFile(handle, buf, len, IntPtr.Zero, ov);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != Native.ERROR_IO_PENDING) { Dead = true; break; }
                        ioOutstanding = true;
                        uint w = Native.WaitForMultipleObjects(2, waitHandles, false, 0xFFFFFFFF);
                        if (w != Native.WAIT_OBJECT_0)
                        {
                            Native.CancelIoEx(handle, ov);
                            Native.GetOverlappedResult(handle, ov, out _, true);
                            ioOutstanding = false;
                            break;
                        }
                    }
                    bool done = Native.GetOverlappedResult(handle, ov, out int n, false);
                    ioOutstanding = false;
                    if (!done) { Dead = true; break; }
                    if (n > 0)
                    {
                        var data = new byte[n];
                        Marshal.Copy(buf, data, 0, n);
                        try { Report?.Invoke(data); } catch { /* never let a handler kill the reader */ }
                    }
                }
            }
            finally
            {
                if (ioOutstanding) { Native.CancelIoEx(handle, ov); Native.GetOverlappedResult(handle, ov, out _, true); }
                Marshal.FreeHGlobal(buf);
                Marshal.FreeHGlobal(ov);
                Native.CloseHandle(evt);
            }
        }

        public bool Write(byte[] report, int timeoutMs)
        {
            if (Dead) return false;
            lock (writeLock)
            {
                if (stopping) return false; // buffers are freed by Dispose under this lock
                int len = Info.OutLen;
                var padded = new byte[len];
                Array.Copy(report, padded, Math.Min(report.Length, len));
                Marshal.Copy(padded, 0, writeBuf, len);
                Native.ResetEvent(writeEvent);
                bool ok = Native.WriteFile(handle, writeBuf, len, IntPtr.Zero, writeOv);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != Native.ERROR_IO_PENDING) { if (err == 1167 /*not connected*/) Dead = true; return false; }
                    if (Native.WaitForSingleObject(writeEvent, (uint)timeoutMs) != Native.WAIT_OBJECT_0)
                    {
                        Native.CancelIoEx(handle, writeOv);
                        Native.GetOverlappedResult(handle, writeOv, out _, true);
                        return false;
                    }
                }
                return Native.GetOverlappedResult(handle, writeOv, out _, false);
            }
        }

        public void Dispose()
        {
            if (stopping) return;
            stopping = true;
            Native.SetEvent(stopEvent);
            reader.Join(2000);
            lock (writeLock)
            {
                Native.CloseHandle(handle);
                Marshal.FreeHGlobal(writeBuf);
                Marshal.FreeHGlobal(writeOv);
                Native.CloseHandle(writeEvent);
            }
            Native.CloseHandle(stopEvent);
        }
    }

    /// <summary>
    /// All vendor HID++ collections that belong to one physical device (or receiver).
    /// Writes go to the collection that fits the report size; reads come from all of them.
    /// </summary>
    public sealed class WinHidGroup : IHidLink
    {
        readonly List<WinHidCollection> cols = new List<WinHidCollection>();
        public readonly string Key;
        public readonly ushort Pid;
        public readonly string Product;
        public event Action<byte[]> Report;

        public WinHidGroup(string key, IEnumerable<HidCollectionInfo> infos)
        {
            Key = key;
            foreach (var info in infos)
            {
                var c = WinHidCollection.Open(info);
                if (c == null) continue;
                c.Report += d => Report?.Invoke(d);
                cols.Add(c);
            }
            var first = infos.FirstOrDefault();
            Pid = first?.Pid ?? 0;
            Product = infos.Select(i => i.Product).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        }

        public int OpenCount => cols.Count;
        public bool CanLong => cols.Any(c => !c.Dead && c.Info.OutLen >= 20);
        public bool CanShort => cols.Any(c => !c.Dead && c.Info.OutLen >= 7);
        public bool Alive => cols.Any(c => !c.Dead);

        public bool Write(byte[] report, int timeoutMs)
        {
            int need = report.Length;
            // Prefer the smallest collection the report fits into (exact size match first).
            var target = cols.Where(c => !c.Dead && c.Info.OutLen >= need).OrderBy(c => c.Info.OutLen).FirstOrDefault();
            return target != null && target.Write(report, timeoutMs);
        }

        public void Dispose() { foreach (var c in cols) c.Dispose(); cols.Clear(); }
    }
}
