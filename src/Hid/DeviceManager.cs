using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LiteOptions.Hid
{
    public sealed class LogiDevice
    {
        public string Key;
        public HidppDevice Dev;
        public string Name = "Logitech device";
        public DeviceKind Kind;
        public string Connection = "Bluetooth";
        public ushort Pid;
        public string Firmware;
        public BatteryInfo Battery;
        public DpiInfo Dpi;
        public bool FnSupported;
        public bool? FnMedia;
        public HostsInfo Hosts;
        public volatile bool Online = true;
        public int Failures;
        public string LastError;
        internal int ApplyRetries;
        public int ApplyRetriesForTests => ApplyRetries;
        internal System.Threading.Timer RetryTimer;
        public string StatusText => !Online ? "Not responding (asleep or off)" : Connection + (Battery != null ? " · Battery " + Battery.Text : "");
    }

    /// <summary>Abstracts the OS so the device logic can be tested without hardware.</summary>
    public interface IDeviceSource
    {
        List<HidCollectionInfo> Enumerate();
        IHidLink Open(string groupKey, List<HidCollectionInfo> collections);
    }

    public sealed class WinDeviceSource : IDeviceSource
    {
        public List<HidCollectionInfo> Enumerate() => HidEnumerator.Enumerate(0x046D);
        public IHidLink Open(string key, List<HidCollectionInfo> cols)
        {
            var g = new WinHidGroup(key, cols);
            if (g.OpenCount == 0) { g.Dispose(); return null; }
            return g;
        }
    }

    /// <summary>
    /// Finds Logitech HID++ devices, reads battery/DPI/Fn state and keeps the user's
    /// device settings applied (re-applies after reconnects). All device I/O runs on one worker thread.
    /// </summary>
    public sealed class DeviceManager : IDisposable
    {
        static readonly HashSet<ushort> ReceiverPids = new HashSet<ushort>
        { 0xC52B, 0xC532, 0xC534, 0xC539, 0xC53A, 0xC53D, 0xC53F, 0xC541, 0xC545, 0xC547, 0xC548, 0xC54D };

        sealed class Group
        {
            public string Key;
            public ushort Pid;
            public string Product;
            public bool IsReceiver;
            public HidppChannel Channel;
            public readonly List<LogiDevice> Devices = new List<LogiDevice>();
            public int EmptyProbes;
        }

        readonly IDeviceSource source;
        readonly Func<AppSettings> getSettings;
        readonly BlockingCollection<Action> work = new BlockingCollection<Action>();
        readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();
        readonly object stateLock = new object();
        Thread worker;
        Timer pollTimer, reviveTimer, scanDebounce;
        readonly HashSet<string> quietKeys = new HashSet<string>(); // avoid repeating the same log line every retry
        readonly HashSet<string> featuresLogged = new HashSet<string>();
        volatile bool disposed;
        int pendingDpi = -1;
        string lastEnumSignature = "";

        public event Action Changed;
        public TimeSpan PollInterval = TimeSpan.FromMinutes(5);
        /// <summary>A device that has just woken up often ignores the first request. Retry applying settings after these delays.</summary>
        public int[] ApplyRetryDelaysMs = { 2000, 5000, 12000 };
        /// <summary>Write each device's HID++ feature list to the log once (off in tests).</summary>
        public bool LogFeatureLists = true;
        public List<string> LastEnumeration { get; private set; } = new List<string>();

        public DeviceManager(IDeviceSource source, Func<AppSettings> getSettings)
        {
            this.source = source;
            this.getSettings = getSettings;
        }

        public void Start()
        {
            worker = new Thread(WorkLoop) { IsBackground = true, Name = "devices" };
            worker.Start();
            Post(Scan);
            pollTimer = new Timer(_ => Post(Poll), null, PollInterval, PollInterval);
            // quick retry for devices that are off/asleep or whose link died, so settings come back soon after wake-up
            reviveTimer = new Timer(_ => Post(Revive), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        void WorkLoop()
        {
            foreach (var job in work.GetConsumingEnumerable())
            {
                if (disposed) break;
                try { job(); }
                catch (Exception ex) { Logger.Write("device worker: " + ex); }
            }
        }

        void Post(Action a) { if (!disposed) try { work.Add(a); } catch (InvalidOperationException) { } }

        /// <summary>Run every queued job now (tests only).</summary>
        public void RunPendingForTests() { while (work.TryTake(out var a)) a(); }
        public void StartForTests() { Post(Scan); }

        public List<LogiDevice> Snapshot()
        {
            lock (stateLock) return groups.Values.SelectMany(g => g.Devices).ToList();
        }

        public LogiDevice Mouse => Snapshot().FirstOrDefault(d => d.Kind == DeviceKind.Mouse);
        public LogiDevice Keyboard => Snapshot().FirstOrDefault(d => d.Kind == DeviceKind.Keyboard);

        /// <summary>Debounced rescan, used for device arrival/removal and resume from sleep.</summary>
        public void RequestScan(int delayMs = 1500)
        {
            if (disposed) return;
            scanDebounce?.Dispose();
            scanDebounce = new Timer(_ => Post(Scan), null, delayMs, Timeout.Infinite);
        }

        public void RequestPoll() => Post(Poll);
        public void RequestReviveForTests() => Post(Revive);
        public void ApplySettingsNow() => Post(() => { foreach (var d in Snapshot()) ApplyFresh(d); RaiseChanged(); });

        /// <summary>Live DPI change (slider drag). Coalesces to the latest value.</summary>
        public void SetDpi(int dpi)
        {
            Interlocked.Exchange(ref pendingDpi, dpi);
            Post(() =>
            {
                int v = Interlocked.Exchange(ref pendingDpi, -1);
                if (v <= 0) return;
                foreach (var d in Snapshot().Where(x => x.Kind == DeviceKind.Mouse && x.Dpi != null && x.Online))
                {
                    try
                    {
                        int target = d.Dpi.Nearest(v);
                        d.Dev.WriteDpi(target);
                        d.Dpi.Current = target;
                    }
                    catch (HidppException ex) { d.LastError = ex.Message; }
                }
                RaiseChanged();
            });
        }

        public void SetFnMode(bool media) => Post(() =>
        {
            foreach (var d in Snapshot().Where(x => x.Kind == DeviceKind.Keyboard && x.FnSupported))
            {
                try { d.Dev.WriteFnInversion(media); d.FnMedia = d.Dev.ReadFnInversion(); Logger.Write($"{d.Name}: top row set to {(media ? "media keys" : "F1-F12")}"); }
                catch (HidppException ex) { d.LastError = ex.Message; Logger.Write($"{d.Name}: Fn mode failed: {ex.Message}"); }
            }
            RaiseChanged();
        });

        void RaiseChanged() { try { Changed?.Invoke(); } catch { } }

        // ───────────────────────── scanning ─────────────────────────

        void Scan()
        {
            List<HidCollectionInfo> all;
            try { all = source.Enumerate(); }
            catch (Exception ex) { Logger.Write("HID enumeration failed: " + ex.Message); return; }

            var lines = all.Select(i => i.ToString()).ToList();
            string sig = string.Join("|", lines);
            if (sig != lastEnumSignature)
            {
                lastEnumSignature = sig;
                LastEnumeration = lines;
                Logger.Write($"Found {all.Count} Logitech HID collection(s):");
                foreach (var l in lines) Logger.Write("   " + l);
            }

            var candidates = all.Where(i => i.IsHidppCandidate).GroupBy(i => i.GroupKey).ToDictionary(g => g.Key, g => g.ToList());

            // drop groups that vanished or died
            List<Group> gone;
            lock (stateLock)
                gone = groups.Values.Where(g => !candidates.ContainsKey(g.Key) || !g.Channel.Link.Alive).ToList();
            foreach (var g in gone)
            {
                lock (stateLock) groups.Remove(g.Key);
                if (g.Devices.Count > 0) Logger.Write($"Device link closed: {g.Product ?? g.Pid.ToString("X4")}");
                try { g.Channel.Dispose(); } catch { }
            }

            foreach (var kv in candidates)
            {
                Group existing;
                lock (stateLock) groups.TryGetValue(kv.Key, out existing);
                if (existing != null)
                {
                    if (existing.Devices.Count == 0) ProbeGroup(existing);
                    continue;
                }

                var link = source.Open(kv.Key, kv.Value);
                if (link == null) { Logger.Write($"Could not open HID++ interface of PID {kv.Value[0].Pid:X4}"); continue; }
                var first = kv.Value[0];
                var g = new Group
                {
                    Key = kv.Key,
                    Pid = first.Pid,
                    Product = kv.Value.Select(i => i.Product).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)),
                    IsReceiver = ReceiverPids.Contains(first.Pid),
                    Channel = new HidppChannel(link) { Log = Logger.Write },
                };
                g.Channel.Notification += m => OnNotification(g, m);
                lock (stateLock) groups[g.Key] = g;
                ProbeGroup(g);
            }
            RaiseChanged();
        }

        void ProbeGroup(Group g)
        {
            var indices = g.IsReceiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : new byte[] { 0xFF };
            foreach (byte idx in indices)
            {
                if (Find(g, d => d.Dev.Index == idx) != null) continue;
                var dev = new HidppDevice(g.Channel, idx);
                if (!dev.Ping(g.IsReceiver ? 500 : 1500))
                {
                    if (!g.IsReceiver)
                    {
                        g.EmptyProbes++;
                        if (quietKeys.Add(g.Key)) Logger.Write($"PID {g.Pid:X4} \"{g.Product}\" did not answer HID++ (asleep or off?); will keep retrying quietly.");
                    }
                    continue;
                }
                g.EmptyProbes = 0;
                quietKeys.Remove(g.Key);
                var d = new LogiDevice
                {
                    Key = g.Key + "#" + idx,
                    Dev = dev,
                    Pid = g.Pid,
                    Connection = g.IsReceiver ? (g.Pid == 0xC548 ? "Logi Bolt receiver" : "USB receiver") : "Bluetooth",
                };
                Identify(d, g);
                lock (stateLock) g.Devices.Add(d);
                ApplyFresh(d);
                RaiseChanged();
                if (LogFeatureLists) Post(() => { if (Snapshot().Contains(d)) LogFeatures(d); });
            }
        }

        void Identify(LogiDevice d, Group g)
        {
            var dev = d.Dev;
            try { d.Name = dev.ReadName(); } catch (HidppException) { d.Name = g.Product ?? "Logitech device"; }
            if (string.IsNullOrWhiteSpace(d.Name)) d.Name = g.Product ?? "Logitech device";

            int type = -1;
            try { type = dev.ReadDeviceType(); } catch (HidppException) { }
            switch (type)
            {
                case 0: case 2: d.Kind = DeviceKind.Keyboard; break;
                case 3: case 4: case 5: d.Kind = DeviceKind.Mouse; break;
                case -1: break;
                default: d.Kind = DeviceKind.Other; break;
            }
            try { d.FnSupported = dev.SupportsFnInversion(); } catch (HidppException) { }
            try { d.Dpi = dev.ReadDpi(); } catch (HidppException ex) { Logger.Write($"{d.Name}: DPI read failed: {ex.Message}"); }
            if (d.Kind == DeviceKind.Unknown)
            {
                string n = d.Name.ToLowerInvariant();
                if (d.FnSupported || n.Contains("keyboard") || n.Contains("keys") || n.StartsWith("k")) d.Kind = DeviceKind.Keyboard;
                else if (d.Dpi != null || n.Contains("mouse") || n.StartsWith("m")) d.Kind = DeviceKind.Mouse;
                else d.Kind = DeviceKind.Other;
            }
            if (d.FnSupported) try { d.FnMedia = dev.ReadFnInversion(); } catch (HidppException) { }
            try { d.Firmware = dev.ReadFirmware(); } catch (HidppException) { }
            try { d.Battery = dev.ReadBattery(); } catch (HidppException) { }
            try { d.Hosts = dev.ReadHosts(); } catch (HidppException ex) { Logger.Write($"{d.Name}: Easy-Switch read failed: {ex.Message}"); }
            dev.Has(Feature.WirelessStatus); // cache its index so reconnect notifications are recognised
            Logger.Write($"Connected: {d.Name} ({d.Kind}, {d.Connection}, HID++ {dev.ProtocolMajor}.{dev.ProtocolMinor}" +
                         $"{(d.Firmware != null ? ", fw " + d.Firmware : "")})" +
                         $"{(d.Battery != null ? " battery " + d.Battery.Text : "")}" +
                         $"{(d.Dpi != null ? $" DPI {d.Dpi.Current} ({d.Dpi.Min}-{d.Dpi.Max})" : "")}" +
                         $"{(d.FnSupported ? $" Fn-swap 0x{dev.FnFeatureId:X4} media-first={d.FnMedia}" : "")}");
            if (d.Hosts != null) Logger.Write($"{d.Name}: Easy-Switch channels {d.Hosts.Text}");
        }

        /// <summary>Once per device per run: list its HID++ features in the log (helps when adding support for more of them).</summary>
        void LogFeatures(LogiDevice d)
        {
            if (!featuresLogged.Add(d.Key + "|" + d.Name)) return;
            try { Logger.Write($"{d.Name}: features " + string.Join(" ", d.Dev.ReadFeatureIds().Select(f => f.ToString("X4")))); }
            catch (HidppException ex) { Logger.Write($"{d.Name}: feature list unavailable ({ex.Message})"); }
        }

        /// <summary>Apply settings after a (re)connect or a settings change, with a few quick retries if the device isn't ready yet.</summary>
        void ApplyFresh(LogiDevice d)
        {
            d.ApplyRetries = 0;
            SafeApply(d);
        }

        void SafeApply(LogiDevice d)
        {
            try
            {
                Apply(d);
                d.Online = true; d.Failures = 0; d.ApplyRetries = 0;
                d.RetryTimer?.Dispose(); d.RetryTimer = null;
            }
            catch (HidppException ex)
            {
                d.LastError = ex.Message;
                var delays = ApplyRetryDelaysMs ?? new int[0];
                if (d.ApplyRetries < delays.Length && !disposed)
                {
                    int delay = delays[d.ApplyRetries++];
                    Logger.Write($"{d.Name}: could not apply settings ({ex.Message}); trying again in {Math.Max(1, delay / 1000)} s");
                    d.RetryTimer?.Dispose();
                    d.RetryTimer = new Timer(_ => Post(() =>
                    {
                        if (!Snapshot().Contains(d)) return; // gone or replaced by a rescan
                        SafeApply(d);
                        RaiseChanged();
                    }), null, delay, Timeout.Infinite);
                }
                else Logger.Write($"{d.Name}: could not apply settings: {ex.Message}");
            }
        }

        void Apply(LogiDevice d)
        {
            var s = getSettings();
            if (d.Kind == DeviceKind.Mouse && s.MouseDpi > 0 && d.Dpi != null)
            {
                int target = d.Dpi.Nearest(s.MouseDpi);
                var fresh = d.Dev.ReadDpi();
                if (fresh != null) d.Dpi = fresh;
                if (d.Dpi.Current != target)
                {
                    d.Dev.WriteDpi(target);
                    d.Dpi.Current = target;
                    Logger.Write($"{d.Name}: DPI set to {target}");
                }
            }
            if (d.Kind == DeviceKind.Keyboard && d.FnSupported && s.FnMode != "unchanged")
            {
                bool want = s.FnMode == "media";
                bool? cur = d.Dev.ReadFnInversion();
                if (cur != want)
                {
                    d.Dev.WriteFnInversion(want);
                    d.FnMedia = d.Dev.ReadFnInversion();
                    Logger.Write($"{d.Name}: top row set to {(want ? "media keys" : "F1-F12")}");
                }
                else d.FnMedia = cur;
            }
        }

        void Poll()
        {
            bool needScan = false;
            List<Group> snapshot;
            lock (stateLock) snapshot = groups.Values.ToList();
            foreach (var g in snapshot)
            {
                if (!g.Channel.Link.Alive) { needScan = true; continue; }
                if (g.Devices.Count == 0 || g.IsReceiver) ProbeGroup(g);
                List<LogiDevice> devs;
                lock (stateLock) devs = g.Devices.ToList();
                foreach (var d in devs)
                {
                    try
                    {
                        d.Battery = d.Dev.ReadBattery() ?? d.Battery;
                        bool wasOffline = !d.Online;
                        d.Online = true; d.Failures = 0;
                        SafeApply(d);
                        if (wasOffline) Logger.Write($"{d.Name} is responding again");
                    }
                    catch (HidppException ex)
                    {
                        d.Failures++;
                        d.LastError = ex.Message;
                        if (d.Failures >= 2 && d.Online) { d.Online = false; Logger.Write($"{d.Name} stopped responding ({ex.Message})"); }
                    }
                }
            }
            if (needScan) Scan();
            RaiseChanged();
        }

        LogiDevice Find(Group g, Func<LogiDevice, bool> pred)
        {
            lock (stateLock) return g.Devices.FirstOrDefault(pred);
        }

        /// <summary>Every 30 s: reopen dead links and re-probe silent devices. Cheap when everything is healthy.</summary>
        void Revive()
        {
            List<Group> snapshot;
            lock (stateLock) snapshot = groups.Values.ToList();
            bool changed = false;
            if (snapshot.Any(g => !g.Channel.Link.Alive)) { Scan(); return; }
            foreach (var g in snapshot)
            {
                if (!g.IsReceiver && g.Devices.Count == 0) { ProbeGroup(g); changed |= g.Devices.Count > 0; continue; }
                List<LogiDevice> offline;
                lock (stateLock) offline = g.Devices.Where(d => !d.Online).ToList();
                foreach (var d in offline)
                {
                    int saved = d.Dev.Timeout;
                    d.Dev.Timeout = 800;
                    try
                    {
                        if (!d.Dev.Ping(800)) continue;
                        d.Online = true; d.Failures = 0;
                        Logger.Write($"{d.Name} is back; re-applying settings");
                        ApplyFresh(d);
                        try { d.Battery = d.Dev.ReadBattery() ?? d.Battery; } catch (HidppException) { }
                        changed = true;
                    }
                    finally { d.Dev.Timeout = saved; }
                }
            }
            if (changed) RaiseChanged();
        }

        void OnNotification(Group g, HidppMessage m)
        {
            // Receiver: HID++ 1.0 "device connection" notification (sub-id 0x41). Bit 6 of the address byte = link lost.
            if (g.IsReceiver && m.FeatureIndex == 0x41)
            {
                bool linkUp = (m.FunctionSw & 0x40) == 0;
                byte idx = m.DeviceIndex;
                if (linkUp)
                    Post(() =>
                    {
                        Thread.Sleep(300);
                        var d = Find(g, x => x.Dev.Index == idx);
                        if (d == null) ProbeGroup(g);
                        else { d.Online = true; ApplyFresh(d); try { d.Battery = d.Dev.ReadBattery() ?? d.Battery; } catch (HidppException) { } }
                        RaiseChanged();
                    });
                else
                {
                    var d = Find(g, x => x.Dev.Index == idx);
                    if (d != null) { d.Online = false; RaiseChanged(); }
                }
                return;
            }

            var dev = Find(g, x => x.Dev.Index == m.DeviceIndex || (x.Dev.Index == 0xFF && m.DeviceIndex == 0x00));
            if (dev == null) return;

            // HID++ 2.0 wireless status: device powered on / reconnected and asks to be reconfigured.
            if (dev.Dev.IsFeatureNotification(m, Feature.WirelessStatus) && m.Function == 0 && m.Params[1] == 1)
            {
                Post(() =>
                {
                    Thread.Sleep(300);
                    dev.Online = true;
                    Logger.Write($"{dev.Name} reconnected; re-applying settings");
                    ApplyFresh(dev);
                    try { dev.Battery = dev.Dev.ReadBattery() ?? dev.Battery; } catch (HidppException) { }
                    RaiseChanged();
                });
                return;
            }

            if (dev.Dev.IsFeatureNotification(m, Feature.UnifiedBattery) || dev.Dev.IsFeatureNotification(m, Feature.BatteryStatus) ||
                dev.Dev.IsFeatureNotification(m, Feature.BatteryVoltage))
            {
                Post(() => { try { dev.Battery = dev.Dev.ReadBattery() ?? dev.Battery; RaiseChanged(); } catch (HidppException) { } });
            }
        }

        public void Dispose()
        {
            disposed = true;
            pollTimer?.Dispose();
            reviveTimer?.Dispose();
            scanDebounce?.Dispose();
            foreach (var d in Snapshot()) { d.RetryTimer?.Dispose(); d.RetryTimer = null; }
            work.CompleteAdding();
            worker?.Join(1500);
            lock (stateLock)
            {
                foreach (var g in groups.Values) try { g.Channel.Dispose(); } catch { }
                groups.Clear();
            }
        }
    }
}
