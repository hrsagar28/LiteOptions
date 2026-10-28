using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace LiteOptions.Input
{
    public sealed class Preset
    {
        public string Id, Label, Group;
        public int Mods, Vk;           // keyboard shortcut presets
        public string Mouse;           // "middle" | "back" | "forward" | "left" | "right" | "double"
        public string Special;         // "lock" | "calc"
        public bool Repeats;           // auto-repeat while a key is held
    }

    public static class Presets
    {
        const int C = (int)Mods.Ctrl, S = (int)Mods.Shift, A = (int)Mods.Alt, W = (int)Mods.Win;

        public static readonly List<Preset> All = new List<Preset>
        {
            P("middle_click", "Middle click", "Mouse", mouse: "middle"),
            P("back", "Back", "Mouse", mouse: "back"),
            P("forward", "Forward", "Mouse", mouse: "forward"),
            P("double_click", "Double click", "Mouse", mouse: "double"),
            P("right_click", "Right click", "Mouse", mouse: "right"),

            P("task_view", "Task View", "Windows", W, Vk.Tab),
            P("show_desktop", "Show desktop", "Windows", W, Vk.D),
            P("desktop_left", "Previous virtual desktop", "Windows", W | C, Vk.Left),
            P("desktop_right", "Next virtual desktop", "Windows", W | C, Vk.Right),
            P("switch_app", "Switch to last app (Alt+Tab)", "Windows", A, Vk.Tab),
            P("snip", "Screenshot / Snipping Tool", "Windows", W | S, Vk.S),
            P("emoji", "Emoji panel", "Windows", W, Vk.Period),
            P("dictation", "Voice typing", "Windows", W, Vk.H),
            P("clipboard", "Clipboard history", "Windows", W, Vk.V),
            P("search", "Windows search", "Windows", W, Vk.S),
            P("explorer", "File Explorer", "Windows", W, Vk.E),
            P("notifications", "Notification centre", "Windows", W, Vk.N),
            P("minimize", "Minimise window", "Windows", W, Vk.Down),
            P("maximize", "Maximise window", "Windows", W, Vk.Up),
            P("close_window", "Close window (Alt+F4)", "Windows", A, Vk.F4),
            P("lock", "Lock PC", "Windows", special: "lock"),
            P("calc", "Calculator", "Windows", special: "calc"),

            P("fn_toggle", "Switch top row (media keys / F1–F12)", "LiteOptions", special: "app:fn_toggle"),
            P("pause_toggle", "Pause or resume remapping", "LiteOptions", special: "app:pause_toggle"),
            P("open_app", "Open LiteOptions", "LiteOptions", special: "app:open"),

            P("copy", "Copy", "Editing", C, Vk.C),
            P("cut", "Cut", "Editing", C, Vk.X),
            P("paste", "Paste", "Editing", C, Vk.V),
            P("paste_plain", "Paste as plain text", "Editing", C | S, Vk.V),
            P("undo", "Undo", "Editing", C, Vk.Z),
            P("redo", "Redo", "Editing", C, Vk.Y),
            P("select_all", "Select all", "Editing", C, Vk.A),
            P("save", "Save", "Editing", C, Vk.S),
            P("find", "Find", "Editing", C, Vk.F),
            P("print", "Print", "Editing", C, Vk.P),

            P("new_tab", "New tab", "Browser & tabs", C, Vk.T),
            P("close_tab", "Close tab", "Browser & tabs", C, Vk.W),
            P("reopen_tab", "Reopen closed tab", "Browser & tabs", C | S, Vk.T),
            P("next_tab", "Next tab", "Browser & tabs", C, Vk.Tab),
            P("prev_tab", "Previous tab", "Browser & tabs", C | S, Vk.Tab),
            P("refresh", "Refresh", "Browser & tabs", 0, Vk.F5),
            P("zoom_in", "Zoom in", "Browser & tabs", C, Vk.OemPlus, repeats: true),
            P("zoom_out", "Zoom out", "Browser & tabs", C, Vk.OemMinus, repeats: true),
            P("zoom_reset", "Reset zoom", "Browser & tabs", C, Vk.D0),

            P("play_pause", "Play / Pause", "Media", 0, Vk.MediaPlayPause),
            P("next_track", "Next track", "Media", 0, Vk.MediaNext),
            P("prev_track", "Previous track", "Media", 0, Vk.MediaPrev),
            P("stop", "Stop", "Media", 0, Vk.MediaStop),
            P("mute", "Mute", "Media", 0, Vk.VolumeMute),
            P("vol_up", "Volume up", "Media", 0, Vk.VolumeUp, repeats: true),
            P("vol_down", "Volume down", "Media", 0, Vk.VolumeDown, repeats: true),
        };

        static readonly Dictionary<string, Preset> byId = All.ToDictionary(p => p.Id);
        public static Preset Get(string id) => id != null && byId.TryGetValue(id, out var p) ? p : null;

        static Preset P(string id, string label, string group, int mods = 0, int vk = 0, string mouse = null, string special = null, bool repeats = false) =>
            new Preset { Id = id, Label = label, Group = group, Mods = mods, Vk = vk, Mouse = mouse, Special = special, Repeats = repeats };

        public static string Describe(ActionSpec a, string defaultLabel = "Default")
        {
            if (a == null || a.IsDefault) return defaultLabel;
            switch (a.Kind)
            {
                case "none": return "Do nothing";
                case "preset": return Get(a.Preset)?.Label ?? a.Preset;
                case "keys": return "Shortcut: " + KeyNames.Combo(a.Mods, a.Vk);
                case "launch": return "Open: " + ShortTarget(a.Target);
                case "text": return "Type: \"" + (a.Text != null && a.Text.Length > 24 ? a.Text.Substring(0, 24) + "…" : a.Text) + "\"";
            }
            return defaultLabel;
        }

        static string ShortTarget(string t)
        {
            if (string.IsNullOrEmpty(t)) return "?";
            if (t.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return t.Length > 40 ? t.Substring(0, 40) + "…" : t;
            try { return System.IO.Path.GetFileName(t.TrimEnd('\\')) is string f && f.Length > 0 ? f : t; } catch { return t; }
        }

        public static bool Repeats(ActionSpec a)
        {
            if (a == null) return false;
            if (a.Kind == "preset") return Get(a.Preset)?.Repeats ?? false;
            if (a.Kind == "keys") return a.Mods == 0 || KeyNames.IsArrowOrNav(a.Vk);
            return false;
        }
    }

    public static class Vk
    {
        public const int Back = 0x08, Tab = 0x09, Enter = 0x0D, Shift = 0x10, Control = 0x11, Menu = 0x12, Pause = 0x13, CapsLock = 0x14,
            Escape = 0x1B, Space = 0x20, PageUp = 0x21, PageDown = 0x22, End = 0x23, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28,
            PrintScreen = 0x2C, Insert = 0x2D, Delete = 0x2E, D0 = 0x30, A = 0x41, C = 0x43, D = 0x44, E = 0x45, F = 0x46, H = 0x48, N = 0x4E,
            P = 0x50, S = 0x53, T = 0x54, V = 0x56, W = 0x57, X = 0x58, Y = 0x59, Z = 0x5A,
            LWin = 0x5B, RWin = 0x5C, Apps = 0x5D, F1 = 0x70, F4 = 0x73, F5 = 0x74, F24 = 0x87, NumLock = 0x90, ScrollLock = 0x91,
            LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3, LMenu = 0xA4, RMenu = 0xA5,
            BrowserBack = 0xA6, BrowserForward = 0xA7, BrowserRefresh = 0xA8, BrowserStop = 0xA9, BrowserSearch = 0xAA, BrowserFavorites = 0xAB, BrowserHome = 0xAC,
            VolumeMute = 0xAD, VolumeDown = 0xAE, VolumeUp = 0xAF, MediaNext = 0xB0, MediaPrev = 0xB1, MediaStop = 0xB2, MediaPlayPause = 0xB3,
            LaunchMail = 0xB4, LaunchMedia = 0xB5, LaunchApp1 = 0xB6, LaunchApp2 = 0xB7,
            OemPlus = 0xBB, OemComma = 0xBC, OemMinus = 0xBD, Period = 0xBE, Divide = 0x6F,
            MaskKey = 0xE8; // unassigned; used to stop a lone Win/Alt release from opening Start / the menu bar
    }

    public static class KeyNames
    {
        static readonly Dictionary<int, string> names = new Dictionary<int, string>
        {
            {0x08,"Backspace"},{0x09,"Tab"},{0x0D,"Enter"},{0x13,"Pause"},{0x14,"Caps Lock"},{0x1B,"Esc"},{0x20,"Space"},
            {0x21,"Page Up"},{0x22,"Page Down"},{0x23,"End"},{0x24,"Home"},{0x25,"Left"},{0x26,"Up"},{0x27,"Right"},{0x28,"Down"},
            {0x2C,"Print Screen"},{0x2D,"Insert"},{0x2E,"Delete"},{0x5B,"Win"},{0x5C,"Right Win"},{0x5D,"Menu key"},
            {0x60,"Num 0"},{0x61,"Num 1"},{0x62,"Num 2"},{0x63,"Num 3"},{0x64,"Num 4"},{0x65,"Num 5"},{0x66,"Num 6"},{0x67,"Num 7"},{0x68,"Num 8"},{0x69,"Num 9"},
            {0x6A,"Num *"},{0x6B,"Num +"},{0x6D,"Num -"},{0x6E,"Num ."},{0x6F,"Num /"},{0x90,"Num Lock"},{0x91,"Scroll Lock"},
            {0xA6,"Browser Back"},{0xA7,"Browser Forward"},{0xA8,"Browser Refresh"},{0xA9,"Browser Stop"},{0xAA,"Browser Search"},{0xAB,"Favorites"},{0xAC,"Browser Home"},
            {0xAD,"Mute"},{0xAE,"Volume Down"},{0xAF,"Volume Up"},{0xB0,"Next Track"},{0xB1,"Previous Track"},{0xB2,"Stop Media"},{0xB3,"Play/Pause"},
            {0xB4,"Mail"},{0xB5,"Media Select"},{0xB6,"App 1 (This PC)"},{0xB7,"App 2 (Calculator)"},
            {0xBA,";"},{0xBB,"="},{0xBC,","},{0xBD,"-"},{0xBE,"."},{0xBF,"/"},{0xC0,"`"},{0xDB,"["},{0xDC,"\\"},{0xDD,"]"},{0xDE,"'"},
            {0xA0,"Shift"},{0xA1,"Right Shift"},{0xA2,"Ctrl"},{0xA3,"Right Ctrl"},{0xA4,"Alt"},{0xA5,"Right Alt"},
        };

        public static string Key(int vk)
        {
            if (names.TryGetValue(vk, out var n)) return n;
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            return "Key 0x" + vk.ToString("X2");
        }

        public static string Combo(int mods, int vk)
        {
            var parts = new List<string>();
            if ((mods & (int)Mods.Ctrl) != 0) parts.Add("Ctrl");
            if ((mods & (int)Mods.Shift) != 0) parts.Add("Shift");
            if ((mods & (int)Mods.Alt) != 0) parts.Add("Alt");
            if ((mods & (int)Mods.Win) != 0) parts.Add("Win");
            parts.Add(Key(vk));
            return string.Join(" + ", parts);
        }

        public static bool IsModifier(int vk) =>
            vk == Vk.LShift || vk == Vk.RShift || vk == Vk.LControl || vk == Vk.RControl || vk == Vk.LMenu || vk == Vk.RMenu ||
            vk == Vk.LWin || vk == Vk.RWin || vk == Vk.Shift || vk == Vk.Control || vk == Vk.Menu;

        public static Mods ModOf(int vk)
        {
            switch (vk)
            {
                case Vk.LShift: case Vk.RShift: case Vk.Shift: return Mods.Shift;
                case Vk.LControl: case Vk.RControl: case Vk.Control: return Mods.Ctrl;
                case Vk.LMenu: case Vk.RMenu: case Vk.Menu: return Mods.Alt;
                case Vk.LWin: case Vk.RWin: return Mods.Win;
            }
            return Mods.None;
        }

        public static bool IsArrowOrNav(int vk) => vk >= 0x21 && vk <= 0x28;

        /// <summary>Keys that need KEYEVENTF_EXTENDEDKEY when injected.</summary>
        public static bool IsExtended(int vk) =>
            (vk >= 0x21 && vk <= 0x2E) || vk == Vk.LWin || vk == Vk.RWin || vk == Vk.Apps || vk == Vk.RControl || vk == Vk.RMenu ||
            vk == Vk.Divide || vk == Vk.NumLock || vk == Vk.PrintScreen || (vk >= 0xA6 && vk <= 0xB7);
    }

    /// <summary>Runs actions on a dedicated thread so the input hooks return immediately.</summary>
    public static class ActionRunner
    {
        public static readonly IntPtr Signature = new IntPtr(0x4C4F5054); // "LOPT"
        static readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();

        /// <summary>Commands handled by the app itself (switch top row, pause, open window). Raised on the action thread.</summary>
        public static Action<string> AppCommand;
        static Thread thread;

        public static void Start()
        {
            if (thread != null) return;
            thread = new Thread(() =>
            {
                foreach (var a in queue.GetConsumingEnumerable())
                {
                    try { a(); } catch (Exception ex) { Logger.Write("action failed: " + ex.Message); }
                }
            }) { IsBackground = true, Name = "actions" };
            thread.Start();
        }

        public static void Enqueue(Action a) { try { queue.Add(a); } catch { } }

        /// <summary>Run an action. releaseVks: physically-held modifiers to lift first; mask: send the mask key first.</summary>
        /// <summary>Tests capture actions here instead of injecting input.</summary>
        internal static Action<ActionSpec, IList<int>, bool> TestSink;

        public static void Run(ActionSpec a, IList<int> releaseVks = null, bool mask = false)
        {
            if (TestSink != null) { TestSink(a, releaseVks, mask); return; }
            Enqueue(() =>
            {
                var inputs = new List<Native.INPUT>();
                if (mask) { Key(inputs, Vk.MaskKey, false); Key(inputs, Vk.MaskKey, true); }
                if (releaseVks != null) foreach (var vk in releaseVks) Key(inputs, vk, true);
                if (inputs.Count > 0) Send(inputs);
                Execute(a);
            });
        }

        static void Execute(ActionSpec a)
        {
            if (a == null || a.IsDefault || a.Kind == "none") return;
            switch (a.Kind)
            {
                case "preset":
                    var p = Presets.Get(a.Preset);
                    if (p == null) return;
                    if (p.Mouse != null) MouseClick(p.Mouse);
                    else if (p.Special != null && p.Special.StartsWith("app:")) AppCommand?.Invoke(p.Special.Substring(4));
                    else if (p.Special == "lock") Native.LockWorkStation();
                    else if (p.Special == "calc") Launch("calc.exe", null);
                    else Combo(p.Mods, p.Vk);
                    break;
                case "keys": Combo(a.Mods, a.Vk); break;
                case "launch": Launch(a.Target, a.Args); break;
                case "text": TypeText(a.Text); break;
            }
        }

        public static void Combo(int mods, int vk)
        {
            var inputs = new List<Native.INPUT>();
            var modVks = new List<int>();
            if ((mods & (int)Mods.Ctrl) != 0) modVks.Add(Vk.LControl);
            if ((mods & (int)Mods.Shift) != 0) modVks.Add(Vk.LShift);
            if ((mods & (int)Mods.Alt) != 0) modVks.Add(Vk.LMenu);
            if ((mods & (int)Mods.Win) != 0) modVks.Add(Vk.LWin);
            foreach (var m in modVks) Key(inputs, m, false);
            if (vk != 0) { Key(inputs, vk, false); Key(inputs, vk, true); }
            for (int i = modVks.Count - 1; i >= 0; i--) Key(inputs, modVks[i], true);
            Send(inputs);
        }

        static void Key(List<Native.INPUT> list, int vk, bool up)
        {
            uint flags = up ? Native.KEYEVENTF_KEYUP : 0;
            if (KeyNames.IsExtended(vk)) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            ushort scan = vk == Vk.MaskKey ? (ushort)0 : (ushort)Native.MapVirtualKey((uint)vk, 0);
            list.Add(new Native.INPUT
            {
                type = Native.INPUT_KEYBOARD,
                U = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = (ushort)vk, wScan = scan, dwFlags = flags, dwExtraInfo = Signature } }
            });
        }

        static void MouseClick(string which)
        {
            var list = new List<Native.INPUT>();
            void Btn(uint down, uint up, uint data = 0)
            {
                list.Add(new Native.INPUT { type = Native.INPUT_MOUSE, U = new Native.InputUnion { mi = new Native.MOUSEINPUT { dwFlags = down, mouseData = data, dwExtraInfo = Signature } } });
                list.Add(new Native.INPUT { type = Native.INPUT_MOUSE, U = new Native.InputUnion { mi = new Native.MOUSEINPUT { dwFlags = up, mouseData = data, dwExtraInfo = Signature } } });
            }
            switch (which)
            {
                case "middle": Btn(Native.MOUSEEVENTF_MIDDLEDOWN, Native.MOUSEEVENTF_MIDDLEUP); break;
                case "right": Btn(Native.MOUSEEVENTF_RIGHTDOWN, Native.MOUSEEVENTF_RIGHTUP); break;
                case "left": Btn(Native.MOUSEEVENTF_LEFTDOWN, Native.MOUSEEVENTF_LEFTUP); break;
                case "double": Btn(Native.MOUSEEVENTF_LEFTDOWN, Native.MOUSEEVENTF_LEFTUP); Btn(Native.MOUSEEVENTF_LEFTDOWN, Native.MOUSEEVENTF_LEFTUP); break;
                case "back": Btn(Native.MOUSEEVENTF_XDOWN, Native.MOUSEEVENTF_XUP, Native.XBUTTON1); break;
                case "forward": Btn(Native.MOUSEEVENTF_XDOWN, Native.MOUSEEVENTF_XUP, Native.XBUTTON2); break;
            }
            Send(list);
        }

        static void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var list = new List<Native.INPUT>();
            foreach (char ch in text.Replace("\r\n", "\n"))
            {
                if (ch == '\n') { Key(list, Vk.Enter, false); Key(list, Vk.Enter, true); continue; }
                foreach (bool up in new[] { false, true })
                    list.Add(new Native.INPUT
                    {
                        type = Native.INPUT_KEYBOARD,
                        U = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0), dwExtraInfo = Signature } }
                    });
            }
            Send(list);
        }

        static void Launch(string target, string args)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            try
            {
                var psi = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'))) { UseShellExecute = true };
                if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args;
                Process.Start(psi)?.Dispose();
            }
            catch (Exception ex) { Logger.Write("could not open " + target + ": " + ex.Message); }
        }

        static void Send(List<Native.INPUT> list)
        {
            if (list.Count == 0) return;
            var arr = list.ToArray();
            uint sent = Native.SendInput((uint)arr.Length, arr, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
            if (sent != arr.Length) Logger.Write($"SendInput sent {sent}/{arr.Length} (blocked by a higher-privilege window?)");
        }
    }
}
