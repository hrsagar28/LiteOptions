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
        /// <summary>Open the dongle's 33-byte channel (page FF60) for reading and writing; null if it won't open.</summary>
        IAulaDongleLink OpenDongle(HidCollectionInfo info, Action<byte[]> report);
    }

    /// <summary>The dongle's command channel: 33-byte output reports (report ID 0 first); answers arrive as input reports.</summary>
    public interface IAulaDongleLink : IDisposable
    {
        bool Write(byte[] report33);
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

        /// <summary>System Settings over the cable, as the Aula app sends them: 04 17 header, then one data packet
        /// (00 01, Windows key, Alt+F4, Alt+Tab, Fn switch, sleep, -, key response; AA 55 at 62), no read-back after it.</summary>
        public static byte[] SystemStart() { var p = Cmd(0x04, 0x17); p[2] = 0x01; p[8] = 0x01; return p; }

        public static byte[] SystemData(AulaSystemSettings s)
        {
            var c = s.Clone(); c.Normalize();
            var p = new byte[ReportSize];
            p[1] = 0x01;
            p[2] = (byte)(c.BlockWin ? 1 : 0);
            p[3] = (byte)(c.BlockAltF4 ? 1 : 0);
            p[4] = (byte)(c.BlockAltTab ? 1 : 0);
            p[5] = (byte)c.FnSwitch;
            p[6] = (byte)c.Sleep;
            p[8] = (byte)c.Response;
            p[62] = 0xAA; p[63] = 0x55;
            return p;
        }

        public static List<(byte[] data, bool readBack)> SystemSequence(AulaSystemSettings s) => new List<(byte[], bool)>
        {
            (Begin(), true), (SystemStart(), true), (SystemData(s), false), (Apply(), true),
        };

        public static List<(byte[] data, bool readBack)> ClockSequence(DateTime t) => new List<(byte[], bool)>
        {
            (Begin(), true), (ClockStart(), true), (Clock(t), true), (Apply(), true),
        };

        static byte[] Cmd(byte a, byte b) { var p = new byte[ReportSize]; p[0] = a; p[1] = b; return p; }

        // ── 2.4 GHz dongle ──
        // Read from the Aula app (DeviceDriver.exe): one 33-byte output report per command on the dongle's page-FF60
        // collection: report ID 0, the command, its data, and at byte 32 the low byte of the sum of bytes 0-31.
        // The keyboard echoes the first three command bytes back; the app retries until it sees them.
        // All three were seen answered by the keyboard when the Aula app sent them (6 Nov 2026).
        public const ushort DongleChannelPage = 0xFF60;
        public const int DongleReportSize = 33;

        /// <summary>Battery query, sent by the Aula app every 15 s. Answer: 20 01 00 &lt;percent&gt;.</summary>
        public static byte[] DongleBatteryQuery() => Dongle(0x20, 0x01);

        /// <summary>Backlight: 05 10 00, then the same fields as over the cable, three bytes further on.</summary>
        public static byte[] DongleLighting(AulaLighting l)
        {
            var c = l.Clone();
            c.Normalize();
            var r = new byte[DongleReportSize];
            r[1] = 0x05; r[2] = 0x10;
            r[4] = (byte)c.Mode;
            if (c.Mode != 0)
            {
                r[5] = (byte)c.R; r[6] = (byte)c.G; r[7] = (byte)c.B;
                r[12] = (byte)(c.Rainbow ? 1 : 0);
                r[13] = (byte)c.Brightness;
                r[14] = (byte)c.Speed;
                r[15] = (byte)c.Direction;
            }
            r[18] = 0xAA; r[19] = 0x55;
            return Checksum(r);
        }

        /// <summary>Screen clock: 0C 10 00 00, profile 1, 5A, then the date and time as over the cable.</summary>
        public static byte[] DongleClock(DateTime t)
        {
            var r = new byte[DongleReportSize];
            r[1] = 0x0C; r[2] = 0x10;
            r[5] = 0x01;
            r[6] = 0x5A;
            r[7] = (byte)(t.Year % 2000);
            r[8] = (byte)t.Month;
            r[9] = (byte)t.Day;
            r[10] = (byte)t.Hour;
            r[11] = (byte)t.Minute;
            r[12] = (byte)t.Second;
            r[14] = (byte)(int)t.DayOfWeek;
            r[18] = 0xAA; r[19] = 0x55;
            return Checksum(r);
        }

        /// <summary>System Settings through the dongle: 07 10, then the cable's fields four bytes on, AA 55 at 18.</summary>
        public static byte[] DongleSystem(AulaSystemSettings s)
        {
            var c = s.Clone(); c.Normalize();
            var r = new byte[DongleReportSize];
            r[1] = 0x07; r[2] = 0x10;
            r[5] = 0x01;
            r[6] = (byte)(c.BlockWin ? 1 : 0);
            r[7] = (byte)(c.BlockAltF4 ? 1 : 0);
            r[8] = (byte)(c.BlockAltTab ? 1 : 0);
            r[9] = (byte)c.FnSwitch;
            r[10] = (byte)c.Sleep;
            r[12] = (byte)c.Response;
            r[18] = 0xAA; r[19] = 0x55;
            return Checksum(r);
        }

        static byte[] Dongle(byte a, byte b) { var r = new byte[DongleReportSize]; r[1] = a; r[2] = b; return Checksum(r); }

        public static byte[] Checksum(byte[] r)
        {
            int sum = 0;
            for (int i = 0; i < 32; i++) sum += r[i];
            r[32] = (byte)sum;
            return r;
        }

        public static bool ChecksumOk(byte[] r)
        {
            if (r == null || r.Length < DongleReportSize) return false;
            int sum = 0;
            for (int i = 0; i < 32; i++) sum += r[i];
            return r[32] == (byte)sum;
        }

        /// <summary>The keyboard's answer to <paramref name="sent"/>: the same three command bytes.</summary>
        public static bool IsEcho(byte[] sent, byte[] got) =>
            got != null && got.Length >= 4 && got[1] == sent[1] && got[2] == sent[2] && got[3] == sent[3];

        /// <summary>The battery percentage in an answer to the battery query, or -1. Like the app: 0 is ignored, over 100 is 100.</summary>
        public static int ParseBattery(byte[] r)
        {
            if (r == null || r.Length < 5 || r[1] != 0x20 || r[2] != 0x01) return -1;
            if (r.Length >= DongleReportSize && !ChecksumOk(r)) return -1;
            return r[4] == 0 ? -1 : Math.Min(100, (int)r[4]);
        }

        /// <summary>One of our own commands echoed back (not worth logging).</summary>
        public static bool IsKnownAnswer(byte[] r) =>
            r != null && r.Length >= 3 && ((r[1] == 0x20 && r[2] == 0x01) || (r[1] == 0x05 && r[2] == 0x10) || (r[1] == 0x0C && r[2] == 0x10) || (r[1] == 0x07 && r[2] == 0x10));
    }

    public sealed class AulaState
    {
        public string Connection = "";      // "" not found | "USB cable" | "Bluetooth" | "2.4 GHz dongle"
        public string BluetoothName = "";
        public int Battery = -1;            // percent, -1 unknown (from Windows over Bluetooth, from the keyboard on the dongle)
        public bool CanConfigure;           // lighting and the clock can be changed: on the cable, or on the dongle and answering
        public bool DonglePlugged, DongleAwake;
        public ushort Version;              // the keyboard's (or dongle's) device version
        public DateTime? ClockSyncedAt;
        public string LastError = "";
        public bool Present => Connection != "";
        public AulaState Clone() => (AulaState)MemberwiseClone();
    }

    /// <summary>
    /// Finds the Aula F108 Pro on its cable, Bluetooth or 2.4 GHz dongle.
    /// Cable: backlight (only when asked) and the screen clock (on plug-in, if switched on), as 64-byte feature reports.
    /// Dongle: the same through its 33-byte channel, plus the battery level, asked for every two minutes (the Aula app
    /// asks every 15 seconds). The clock is set when the keyboard turns up on the dongle, at most once an hour.
    /// Bluetooth: only the battery level Windows has; the keyboard takes no settings over Bluetooth.
    /// All I/O runs on one worker thread.
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
        readonly ManualResetEventSlim echoEvent = new ManualResetEventSlim(false);
        Thread worker;
        Timer scanDebounce, poll, donglePoll;
        IAulaPort port;
        IAulaDongleLink dongle;
        string portPath, dongleKey, lastSignature = "";
        int reportLines;
        volatile bool disposed;

        // what the last scan found, for working out the connection between scans
        bool cablePresent, donglePlugged;
        ushort lastVersion;
        BluetoothDeviceInfo lastBt;
        // dongle answers (guarded by gate)
        byte[] awaiting, echo;
        int dongleBattery = -1, loggedBattery = -1;
        DateTime dongleAnsweredAt = DateTime.MinValue, lastHello = DateTime.MinValue;
        DateTime? dongleClockAt;

        public event Action Changed;
        public Func<DateTime> Now = () => DateTime.Now;
        /// <summary>Pause after each report over the cable, as the Aula app does (config.xml cmd_delaytime).</summary>
        public int DelayMs = 35;
        public int MaxReportLines = 80;
        /// <summary>How often to look again while it's on Bluetooth (Windows doesn't announce battery changes).</summary>
        public int PollMs = 5 * 60 * 1000;
        /// <summary>How often to ask for the battery through the dongle.</summary>
        public int DonglePollMs = 2 * 60 * 1000;
        /// <summary>How long to wait for the keyboard's echo of a dongle command (each of up to 3 tries).</summary>
        public int EchoTimeoutMs = 300;

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
            donglePoll = new Timer(_ => { if (dongle != null) Post(() => DongleCheck(false)); }, null, DonglePollMs, DonglePollMs);
        }

        void Post(Action a) { if (!disposed) try { work.Add(a); } catch (InvalidOperationException) { } }

        public void RunPendingForTests() { while (work.TryTake(out var a)) a(); }
        public void ScanForTests() => Post(Scan);
        public void DonglePollForTests() => Post(() => DongleCheck(false));

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

        bool DongleAwake()
        {
            lock (gate) return dongle != null && (Now() - dongleAnsweredAt).TotalMilliseconds < DonglePollMs + 60000;
        }

        void Scan()
        {
            if (source == null) return;
            List<HidCollectionInfo> all;
            try { all = source.Enumerate() ?? new List<HidCollectionInfo>(); }
            catch (Exception ex) { Logger.Write("Aula: listing devices failed: " + ex.Message); return; }

            var cable = all.Where(c => c.Vid == AulaProtocol.CableVid && c.Pid == AulaProtocol.CablePid).ToList();
            var config = cable.FirstOrDefault(c => c.UsagePage == AulaProtocol.ConfigUsagePage);
            var dongles = all.Where(c => c.Vid == AulaProtocol.DongleVid && c.Pid == AulaProtocol.DonglePid).ToList();
            var channel = dongles.FirstOrDefault(c => c.UsagePage == AulaProtocol.DongleChannelPage && c.OutLen >= AulaProtocol.DongleReportSize);
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
                lock (gate) state.LastError = p == null ? "Couldn't open the keyboard's settings. Close the Aula app and try again." : "";
                port = p; portPath = p == null ? null : config.Path;
                Logger.Write(p == null ? "Aula F108 Pro on the USB cable, but its settings channel didn't open" : "Aula F108 Pro on the USB cable");
                changed = true;
                if (p != null && getSettings().AulaClockSync) SyncClockNow();
            }
            else if (config == null && port != null)
            {
                ClosePort();
                Logger.Write("Aula F108 Pro: USB cable unplugged");
                changed = true;
            }

            // ── dongle: its command channel, and the other vendor collections just listened to ──
            string dk = dongles.Count == 0 ? null : string.Join("|", dongles.Select(d => d.Path).OrderBy(x => x));
            if (dk != dongleKey)
            {
                StopListening();
                CloseDongle();
                dongleKey = dk;
                if (dk != null)
                {
                    if (channel != null)
                    {
                        IAulaDongleLink link = null;
                        try { link = source.OpenDongle(channel, data => OnDongleReport(channel, data)); } catch (Exception ex) { Logger.Write("Aula: " + ex.Message); }
                        lock (gate) dongle = link;
                    }
                    foreach (var c in dongles.Where(c => c.UsagePage >= 0xFF00 && c.InLen > 0 && c != channel))
                    {
                        var info = c;
                        IDisposable l = null;
                        try { l = source.Listen(info, data => OnDongleReport(info, data)); } catch (Exception ex) { Logger.Write("Aula: " + ex.Message); }
                        if (l != null) listeners.Add(l);
                    }
                    Logger.Write($"Aula F108 Pro dongle plugged in: command channel {(dongle != null ? "open" : "not found")}, listening on {listeners.Count} other collection(s)");
                    if (dongle != null) Post(() => DongleCheck(true));
                }
                changed = true;
            }

            cablePresent = config != null;
            donglePlugged = dongles.Count > 0;
            lastVersion = (config ?? cable.FirstOrDefault() ?? dongles.FirstOrDefault())?.Version ?? 0;
            lastBt = bt;
            if (Recompute() || changed) RaiseChanged();
        }

        /// <summary>Work out the connection from the last scan and the dongle's answers. True if anything changed.</summary>
        bool Recompute()
        {
            bool awake = DongleAwake();
            // The dongle stays plugged in whichever mode the keyboard uses. If the keyboard answers through it, it's on it,
            // even while Windows still lists it on Bluetooth for a moment after switching.
            string conn = cablePresent ? "USB cable" : awake ? "2.4 GHz dongle" : lastBt != null ? "Bluetooth" : donglePlugged ? "2.4 GHz dongle" : "";
            lock (gate)
            {
                int battery = conn == "Bluetooth" ? lastBt.Battery : conn == "2.4 GHz dongle" ? dongleBattery : -1;
                bool can = port != null || (dongle != null && conn == "2.4 GHz dongle");
                bool changed = state.Connection != conn || state.Version != lastVersion || state.Battery != battery
                               || state.CanConfigure != can || state.DongleAwake != awake || state.DonglePlugged != donglePlugged;
                if (state.Connection != conn && conn != "")
                    Logger.Write($"Aula F108 Pro: now on {(conn == "Bluetooth" ? "Bluetooth as \"" + lastBt.Name + "\"" : conn)}" +
                                 (conn == "2.4 GHz dongle" && !awake ? " (the keyboard isn't answering: asleep, or on another mode)" : "") +
                                 (battery >= 0 ? $", battery {battery}%" : conn == "Bluetooth" ? ", battery not reported" : ""));
                else if (battery >= 0 && (loggedBattery < 0 || Math.Abs(battery - loggedBattery) >= 5))
                    Logger.Write($"Aula F108 Pro: battery {battery}%");
                if (battery >= 0) loggedBattery = battery;
                state.Connection = conn;
                state.Version = lastVersion;
                state.Battery = battery;
                state.BluetoothName = conn == "Bluetooth" ? lastBt.Name : "";
                state.CanConfigure = can;
                state.DongleAwake = awake;
                state.DonglePlugged = donglePlugged;
                if (conn == "") state.LastError = "";
                return changed;
            }
        }

        void OnDongleReport(HidCollectionInfo c, byte[] data)
        {
            if (c.UsagePage == AulaProtocol.DongleChannelPage)
            {
                bool answered = false;
                lock (gate)
                {
                    if (awaiting != null && AulaProtocol.IsEcho(awaiting, data)) { echo = data; echoEvent.Set(); answered = true; }
                    int b = AulaProtocol.ParseBattery(data);
                    if (b > 0) { dongleBattery = b; answered = true; }
                    if (answered) dongleAnsweredAt = Now();
                }
                if (answered) Post(() => { if (Recompute()) RaiseChanged(); });
                if (AulaProtocol.IsKnownAnswer(data)) return;
            }
            else if (c.UsagePage == 0xFFFF)
            {
                // sent when the keyboard links up with the dongle: ask straight away rather than at the next poll
                bool ask;
                lock (gate) { ask = (Now() - lastHello).TotalSeconds > 5; if (ask) lastHello = Now(); }
                if (ask) Post(() => DongleCheck(false));
            }

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

        /// <summary>Send a dongle command and wait for the keyboard to echo it (3 tries). The echo, or null.</summary>
        byte[] Exchange(byte[] packet)
        {
            IAulaDongleLink link;
            lock (gate) link = dongle;
            if (link == null) return null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                lock (gate) { awaiting = packet; echo = null; echoEvent.Reset(); }
                if (!link.Write(packet)) { lock (gate) awaiting = null; Logger.Write("Aula dongle: write failed"); return null; }
                if (echoEvent.Wait(EchoTimeoutMs))
                    lock (gate) { awaiting = null; dongleAnsweredAt = Now(); return echo; }
            }
            lock (gate) awaiting = null;
            return null;
        }

        /// <summary>Ask for the battery; if the keyboard has just turned up on the dongle, set its clock.</summary>
        void DongleCheck(bool fresh)
        {
            if (dongle == null) return;
            bool wasAwake = DongleAwake();
            var answer = Exchange(AulaProtocol.DongleBatteryQuery());
            int b = AulaProtocol.ParseBattery(answer);
            if (b > 0) lock (gate) dongleBattery = b;
            bool awake = answer != null;
            if (awake && getSettings().AulaClockSync && (fresh || !wasAwake))
            {
                DateTime? last;
                lock (gate) last = dongleClockAt;
                if (last == null || (Now() - last.Value).TotalHours >= 1) SyncClockNow();
            }
            if (Recompute()) RaiseChanged();
        }

        /// <summary>Change the backlight, over the cable or the dongle. <paramref name="done"/> gets null or an error.</summary>
        public void SetLighting(AulaLighting l, Action<string> done = null)
        {
            if (l == null) { done?.Invoke("No lighting chosen"); return; }
            var copy = l.Clone();
            Post(() =>
            {
                string err;
                if (port != null) err = Run(AulaProtocol.LightingSequence(copy), "lighting");
                else err = SendDongle(AulaProtocol.DongleLighting(copy), "lighting");
                if (err == null) Logger.Write($"Aula F108 Pro: lighting set to {copy.Describe()}{(port == null ? " (dongle)" : "")}");
                Recompute();
                RaiseChanged();
                done?.Invoke(err);
            });
        }

        /// <summary>Sleep time, key response and key blocking, over the cable or the dongle.</summary>
        public void SetSystem(AulaSystemSettings s, Action<string> done = null)
        {
            if (s == null) { done?.Invoke("Nothing chosen"); return; }
            var copy = s.Clone();
            Post(() =>
            {
                string err = port != null ? Run(AulaProtocol.SystemSequence(copy), "settings") : SendDongle(AulaProtocol.DongleSystem(copy), "settings");
                if (err == null) Logger.Write($"Aula F108 Pro: settings sent ({copy.Describe()}){(port == null ? " (dongle)" : "")}");
                Recompute();
                RaiseChanged();
                done?.Invoke(err);
            });
        }

        public void SyncClock(Action<string> done = null) => Post(() => { var err = SyncClockNow(); Recompute(); RaiseChanged(); done?.Invoke(err); });

        string SyncClockNow()
        {
            var t = Now();
            string err = port != null ? Run(AulaProtocol.ClockSequence(t), "clock") : SendDongle(AulaProtocol.DongleClock(t), "clock");
            if (err == null)
            {
                lock (gate) { state.ClockSyncedAt = t; if (port == null) dongleClockAt = t; }
                Logger.Write($"Aula F108 Pro: screen clock set to {t:yyyy-MM-dd HH:mm:ss}{(port == null ? " (dongle)" : "")}");
            }
            return err;
        }

        string SendDongle(byte[] packet, string what)
        {
            string msg = null;
            if (dongle == null) msg = "Plug the keyboard in with its USB-C cable, or switch it to the 2.4 GHz dongle, to change this.";
            else if (Exchange(packet) == null) msg = $"The keyboard didn't answer through the dongle. Press a key to wake it (and check it's switched to 2.4G), then try again.";
            lock (gate) state.LastError = msg ?? "";
            if (msg != null) Logger.Write($"Aula F108 Pro: {what} not confirmed through the dongle");
            return msg;
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

        void CloseDongle()
        {
            IAulaDongleLink d;
            lock (gate) { d = dongle; dongle = null; dongleAnsweredAt = DateTime.MinValue; dongleBattery = -1; }
            try { d?.Dispose(); } catch { }
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
            lock (gate) { scanDebounce?.Dispose(); poll?.Dispose(); donglePoll?.Dispose(); }
            work.CompleteAdding();
            worker?.Join(1000);
            ClosePort();
            CloseDongle();
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

        public IAulaDongleLink OpenDongle(HidCollectionInfo info, Action<byte[]> report)
        {
            var c = WinHidCollection.Open(info);
            if (c == null) return null;
            c.Report += report;
            return new DongleLink(c);
        }

        sealed class DongleLink : IAulaDongleLink
        {
            readonly WinHidCollection c;
            public DongleLink(WinHidCollection c) { this.c = c; }
            public bool Write(byte[] report33) => c.Write(report33, 500);
            public void Dispose() => c.Dispose();
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
