using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace LiteOptions.SettingsApp
{
    /// <summary>
    /// --selftest [file]: open the window, talk to the running engine, round-trip the settings, write PASS/FAIL and exit.
    /// Used by the build to prove the window starts on x64 and ARM64 and that the two halves understand each other.
    /// </summary>
    static class SelfTestRunner
    {
        static readonly List<string> log = new List<string>();
        static bool finished;

        public static async void Run(MainWindow w, Store store)
        {
            _ = Task.Delay(45000).ContinueWith(_ => Finish(false, "timed out"));
            try
            {
                log.Add($"window created ({RuntimeInformation.ProcessArchitecture}, {RuntimeInformation.FrameworkDescription}, {Environment.OSVersion.VersionString})");
                var start = DateTime.UtcNow;
                while (!store.Loaded && (DateTime.UtcNow - start).TotalSeconds < 20) await Task.Delay(200);
                if (!store.Loaded) { Finish(false, "no answer from the engine (connected=" + store.Connected + ")"); return; }
                log.Add($"hello: engine {store.System.Version} {store.System.Architecture}, {store.Settings.Profiles.Count} profile(s), {store.Devices.Count} device(s)");

                var r = await store.Send("state");
                if (r.Error != null) { Finish(false, "state: " + r.Error); return; }
                if (r.Settings == null || r.Settings.Global == null) { Finish(false, "state came back without settings"); return; }

                r = await store.Send("diagnostics");
                log.Add("diagnostics: " + (r.Error ?? (r.Text?.Length ?? 0) + " characters"));

                // settings travel window -> engine -> window unchanged
                string before = AppSettings.Serialize(store.Settings);
                await store.SaveNowAsync();
                r = await store.Send("state");
                if (r.Error != null) { Finish(false, "state after save: " + r.Error); return; }
                r.Settings.Normalize();
                string after = AppSettings.Serialize(r.Settings);
                if (before != after) { log.Add("before: " + before); log.Add("after: " + after); Finish(false, "settings changed on the round trip"); return; }
                log.Add("settings round trip: identical");

                // every page builds and lays out
                foreach (var tag in new[] { "keys", "apps", "alerts", "settings", "mouse" }) { w.ShowPage(tag); await Task.Delay(400); }
                log.Add("pages: mouse, keyboard, app profiles, pop-ups & alerts, settings shown");

                // the search box finds settings on other pages and goes to them
                w.ShowSearch("smooth");
                await Task.Delay(400);
                bool found = w.SearchResults("smooth").Contains("Smooth scrolling");
                w.ClearSearch();
                if (!found) { Finish(false, "search: 'smooth' did not find Smooth scrolling (got: " + string.Join(", ", w.SearchResults("smooth")) + ")"); return; }
                w.ShowPage("settings"); await Task.Delay(300);
                w.GoToFirst("caps lock"); await Task.Delay(600);
                if (w.CurrentTag != "alerts") { Finish(false, "search: 'caps lock' went to " + w.CurrentTag + ", not pop-ups & alerts"); return; }
                w.ShowPage("mouse"); await Task.Delay(300);
                log.Add("search: finds and opens settings on other pages");

                // the tray panel builds, lays out and hides again
                var panel = new TrayFlyout(store, _ => { });
                panel.ShowFlyout();
                await Task.Delay(1500);
                double ph = panel.RootElement.ActualHeight;
                panel.HideFlyout();
                if (ph < 200) { Finish(false, "tray panel: laid out only " + ph + " px tall"); return; }
                log.Add($"tray panel: {ph:0} px tall, hides again");

                // the menu folds away on a narrow window and comes back on a wide one
                var size = w.AppWindow.Size;
                w.ResizeTo(760, 640); await Task.Delay(800);
                bool folded = w.IsNarrow;
                w.ResizeTo(1200, 760); await Task.Delay(800);
                bool back = !w.IsNarrow && w.PaneOpen;
                w.AppWindow.Resize(size); await Task.Delay(500);
                if (!folded || !back) { Finish(false, $"menu folding: folded when narrow={folded}, back in full when wide={back}"); return; }
                log.Add("menu: folds behind the menu button when narrow, back on the left when wide");

                string dir = string.IsNullOrEmpty(Entry.SelfTestFile) ? null : Path.GetDirectoryName(Path.GetFullPath(Entry.SelfTestFile));
                if (dir != null) { await Task.Delay(1200); await Shots.CaptureAsync(w, dir, "selftest-window"); }
                Finish(true, "the settings window works with this engine");
            }
            catch (Exception ex) { Finish(false, ex.GetType().Name + ": " + ex.Message); }
        }

        public static void Fail(string why) => Finish(false, why);

        static void Finish(bool ok, string message)
        {
            lock (log)
            {
                if (finished) return;
                finished = true;
                log.Add((ok ? "PASS: " : "FAIL: ") + message);
                try { if (!string.IsNullOrEmpty(Entry.SelfTestFile)) File.WriteAllLines(Entry.SelfTestFile, log); } catch { }
                try { Console.WriteLine(string.Join(Environment.NewLine, log)); } catch { }
            }
            Environment.Exit(ok ? 0 : 3);
        }
    }

    /// <summary>--shots dir: walk through the window with the demo devices and save pictures of each part (light and dark).</summary>
    static class Shots
    {
        public static async Task RunAsync(MainWindow w, Store store, string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                await Task.Delay(3000);
                // run once as it starts (light), and again with --dark: the whole app then starts in the dark theme, as it does on a dark Windows
                await Tour(w, store, dir, Entry.Dark ? "dark-" : "");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); }
            Environment.Exit(0);
        }

        static async Task Tour(MainWindow w, Store store, string dir, string prefix)
        {
            async Task snap(string name) { await Task.Delay(900); await CaptureAsync(w, dir, prefix + name); }
            async Task close() { Ui.Current?.Hide(); await Task.Delay(500); }

            w.ShowPage("mouse"); await snap("01-mouse");
            w.ScrollTo(520); await snap("01b-mouse-buttons");
            w.ScrollTo(1040); await snap("01c-mouse-pointer");
            w.ScrollToEnd(); await snap("02-mouse-lower");
            w.ShowPage("keys"); await snap("03-keyboard");
            w.ScrollTo(480); await snap("03b-keyboard-middle");
            w.ScrollToEnd(); await snap("04-keyboard-lower");
            w.ShowPage("settings"); await snap("05-settings");
            w.ScrollToEnd(); await snap("06-settings-lower");
            w.ShowPage("apps"); await snap("19-apps");
            w.ShowPage("alerts"); await snap("20-alerts");
            w.ShowPage("mouse");
            w.ShowSearch("scroll"); await snap("21-search");
            w.ClearSearch();

            var panel = new TrayFlyout(store, _ => { });
            panel.ShowFlyout();
            await Task.Delay(1500);
            try { await SaveScreen(panel.Handle, Path.Combine(dir, prefix + "22-tray.png")); } catch (Exception ex) { File.AppendAllText(Path.Combine(dir, "errors.txt"), "22-tray (screen): " + ex.Message + Environment.NewLine); }
            try { await SaveXaml(panel.RootElement, Path.Combine(dir, prefix + "22-tray-xaml.png")); } catch (Exception ex) { File.AppendAllText(Path.Combine(dir, "errors.txt"), "22-tray (xaml): " + ex.Message + Environment.NewLine); }
            panel.HideFlyout();
            await Task.Delay(400);

            store.CurrentApp = "excel.exe"; w.ReloadPages();
            w.ShowPage("mouse"); await snap("07-mouse-excel");
            store.CurrentApp = ""; w.ReloadPages();

            w.ShowPage("keys");
            w.ShowPage("mouse");
            await Task.Delay(500);
            var menu = w.OpenFirstMenu();
            await snap("08-action-menu");
            menu?.Hide();
            await Task.Delay(400);

            _ = Dialogs.ChooseActionAsync(store, (int)Mods.Ctrl | (int)Mods.Shift, 0x56, ActionSpec.Of("paste_plain"));
            await snap("09-choose-action"); await close();
            _ = Dialogs.ChooseActionAsync(store, 0, 0x2C, ActionSpec.Keys((int)Mods.Win | (int)Mods.Shift, 0x53));
            await Task.Delay(1500); await snap("10-choose-shortcut"); await close();
            _ = Dialogs.CaptureKeyAsync(store);
            await Task.Delay(1600); await snap("11-capture"); await close();
            _ = Dialogs.EditCustomAsync(store, "launch", null);
            await snap("12-open-something"); await close();
            _ = Ui.Confirm("Uninstall LiteOptions?", "This removes LiteOptions, its settings and its startup entry.", "Uninstall", danger: true);
            await snap("13-confirm"); await close();

            // a narrow window: the menu folds behind ☰ (and opens over the page when asked)
            var size = w.AppWindow.Size;
            w.ResizeTo(780, 700);
            w.ShowPage("mouse");
            await Task.Delay(600); await snap("17-narrow");
            w.SetPaneOpen(true); await snap("18-narrow-menu");
            w.SetPaneOpen(false);
            // a big, wide window (like a maximised one on a large screen): pages fill it without running off the edge
            w.ResizeTo(1560, 900);
            w.ShowPage("apps"); await Task.Delay(600); await snap("23-wide-apps");
            w.ShowPage("alerts"); await snap("24-wide-alerts");
            w.ShowPage("mouse"); await snap("25-wide-mouse");
            w.AppWindow.Resize(size);
            await Task.Delay(600);

            _ = SetupFlow.RunAsync(store);
            await snap("14-setup-1");
            SetupFlow.GoTo?.Invoke(1); await snap("15-setup-2");
            SetupFlow.GoTo?.Invoke(2); await snap("16-setup-3");
            await close();
            await Task.Delay(800);
        }

        public static async Task CaptureAsync(MainWindow w, string dir, string name)
        {
            try { await SaveScreen(MainWindow.Hwnd, Path.Combine(dir, name + ".png")); }
            catch (Exception ex) { try { File.AppendAllText(Path.Combine(dir, "errors.txt"), name + " (screen): " + ex.Message + Environment.NewLine); } catch { } }
            try { await SaveXaml(w.RootElement, Path.Combine(dir, name + "-xaml.png")); }
            catch (Exception ex) { try { File.AppendAllText(Path.Combine(dir, "errors.txt"), name + " (xaml): " + ex.Message + Environment.NewLine); } catch { } }
        }

        /// <summary>What's actually on screen inside the window (includes menus, dialogs and the Mica backdrop).</summary>
        static async Task SaveScreen(IntPtr hwnd, string path)
        {
            if (W32.DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out W32.RECT r, Marshal.SizeOf<W32.RECT>()) != 0)
                W32.GetWindowRect(hwnd, out r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) throw new InvalidOperationException("window has no size");
            IntPtr screen = W32.GetDC(IntPtr.Zero);
            IntPtr mem = W32.CreateCompatibleDC(screen);
            var bi = new W32.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr dib = W32.CreateDIBSection(screen, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
            IntPtr old = W32.SelectObject(mem, dib);
            var px = new byte[w * h * 4];
            try
            {
                W32.BitBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, 0x00CC0020 | 0x40000000 /* SRCCOPY | CAPTUREBLT */);
                Marshal.Copy(bits, px, 0, px.Length);
            }
            finally
            {
                W32.SelectObject(mem, old);
                W32.DeleteObject(dib);
                W32.DeleteDC(mem);
                W32.ReleaseDC(IntPtr.Zero, screen);
            }
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            await SavePng(path, px, w, h, BitmapAlphaMode.Ignore);
        }

        /// <summary>The window's own drawing of itself (works even without a visible desktop; no menus or dialogs).</summary>
        static async Task SaveXaml(UIElement element, string path)
        {
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(element);
            var buffer = await rtb.GetPixelsAsync();
            var px = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(px);
            await SavePng(path, px, rtb.PixelWidth, rtb.PixelHeight, BitmapAlphaMode.Premultiplied);
        }

        static async Task SavePng(string path, byte[] px, int w, int h, BitmapAlphaMode alpha)
        {
            using (var ms = new InMemoryRandomAccessStream())
            {
                var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
                enc.SetPixelData(BitmapPixelFormat.Bgra8, alpha, (uint)w, (uint)h, 96, 96, px);
                await enc.FlushAsync();
                var bytes = new byte[ms.Size];
                using (var reader = new DataReader(ms.GetInputStreamAt(0)))
                {
                    await reader.LoadAsync((uint)ms.Size);
                    reader.ReadBytes(bytes);
                }
                File.WriteAllBytes(path, bytes);
            }
        }
    }
}
