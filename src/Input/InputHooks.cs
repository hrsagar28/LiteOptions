using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace LiteOptions.Input
{
    /// <summary>Immutable snapshot of the remapping rules the hooks read (swapped atomically).</summary>
    public sealed class RemapConfig
    {
        public sealed class Middle
        {
            public string Mode; // default | action | gestures
            public ActionSpec Action, Click, Up, Down, Left, Right;
        }

        public Middle GlobalMiddle;
        public Dictionary<string, Middle> AppMiddle = new Dictionary<string, Middle>();
        public Dictionary<int, ActionSpec> GlobalKeys = new Dictionary<int, ActionSpec>();
        public Dictionary<string, Dictionary<int, ActionSpec>> AppKeys = new Dictionary<string, Dictionary<int, ActionSpec>>();
        public bool Paused, SmoothScroll;

        public static RemapConfig From(AppSettings s)
        {
            var c = new RemapConfig { Paused = s.Paused, SmoothScroll = s.SmoothScroll };
            c.GlobalMiddle = MiddleOf(s.Global);
            foreach (var b in s.Global.Keys) c.GlobalKeys[b.TriggerId] = b.Action;
            foreach (var p in s.Profiles.Where(p => !p.IsGlobal))
            {
                if (p.MiddleMode != "inherit") c.AppMiddle[p.App] = MiddleOf(p);
                if (p.Keys.Count > 0) c.AppKeys[p.App] = p.Keys.ToDictionary(k => k.TriggerId, k => k.Action);
            }
            return c;
        }

        static Middle MiddleOf(Profile p) => new Middle
        {
            Mode = p.MiddleMode, Action = p.MiddleAction, Click = p.GestureClick,
            Up = p.GestureUp, Down = p.GestureDown, Left = p.GestureLeft, Right = p.GestureRight
        };

        public Middle MiddleFor(string app) =>
            app != null && AppMiddle.TryGetValue(app, out var m) ? m : GlobalMiddle;

        public ActionSpec KeyFor(string app, int trigger)
        {
            if (app != null && AppKeys.TryGetValue(app, out var map) && map.TryGetValue(trigger, out var a)) return a;
            return GlobalKeys.TryGetValue(trigger, out var g) ? g : null;
        }
    }

    /// <summary>
    /// Low-level mouse and keyboard hooks on a dedicated thread with its own message loop.
    /// Callbacks only decide pass/swallow and queue work, so they stay well inside Windows' hook timeout.
    /// </summary>
    public sealed class InputHooks : IDisposable
    {
        const uint MSG_REHOOK = Native.WM_APP + 1;
        const int GestureThreshold = 45;

        Thread thread;
        uint threadId;
        IntPtr mouseHook, keyHook, winEventHook;
        readonly Native.LowLevelProc mouseProc, keyProc;
        readonly Native.WinEventProc winEventProc;
        readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);

        volatile RemapConfig config = new RemapConfig { GlobalMiddle = new RemapConfig.Middle { Mode = "default" } };
        public string ForegroundApp { get; internal set; }

        // mouse state (hook thread only)
        internal readonly SmoothScroller Smooth = new SmoothScroller();
        bool middleSwallowed;
        bool gestureActive;
        RemapConfig.Middle gestureCfg;
        int anchorX, anchorY, gestDx, gestDy;
        uint gestureStart;

        // keyboard state (hook thread only)
        Mods physicalMods;
        readonly HashSet<int> physicalModVks = new HashSet<int>();
        readonly HashSet<int> swallowedKeys = new HashSet<int>();
        readonly Dictionary<int, ActionSpec> swallowedActions = new Dictionary<int, ActionSpec>();
        readonly HashSet<int> releasedByUs = new HashSet<int>();

        // key capture for the settings UI
        volatile Action<int, int> captureCallback;
        bool captureDone;
        readonly HashSet<int> captureHeld = new HashSet<int>();

        /// <summary>Raised on the hook thread after a gesture: direction ("up", "down", "left", "right") and the action run.</summary>
        public event Action<string, ActionSpec> Gesture;

        /// <summary>Raised on the hook thread when Caps Lock, Num Lock or Scroll Lock is released (and wasn't remapped).</summary>
        public event Action<int> LockKey;

        public InputHooks()
        {
            mouseProc = MouseHookProc;
            keyProc = KeyHookProc;
            winEventProc = OnForegroundChanged;
        }

        public void Start()
        {
            thread = new Thread(ThreadMain) { IsBackground = true, Name = "input-hooks", Priority = ThreadPriority.Highest };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait(3000);
        }

        public void Update(AppSettings s) => config = RemapConfig.From(s);

        /// <summary>Capture the next key or combo (Esc cancels). Callback gets (mods, vk), vk = 0 on cancel.</summary>
        public void BeginCapture(Action<int, int> callback)
        {
            captureDone = false;
            captureHeld.Clear();
            captureCallback = callback;
        }

        public void CancelCapture() => captureCallback = null;

        /// <summary>Re-install hooks (Windows silently drops hooks that time out, e.g. after heavy load or resume).</summary>
        public void Rehook()
        {
            if (threadId != 0) Native.PostThreadMessage(threadId, MSG_REHOOK, IntPtr.Zero, IntPtr.Zero);
        }

        void ThreadMain()
        {
            threadId = Native.GetCurrentThreadId();
            InstallHooks();
            winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            OnForegroundChanged(IntPtr.Zero, 0, Native.GetForegroundWindow(), 0, 0, 0, 0);
            // refresh hooks every 10 minutes as a safety net
            Native.SetTimer(IntPtr.Zero, UIntPtr.Zero, 10 * 60 * 1000, IntPtr.Zero);
            ready.Set();

            while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == MSG_REHOOK || (msg.message == Native.WM_TIMER && msg.hwnd == IntPtr.Zero))
                {
                    InstallHooks();
                    continue;
                }
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            RemoveHooks();
            if (winEventHook != IntPtr.Zero) Native.UnhookWinEvent(winEventHook);
        }

        void InstallHooks()
        {
            RemoveHooks();
            IntPtr hMod = Native.GetModuleHandle(null);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, hMod, 0);
            keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, hMod, 0);
            if (mouseHook == IntPtr.Zero || keyHook == IntPtr.Zero)
                Logger.Write("Warning: could not install input hooks (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
            // releases may have been missed while unhooked; start clean rather than eat a later key press
            middleSwallowed = false; gestureActive = false;
            swallowedKeys.Clear(); swallowedActions.Clear(); releasedByUs.Clear();
        }

        void RemoveHooks()
        {
            if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            if (keyHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; }
        }

        void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return;
                ForegroundApp = ExeName(pid); // cheap; resolved fresh so a reused process id can't mislead us
            }
            catch (Exception ex) { Logger.Write("foreground tracking: " + ex.Message); }
        }

        public static string ExePath(uint pid)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(h); }
        }

        public static string ExeName(uint pid)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (!Native.QueryFullProcessImageName(h, 0, sb, ref size)) return null;
                return System.IO.Path.GetFileName(sb.ToString()).ToLowerInvariant();
            }
            finally { Native.CloseHandle(h); }
        }

        // ───────────────────────── mouse ─────────────────────────

        unsafe IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int msg = (int)wParam;
                    var info = (Native.MSLLHOOKSTRUCT*)lParam;
                    bool injected = (info->flags & Native.LLMHF_INJECTED) != 0;
                    int wheel = msg == Native.WM_MOUSEWHEEL ? (short)(info->mouseData >> 16) : 0;
                    if (!injected && HandleMouse(msg, info->pt.X, info->pt.Y, info->time, wheel))
                        return (IntPtr)1;
                }
                catch (Exception ex) { Logger.Write("mouse hook: " + ex.Message); }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        /// <summary>Returns true to swallow the event.</summary>
        internal bool HandleMouse(int msg, int x, int y, uint time, int wheel = 0)
        {
            if (msg == Native.WM_MOUSEWHEEL)
            {
                // Smooth scrolling: only whole clicks of a notched wheel (touchpads and free-spinning wheels already send
                // small steps), and not with Ctrl/Shift/Alt held, where apps zoom or scroll sideways once per message.
                var c = config;
                if (!c.SmoothScroll || c.Paused || wheel == 0 || wheel % 120 != 0 || physicalMods != Mods.None) return false;
                Smooth.Add(wheel);
                return true;
            }

            if (msg == Native.WM_MOUSEMOVE)
            {
                if (!gestureActive) return false;
                if (time - gestureStart > 15000) { gestureActive = false; middleSwallowed = false; return false; } // safety
                // pointer is frozen at the anchor while a gesture is held, so each event is a fresh delta
                gestDx += x - anchorX;
                gestDy += y - anchorY;
                return true;
            }

            if (msg == Native.WM_MBUTTONDOWN || msg == Native.WM_MBUTTONDBLCLK)
            {
                var cfg = config;
                if (cfg.Paused) return false;
                var m = cfg.MiddleFor(ForegroundApp);
                if (m == null || m.Mode == "default") return false;
                if (m.Mode == "action")
                {
                    if (m.Action == null || m.Action.IsDefault || (m.Action.Kind == "preset" && m.Action.Preset == "middle_click")) return false;
                    middleSwallowed = true;
                    ActionRunner.Run(m.Action);
                    return true;
                }
                if (m.Mode == "gestures")
                {
                    middleSwallowed = true;
                    gestureActive = true;
                    gestureCfg = m;
                    anchorX = x; anchorY = y; gestDx = 0; gestDy = 0;
                    gestureStart = time;
                    return true;
                }
                return false;
            }

            if (msg == Native.WM_MBUTTONUP)
            {
                if (!middleSwallowed) return false;
                middleSwallowed = false;
                if (gestureActive)
                {
                    gestureActive = false;
                    var g = gestureCfg;
                    ActionSpec a;
                    string dir = null;
                    if (Math.Abs(gestDx) < GestureThreshold && Math.Abs(gestDy) < GestureThreshold) a = g.Click;
                    else if (Math.Abs(gestDx) >= Math.Abs(gestDy)) { dir = gestDx > 0 ? "right" : "left"; a = gestDx > 0 ? g.Right : g.Left; }
                    else { dir = gestDy > 0 ? "down" : "up"; a = gestDy > 0 ? g.Down : g.Up; }
                    if (dir != null && a != null && !a.IsDefault) { try { Gesture?.Invoke(dir, a); } catch { } }
                    if (a != null && !a.IsDefault) ActionRunner.Run(a);
                    else if (ReferenceEquals(a, g.Click)) ActionRunner.Run(ActionSpec.Of("middle_click")); // plain click keeps its normal job
                }
                return true;
            }
            return false;
        }

        // ───────────────────────── keyboard ─────────────────────────

        unsafe IntPtr KeyHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var info = (Native.KBDLLHOOKSTRUCT*)lParam;
                    bool injected = (info->flags & Native.LLKHF_INJECTED) != 0;
                    if (!injected)
                    {
                        int msg = (int)wParam;
                        bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                        int vk = (int)info->vkCode;
                        if (HandleKey(vk, down))
                            return (IntPtr)1;
                        if (!down && (vk == Vk.CapsLock || vk == Vk.NumLock || vk == Vk.ScrollLock))
                            try { LockKey?.Invoke(vk); } catch { }
                    }
                }
                catch (Exception ex) { Logger.Write("keyboard hook: " + ex.Message); }
            }
            return Native.CallNextHookEx(keyHook, nCode, wParam, lParam);
        }

        internal bool HandleKey(int vk, bool down)
        {
            // keep an accurate picture of physically held modifiers
            var mod = KeyNames.ModOf(vk);
            if (mod != Mods.None)
            {
                if (down) physicalModVks.Add(vk); else physicalModVks.Remove(vk);
                physicalMods = Mods.None;
                foreach (var m in physicalModVks) physicalMods |= KeyNames.ModOf(m);
            }

            if (captureCallback != null) return HandleCapture(vk, down, mod);

            if (mod != Mods.None)
            {
                // modifiers we lifted for an action: hide their repeats and their eventual release
                if (releasedByUs.Contains(vk))
                {
                    if (!down) releasedByUs.Remove(vk);
                    return true;
                }
                return false;
            }

            if (!down)
            {
                if (swallowedKeys.Remove(vk)) { swallowedActions.Remove(vk); return true; }
                return false;
            }

            // key down (or auto-repeat)
            if (swallowedKeys.Contains(vk))
            {
                if (swallowedActions.TryGetValue(vk, out var held) && Presets.Repeats(held)) ActionRunner.Run(held);
                return true;
            }

            var cfg = config;
            int trigger = ((int)physicalMods << 16) | vk;
            var action = cfg.KeyFor(ForegroundApp, trigger);
            if (action == null || action.IsDefault) return false;
            if (cfg.Paused && !(action.Kind == "preset" && action.Preset == "pause_toggle")) return false; // the pause key itself keeps working

            swallowedKeys.Add(vk);
            swallowedActions[vk] = action;

            // lift the modifiers that were part of the trigger so they don't mix into the action
            List<int> release = null;
            bool mask = false;
            if (physicalModVks.Count > 0)
            {
                release = physicalModVks.Where(m => !releasedByUs.Contains(m)).ToList();
                foreach (var m in release) releasedByUs.Add(m);
                mask = physicalModVks.Any(m => m == Vk.LWin || m == Vk.RWin || m == Vk.LMenu || m == Vk.RMenu);
            }
            ActionRunner.Run(action, release, mask);
            return true;
        }

        bool HandleCapture(int vk, bool down, Mods mod)
        {
            if (down) captureHeld.Add(vk); else captureHeld.Remove(vk);

            if (!captureDone)
            {
                if (mod != Mods.None) return true;
                if (down)
                {
                    var cb = captureCallback;
                    int mods = (int)physicalMods;
                    captureDone = true;
                    int outMods = vk == Vk.Escape && mods == 0 ? 0 : mods, outVk = vk == Vk.Escape && mods == 0 ? 0 : vk;
                    ThreadPool.QueueUserWorkItem(_ => { try { cb?.Invoke(outMods, outVk); } catch (Exception ex) { Logger.Write("capture callback: " + ex.Message); } });
                }
                return true;
            }

            // after capturing, keep swallowing until everything is released
            if (captureHeld.Count == 0) captureCallback = null;
            return true;
        }

        public void Dispose()
        {
            if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread?.Join(1000);
            Smooth.Dispose();
        }
    }
}
