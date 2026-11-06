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
                SmoothScrolling();
                CaptureLogic();
                V12();
                AulaFlow();
                AulaSystemFlow();
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

            Section("Easy-Switch channels");
            var hosts = dm.Keyboard.Hosts;
            Check(hosts != null && hosts.Hosts.Count == 3 && hosts.Current == 1, "keyboard: 3 channels, channel 2 is this computer");
            Check(hosts != null && !hosts.Hosts[0].Paired && hosts.Hosts[1].Name == "WORK-LAPTOP", "empty channel 1, channel 2 named");
            Check(hosts != null && hosts.Hosts[2].Name == "Anna\u2019s iPhone 15 Pro Max", "long UTF-8 name read in chunks: " + hosts?.Hosts[2].Name);
            Section("Keys the K380 switches off itself (0x4521)");
            Check(dm.Keyboard.DisableCaps == 0x1D && kb.Disabled == 0, "capabilities read; nothing switched off while the setting is untouched");
            s.DisabledKeys = 0x01 | 0x02; // Caps Lock, plus Num Lock which this keyboard can't do
            dm.ApplySettingsNow(); dm.RunPendingForTests();
            Check(kb.Disabled == 0x01 && dm.Keyboard.DisabledKeys == 0x01, "Caps Lock switched off; unsupported keys ignored");
            kb.Disabled = 0;
            dm.RequestPoll(); dm.RunPendingForTests();
            Check(kb.Disabled == 0x01, "poll puts it back if the keyboard forgot");
            s.DisabledKeys = 0;
            dm.ApplySettingsNow(); dm.RunPendingForTests();
            Check(kb.Disabled == 0, "unticking turns it back on");
            s.DisabledKeys = -1;

            var mh = dm.Mouse.Hosts;
            Check(mh != null && mh.Hosts.Count == 3 && mh.Current == 0 && mh.Hosts[1].Unknown, "mouse without names: channel count and current channel only");

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

        static void SmoothScrolling()
        {
            Section("Smooth scrolling");
            var sc = new SmoothScroller();
            sc.Queue(120);
            int sent = 0, steps = 0, first = 0;
            for (int t = 0; t < 60 && sc.PendingForTests != 0; t++) { int d = sc.Step(0.005); if (d != 0) { steps++; if (first == 0) first = d; } sent += d; }
            Check(sent == 120, "one wheel click arrives in full: " + sent);
            Check(steps >= 8 && first < 30, $"spread over small steps ({steps} steps, first {first})");
            sc.Queue(-120); sc.Queue(-120);
            int back = 0; for (int t = 0; t < 80; t++) back += sc.Step(0.005);
            Check(back == -240, "two quick clicks the other way add up: " + back);
            sc.Queue(120); sc.Step(0.005); sc.Queue(-120);
            int after = 0; for (int t = 0; t < 80; t++) after += sc.Step(0.005);
            Check(after == -120, "reversing drops the rest of the old glide: " + after);

            var hooks = new InputHooks();
            var cfg = NewSettings(); cfg.SmoothScroll = true; hooks.Update(cfg);
            hooks.Smooth.Output = _ => { };
            Check(hooks.HandleMouse(0x020A, 0, 0, 0, 120), "notched wheel click taken over when smooth scrolling is on");
            Check(!hooks.HandleMouse(0x020A, 0, 0, 0, 30), "touchpad-sized steps pass straight through");
            hooks.HandleKey(0xA2, true);
            Check(!hooks.HandleMouse(0x020A, 0, 0, 0, 120), "Ctrl + wheel (zoom) passes straight through");
            hooks.HandleKey(0xA2, false);
            cfg.SmoothScroll = false; hooks.Update(cfg);
            Check(!hooks.HandleMouse(0x020A, 0, 0, 0, 120), "off by default: wheel untouched");
            hooks.Smooth.Dispose();
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
                Check(pop.Width >= 220 && pop.Height < 90, $"DPI pop-up with progress bar renders, about Windows' size ({pop.Width}×{pop.Height})");
            using (var pop = LiteOptions.UI.Osd.Render(1f, "A", "Caps Lock on", "Letters type in capitals", null, false, LiteOptions.UI.Osd.Badge.KeyCap, true))
                Check(pop.Height < 90 && pop.Width < 300, $"Caps Lock pop-up with a key cap and its light ({pop.Width}×{pop.Height})");
        }

        static void AulaFlow()
        {
            Section("Aula F108 Pro (pretend keyboard)");

            // packets match what f108-pro tested on the keyboard
            var l = new AulaLighting { Mode = 7, R = 0x80, G = 0x10, B = 0xFF, Rainbow = false, Brightness = 4, Speed = 2, Direction = 0 };
            var p = AulaProtocol.Lighting(l);
            Check(p.Length == 64 && p[0] == 7 && p[1] == 0x80 && p[2] == 0x10 && p[3] == 0xFF && p[8] == 0 && p[9] == 4 && p[10] == 2 && p[11] == 0 && p[14] == 0x55 && p[15] == 0xAA,
                "lighting packet: mode, colour, brightness, speed, trailer 55 AA at 14");
            var off = AulaProtocol.Lighting(new AulaLighting { Mode = 0, R = 1, G = 2, B = 3 });
            Check(off[0] == 0 && off[1] == 0 && off[9] == 0 && off[14] == 0x55 && off[15] == 0xAA, "lighting off: only the mode and the trailer");
            var wild = AulaProtocol.Lighting(new AulaLighting { Mode = 40, R = 999, Brightness = 9, Speed = -1, Direction = 7 });
            Check(wild[0] == 19 && wild[1] == 255 && wild[9] == 5 && wild[10] == 0 && wild[11] == 0, "lighting values clamped to what the keyboard accepts");
            var c = AulaProtocol.Clock(new DateTime(2026, 11, 6, 16, 5, 9));
            Check(c[0] == 0 && c[1] == 1 && c[2] == 0x5A && c[3] == 26 && c[4] == 11 && c[5] == 6 && c[6] == 16 && c[7] == 5 && c[8] == 9 && c[10] == 5 && c[62] == 0x55 && c[63] == 0xAA,
                "clock packet: 26-11-06 16:05:09 Friday, trailer 55 AA at 62");
            Check(AulaProtocol.Begin()[0] == 4 && AulaProtocol.Begin()[1] == 0x18 && AulaProtocol.Apply()[1] == 0x02 && AulaProtocol.Finish()[1] == 0xF0, "begin 04 18, apply 04 02, finish 04 F0");
            Check(AulaProtocol.LightingStart()[1] == 0x13 && AulaProtocol.LightingStart()[8] == 1 && AulaProtocol.ClockStart()[1] == 0x28 && AulaProtocol.ClockStart()[8] == 1, "lighting start 04 13, clock start 04 28, both with byte 8 = 1");
            Check(AulaLighting.HasColour(1) && !AulaLighting.HasColour(6) && !AulaLighting.HasColour(8) && !AulaLighting.HasSpeed(1) && AulaLighting.HasSpeed(2)
                  && AulaLighting.DirectionKind(10) == 1 && AulaLighting.DirectionKind(11) == 2 && AulaLighting.DirectionKind(7) == 0, "which effects take a colour, a speed and a direction");

            // dongle only: listens, never writes, can't configure
            var settings = new AppSettings(); settings.Normalize();
            var src = new FakeAulaSource { Dongle = true };
            var now = new DateTime(2026, 11, 6, 16, 5, 9);
            var m = new AulaManager(src, () => settings) { DelayMs = 0, EchoTimeoutMs = 1, Now = () => now };
            int changes = 0; m.Changed += () => changes++;
            m.ScanForTests(); m.RunPendingForTests();
            var st = m.Snapshot();
            Check(st.Connection == "2.4 GHz dongle" && !st.DongleAwake && st.Battery == -1, "dongle plugged in, keyboard not answering: " + st.Connection);
            Check(src.DongleOpened == 1 && src.Listening == 2 && src.Opened == 0 && src.Written.Count == 0, "dongle: command channel opened, two others listened to, nothing on a cable");
            Check(src.DongleWritten.Count == 3 && src.DongleWritten.All(r => r.Length == 33 && r[1] == 0x20 && r[2] == 0x01 && r[32] == 0x21),
                "keyboard not answering: only the battery query (20 01, checksum 21), 3 tries");
            src.Listeners[0](new byte[] { 0x05, 0x40, 0x18 });
            src.Listeners[0](new byte[] { 0x05, 0x40, 0x18 });
            Check(Logger.Snapshot().Contains("Aula dongle page FFFF: 05 40 18"), "other dongle reports logged as hex");
            string err = null;
            src.DongleWritten.Clear();
            m.SetLighting(new AulaLighting { Mode = 1 }, e => err = e); m.RunPendingForTests();
            Check(err != null && err.Contains("wake") && src.Written.Count == 0, "lighting with the keyboard not answering: error, tells you to wake it: " + err);
            Check(changes > 0, "the window is told about the keyboard");

            // the keyboard wakes up on the dongle: battery, then the clock
            src.OnDongle = true; src.DongleWritten.Clear();
            now = now.AddMinutes(1);   // the "linked" report is acted on at most every 5 seconds
            src.Listeners[0](new byte[] { 0x05, 0xA6, 0xFF, 0x01 });   // the dongle's "linked" report: ask straight away
            m.RunPendingForTests();
            st = m.Snapshot();
            Check(st.Connection == "2.4 GHz dongle" && st.DongleAwake && st.Battery == 59 && st.CanConfigure, "keyboard on the dongle: battery 59% from its answer: " + st.Connection + " awake=" + st.DongleAwake + " batt=" + st.Battery + " can=" + st.CanConfigure + " writes=" + src.DongleWritten.Count);
            var clock = src.DongleWritten.FirstOrDefault(r => r[1] == 0x0C);
            Check(clock != null && clock[2] == 0x10 && clock[5] == 1 && clock[6] == 0x5A && clock[7] == 26 && clock[8] == 11 && clock[9] == 6 && clock[10] == 16 && clock[11] == 6 && clock[12] == 9
                  && clock[14] == 5 && clock[18] == 0xAA && clock[19] == 0x55 && AulaProtocol.ChecksumOk(clock), "clock set through the dongle when it turns up: 0C 10 … 5A 26-11-06 16:06:09 Fri, AA 55, checksum");
            Check(m.Snapshot().ClockSyncedAt == now, "dongle clock sync remembered");
            src.DongleWritten.Clear();
            m.DonglePollForTests(); m.RunPendingForTests();
            Check(src.DongleWritten.Count == 1 && src.DongleWritten[0][1] == 0x20, "next poll: just the battery query, no second clock within the hour");
            src.DongleBattery = 41;
            m.DonglePollForTests(); m.RunPendingForTests();
            Check(m.Snapshot().Battery == 41, "battery follows the keyboard's answers");
            src.DongleSends(AulaProtocol.Checksum(new byte[] { 0, 0x20, 0x01, 0x00, 0x26, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 }));
            m.RunPendingForTests();
            Check(m.Snapshot().Battery == 38, "battery answers to the Aula app's own queries count too");
            var bad = AulaProtocol.Checksum(new byte[33]); bad[1] = 0x20; bad[2] = 0x01; bad[4] = 0x10;  // checksum now wrong
            src.DongleSends(bad); m.RunPendingForTests();
            Check(m.Snapshot().Battery == 38, "an answer with a wrong checksum is ignored");

            // lighting through the dongle
            src.DongleWritten.Clear(); err = "x";
            m.SetLighting(new AulaLighting { Mode = 7, R = 0x80, G = 0x10, B = 0xFF, Brightness = 4, Speed = 2, Direction = 1 }, e => err = e); m.RunPendingForTests();
            var lp = src.DongleWritten.FirstOrDefault();
            Check(err == null && src.DongleWritten.Count == 1 && src.Written.Count == 0, "lighting over the dongle: one report, echoed");
            Check(lp != null && lp[0] == 0 && lp[1] == 0x05 && lp[2] == 0x10 && lp[3] == 0 && lp[4] == 7 && lp[5] == 0x80 && lp[6] == 0x10 && lp[7] == 0xFF
                  && lp[12] == 0 && lp[13] == 4 && lp[14] == 2 && lp[15] == 1 && lp[18] == 0xAA && lp[19] == 0x55 && AulaProtocol.ChecksumOk(lp),
                "dongle lighting packet: 05 10 00, mode, colour, rainbow, brightness, speed, direction, AA 55, checksum");
            var loff = AulaProtocol.DongleLighting(new AulaLighting { Mode = 0, R = 9 });
            Check(loff[4] == 0 && loff[5] == 0 && loff[13] == 0 && loff[18] == 0xAA, "dongle lighting off: only the mode and the end marker");
            Check(Logger.Snapshot().Contains("lighting set to Breathing") && Logger.Snapshot().Contains("(dongle)"), "dongle lighting logged");

            // the keyboard goes quiet (asleep): after the poll window it no longer counts as answering
            src.OnDongle = false;
            now = now.AddMinutes(4);
            m.DonglePollForTests(); m.RunPendingForTests();
            st = m.Snapshot();
            Check(st.Connection == "2.4 GHz dongle" && !st.DongleAwake && st.Battery == 38 && st.CanConfigure, "keyboard quiet for 4 minutes (asleep): not answering, last battery kept, changes still allowed (they wake-check)");

            // cable plugged in: the clock is set once, lighting left alone
            src.Cable = true;
            m.ScanForTests(); m.RunPendingForTests();
            st = m.Snapshot();
            Check(st.Connection == "USB cable" && st.CanConfigure && st.Version == 0x0120, "cable wins over the dongle; settings open");
            Check(src.Written.Count == 4 && src.Written[0][1] == 0x18 && src.Written[1][1] == 0x28 && src.Written[2][2] == 0x5A && src.Written[3][1] == 0x02 && src.Reads == 4,
                "plug-in: clock set with begin, start, data, apply and four read-backs");
            Check(st.ClockSyncedAt == now, "clock sync time remembered");
            m.ScanForTests(); m.RunPendingForTests();
            Check(src.Written.Count == 4 && src.Opened == 1, "scanning again changes nothing");

            // lighting change: five reports, three read-backs
            src.Written.Clear(); src.Reads = 0;
            err = "x";
            m.SetLighting(new AulaLighting { Mode = 11, R = 255, G = 0, B = 0, Brightness = 3, Speed = 4, Direction = 1 }, e => err = e); m.RunPendingForTests();
            Check(err == null && src.Written.Count == 5 && src.Reads == 3, "lighting: 5 reports, 3 read-backs");
            Check(src.Written[1][1] == 0x13 && src.Written[2][0] == 11 && src.Written[2][11] == 1 && src.Written[4][1] == 0xF0, "lighting: start, data (Rolling, direction 1), finish");

            // cable unplugged, plugged in with clock sync off
            src.Cable = false; m.ScanForTests(); m.RunPendingForTests();
            Check(src.Closed == 1 && m.Snapshot().Connection == "2.4 GHz dongle", "cable out: settings channel closed, back to the dongle");
            settings.AulaClockSync = false; src.Written.Clear();
            src.Cable = true; m.ScanForTests(); m.RunPendingForTests();
            Check(m.Snapshot().CanConfigure && src.Written.Count == 0, "clock sync switched off: nothing written on plug-in");

            // a failed write closes the channel and reports it
            src.FailWrites = true;
            m.SyncClock(e => err = e); m.RunPendingForTests();
            st = m.Snapshot();
            Check(err != null && !st.CanConfigure && st.LastError != "" && src.Closed == 2, "failed write: error shown, channel closed to reopen: " + err);

            // on Bluetooth (dongle still plugged in): Bluetooth wins, battery from Windows, nothing written
            src.FailWrites = false; src.Cable = false; src.Dongle = true; src.OnDongle = false; src.Bluetooth = 64; src.Written.Clear();
            now = now.AddMinutes(10);
            m.ScanForTests(); m.RunPendingForTests();
            st = m.Snapshot();
            Check(st.Connection == "Bluetooth" && st.Battery == 64 && st.BluetoothName == "AULA-F108Pro-1" && !st.CanConfigure && src.Written.Count == 0,
                "Bluetooth: connection and Windows' battery level, nothing written: " + st.Connection + " " + st.Battery);
            Check(Logger.Snapshot().Contains("now on Bluetooth as \"AULA-F108Pro-1\", battery 64%"), "Bluetooth battery logged");
            src.Bluetooth = 12; changes = 0;
            m.ScanForTests(); m.RunPendingForTests();
            Check(m.Snapshot().Battery == 12 && changes > 0, "battery change noticed on the next look");
            src.Bluetooth = -1;
            m.ScanForTests(); m.RunPendingForTests();
            Check(m.Snapshot().Connection == "Bluetooth" && m.Snapshot().Battery == -1, "Bluetooth without a battery level: unknown, not 0");
            // switched to the dongle while Windows still lists it on Bluetooth: its answer through the dongle wins
            src.Bluetooth = 60; src.OnDongle = true; src.DongleBattery = 58;
            m.DonglePollForTests(); m.RunPendingForTests();
            st = m.Snapshot();
            Check(st.Connection == "2.4 GHz dongle" && st.Battery == 58 && st.CanConfigure, "answering through the dongle beats a lingering Bluetooth entry: " + st.Connection);
            Check(Logger.Snapshot().Contains("now on 2.4 GHz dongle, battery 58%"), "connection change logged");
            src.Bluetooth = -2; src.OnDongle = false;
            now = now.AddMinutes(10);
            m.ScanForTests(); m.RunPendingForTests();
            Check(m.Snapshot().Connection == "2.4 GHz dongle" && m.Snapshot().Battery == 58 && !m.Snapshot().DongleAwake, "off Bluetooth, keyboard quiet: dongle, last battery kept");

            // keyboard gone entirely
            src.FailWrites = false; src.Cable = false; src.Dongle = false;
            m.ScanForTests(); m.RunPendingForTests();
            Check(!m.Snapshot().Present && src.Listening == 0 && src.DongleClosed >= 1, "nothing plugged in: not present, dongle channel and listeners closed");

            // settings: the Aula fields survive a save and load; old files get clock sync on
            settings.AulaLight = new AulaLighting { Mode = 7, R = 10, G = 20, B = 30, Rainbow = true, Brightness = 2, Speed = 1 };
            settings.AulaClockSync = false;
            var back = AppSettings.Deserialize(AppSettings.Serialize(settings));
            Check(back.AulaLight != null && back.AulaLight.Mode == 7 && back.AulaLight.B == 30 && back.AulaLight.Rainbow && !back.AulaClockSync, "Aula settings round-trip");
            var old = AppSettings.Deserialize("{\"Version\":2,\"Profiles\":[]}");
            old.Normalize();
            Check(old.AulaClockSync && old.AulaLight == null, "settings from 2.3.3: clock sync on, lighting untouched");
            m.Dispose();
        }

        static void AulaSystemFlow()
        {
            Section("Aula F108 Pro system settings (pretend keyboard)");
            var sys = new AulaSystemSettings { Sleep = 3, Response = 4, BlockWin = true, BlockAltTab = true };
            var d = AulaProtocol.SystemData(sys);
            Check(d[0] == 0 && d[1] == 1 && d[2] == 1 && d[3] == 0 && d[4] == 1 && d[5] == 0 && d[6] == 3 && d[7] == 0 && d[8] == 4 && d[62] == 0xAA && d[63] == 0x55,
                "cable settings data: 00 01, Win, Alt+F4, Alt+Tab, Fn, sleep, -, response, AA 55 at 62");
            var st = AulaProtocol.SystemStart();
            Check(st[0] == 4 && st[1] == 0x17 && st[2] == 1 && st[8] == 1, "cable settings header 04 17 with bytes 2 and 8 set");
            var seq = AulaProtocol.SystemSequence(sys);
            Check(seq.Count == 4 && seq[0].readBack && seq[1].readBack && !seq[2].readBack && seq[3].readBack && seq[3].data[1] == 0x02, "cable settings: begin, header, data (no read-back), apply");
            var r = AulaProtocol.DongleSystem(sys);
            Check(r.Length == 33 && r[1] == 7 && r[2] == 0x10 && r[3] == 0 && r[5] == 1 && r[6] == 1 && r[7] == 0 && r[8] == 1 && r[9] == 0 && r[10] == 3 && r[12] == 4
                  && r[18] == 0xAA && r[19] == 0x55 && AulaProtocol.ChecksumOk(r), "dongle settings: 07 10, fields from byte 5, AA 55 at 18, checksum");
            var odd = new AulaSystemSettings { Sleep = 9, Response = 0, FnSwitch = 5 }; odd.Normalize();
            Check(odd.Sleep == 3 && odd.Response == 1 && odd.FnSwitch == 0, "settings clamped to what the keyboard accepts");
            var def = new AulaSystemSettings();
            Check(def.Sleep == 2 && def.Response == 2 && !def.BlockWin && def.FnSwitch == 0, "defaults are the Aula app's: 5 minutes, level 2, nothing blocked");

            var settings = new AppSettings(); settings.Normalize();
            var src = new FakeAulaSource { Dongle = true, OnDongle = true };
            var m = new AulaManager(src, () => settings) { DelayMs = 0, EchoTimeoutMs = 1 };
            settings.AulaClockSync = false;
            m.ScanForTests(); m.RunPendingForTests();
            src.DongleWritten.Clear();
            string err = "x";
            m.SetSystem(sys, e => err = e); m.RunPendingForTests();
            Check(err == null && src.DongleWritten.Count == 1 && src.DongleWritten[0][1] == 7, "settings through the dongle: one report, echoed");
            src.Cable = true; m.ScanForTests(); m.RunPendingForTests();
            src.Written.Clear(); src.Reads = 0; err = "x";
            m.SetSystem(sys, e => err = e); m.RunPendingForTests();
            Check(err == null && src.Written.Count == 4 && src.Reads == 3 && src.Written[1][1] == 0x17 && src.Written[2][6] == 3, "settings over the cable: 4 reports, 3 read-backs");
            Check(Logger.Snapshot().Contains("settings sent (sleep after 30 minutes, key response level 4, blocking Windows key, Alt+Tab)"), "settings logged");

            settings.AulaSystem = sys;
            var back = AppSettings.Deserialize(AppSettings.Serialize(settings));
            Check(back.AulaSystem != null && back.AulaSystem.Sleep == 3 && back.AulaSystem.Response == 4 && back.AulaSystem.BlockWin && !back.AulaSystem.BlockAltF4, "system settings round-trip");
            m.Dispose();
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
