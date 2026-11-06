using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LiteOptions
{
    [Flags]
    public enum Mods { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Win = 8 }

    [DataContract]
    public sealed class ActionSpec
    {
        // Kind: "default" (leave the button/key alone), "none" (disable), "preset", "keys", "launch", "text"
        [DataMember(Order = 1)] public string Kind = "default";
        [DataMember(Order = 2, EmitDefaultValue = false)] public string Preset;
        [DataMember(Order = 3, EmitDefaultValue = false)] public int Mods;
        [DataMember(Order = 4, EmitDefaultValue = false)] public int Vk;
        [DataMember(Order = 5, EmitDefaultValue = false)] public string Target;
        [DataMember(Order = 6, EmitDefaultValue = false)] public string Args;
        [DataMember(Order = 7, EmitDefaultValue = false)] public string Text;

        [OnDeserializing]
        void SetDefaults(StreamingContext c) { Kind = "default"; }

        public static ActionSpec Default() => new ActionSpec { Kind = "default" };
        public static ActionSpec Nothing() => new ActionSpec { Kind = "none" };
        public static ActionSpec Of(string preset) => new ActionSpec { Kind = "preset", Preset = preset };
        public static ActionSpec Keys(int mods, int vk) => new ActionSpec { Kind = "keys", Mods = mods, Vk = vk };

        public bool IsDefault => Kind == null || Kind == "default";
        public ActionSpec Clone() => (ActionSpec)MemberwiseClone();

        public bool SameAs(ActionSpec o) =>
            o != null && Kind == o.Kind && Preset == o.Preset && Mods == o.Mods && Vk == o.Vk &&
            Target == o.Target && Args == o.Args && Text == o.Text;
    }

    [DataContract]
    public sealed class KeyBinding
    {
        [DataMember(Order = 1)] public int Mods;
        [DataMember(Order = 2)] public int Vk;
        [DataMember(Order = 3)] public ActionSpec Action;
        public int TriggerId => (Mods << 16) | (Vk & 0xFFFF);
    }

    [DataContract]
    public sealed class Profile
    {
        [DataMember(Order = 1)] public string App = "";               // lower-case exe name, "" = all applications
        [DataMember(Order = 2)] public string MiddleMode = "default"; // default | action | gestures | inherit
        [DataMember(Order = 3)] public ActionSpec MiddleAction;
        [DataMember(Order = 4)] public ActionSpec GestureClick;
        [DataMember(Order = 5)] public ActionSpec GestureUp;
        [DataMember(Order = 6)] public ActionSpec GestureDown;
        [DataMember(Order = 7)] public ActionSpec GestureLeft;
        [DataMember(Order = 8)] public ActionSpec GestureRight;
        [DataMember(Order = 9)] public List<KeyBinding> Keys = new List<KeyBinding>();

        [OnDeserializing]
        void SetDefaults(StreamingContext c) { App = ""; MiddleMode = "default"; Keys = new List<KeyBinding>(); }

        public bool IsGlobal => string.IsNullOrEmpty(App);

        public void Normalize()
        {
            if (App == null) App = "";
            App = App.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(MiddleMode)) MiddleMode = IsGlobal ? "default" : "inherit";
            if (IsGlobal && MiddleMode == "inherit") MiddleMode = "default";
            if (MiddleAction == null) MiddleAction = ActionSpec.Of("back");
            if (GestureClick == null) GestureClick = ActionSpec.Of("middle_click");
            if (GestureUp == null) GestureUp = ActionSpec.Of("task_view");
            if (GestureDown == null) GestureDown = ActionSpec.Of("show_desktop");
            if (GestureLeft == null) GestureLeft = ActionSpec.Of("desktop_left");
            if (GestureRight == null) GestureRight = ActionSpec.Of("desktop_right");
            if (Keys == null) Keys = new List<KeyBinding>();
            Keys.RemoveAll(k => k == null || k.Action == null || k.Vk == 0);
        }
    }

    /// <summary>
    /// Backlight of the Aula F108 Pro (Sonix keyboard). Effect numbers and limits follow the Aula app and the
    /// open-source f108-pro tool (github.com/parsiya/f108-pro), which tested them on the keyboard.
    /// </summary>
    [DataContract]
    public sealed class AulaLighting
    {
        [DataMember(Order = 1)] public int Mode;            // 0 off … 19, see Names
        [DataMember(Order = 2)] public int R = 255, G = 255, B = 255;
        [DataMember(Order = 3)] public bool Rainbow;
        [DataMember(Order = 4)] public int Brightness = 5;  // 0-5
        [DataMember(Order = 5)] public int Speed = 3;       // 0-5
        [DataMember(Order = 6)] public int Direction;       // 0 or 1

        [OnDeserializing]
        void SetDefaults(StreamingContext c) { R = G = B = 255; Brightness = 5; Speed = 3; }

        public static readonly string[] Names =
        {
            "Off", "Static", "Light up when pressed", "Go dark when pressed", "Glittering", "Falling", "Colourful", "Breathing",
            "Spectrum", "Outward", "Scrolling", "Rolling", "Rotating", "Explode", "Launch", "Ripples", "Flowing", "Pulsating", "Tilt", "Shuttle",
        };

        /// <summary>The effect takes a colour (or rainbow): all but Off, Colourful and Spectrum.</summary>
        public static bool HasColour(int mode) => mode != 0 && mode != 6 && mode != 8;
        public static bool HasSpeed(int mode) => mode >= 2;
        /// <summary>0 none, 1 left/right (Scrolling), 2 up/down (Rolling, Rotating, Flowing, Tilt).</summary>
        public static int DirectionKind(int mode) => mode == 10 ? 1 : mode == 11 || mode == 12 || mode == 16 || mode == 18 ? 2 : 0;

        public void Normalize()
        {
            Mode = Math.Max(0, Math.Min(19, Mode));
            R = Clamp(R); G = Clamp(G); B = Clamp(B);
            Brightness = Math.Max(0, Math.Min(5, Brightness));
            Speed = Math.Max(0, Math.Min(5, Speed));
            Direction = Direction == 1 ? 1 : 0;
        }

        static int Clamp(int v) => Math.Max(0, Math.Min(255, v));
        public AulaLighting Clone() => (AulaLighting)MemberwiseClone();
        public string Describe() => Names[Math.Max(0, Math.Min(19, Mode))] + (Mode == 0 ? "" : $", brightness {Brightness}" + (HasColour(Mode) ? (Rainbow ? ", rainbow" : $", #{R:X2}{G:X2}{B:X2}") : ""));
    }

    [DataContract]
    public sealed class AppSettings
    {
        [DataMember(Order = 1)] public int Version = 1;
        [DataMember(Order = 2)] public List<Profile> Profiles = new List<Profile>();
        [DataMember(Order = 3)] public int MouseDpi;                  // 0 = leave the mouse alone
        [DataMember(Order = 4)] public string FnMode = "unchanged";   // unchanged | media | standard
        [DataMember(Order = 5)] public bool Paused;
        [DataMember(Order = 6)] public int LowBatteryPercent = 15;
        [DataMember(Order = 7)] public bool Notifications = true;
        [DataMember(Order = 8)] public bool FirstRunDone;
        [DataMember(Order = 9)] public int ReverseScrollPid;           // set while we have reversed a mouse's wheel in Windows
        [DataMember(Order = 10)] public bool ShowPopups = true;
        [DataMember(Order = 11)] public bool SetupDone;
        [DataMember(Order = 12)] public bool LockKeyPopups = true;     // pop-up when Caps Lock / Num Lock / Scroll Lock changes
        [DataMember(Order = 13)] public bool SmoothScroll;             // turn each wheel notch into a short glide
        [DataMember(Order = 14)] public int DisabledKeys = -1;        // keys the keyboard switches off itself (bit mask, see HidppDevice); -1 = leave alone
        [DataMember(Order = 15)] public AulaLighting AulaLight;        // Aula F108 Pro backlight last chosen here; null = never set from LiteOptions
        [DataMember(Order = 16)] public bool AulaClockSync = true;     // set the Aula's screen clock whenever it is plugged in by cable

        // DataContract skips field initialisers, so set defaults for members missing from the file.
        [OnDeserializing]
        void SetDefaults(StreamingContext c)
        {
            Version = 1; Profiles = new List<Profile>(); FnMode = "unchanged"; LowBatteryPercent = 15; Notifications = true; ShowPopups = true; LockKeyPopups = true; DisabledKeys = -1; AulaClockSync = true;
        }

        public Profile Global => Profiles.First(p => p.IsGlobal);
        public Profile ForApp(string exe) => Profiles.FirstOrDefault(p => !p.IsGlobal && p.App == exe);

        public void Normalize()
        {
            if (Profiles == null) Profiles = new List<Profile>();
            Profiles.RemoveAll(p => p == null);
            foreach (var p in Profiles) p.Normalize();
            if (!Profiles.Any(p => p.IsGlobal)) Profiles.Insert(0, new Profile());
            // keep the global profile first and drop duplicate app profiles
            var global = Profiles.First(p => p.IsGlobal);
            var apps = Profiles.Where(p => !p.IsGlobal).GroupBy(p => p.App).Select(g => g.First()).OrderBy(p => p.App).ToList();
            Profiles = new List<Profile> { global };
            Profiles.AddRange(apps);
            global.Normalize();
            if (Version < 2)
            {
                // v1.2: Ctrl + Esc switches the K380's top row (the keyboard has no Fn-lock of its own)
                if (!global.Keys.Any(k => k.Mods == (int)Mods.Ctrl && k.Vk == 0x1B))
                    global.Keys.Add(new KeyBinding { Mods = (int)Mods.Ctrl, Vk = 0x1B, Action = ActionSpec.Of("fn_toggle") });
                Version = 2;
            }
            if (FnMode != "media" && FnMode != "standard") FnMode = "unchanged";
            if (MouseDpi < 0) MouseDpi = 0;
            if (LowBatteryPercent < 0 || LowBatteryPercent > 50) LowBatteryPercent = 15;
            if (DisabledKeys < -1 || DisabledKeys > 0xFF) DisabledKeys = -1;
            AulaLight?.Normalize();
        }

        public AppSettings Clone()
        {
            var s = Serialize(this);
            return Deserialize(s);
        }

        // ── persistence ──
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteOptions");
        public static string FilePath => Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = Deserialize(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (s != null) { s.Normalize(); return s; }
                }
            }
            catch (Exception ex)
            {
                try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
                Logger.Write("settings load failed: " + ex.Message);
            }
            var fresh = new AppSettings();
            fresh.Normalize();
            return fresh;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Serialize(this), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Logger.Write("settings save failed: " + ex.Message); }
        }

        public static string Serialize(AppSettings s)
        {
            var ser = new DataContractJsonSerializer(typeof(AppSettings));
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    ser.WriteObject(w, s);
                    w.Flush();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static AppSettings Deserialize(string json)
        {
            var ser = new DataContractJsonSerializer(typeof(AppSettings));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var s = (AppSettings)ser.ReadObject(ms);
                return s;
            }
        }
    }

    /// <summary>Tiny rolling log file + in-memory ring for the diagnostics view.</summary>
    public static class Logger
    {
        static readonly object gate = new object();
        static readonly LinkedList<string> ring = new LinkedList<string>();
        public static event Action<string> Line;

        public static void Write(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            lock (gate)
            {
                ring.AddLast(line);
                while (ring.Count > 400) ring.RemoveFirst();
                try
                {
                    Directory.CreateDirectory(AppSettings.Folder);
                    string path = Path.Combine(AppSettings.Folder, "log.txt");
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 256 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd ") + line + Environment.NewLine);
                }
                catch { }
            }
            try { Line?.Invoke(line); } catch { }
        }

        public static string Snapshot()
        {
            lock (gate) return string.Join(Environment.NewLine, ring);
        }
    }
}
