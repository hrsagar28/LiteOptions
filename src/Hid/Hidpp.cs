using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace LiteOptions.Hid
{
    public sealed class HidppException : Exception
    {
        public const int Timeout = -1, WriteFailed = -2, NotSupported = -3;
        public readonly int Code; // >=0: HID++ 2.0 error code; 0x100+n: HID++ 1.0 error n; negative: local
        public HidppException(string msg, int code) : base(msg) { Code = code; }
        public bool IsTimeout => Code == Timeout;
        public bool IsHidpp10Error => Code >= 0x100;
    }

    public struct HidppMessage
    {
        public byte ReportId, DeviceIndex, FeatureIndex, FunctionSw;
        public byte[] Params;
        public int Function => FunctionSw >> 4;
        public int SoftwareId => FunctionSw & 0x0F;
    }

    /// <summary>HID++ request/response over one physical link. One request in flight at a time.</summary>
    public sealed class HidppChannel : IDisposable
    {
        public readonly IHidLink Link;
        readonly object requestLock = new object();
        readonly object pendingLock = new object();
        Pending pending;
        int swCounter;
        public event Action<HidppMessage> Notification;
        public Action<string> Log;

        sealed class Pending
        {
            public byte Dev, Feat, FnSw;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public byte[] Params;
            public int Error = -1;
        }

        public HidppChannel(IHidLink link)
        {
            Link = link;
            link.Report += OnReport;
        }

        byte NextSw()
        {
            // Software IDs 2..15; 0 is reserved for device notifications.
            swCounter = (swCounter + 1) % 14;
            return (byte)(swCounter + 2);
        }

        public byte[] Request(byte device, byte feature, int function, byte[] args, int timeoutMs = 1500)
        {
            args = args ?? new byte[0];
            lock (requestLock)
            {
                byte sw = NextSw();
                bool useLong = Link.CanLong || args.Length > 3;
                var r = new byte[useLong ? 20 : 7];
                r[0] = (byte)(useLong ? 0x11 : 0x10);
                r[1] = device;
                r[2] = feature;
                r[3] = (byte)(((function & 0x0F) << 4) | sw);
                Array.Copy(args, 0, r, 4, Math.Min(args.Length, r.Length - 4));

                var p = new Pending { Dev = device, Feat = feature, FnSw = r[3] };
                lock (pendingLock) pending = p;
                try
                {
                    if (!Link.Write(r, 1000))
                        throw new HidppException("write failed", HidppException.WriteFailed);
                    if (!p.Done.Wait(timeoutMs))
                        throw new HidppException("no response", HidppException.Timeout);
                    if (p.Error >= 0)
                        throw new HidppException($"device error 0x{p.Error:X}", p.Error);
                    return p.Params;
                }
                finally
                {
                    lock (pendingLock) pending = null;
                }
            }
        }

        static bool DeviceMatches(byte requested, byte got) =>
            requested == got || (requested == 0xFF && got == 0x00);

        void OnReport(byte[] d)
        {
            if (d == null || d.Length < 7) return;
            byte id = d[0];
            if (id != 0x10 && id != 0x11 && id != 0x12) return;

            var msg = new HidppMessage { ReportId = id, DeviceIndex = d[1], FeatureIndex = d[2], FunctionSw = d[3] };
            int plen = d.Length - 4;
            msg.Params = new byte[Math.Max(plen, 16)];
            Array.Copy(d, 4, msg.Params, 0, plen);

            lock (pendingLock)
            {
                var p = pending;
                if (p != null && DeviceMatches(p.Dev, msg.DeviceIndex))
                {
                    // HID++ 2.0 error: feature index 0xFF, then original feature index, fn|sw, code
                    if (msg.FeatureIndex == 0xFF && d[3] == p.Feat && d[4] == p.FnSw)
                    { p.Error = d[5]; p.Done.Set(); return; }
                    // HID++ 1.0 error: sub-id 0x8F, then original sub-id, address, code
                    if (msg.FeatureIndex == 0x8F && d[3] == p.Feat && d[4] == p.FnSw)
                    { p.Error = 0x100 + d[5]; p.Done.Set(); return; }
                    if (msg.FeatureIndex == p.Feat && msg.FunctionSw == p.FnSw)
                    { p.Params = msg.Params; p.Done.Set(); return; }
                }
            }
            try { Notification?.Invoke(msg); } catch (Exception ex) { Log?.Invoke("notification handler: " + ex.Message); }
        }

        public void Dispose()
        {
            Link.Report -= OnReport;
            Link.Dispose();
        }
    }

    public static class Feature
    {
        public const ushort Root = 0x0000, FeatureSet = 0x0001, FwVersion = 0x0003, DeviceName = 0x0005,
            BatteryStatus = 0x1000, BatteryVoltage = 0x1001, UnifiedBattery = 0x1004,
            ChangeHost = 0x1814, HostsInfo = 0x1815, WirelessStatus = 0x1D4B,
            AdjustableDpi = 0x2201, FnInversion = 0x40A0, NewFnInversion = 0x40A2, K375sFnInversion = 0x40A3;
    }

    public enum DeviceKind { Unknown, Keyboard, Mouse, Other }

    public sealed class BatteryInfo
    {
        public int? Percent;          // null when the device only reports a coarse level
        public string Level = "";     // Full / Good / Low / Critical
        public bool Charging;
        public int? Millivolts;
        public bool IsLow;
        public string Text => Percent.HasValue
            ? $"{Percent}%{(Charging ? " (charging)" : "")}"
            : (string.IsNullOrEmpty(Level) ? "unknown" : Level) + (Charging ? " (charging)" : "");
    }

    public sealed class DpiInfo
    {
        public int Current, Default;
        public List<int> Choices = new List<int>();
        public int Min => Choices.Count > 0 ? Choices[0] : 0;
        public int Max => Choices.Count > 0 ? Choices[Choices.Count - 1] : 0;
        public int Step => Choices.Count > 1 ? Choices[1] - Choices[0] : 100;
        public int Nearest(int dpi)
        {
            int best = Current;
            int bestDiff = int.MaxValue;
            foreach (var c in Choices) { int diff = Math.Abs(c - dpi); if (diff < bestDiff) { bestDiff = diff; best = c; } }
            return best;
        }
    }

    /// <summary>One HID++ 2.0 device (a device index on a channel).</summary>
    public sealed class HidppDevice
    {
        public readonly HidppChannel Channel;
        public readonly byte Index;
        readonly Dictionary<ushort, byte> features = new Dictionary<ushort, byte>();
        public int ProtocolMajor, ProtocolMinor;
        public int Timeout = 1500;

        public HidppDevice(HidppChannel channel, byte index) { Channel = channel; Index = index; }

        public bool Ping(int timeoutMs)
        {
            try
            {
                var r = Channel.Request(Index, 0x00, 1, new byte[] { 0, 0, 0x5A }, timeoutMs);
                ProtocolMajor = r[0]; ProtocolMinor = r[1];
                return ProtocolMajor >= 2;
            }
            catch (HidppException) { return false; }
        }

        /// <summary>Feature index for a feature id, or 0 when the device doesn't have it.</summary>
        public byte FeatureIndex(ushort featureId)
        {
            lock (features)
                if (features.TryGetValue(featureId, out byte cached)) return cached;
            var r = Channel.Request(Index, 0x00, 0, new byte[] { (byte)(featureId >> 8), (byte)featureId }, Timeout);
            byte idx = r[0];
            lock (features) features[featureId] = idx;
            return idx;
        }

        public bool Has(ushort featureId)
        {
            try { return FeatureIndex(featureId) != 0; } catch (HidppException) { return false; }
        }

        public byte[] Call(ushort featureId, int function, params byte[] args)
        {
            byte idx = FeatureIndex(featureId);
            if (idx == 0) throw new HidppException($"feature {featureId:X4} not supported", HidppException.NotSupported);
            return Channel.Request(Index, idx, function, args, Timeout);
        }

        public bool IsFeatureNotification(HidppMessage m, ushort featureId)
        {
            lock (features) return features.TryGetValue(featureId, out byte idx) && idx != 0 && m.FeatureIndex == idx;
        }

        // ── identity ──
        public string ReadName()
        {
            var count = Call(Feature.DeviceName, 0)[0];
            var sb = new StringBuilder();
            int guard = 0;
            while (sb.Length < count && guard++ < 32)
            {
                var chunk = Call(Feature.DeviceName, 1, (byte)sb.Length);
                int before = sb.Length;
                foreach (var b in chunk)
                {
                    if (sb.Length >= count || b == 0) break;
                    sb.Append((char)b);
                }
                if (sb.Length == before) break;
            }
            return sb.ToString().Trim();
        }

        /// <summary>0 keyboard, 1 remote, 2 numpad, 3 mouse, 4 trackpad, 5 trackball, 6 presenter, 7 receiver.</summary>
        public int ReadDeviceType() => Call(Feature.DeviceName, 2)[0];

        public string ReadFirmware()
        {
            int entities = Call(Feature.FwVersion, 0)[0];
            for (int e = 0; e < entities && e < 8; e++)
            {
                var r = Call(Feature.FwVersion, 1, (byte)e);
                if (r[0] != 0) continue; // 0 = main application firmware
                string prefix = Encoding.ASCII.GetString(r, 1, 3).Trim('\0', ' ');
                return $"{prefix} {r[4]:X2}.{r[5]:X2}.B{r[6]:X2}{r[7]:X2}";
            }
            return null;
        }

        // ── battery ──
        public BatteryInfo ReadBattery()
        {
            if (Has(Feature.UnifiedBattery))
            {
                var r = Call(Feature.UnifiedBattery, 1);
                var b = new BatteryInfo { Level = LevelName(r[1]), Charging = r[2] == 1 || r[2] == 2 };
                if (r[0] > 0) b.Percent = r[0];
                b.IsLow = (r[1] & 0x03) != 0 && (r[1] & 0x0C) == 0;
                return b;
            }
            if (Has(Feature.BatteryStatus))
            {
                var r = Call(Feature.BatteryStatus, 0);
                var b = new BatteryInfo { Charging = r[2] == 1 || r[2] == 2 || r[2] == 4 };
                if (r[0] > 0) b.Percent = r[0];
                b.Level = r[2] == 3 ? "Full" : "";
                return b;
            }
            if (Has(Feature.BatteryVoltage))
            {
                var r = Call(Feature.BatteryVoltage, 0);
                int mv = (r[0] << 8) | r[1];
                var b = new BatteryInfo { Millivolts = mv, Charging = (r[2] & 0x80) != 0, Percent = EstimateFromVoltage(mv) };
                return b;
            }
            return null;
        }

        static string LevelName(byte mask)
        {
            if ((mask & 0x08) != 0) return "Full";
            if ((mask & 0x04) != 0) return "Good";
            if ((mask & 0x02) != 0) return "Low";
            if ((mask & 0x01) != 0) return "Critical";
            return "";
        }

        /// <summary>Rough charge estimate. Alkaline AA/AAA cells (1 or 2 in series) or a Li-ion cell.</summary>
        public static int EstimateFromVoltage(int mv)
        {
            int[,] table;
            if (mv >= 3300)
                table = new[,] { { 4186, 100 }, { 4067, 90 }, { 3989, 80 }, { 3922, 70 }, { 3859, 60 }, { 3811, 50 }, { 3778, 40 }, { 3751, 30 }, { 3717, 20 }, { 3671, 10 }, { 3646, 5 }, { 3579, 2 }, { 3500, 0 } };
            else
            {
                if (mv >= 2000) mv /= 2; // two cells in series
                table = new[,] { { 1550, 100 }, { 1450, 90 }, { 1400, 80 }, { 1350, 65 }, { 1300, 50 }, { 1250, 35 }, { 1200, 20 }, { 1150, 10 }, { 1100, 5 }, { 1000, 0 } };
            }
            int n = table.GetLength(0);
            if (mv >= table[0, 0]) return table[0, 1];
            if (mv <= table[n - 1, 0]) return table[n - 1, 1];
            for (int i = 0; i < n - 1; i++)
            {
                int v1 = table[i, 0], p1 = table[i, 1], v2 = table[i + 1, 0], p2 = table[i + 1, 1];
                if (mv <= v1 && mv >= v2)
                    return p2 + (int)Math.Round((double)(mv - v2) * (p1 - p2) / (v1 - v2));
            }
            return 0;
        }

        // ── DPI (feature 0x2201) ──
        public DpiInfo ReadDpi()
        {
            if (!Has(Feature.AdjustableDpi)) return null;
            var info = new DpiInfo();
            var list = Call(Feature.AdjustableDpi, 1, 0);
            info.Choices = ParseDpiList(list, 1);
            var cur = Call(Feature.AdjustableDpi, 2, 0);
            info.Current = (cur[1] << 8) | cur[2];
            info.Default = (cur[3] << 8) | cur[4];
            if (info.Current == 0) info.Current = info.Default;
            if (info.Default == 0) info.Default = 1000;
            return info.Choices.Count > 0 ? info : null;
        }

        public static List<int> ParseDpiList(byte[] reply, int skip)
        {
            var result = new List<int>();
            for (int i = skip; i + 1 < reply.Length; i += 2)
            {
                int v = (reply[i] << 8) | reply[i + 1];
                if (v == 0) break;
                if ((v >> 13) == 0x7)
                {
                    int step = v & 0x1FFF;
                    if (i + 3 >= reply.Length || result.Count == 0 || step == 0) break;
                    int last = (reply[i + 2] << 8) | reply[i + 3];
                    for (int x = result[result.Count - 1] + step; x <= last; x += step) result.Add(x);
                    i += 2;
                }
                else result.Add(v);
            }
            return result;
        }

        public void WriteDpi(int dpi)
        {
            Call(Feature.AdjustableDpi, 3, 0, (byte)(dpi >> 8), (byte)dpi);
        }

        // ── Fn inversion (K380 & friends) ──
        // State true  = top row sends media/special functions; hold Fn for F1..F12.
        // State false = top row sends F1..F12; hold Fn for media/special functions.
        ushort fnFeature;
        byte fnHost = 0xFF;

        public bool SupportsFnInversion()
        {
            if (fnFeature != 0) return true;
            foreach (var f in new[] { Feature.K375sFnInversion, Feature.NewFnInversion, Feature.FnInversion })
                if (Has(f)) { fnFeature = f; break; }
            if (fnFeature == Feature.K375sFnInversion)
            {
                try { fnHost = Call(Feature.HostsInfo, 0)[3]; } catch (HidppException) { fnHost = 0xFF; }
            }
            return fnFeature != 0;
        }

        public ushort FnFeatureId => fnFeature;

        public bool? ReadFnInversion()
        {
            if (!SupportsFnInversion()) return null;
            if (fnFeature == Feature.K375sFnInversion)
                return (Call(fnFeature, 0, fnHost)[1] & 0x01) != 0;
            return (Call(fnFeature, 0)[0] & 0x01) != 0;
        }

        public void WriteFnInversion(bool mediaKeysByDefault)
        {
            if (!SupportsFnInversion()) throw new HidppException("Fn inversion not supported", HidppException.NotSupported);
            byte v = (byte)(mediaKeysByDefault ? 1 : 0);
            if (fnFeature == Feature.K375sFnInversion) Call(fnFeature, 1, fnHost, v);
            else Call(fnFeature, 1, v);
        }
    }
}
