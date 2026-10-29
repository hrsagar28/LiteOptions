using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using LiteOptions.Hid;

namespace LiteOptions.Tests
{
    /// <summary>Simulated HID++ 2.0 device, modelled on what Solaar reports for these devices.</summary>
    class FakeDevice
    {
        public string Name;
        public int Type;
        public readonly Dictionary<ushort, byte> Features = new Dictionary<ushort, byte>();
        public int Dpi = 1000, DpiDefault = 1000;
        public bool FnInv = true;
        public int BatteryPct = 80;
        public int Millivolts = 1400;
        public byte UnifiedLevel = 0x04;
        public bool Asleep;
        public int Requests;
        public int DropFnWrites; // ignore this many "set Fn mode" requests, like a keyboard that has only just woken up
        public readonly List<FakeHost> Hosts = new List<FakeHost>();
        public int CurrentHost;

        public static FakeDevice PebbleM350s()
        {
            var d = new FakeDevice { Name = "Pebble Mouse 2 M350s", Type = 3 };
            byte i = 1;
            foreach (var f in new ushort[] { Feature.FeatureSet, Feature.FwVersion, Feature.DeviceName, Feature.WirelessStatus, Feature.UnifiedBattery, Feature.ChangeHost, Feature.AdjustableDpi })
                d.Features[f] = i++;
            return d;
        }

        public static FakeDevice K380()
        {
            var d = new FakeDevice { Name = "Keyboard K380", Type = 0 };
            d.Features[Feature.FeatureSet] = 1; d.Features[Feature.FwVersion] = 2; d.Features[Feature.DeviceName] = 3;
            d.Features[Feature.BatteryStatus] = 4; d.Features[Feature.ChangeHost] = 5; d.Features[Feature.WirelessStatus] = 6;
            d.Features[Feature.FnInversion] = 0x0B; // the index k380_conf.c hard-codes
            d.Features[Feature.HostsInfo] = 7;
            d.Hosts.Add(new FakeHost());
            d.Hosts.Add(new FakeHost { Paired = true, Name = "WORK-LAPTOP" });
            d.Hosts.Add(new FakeHost { Paired = true, Name = "Anna\u2019s iPhone 15 Pro Max" }); // longer than one 14-byte chunk, with a 3-byte character
            d.CurrentHost = 1;
            return d;
        }

        ushort FeatureAt(byte idx) => Features.FirstOrDefault(kv => kv.Value == idx).Key;

        /// <summary>Returns response params (or null = error code in errCode, or no reply when Asleep).</summary>
        public byte[] Handle(byte featIdx, int fn, byte[] p, out int errCode)
        {
            Requests++;
            errCode = -1;
            if (featIdx == 0)
            {
                if (fn == 0)
                {
                    ushort id = (ushort)((p[0] << 8) | p[1]);
                    if (id == 0) return new byte[] { 0, 0, 0 };
                    return Features.TryGetValue(id, out var ix) ? new byte[] { ix, 0, 0 } : new byte[] { 0, 0, 0 };
                }
                if (fn == 1) return new byte[] { 4, 5, p[2] };
            }
            ushort f = FeatureAt(featIdx);
            switch (f)
            {
                case Feature.DeviceName:
                    if (fn == 0) return new[] { (byte)Name.Length };
                    if (fn == 1) return Encoding.ASCII.GetBytes(Name.Substring(Math.Min(p[0], Name.Length)));
                    if (fn == 2) return new[] { (byte)Type };
                    break;
                case Feature.FwVersion:
                    if (fn == 0) return new byte[] { 1 };
                    if (fn == 1) return new byte[] { 0, (byte)'R', (byte)'Q', (byte)'M', 0x40, 0x02, 0x00, 0x17 };
                    break;
                case Feature.UnifiedBattery:
                    if (fn == 0) return new byte[] { 0x0F, 0x00 };
                    if (fn == 1) return new byte[] { 0, UnifiedLevel, 0, 0 };
                    break;
                case Feature.BatteryStatus:
                    if (fn == 0) return new byte[] { (byte)BatteryPct, 50, 0 };
                    break;
                case Feature.BatteryVoltage:
                    if (fn == 0) return new byte[] { (byte)(Millivolts >> 8), (byte)Millivolts, 0 };
                    break;
                case Feature.AdjustableDpi:
                    if (fn == 0) return new byte[] { 1 };
                    if (fn == 1) return new byte[] { 0, 0x01, 0x90, 0xE0, 0x64, 0x0F, 0xA0, 0, 0 };
                    if (fn == 2) return new byte[] { 0, (byte)(Dpi >> 8), (byte)Dpi, (byte)(DpiDefault >> 8), (byte)DpiDefault };
                    if (fn == 3) { Dpi = (p[1] << 8) | p[2]; return new byte[] { 0, p[1], p[2] }; }
                    break;
                case Feature.FeatureSet:
                    if (fn == 0) return new[] { (byte)Features.Count };
                    if (fn == 1)
                    {
                        ushort id = Features.Where(kv => kv.Value == p[0]).Select(kv => kv.Key).FirstOrDefault();
                        return new byte[] { (byte)(id >> 8), (byte)id, 0 };
                    }
                    break;
                case Feature.ChangeHost:
                    if (fn == 0) return new byte[] { 3, (byte)CurrentHost, 0 };
                    break;
                case Feature.HostsInfo:
                    if (fn == 0) return new byte[] { 0x01, 0, (byte)Hosts.Count, (byte)CurrentHost };
                    if (fn == 1 && p[0] < Hosts.Count)
                    {
                        var h = Hosts[p[0]];
                        return new byte[] { p[0], (byte)(h.Paired ? 1 : 0), 2, 1, (byte)Encoding.UTF8.GetByteCount(h.Name), 24 };
                    }
                    if (fn == 3 && p[0] < Hosts.Count)
                    {
                        var nb = Encoding.UTF8.GetBytes(Hosts[p[0]].Name);
                        var r = new byte[16]; r[0] = p[0]; r[1] = p[1];
                        if (p[1] < nb.Length) Array.Copy(nb, p[1], r, 2, Math.Min(14, nb.Length - p[1]));
                        return r;
                    }
                    break;
                case Feature.FnInversion:
                    if (fn == 0) return new byte[] { (byte)(FnInv ? 1 : 0) };
                    if (fn == 1) { FnInv = p[0] != 0; return new byte[] { p[0] }; }
                    break;
            }
            errCode = f == 0 ? 0x05 /*invalid feature index*/ : 0x07 /*invalid function*/;
            return null;
        }
    }

    class FakeHost { public bool Paired; public string Name = ""; }

    /// <summary>A link with one direct device (index 0xFF) or a receiver with devices in slots.</summary>
    class FakeLink : IHidLink
    {
        readonly bool canLong, canShort;
        public readonly Dictionary<byte, FakeDevice> Slots = new Dictionary<byte, FakeDevice>();
        public readonly bool Receiver;
        public bool AnswerDirectAsZero;     // some devices answer index 0x00 for 0xFF
        public bool NotifyBeforeReply;      // inject a notification ahead of each reply
        public bool alive = true;
        public readonly List<byte[]> Written = new List<byte[]>();
        public event Action<byte[]> Report;

        public FakeLink(bool canLong, bool canShort, bool receiver = false) { this.canLong = canLong; this.canShort = canShort; Receiver = receiver; }

        public bool CanLong => canLong;
        public bool CanShort => canShort;
        public bool Alive => alive;

        public bool Write(byte[] r, int timeoutMs)
        {
            if (!alive) return false;
            if (r[0] == 0x11 && !canLong) return false;
            if (r[0] == 0x10 && !canShort) return false;
            lock (Written) Written.Add((byte[])r.Clone());
            byte dev = r[1], feat = r[2], fnsw = r[3];
            int plen = r.Length - 4;
            var p = new byte[16];
            Array.Copy(r, 4, p, 0, plen);

            if (!Slots.TryGetValue(dev, out var d))
            {
                if (Receiver) Send(new byte[] { 0x10, dev, 0x8F, feat, fnsw, 0x09, 0 });
                return true;
            }
            if (d.Asleep) return true; // no answer
            if (d.DropFnWrites > 0 && d.Features.TryGetValue(Feature.FnInversion, out var fnIdx) && feat == fnIdx && (fnsw >> 4) == 1)
            {
                d.DropFnWrites--;
                return true; // no answer
            }
            var resp = d.Handle(feat, fnsw >> 4, p, out int err);
            byte outDev = dev == 0xFF && AnswerDirectAsZero ? (byte)0 : dev;
            if (NotifyBeforeReply) Send(new byte[] { 0x11, outDev, 0x05, 0x00, 1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            if (resp == null)
            {
                Send(Pad(new byte[] { (byte)(canLong ? 0x11 : 0x10), outDev, 0xFF, feat, fnsw, (byte)err }, canLong ? 20 : 7));
                return true;
            }
            var reply = new byte[canLong ? 20 : 7];
            reply[0] = (byte)(canLong ? 0x11 : 0x10);
            reply[1] = outDev; reply[2] = feat; reply[3] = fnsw;
            Array.Copy(resp, 0, reply, 4, Math.Min(resp.Length, reply.Length - 4));
            Send(reply);
            return true;
        }

        static byte[] Pad(byte[] b, int len) { var r = new byte[len]; Array.Copy(b, r, Math.Min(b.Length, len)); return r; }

        public void Send(byte[] report)
        {
            // deliver asynchronously like a real reader thread
            ThreadPool.QueueUserWorkItem(_ => Report?.Invoke(report));
        }

        public void Dispose() { alive = false; }
    }

    class FakeSource : IDeviceSource
    {
        public readonly List<(HidCollectionInfo info, FakeLink link)> Entries = new List<(HidCollectionInfo, FakeLink)>();
        public int Opens;

        public void Add(string parent, ushort pid, FakeLink link, string product, int outLen = 20)
        {
            Entries.Add((new HidCollectionInfo { Path = parent + "&col02", Vid = 0x046D, Pid = pid, UsagePage = 0xFF43, Usage = 0x0202, InLen = 20, OutLen = outLen, Product = product, ParentId = parent }, link));
            // the ordinary mouse/keyboard collections that the HID++ layer must ignore
            Entries.Add((new HidCollectionInfo { Path = parent + "&col01", Vid = 0x046D, Pid = pid, UsagePage = 0x0001, Usage = 0x0002, InLen = 8, OutLen = 0, Product = product, ParentId = parent }, null));
        }

        public List<HidCollectionInfo> Enumerate() => Entries.Where(e => e.link == null || e.link.Alive).Select(e => e.info).ToList();

        public IHidLink Open(string key, List<HidCollectionInfo> cols)
        {
            Opens++;
            return Entries.First(e => e.info.GroupKey == key && e.link != null).link;
        }
    }
}
