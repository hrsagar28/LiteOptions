using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LiteOptions.Hid;
using LiteOptions.Input;

namespace LiteOptions.Tests
{
    static class TestMain
    {
        static int passed, failed;

        static void Check(bool cond, string what)
        {
            if (cond) { passed++; Console.WriteLine("  ok   " + what); }
            else { failed++; Console.WriteLine("  FAIL " + what); }
        }

        static void Section(string s) => Console.WriteLine("\n== " + s);

        static int Main(string[] args)
        {
            try
            {
                HidppBasics();
                ShortOnlyLink();
                Errors();
                DeviceManagerFlow();
                ReceiverFlow();
                SettingsRoundTrip();
                MouseHookLogic();
                KeyHookLogic();
                CaptureLogic();
                V12();
                Misc();
            }
            catch (Exception ex) { failed++; Console.WriteLine("CRASH " + ex); }
            Console.WriteLine($"\n{passed} passed, {failed} failed");
            return failed == 0 ? 0 : 1;
        }

        static void HidppBasics()
        {
            Section("HID++ against a simulated Pebble Mouse 2 (BLE, long reports only)");
            var dev = FakeDevice.PebbleM350s();
            var link = new FakeLink(canLong: true, canShort: false);
            link.Slots[0xFF] = dev;
            var ch = new HidppChannel(link);
            var d = new HidppDevice(ch, 0xFF);
            Check(d.Ping(500), "ping answers with HID++ 4.x");
            Check(d.ProtocolMajor == 4, "protocol major parsed");
            Check(link.Written.All(w => w[0] == 0x11 && w.Length == 20), "uses 20-byte long reports only");
            Check(d.ReadName() == "Pebble Mouse 2 M350s", "device name read in chunks");
            Check(d.ReadDeviceType() == 3, "device type = mouse");
            Check(d.ReadFirmware() == "RQM 40.02.B0017", "firmware string: " + d.ReadFirmware());
            var b = d.ReadBattery();
            Check(b != null && b.Percent == null && b.Level == "Good" && !b.IsLow, "unified battery level-only → Good: " + b?.Text);
            dev.UnifiedLevel = 0x02;
            Check(d.ReadBattery().IsLow, "unified battery Low flagged");
            var dpi = d.ReadDpi();
            Check(dpi != null && dpi.Min == 400 && dpi.Max == 4000 && dpi.Step == 100 && dpi.Choices.Count == 37, $"DPI range parsed {dpi?.Min}-{dpi?.Max} step {dpi?.Step} ({dpi?.Choices.Count})");
            Check(dpi.Current == 1000 && dpi.Default == 1000, "current/default DPI");
            d.WriteDpi(1600);
            Check(dev.Dpi == 1600 && d.ReadDpi().Current == 1600, "DPI write/readback");
            Check(dpi.Nearest(1649) == 1600 && dpi.Nearest(9999) == 4000, "nearest DPI snapping");
            Check(!d.SupportsFnInversion(), "mouse has no Fn inversion");

            Section("Responses with device index 0x00 and interleaved notifications");
            link.AnswerDirectAsZero = true;
            link.NotifyBeforeReply = true;
            int notes = 0;
            ch.Notification += m => Interlocked.Increment(ref notes);
            Check(d.ReadDeviceType() == 3, "reply with index 0x00 accepted for 0xFF request");
            Thread.Sleep(50);
            Check(notes >= 1, "notification delivered separately, not mistaken for the reply");
        }

        static void ShortOnlyLink()
        {
            Section("K380 behind a short-report-only collection");
            var dev = FakeDevice.K380();
            var link = new FakeLink(canLong: false, canShort: true);
            link.Slots[0xFF] = dev;
            var d = new HidppDevice(new HidppChannel(link), 0xFF);
            Check(d.Ping(500), "ping over short reports");
            Check(link.Written.All(w => w[0] == 0x10 && w.Length == 7), "uses 7-byte short reports");
            Check(d.ReadName() == "Keyboard K380", "name assembled from 3-byte chunks");
            Check(d.SupportsFnInversion() && d.FnFeatureId == Feature.FnInversion, "Fn inversion found (0x40A0 at index 0x0B)");
            Check(d.ReadFnInversion() == true, "factory state = media keys first");
            d.WriteFnInversion(false);
            var w = link.Written.Last();
            Check(w[2] == 0x0B && (w[3] >> 4) == 1 && w[4] == 0x00, "write matches k380_conf 'fkeys on' (10 ff 0b 1x 00)");
            Check(dev.FnInv == false && d.ReadFnInversion() == false, "F1-F12 first after write");
            var b = d.ReadBattery();
            Check(b.Percent == 80, "battery status 0x1000 percent");
        }

        static void Errors()
        {
            Section("Errors and timeouts");
            var dev = FakeDevice.PebbleM350s();
            var link = new FakeLink(true, false);
            link.Slots[0xFF] = dev;
            var d = new HidppDevice(new HidppChannel(link), 0xFF) { Timeout = 300 };
            try { d.Call(Feature.FnInversion, 0); Check(false, "missing feature throws"); }
            catch (HidppException ex) { Check(ex.Code == HidppException.NotSupported, "missing feature → NotSupported"); }
            try { d.Call(Feature.AdjustableDpi, 9); Check(false, "bad function throws"); }
            catch (HidppException ex) { Check(ex.Code == 0x07, "device error code surfaced (0x07)"); }
            dev.Asleep = true;
            var t0 = DateTime.UtcNow;
            try { d.ReadDeviceType(); Check(false, "asleep device times out"); }
            catch (HidppException ex) { Check(ex.IsTimeout && (DateTime.UtcNow - t0).TotalMilliseconds < 1500, "asleep device → timeout, promptly"); }
            Check(!d.Ping(200), "ping false when asleep");
            dev.Asleep = false;
            Check(d.Ping(500), "recovers after waking");
        }

        static AppSettings NewSettings()
        {
            var s = new AppSettings();
            s.Normalize();
            return s;
        }

        static void DeviceManagerFlow()
        {
            Section("Device manager: discovery, applying settings, reconnects");
            var mouse = FakeDevice.PebbleM350s();
            var kb = FakeDevice.K380();
            var src = new FakeSource();
            var mlink = new FakeLink(true, false); mlink.Slots[0xFF] = mouse;
            var klink = new FakeLink(true, true); klink.Slots[0xFF] = kb;
            src.Add(@"BTHLEDEVICE\{00001812}_DEV_VID&02046D_PID&B03E", 0xB03E, mlink, "Pebble Mouse 2 M350s");
            src.Add(@"BTHENUM\{00001124}_VID&0002046D_PID&B342", 0xB342, klink, "Keyboard K380");

            var s = NewSettings();
            s.MouseDpi = 1650;
            s.FnMode = "standard";
            var dm = new DeviceManager(src, () => s);
            dm.StartForTests();
            dm.RunPendingForTests();
            var devs = dm.Snapshot();
            Check(devs.Count == 2, "found 2 devices");
            Check(dm.Mouse?.Name == "Pebble Mouse 2 M350s" && dm.Mouse.Kind == DeviceKind.Mouse, "mouse identified");
            Check(dm.Keyboard?.Name == "Keyboard K380" && dm.Keyboard.Kind == DeviceKind.Keyboard, "keyboard identified");
            Check(mouse.Dpi == 1700 || mouse.Dpi == 1600, "saved DPI applied, snapped to 100 steps: " + mouse.Dpi);
            Check(kb.FnInv == false, "saved F-key mode applied to K380");
            Check(dm.Keyboard.Battery?.Percent == 80, "keyboard battery read");

            Section("Mouse power-cycles (DPI resets) and sends 'reconfigure me'");
            mouse.Dpi = 1000;
            byte ws = mouse.Features[Feature.WirelessStatus];
            mlink.Send(new byte[] { 0x11, 0xFF, ws, 0x00, 0x01, 0x01, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            Thread.Sleep(100);
            dm.RunPendingForTests();
            Check(mouse.Dpi == s.MouseDpi / 100 * 100 || mouse.Dpi == 1700, "DPI re-applied after reconnect: " + mouse.Dpi);

            Section("Keyboard forgets setting without notifying; 5-minute poll fixes it");
            kb.FnInv = true;
            dm.RequestPoll();
            dm.RunPendingForTests();
            Check(kb.FnInv == false, "poll re-applied F-key mode");

            Section("Live DPI changes and Fn toggle from the UI");
            dm.SetDpi(2400);
            dm.SetDpi(3000); // coalesced: only the latest should stick
            dm.RunPendingForTests();
            Check(mouse.Dpi == 3000, "latest DPI wins");
            dm.SetFnMode(true);
            dm.RunPendingForTests();
            Check(kb.FnInv == true && dm.Keyboard.FnMedia == true, "Fn toggle to media keys");

            Section("Device asleep, then unplugged");
            mouse.Asleep = true;
            foreach (var d in dm.Snapshot()) d.Dev.Timeout = 200;
            dm.RequestPoll(); dm.RunPendingForTests();
            dm.RequestPoll(); dm.RunPendingForTests();
            Check(dm.Mouse != null && !dm.Mouse.Online, "mouse marked asleep after 2 failed polls");
            mouse.Asleep = false;
            mouse.Dpi = 1000; // it was switched off and on: settings lost, no notification sent
            dm.RequestReviveForTests(); dm.RunPendingForTests();
            Check(dm.Mouse.Online && mouse.Dpi == 1600, "30-second revive check: back online and DPI restored (" + mouse.Dpi + ")");
            mlink.alive = false;
            dm.RequestPoll(); dm.RunPendingForTests();
            Check(dm.Mouse == null && dm.Keyboard != null, "dead link dropped, keyboard kept");
            Check(src.Opens == 2, "no reopen churn for healthy devices");
            dm.Dispose();

            Section("Keyboard reconnects but ignores the first write; a quick retry fixes it");
            var kb3 = FakeDevice.K380(); kb3.FnInv = true; kb3.DropFnWrites = 1;
            var src3 = new FakeSource(); var l3 = new FakeLink(true, true); l3.Slots[0xFF] = kb3;
            src3.Add(@"BTHENUM\{00001124}_VID&0002046D_PID&B342#3", 0xB342, l3, "Keyboard K380");
            var s3 = NewSettings(); s3.FnMode = "standard";
            var dm3 = new DeviceManager(src3, () => s3) { ApplyRetryDelaysMs = new[] { 50, 50, 50 } };
            dm3.StartForTests(); dm3.RunPendingForTests();
            Check(kb3.FnInv == true && dm3.Keyboard != null, "first write unanswered: keyboard still on media keys");
            Thread.Sleep(400);
            dm3.RunPendingForTests();
            Check(kb3.FnInv == false, "retry a couple of seconds later sets F1–F12 (no 5-minute wait)");
            foreach (var d in dm3.Snapshot()) d.Dev.Timeout = 200;
            kb3.DropFnWrites = 10; kb3.FnInv = true;
            dm3.ApplySettingsNow(); dm3.RunPendingForTests();
            for (int i = 0; i < 4; i++) { Thread.Sleep(150); dm3.RunPendingForTests(); }
            Check(kb3.FnInv == true && dm3.Keyboard.ApplyRetriesForTests == 3, "gives up after 3 retries instead of looping");
            dm3.Dispose();

            Section("Settings 'unchanged' leaves devices alone");
            var mouse2 = FakeDevice.PebbleM350s(); mouse2.Dpi = 800;
            var src2 = new FakeSource(); var l2 = new FakeLink(true, false); l2.Slots[0xFF] = mouse2;
            src2.Add("P2", 0xB03E, l2, "m");
            var s2 = NewSettings();
            var dm2 = new DeviceManager(src2, () => s2);
            dm2.StartForTests(); dm2.RunPendingForTests();
            Check(mouse2.Dpi == 800, "DPI untouched when no DPI saved");
            dm2.Dispose();
        }

        static void ReceiverFlow()
        {
            Section("Logi Bolt receiver with the mouse in slot 2");
            var mouse = FakeDevice.PebbleM350s();
            var link = new FakeLink(true, true, receiver: true);
            link.Slots[2] = mouse;
            var src = new FakeSource();
            src.Add(@"USB\VID_046D&PID_C548&MI_02", 0xC548, link, "USB Receiver");
            var s = NewSettings();
            var dm = new DeviceManager(src, () => s);
            dm.StartForTests(); dm.RunPendingForTests();
            Check(dm.Mouse != null && dm.Mouse.Dev.Index == 2, "mouse found in slot 2");
            Check(dm.Mouse?.Connection == "Logi Bolt receiver", "connection labelled Bolt");
            dm.Dispose();
        }

        static void SettingsRoundTrip()
        {
            Section("Settings file");
            var s = AppSettings.Deserialize("{}");
            s.Normalize();
            Check(s.Profiles.Count == 1 && s.Global.MiddleMode == "default" && s.LowBatteryPercent == 15 && s.Notifications && s.FnMode == "unchanged", "empty file → sensible defaults");
            s.Global.Keys.Add(new KeyBinding { Mods = 1, Vk = 0x46, Action = ActionSpec.Of("copy") });
            s.Profiles.Add(new Profile { App = "EXCEL.EXE", MiddleMode = "gestures" });
            s.MouseDpi = 1200;
            s.Normalize();
            var json = AppSettings.Serialize(s);
            var back = AppSettings.Deserialize(json); back.Normalize();
            Check(back.MouseDpi == 1200 && back.Global.Keys.Any(k => k.Action.Preset == "copy"), "round-trip keeps values");
            Check(back.Global.Keys.Count(k => k.Mods == 1 && k.Vk == 0x1B && k.Action.Preset == "fn_toggle") == 1 && back.Version == 2, "v1.2 adds Ctrl+Esc → switch top row, exactly once");
            var mine = AppSettings.Deserialize("{\"Version\":1,\"Profiles\":[{\"App\":\"\",\"Keys\":[{\"Mods\":1,\"Vk\":27,\"Action\":{\"Kind\":\"preset\",\"Preset\":\"calc\"}}]}]}");
            mine.Normalize();
            Check(mine.Global.Keys.Count == 1 && mine.Global.Keys[0].Action.Preset == "calc", "an existing Ctrl+Esc remap is left alone");
            Check(back.ForApp("excel.exe")?.MiddleMode == "gestures", "app profile lower-cased and kept");
            Check(json.Contains("\n"), "file is indented/readable");
        }

        static (InputHooks h, List<(ActionSpec a, IList<int> rel, bool mask)> got) Hooks(AppSettings s)
        {
            var got = new List<(ActionSpec, IList<int>, bool)>();
            ActionRunner.TestSink = (a, r, m) => got.Add((a, r, m));
            var h = new InputHooks();
            h.Update(s);
            return (h, got);
        }

        static void MouseHookLogic()
        {
            Section("Middle button");
            var s = NewSettings();
            var (h, got) = Hooks(s);
            Check(!h.HandleMouse(Native.WM_MBUTTONDOWN, 10, 10, 1) && !h.HandleMouse(Native.WM_MBUTTONUP, 10, 10, 2), "default: middle click passes through");

            s.Global.MiddleMode = "action"; s.Global.MiddleAction = ActionSpec.Of("back"); h.Update(s);
            Check(h.HandleMouse(Native.WM_MBUTTONDOWN, 10, 10, 1), "action: press swallowed");
            Check(got.Count == 1 && got[0].a.Preset == "back", "action: Back fired once on press");
            Check(h.HandleMouse(Native.WM_MBUTTONUP, 10, 10, 2), "action: release swallowed");
            Check(!h.HandleMouse(Native.WM_MOUSEMOVE, 11, 11, 3), "pointer moves normally");

            s.Global.MiddleMode = "gestures"; h.Update(s); got.Clear();
            h.HandleMouse(Native.WM_MBUTTONDOWN, 500, 500, 10);
            Check(h.HandleMouse(Native.WM_MOUSEMOVE, 500, 470, 11) && h.HandleMouse(Native.WM_MOUSEMOVE, 501, 470, 12), "gesture: pointer frozen while held");
            h.HandleMouse(Native.WM_MBUTTONUP, 500, 500, 13);
            Check(got.Count == 1 && got[0].a.Preset == "task_view", "swipe up → Task View");
            got.Clear();
            h.HandleMouse(Native.WM_MBUTTONDOWN, 500, 500, 20);
            h.HandleMouse(Native.WM_MOUSEMOVE, 530, 505, 21); h.HandleMouse(Native.WM_MOUSEMOVE, 530, 505, 22);
            h.HandleMouse(Native.WM_MBUTTONUP, 500, 500, 23);
            Check(got.Count == 1 && got[0].a.Preset == "desktop_right", "swipe right → next desktop");
            got.Clear();
            h.HandleMouse(Native.WM_MBUTTONDOWN, 500, 500, 30);
            h.HandleMouse(Native.WM_MOUSEMOVE, 505, 503, 31);
            h.HandleMouse(Native.WM_MBUTTONUP, 500, 500, 32);
            Check(got.Count == 1 && got[0].a.Preset == "middle_click", "tiny movement → click action (middle click)");
            got.Clear();
            s.Global.GestureLeft = ActionSpec.Default(); h.Update(s);
            h.HandleMouse(Native.WM_MBUTTONDOWN, 500, 500, 40);
            h.HandleMouse(Native.WM_MOUSEMOVE, 400, 500, 41);
            h.HandleMouse(Native.WM_MBUTTONUP, 500, 500, 42);
            Check(got.Count == 0, "unassigned direction does nothing");

            Section("Per-app middle button");
            var excel = new Profile { App = "excel.exe", MiddleMode = "action", MiddleAction = ActionSpec.Of("copy") };
            s.Profiles.Add(excel); s.Normalize(); h.Update(s); got.Clear();
            h.ForegroundApp = "excel.exe";
            h.HandleMouse(Native.WM_MBUTTONDOWN, 1, 1, 50); h.HandleMouse(Native.WM_MBUTTONUP, 1, 1, 51);
            Check(got.Count == 1 && got[0].a.Preset == "copy", "Excel profile overrides global");
            excel.MiddleMode = "inherit"; h.Update(s); got.Clear();
            h.HandleMouse(Native.WM_MBUTTONDOWN, 1, 1, 60); h.HandleMouse(Native.WM_MOUSEMOVE, 1, -100, 61); h.HandleMouse(Native.WM_MBUTTONUP, 1, 1, 62);
            Check(got.Count == 1 && got[0].a.Preset == "task_view", "'Same as All applications' falls back to global gestures");
            h.ForegroundApp = "chrome.exe";

            s.Paused = true; h.Update(s);
            Check(!h.HandleMouse(Native.WM_MBUTTONDOWN, 1, 1, 70) && !h.HandleMouse(Native.WM_MBUTTONUP, 1, 1, 71), "paused: passes through");
        }

        static void KeyHookLogic()
        {
            Section("Key remapping");
            var s = NewSettings();
            s.Global.Keys.Add(new KeyBinding { Mods = 0, Vk = Vk.MediaPlayPause, Action = ActionSpec.Of("calc") });
            s.Global.Keys.Add(new KeyBinding { Mods = 0, Vk = Vk.VolumeUp, Action = ActionSpec.Of("vol_down") });
            s.Global.Keys.Add(new KeyBinding { Mods = (int)(Mods.Ctrl | Mods.Shift), Vk = 0x46, Action = ActionSpec.Of("copy") });
            s.Global.Keys.Add(new KeyBinding { Mods = (int)Mods.Win, Vk = 0x4A, Action = ActionSpec.Of("explorer") });
            s.Normalize();
            var (h, got) = Hooks(s);

            Check(h.HandleKey(Vk.MediaPlayPause, true) && got.Count == 1 && got[0].a.Preset == "calc", "remapped media key swallowed, action fired");
            Check(h.HandleKey(Vk.MediaPlayPause, true) && got.Count == 1, "auto-repeat swallowed, one-shot action not re-fired");
            got.Clear();
            Check(h.HandleKey(Vk.MediaPlayPause, false), "release swallowed");
            Check(!h.HandleKey(Vk.MediaPlayPause, false), "stray second release passes");

            got.Clear();
            h.HandleKey(Vk.VolumeUp, true); h.HandleKey(Vk.VolumeUp, true); h.HandleKey(Vk.VolumeUp, true); h.HandleKey(Vk.VolumeUp, false);
            Check(got.Count == 3, "volume-type actions repeat while held");

            got.Clear();
            Check(!h.HandleKey(Vk.LControl, true) && !h.HandleKey(Vk.LShift, true), "modifiers pass through");
            Check(h.HandleKey(0x46, true), "Ctrl+Shift+F swallowed");
            Check(got.Count == 1 && got[0].a.Preset == "copy" && got[0].rel.Contains(Vk.LControl) && got[0].rel.Contains(Vk.LShift) && !got[0].mask, "held modifiers released before the action, no mask needed");
            Check(h.HandleKey(Vk.LShift, true), "auto-repeat of a lifted modifier hidden");
            Check(h.HandleKey(0x46, false) && h.HandleKey(Vk.LShift, false) && h.HandleKey(Vk.LControl, false), "remaining releases swallowed");
            Check(!h.HandleKey(Vk.LControl, true) && !h.HandleKey(Vk.LControl, false), "Ctrl behaves normally afterwards");
            Check(!h.HandleKey(0x46, true) && !h.HandleKey(0x46, false), "plain F still types");

            got.Clear();
            h.HandleKey(Vk.LWin, true);
            h.HandleKey(0x4A, true);
            Check(got.Count == 1 && got[0].mask, "Win combo sends mask key so Start menu stays shut");
            h.HandleKey(0x4A, false);
            Check(h.HandleKey(Vk.LWin, false), "Win release swallowed (already lifted)");

            Section("Per-app key overrides and pause");
            s.Profiles.Add(new Profile { App = "vlc.exe", Keys = new List<KeyBinding> { new KeyBinding { Vk = Vk.MediaPlayPause, Action = ActionSpec.Default() } } });
            s.Normalize(); h.Update(s); got.Clear();
            h.ForegroundApp = "vlc.exe";
            Check(!h.HandleKey(Vk.MediaPlayPause, true) && !h.HandleKey(Vk.MediaPlayPause, false), "'Normal key' in VLC cancels the global remap");
            h.ForegroundApp = "notepad.exe";
            Check(h.HandleKey(Vk.MediaPlayPause, true) && h.HandleKey(Vk.MediaPlayPause, false), "global remap active elsewhere");
            s.Paused = true; h.Update(s);
            Check(!h.HandleKey(Vk.MediaPlayPause, true) && !h.HandleKey(Vk.MediaPlayPause, false), "paused: keys pass through");
        }

        static void CaptureLogic()
        {
            Section("Recording a shortcut");
            var s = NewSettings();
            var (h, got) = Hooks(s);
            int cm = -1, cv = -1;
            var done = new ManualResetEventSlim();
            h.BeginCapture((m, v) => { cm = m; cv = v; done.Set(); });
            Check(h.HandleKey(Vk.LControl, true) && h.HandleKey(Vk.LShift, true), "modifiers swallowed while recording");
            Check(h.HandleKey(0x4B, true), "key swallowed while recording");
            done.Wait(1000);
            Check(cm == (int)(Mods.Ctrl | Mods.Shift) && cv == 0x4B, "recorded Ctrl+Shift+K");
            Check(h.HandleKey(0x4B, false) && h.HandleKey(Vk.LShift, false), "releases swallowed");
            Check(h.HandleKey(Vk.LControl, false), "last release swallowed and recording ends");
            Check(!h.HandleKey(0x41, true) && !h.HandleKey(0x41, false), "typing works again afterwards");

            done.Reset();
            h.BeginCapture((m, v) => { cm = m; cv = v; done.Set(); });
            h.HandleKey(Vk.Escape, true); done.Wait(1000); h.HandleKey(Vk.Escape, false);
            Check(cv == 0, "Esc cancels recording");
            Check(!h.HandleKey(0x41, true), "and input is released");
            h.HandleKey(0x41, false);
        }

        static void V12()
        {
            Section("v1.2: top-row shortcut, pause key, gesture pop-ups, tray badge, pop-up drawing");
            var s = NewSettings();
            var (h, got) = Hooks(s);
            h.HandleKey(Vk.LControl, true);
            Check(h.HandleKey(Vk.Escape, true), "Ctrl+Esc swallowed (Start menu doesn't open)");
            Check(got.Count == 1 && got[0].a.Preset == "fn_toggle", "Ctrl+Esc runs 'switch top row'");
            h.HandleKey(Vk.Escape, false); h.HandleKey(Vk.LControl, false);
            Check(Presets.Get("fn_toggle").Special == "app:fn_toggle", "switch top row is an app command");

            s.Global.Keys.Add(new KeyBinding { Vk = Vk.Pause, Action = ActionSpec.Of("pause_toggle") });
            s.Global.Keys.Add(new KeyBinding { Vk = Vk.MediaPlayPause, Action = ActionSpec.Of("calc") });
            s.Paused = true; h.Update(s); got.Clear();
            Check(!h.HandleKey(Vk.MediaPlayPause, true), "paused: ordinary remaps off");
            h.HandleKey(Vk.MediaPlayPause, false);
            Check(h.HandleKey(Vk.Pause, true) && got.Count == 1 && got[0].a.Preset == "pause_toggle", "paused: the pause key still works");
            h.HandleKey(Vk.Pause, false);

            s.Paused = false; s.Global.MiddleMode = "gestures"; h.Update(s);
            string dir = null; ActionSpec ga = null;
            h.Gesture += (d, a) => { dir = d; ga = a; };
            h.HandleMouse(Native.WM_MBUTTONDOWN, 100, 100, 1);
            h.HandleMouse(Native.WM_MOUSEMOVE, 160, 100, 2);
            h.HandleMouse(Native.WM_MBUTTONUP, 100, 100, 3);
            Check(dir == "right" && ga?.Preset == "desktop_right", "gesture event reports direction + action for the pop-up");
            dir = null;
            h.HandleMouse(Native.WM_MBUTTONDOWN, 100, 100, 4);
            h.HandleMouse(Native.WM_MBUTTONUP, 100, 100, 5);
            Check(dir == null, "plain click: no pop-up");

            using (var icon = new System.Drawing.Icon(typeof(TestMain).Assembly.GetManifestResourceStream("LiteOptions.app.ico"), 24, 24))
            using (var bmp = LiteOptions.UI.TrayBadge.Compose(icon, 0.1, 2))
            {
                var corner = bmp.GetPixel(bmp.Width - 4, bmp.Height - 3);
                Check(bmp.Width == icon.Width && corner.A > 200, $"tray badge drawn in the corner ({bmp.Width}px)");
            }
            using (var pop = LiteOptions.UI.Osd.Render(1.5f, "\uE765", "F1–F12 keys first", "Hold Fn for media keys", null, false))
                Check(pop.Width > 300 && pop.Height > 100 && pop.GetPixel(0, 0).A == 0, $"pop-up renders with transparent edges ({pop.Width}×{pop.Height})");
            using (var pop = LiteOptions.UI.Osd.Render(1f, "\uE962", "1600 DPI", "Mouse sensitivity", 0.33, true))
                Check(pop.Width >= 240, "DPI pop-up with progress bar renders");
        }

        static void Misc()
        {
            Section("Helpers");
            Check(KeyNames.Combo((int)(Mods.Ctrl | Mods.Win), Vk.Right) == "Ctrl + Win + Right", "combo naming");
            Check(KeyNames.Key(Vk.MediaPlayPause) == "Play/Pause" && KeyNames.Key(0x7B) == "F12", "key naming");
            Check(Presets.Describe(ActionSpec.Keys(1, 0x43)) == "Shortcut: Ctrl + C", "describe shortcut");
            int aa = HidppDevice.EstimateFromVoltage(1450), aaa2 = HidppDevice.EstimateFromVoltage(2600), li = HidppDevice.EstimateFromVoltage(4100);
            Check(aa == 90 && aaa2 == 50 && li > 85 && li < 95, $"voltage estimates AA {aa} / 2xAAA {aaa2} / Li-ion {li}");
            Check(HidppDevice.ParseDpiList(new byte[] { 0, 0x03, 0x20, 0x06, 0x40, 0x0C, 0x80, 0, 0 }, 1).SequenceEqual(new[] { 800, 1600, 3200 }), "discrete DPI list");
            Check(Presets.All.Select(p => p.Id).Distinct().Count() == Presets.All.Count, "preset ids unique");
        }
    }
}
