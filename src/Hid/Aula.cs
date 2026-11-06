using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace LiteOptions.Hid
{
    /// <summary>The Aula F108 Pro's settings channel over the USB cable: 64-byte HID feature reports (no report ID).</summary>
    public interface IAulaPort : IDisposable
    {
        bool SetFeature(byte[] payload);
        /// <summary>The keyboard's 64-byte answer, or null if it didn't give one.</summary>
        byte[] GetFeature();
    }

    /// <summary>Abstracts the OS so the Aula logic can be tested without the keyboard.</summary>
    public interface IAulaSource
    {
        List<HidCollectionInfo> Enumerate();
        IAulaPort OpenConfig(HidCollectionInfo info);
        /// <summary>Open a collection read-only and pass on whatever it sends. Never writes.</summary>
        IDisposable Listen(HidCollectionInfo info, Action<byte[]> report);
        /// <summary>The keyboard connected over Bluetooth, with the battery level Windows has for it; null if not connected.</summary>
        BluetoothDeviceInfo FindBluetooth();
    }

    /// <summary>
    /// Packets for the Aula F108 Pro (Sonix SN32F2xx). Only what the open-source f108-pro tool
    /// (github.com/parsiya/f108-pro, MIT) decoded from the Aula app and tested on the keyboard: backlight and clock.
    /// Each change is a transaction: begin (04 18), what to change, the data, apply (04 02), and for lighting finish (04 F0).
    /// Steps marked readBack are followed by a feature-report read; the keyboard ignores the rest of the sequence without it.
    /// </summary>
    public static class AulaProtocol
    {
        public const ushort CableVid = 0x0C45, CablePid = 0x800A;   // Sonix, wired
        public const ushort DongleVid = 0x05AC, DonglePid = 0x024F; // the 2.4 GHz dongle borrows an Apple ID
        public const ushort ConfigUsagePage = 0xFF13;
        public const int ReportSize = 64;

        public static byte[] Begin() => Cmd(0x04, 0x18);
        public static byte[] Apply() => Cmd(0x04, 0x02);
        public static byte[] Finish() => Cmd(0x04, 0xF0);
        public static byte[] LightingStart() { var p = Cmd(0x04, 0x13); p[8] = 0x01; return p; }
        public static byte[] ClockStart() { var p = Cmd(0x04, 0x28); p[8] = 0x01; return p; }

        public static byte[] Lighting(AulaLighting l)
        {
            var c = l.Clone();
            c.Normalize();
            var p = new byte[ReportSize];
            p[0] = (byte)c.Mode;
            if (c.Mode != 0)
            {
                p[1] = (byte)c.R; p[2] = (byte)c.G; p[3] = (byte)c.B;
                p[8] = (byte)(c.Rainbow ? 1 : 0);
                p[9] = (byte)c.Brightness;
                p[10] = (byte)c.Speed;
                p[11] = (byte)c.Direction;
            }
            p[14] = 0x55; p[15] = 0xAA;
            return p;
        }

        public static byte[] Clock(DateTime t)
        {
            var p = new byte[ReportSize];
            p[1] = 0x01;                      // profile 1
            p[2] = 0x5A;
            p[3] = (byte)(t.Year % 2000);
            p[4] = (byte)t.Month;
            p[5] = (byte)t.Day;
            p[6] = (byte)t.Hour;
            p[7] = (byte)t.Minute;
            p[8] = (byte)t.Second;
            p[10] = (byte)(int)t.DayOfWeek;   // 0 = Sunday
            p[62] = 0x55; p[63] = 0xAA;
            return p;
        }

        public static List<(byte[] data, bool readBack)> LightingSequence(AulaLighting l) => new List<(byte[], bool)>
        {
            (Begin(), true), (LightingStart(), true), (Lighting(l), false), (Apply(), true), (Finish(), false),
        };

        public static List<(byte[] data, bool readBack)> ClockSequence(DateTime t) => new List<(byte[], bool)>
        {
            (Begin(), true), (ClockStart(), true), (Clock(t), true), (Apply(), true),
        };

        static byte[] Cmd(byte a, byte b) { var p = new byte[ReportSize]; p[0] = a; p[1] = b; return p; }
    }

    public sealed class AulaState
    {
        public string Connection = "";      // "" not found | "USB cable" | "Bluetooth" | "2.4 GHz dongle"
        public string BluetoothName = "";
        public int Battery = -1;            // percent, -1 unknown (only known over Bluetooth so far)
        public bool CanConfigure;           // the cable's settings channel is open
        public ushort Version;              // the keyboard's (or dongle's) device version
        public DateTime? ClockSyncedAt;
        public string LastError = "";
        public bool Present => Connection != "";
        public AulaState Clone() => (AulaState)MemberwiseClone();
    }

    /// <summary>
    /// Finds the Aula F108 Pro on its cable or dongle. Over the cable it sets the backlight (only when asked) and the
    /// screen clock (on plug-in, if switched on). On the dongle it only listens and logs what the dongle sends, to find
    /// the battery level; it never writes to the dongle. All I/O runs on one worker thread.
    /// </summary>
    public sealed class AulaManager : IDisposable
    {
        readonly IAulaSource source;
        readonly Func<AppSettings> getSettings;
        readonly BlockingCollection<Action> work = new BlockingCollection<Action>();
        readonly object gate = new object();
        readonly AulaState state = new AulaState();
        readonly List<IDisposable> listeners = new List<IDisposable>();
        readonly Dictionary<string, int> seenReports = new Dictionary<string, int>();
        Thread worker;
        Timer scanDebounce, poll;
        IAulaPort port;
        string portPath, dongleKey, lastSignature = "";
        int reportLines;
        volatile bool disposed;

        public event Action Changed;
        public Func<DateTime> Now = () => DateTime.Now;
        /// <summary>Pause after each report, as the Aula app does (config.xml cmd_delaytime).</summary>
        public int DelayMs = 35;
        public int MaxReportLines = 80;
        /// <summary>How often to look again while it's on Bluetooth (Windows doesn't announce battery changes).</summary>
        public int PollMs = 5 * 60 * 1000;

        public AulaManager(IAulaSource source, Func<AppSettings> getSettings)
        {
            this.source = source;
            this.getSettings = getSettings;
        }

        public void Start()
        {
            worker = new Thread(() =>
            {
                foreach (var job in work.GetConsumingEnumerable())
                {
                    if (disposed) break;
                    try { job(); } catch (Exception ex) { Logger.Write("aula worker: " + ex); }
                }
            }) { IsBackground = true, Name = "aula" };
            worker.Start();
            Post(Scan);
            poll = new Timer(_ => { if (Snapshot().Connection == "Bluetooth") Post(Scan); }, null, PollMs, PollMs);
        }

        void Post(Action a) { if (!disposed) try { work.Add(a); } catch (InvalidOperationException) { } }

        public void RunPendingForTests() { while (work.TryTake(out var a)) a(); }
        public void ScanForTests() => Post(Scan);

        public AulaState Snapshot() { lock (gate) return state.Clone(); }

        /// <summary>Look again shortly (devices arrive and leave in bursts).</summary>
        public void RequestScan(int delayMs)
        {
            if (disposed) return;
            lock (gate)
            {
                scanDebounce?.Dispose();
                scanDebounce = new Timer(_ => Post(Scan), null, Math.Max(0, delayMs), Timeout.Infinite);
            }
        }

        void RaiseChanged() { try { Changed?.Invoke(); } catch { } }

        void Scan()
        {
            if (source == null) return;
            List<HidCollectionInfo> all;
            try { all = source.Enumerate() ?? new List<HidCollectionInfo>(); }
            catch (Exception ex) { Logger.Write("Aula: listing devices failed: " + ex.Message); return; }

            var cable = all.Where(c => c.Vid == AulaProtocol.CableVid && c.Pid == AulaProtocol.CablePid).ToList();
            var config = cable.FirstOrDefault(c => c.UsagePage == AulaProtocol.ConfigUsagePage);
            var dongle = all.Where(c => c.Vid == AulaProtocol.DongleVid && c.Pid == AulaProtocol.DonglePid).ToList();
            BluetoothDeviceInfo bt = null;
            try { bt = source.FindBluetooth(); } catch (Exception ex) { Logger.Write("Aula: Bluetooth lookup failed: " + ex.Message); }

            string signature = string.Join("|", all.Select(c => c.Path).OrderBy(p => p));
            if (signature != lastSignature)
            {
                lastSignature = signature;
                if (all.Count > 0)
                {
                    Logger.Write($"Aula F108 Pro: {all.Count} HID collection(s):");
                    foreach (var c in all) Logger.Write($"    {c.Vid:X4}:{c.Pid:X4} v{c.Version:X4} page {c.UsagePage:X4} usage {c.Usage:X4} in {c.InLen} out {c.OutLen} feature {c.FeatureLen} \"{c.Product}\"");
                }
            }

            bool changed = false;

            // ── cable ──
            if (config != null && (port == null || portPath != config.Path))
            {
                ClosePort();
                IAulaPort p = null;
                try { p = source.OpenConfig(config); } catch (Exception ex) { Logger.Write("Aula: " + ex.Message); }
                lock (gate)
                {
                    state.CanConfigure = p != null;
                    state.LastError = p == null ? "Couldn't open the keyboard's settings. Close the Aula app and try again." : "";
                }
                port = p; portPath = p == null ? null : config.Path;
                Logger.Write(p == null ? "Aula F108 Pro on the USB cable, but its settings channel didn't open" : "Aula F108 Pro on the USB cable");
                changed = true;
                if (p != null && getSettings().AulaClockSync) SyncClockNow();
            }
            else if (config == null && port != null)
            {
                ClosePort();
                Logger.Write("Aula F108 Pro: USB cable unplugged");
                lock (gate) state.CanConfigure = false;
                changed = true;
            }

            // ── dongle: listen only ──
            string dk = dongle.Count == 0 ? null : string.Join("|", dongle.Select(d => d.Path).OrderBy(x => x));
            if (dk != dongleKey)
            {
                StopListening();
                dongleKey = dk;
                if (dk != null)
                {
                    foreach (var c in dongle.Where(c => c.UsagePage >= 0xFF00 && c.InLen > 0))
                    {
                        var info = c;
                        IDisposable l = null;
                        try { l = source.Listen(info, data => OnDongleReport(info, data)); } catch (Exception ex) { Logger.Write("Aula: " + ex.Message); }
                        if (l != null) listeners.Add(l);
                    }
                    Logger.Write($"Aula F108 Pro dongle: listening (read-only) on {listeners.Count} vendor collection(s) for the battery level");
                }
                changed = true;
            }

            // the keyboard can only be on one of these; the dongle stays plugged in whichever it uses
            string conn = config != null ? "USB cable" : bt != null ? "Bluetooth" : dongle.Count > 0 ? "2.4 GHz dongle" : "";
            ushort ver = (config ?? cable.FirstOrDefault() ?? dongle.FirstOrDefault())?.Version ?? 0;
            int battery = conn == "Bluetooth" ? bt.Battery : -1;
            lock (gate)
            {
                if (conn == "Bluetooth" && (state.Connection != conn || state.Battery != battery))
                    Logger.Write($"Aula F108 Pro on Bluetooth as \"{bt.Name}\": battery {(battery >= 0 ? battery + "%" : "not reported")}");
                if (state.Connection != conn || state.Version != ver || state.Battery != battery) changed = true;
                state.Connection = conn;
                state.Version = ver;
                state.Battery = battery;
                state.BluetoothName = bt?.Name ?? "";
                if (conn == "") state.LastError = "";
            }
            if (changed) RaiseChanged();
        }

        void OnDongleReport(HidCollectionInfo c, byte[] data)
        {
            string hex = BitConverter.ToString(data).Replace("-", " ");
            lock (gate)
            {
                seenReports.TryGetValue(hex, out int n);
                seenReports[hex] = n + 1;
                if (n > 0 || reportLines >= MaxReportLines) return; // each distinct report once, and not too many
                reportLines++;
            }
            Logger.Write($"Aula dongle page {c.UsagePage:X4}: {hex}");
        }

        /// <summary>Change the backlight. Needs the cable. <paramref name="done"/> gets null or an error.</summary>
        public void SetLighting(AulaLighting l, Action<string> done = null)
        {
            if (l == null) { done?.Invoke("No lighting chosen"); return; }
            var copy = l.Clone();
            Post(() =>
            {
                string err = Run(AulaProtocol.LightingSequence(copy), "lighting");
                if (err == null) Logger.Write("Aula F108 Pro: lighting set to " + copy.Describe());
                RaiseChanged();
                done?.Invoke(err);
            });
        }

        public void SyncClock(Action<string> done = null) => Post(() => { var err = SyncClockNow(); RaiseChanged(); done?.Invoke(err); });

        string SyncClockNow()
        {
            var t = Now();
            string err = Run(AulaProtocol.ClockSequence(t), "clock");
            if (err == null)
            {
                lock (gate) state.ClockSyncedAt = t;
                Logger.Write("Aula F108 Pro: screen clock set to " + t.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            return err;
        }

        string Run(List<(byte[] data, bool readBack)> steps, string what)
        {
            if (port == null)
            {
                const string msg = "Plug the keyboard in with its USB cable to change this.";
                lock (gate) state.LastError = msg;
                return msg;
            }
            for (int i = 0; i < steps.Count; i++)
            {
                bool ok = port.SetFeature(steps[i].data);
                if (ok) Pause();
                if (ok && steps[i].readBack) { ok = port.GetFeature() != null; Pause(); }
                if (!ok)
                {
                    string msg = $"The keyboard didn't take the {what} change. Unplug the cable and plug it in again.";
                    Logger.Write($"Aula F108 Pro: {what} failed at step {i + 1} of {steps.Count}");
                    ClosePort();
                    lock (gate) { state.LastError = msg; state.CanConfigure = false; }
                    RequestScan(1500); // open it afresh
                    return msg;
                }
            }
            lock (gate) state.LastError = "";
            return null;
        }

        void Pause() { if (DelayMs > 0) Thread.Sleep(DelayMs); }

        void ClosePort()
        {
            try { port?.Dispose(); } catch { }
            port = null; portPath = null;
        }

        void StopListening()
        {
            foreach (var l in listeners) try { l.Dispose(); } catch { }
            listeners.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (gate) { scanDebounce?.Dispose(); poll?.Dispose(); }
            work.CompleteAdding();
            worker?.Join(1000);
            ClosePort();
            StopListening();
        }
    }

    public sealed class WinAulaSource : IAulaSource
    {
        public List<HidCollectionInfo> Enumerate()
        {
            var list = HidEnumerator.Enumerate(AulaProtocol.CableVid, p => p.Contains("PID_" + AulaProtocol.CablePid.ToString("X4")));
            list.AddRange(HidEnumerator.Enumerate(AulaProtocol.DongleVid, p => p.Contains("PID_" + AulaProtocol.DonglePid.ToString("X4"))));
            return list;
        }

        public IAulaPort OpenConfig(HidCollectionInfo info) => WinAulaPort.Open(info);

        /// <summary>Windows names it after the keyboard ("AULA-F108Pro-1" and the like).</summary>
        public BluetoothDeviceInfo FindBluetooth() => HidEnumerator.FindBluetooth(n =>
            n.IndexOf("AULA", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("F108", StringComparison.OrdinalIgnoreCase) >= 0);

        public IDisposable Listen(HidCollectionInfo info, Action<byte[]> report)
        {
            var c = WinHidCollection.Open(info, readOnly: true);
            if (c == null) return null;
            c.Report += report;
            return c;
        }
    }

    sealed class WinAulaPort : IAulaPort
    {
        readonly IntPtr handle;
        readonly int len;

        WinAulaPort(IntPtr h, int len) { handle = h; this.len = len; }

        public static WinAulaPort Open(HidCollectionInfo info)
        {
            IntPtr h = Native.CreateFile(info.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE_VALUE) return null;
            // report ID byte + 64 bytes; the collection has no report IDs, so Windows strips the leading 0
            int len = info.FeatureLen > 1 ? info.FeatureLen : AulaProtocol.ReportSize + 1;
            return new WinAulaPort(h, len);
        }

        public bool SetFeature(byte[] payload)
        {
            var buf = new byte[len];
            Array.Copy(payload, 0, buf, 1, Math.Min(payload.Length, len - 1));
            bool ok = Native.HidD_SetFeature(handle, buf, len);
            if (!ok) Logger.Write("Aula: HidD_SetFeature error " + Marshal.GetLastWin32Error());
            return ok;
        }

        public byte[] GetFeature()
        {
            var buf = new byte[len];
            if (!Native.HidD_GetFeature(handle, buf, len)) { Logger.Write("Aula: HidD_GetFeature error " + Marshal.GetLastWin32Error()); return null; }
            var data = new byte[len - 1];
            Array.Copy(buf, 1, data, 0, data.Length);
            return data;
        }

        public void Dispose() => Native.CloseHandle(handle);
    }
}
