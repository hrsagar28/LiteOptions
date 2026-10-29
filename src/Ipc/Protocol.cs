using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Text;

namespace LiteOptions.Ipc
{
    /// <summary>
    /// Messages between the background engine (LiteOptions.exe, .NET Framework) and the settings window
    /// (LiteOptions.Settings.exe, WinUI 3). One JSON object per line over a named pipe. Shared source on both sides.
    /// Requests carry Id + Cmd; replies echo Id; pushes carry Event.
    /// </summary>
    [DataContract]
    public sealed class IpcMessage
    {
        [DataMember(EmitDefaultValue = false)] public int Id;
        [DataMember(EmitDefaultValue = false)] public string Cmd;
        [DataMember(EmitDefaultValue = false)] public string Event;
        [DataMember(EmitDefaultValue = false)] public bool Ok;
        [DataMember(EmitDefaultValue = false)] public string Error;
        [DataMember(EmitDefaultValue = false)] public AppSettings Settings;
        [DataMember(EmitDefaultValue = false)] public List<DeviceDto> Devices;
        [DataMember(EmitDefaultValue = false)] public SystemDto System;
        [DataMember(EmitDefaultValue = false)] public int Value;
        [DataMember(EmitDefaultValue = false)] public int Mods;
        [DataMember(EmitDefaultValue = false)] public int Vk;
        [DataMember(EmitDefaultValue = false)] public bool On;
        [DataMember(EmitDefaultValue = false)] public string Text;
    }

    [DataContract]
    public sealed class DeviceDto
    {
        [DataMember] public string Key = "";
        [DataMember] public string Name = "";
        [DataMember] public string Kind = "Other";      // Mouse | Keyboard | Other
        [DataMember] public string Connection = "";
        [DataMember] public int Pid;
        [DataMember] public bool Online;
        [DataMember] public string Firmware = "";
        [DataMember] public int BatteryPercent = -1;     // -1 = only a coarse level is known
        [DataMember] public string BatteryLevel = "";   // Full | Good | Low | Critical
        [DataMember] public bool HasBattery;
        [DataMember] public bool BatteryLow;
        [DataMember] public bool Charging;
        [DataMember] public int DpiMin, DpiMax, DpiStep, DpiCurrent, DpiDefault; // DpiMax = 0: not adjustable
        [DataMember] public bool FnSupported;
        [DataMember] public int FnMedia = -1;            // -1 unknown, 0 F-keys first, 1 media first
        [DataMember] public string LastError = "";
        [DataMember] public List<HostDto> Hosts;        // Easy-Switch channels; null = the device doesn't say
        [DataMember] public int CurrentHost = -1;        // 0-based

        public string BatteryText => !HasBattery ? "" : BatteryPercent >= 0 ? BatteryPercent + "%" : BatteryLevel;
    }

    [DataContract]
    public sealed class HostDto
    {
        [DataMember] public int Number;      // as printed on the device: 1, 2, 3
        [DataMember] public bool Paired;
        [DataMember] public bool Unknown;    // the device can't tell us about this channel
        [DataMember] public string Name = "";
    }

    [DataContract]
    public sealed class SystemDto
    {
        [DataMember] public string Version = "";
        [DataMember] public bool Installed;
        [DataMember] public int PointerSpeed = 10;
        [DataMember] public bool Precision = true;
        [DataMember] public int WheelLines = 3;
        [DataMember] public bool Startup;
        [DataMember] public int ReverseScroll = -1;      // -1 unknown, 0 off, 1 on
        [DataMember] public string OptionsPlus = "";     // running Logi processes, comma separated
        [DataMember] public string Architecture = "";
    }

    public static class Ipc
    {
        /// <summary>Per-user pipe name, so two people signed in on one PC never see each other's engine.</summary>
        public static string PipeName
        {
            get
            {
                string sid;
                try { sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName; } catch { sid = Environment.UserName; }
                return "LiteOptions.Engine." + sid;
            }
        }

        static readonly DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(IpcMessage));

        public static string ToLine(IpcMessage m)
        {
            using (var ms = new MemoryStream())
            {
                lock (serializer) serializer.WriteObject(ms, m);
                return Encoding.UTF8.GetString(ms.ToArray()).Replace("\n", "").Replace("\r", "");
            }
        }

        public static IpcMessage FromLine(string line)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                lock (serializer) return (IpcMessage)serializer.ReadObject(ms);
        }
    }
}
