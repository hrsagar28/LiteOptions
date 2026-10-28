using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using LiteOptions.Hid;
using LiteOptions.UI;
using System.Linq;

namespace LiteOptions.Tests
{
    /// <summary>Renders the settings window with simulated devices to PNGs (layout check on Linux/Mono).</summary>
    static class PreviewMain
    {
        [STAThread]
        static void Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : "preview";
            Directory.CreateDirectory(outDir);
            Application.EnableVisualStyles();
            System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            var mouse = FakeDevice.PebbleM350s();
            var kb = FakeDevice.K380();
            var src = new FakeSource();
            var ml = new FakeLink(true, false); ml.Slots[0xFF] = mouse;
            var kl = new FakeLink(true, true); kl.Slots[0xFF] = kb;
            src.Add("BTLE_MOUSE", 0xB03E, ml, "Pebble Mouse 2 M350s");
            src.Add("BT_K380", 0xB342, kl, "Keyboard K380");

            var app = new TrayApp(true, src, false);
            app.Settings.Global.MiddleMode = args.Length > 1 ? args[1] : "gestures";
            app.Settings.Global.Keys.Add(new KeyBinding { Vk = 0xB3, Action = ActionSpec.Of("calc") });
            app.Settings.Global.Keys.Add(new KeyBinding { Mods = 3, Vk = 0x56, Action = ActionSpec.Of("paste_plain") });
            app.Settings.Global.Keys.Add(new KeyBinding { Vk = 0x7B, Action = new ActionSpec { Kind = "launch", Target = @"C:\Program Files\Tally\TallyPrime\tally.exe" } });
            app.Settings.FnMode = "standard";
            app.Settings.MouseDpi = 1600;

            int step = 0;
            Form dlg = null;
            var t = new Timer { Interval = 1200 };
            t.Tick += (s, e) =>
            {
                var f = app.Window;
                if (f == null) return;
                switch (step++)
                {
                    case 0: f.ClientSize = new Size(1040, 1380); f.ReloadAll(); f.ShowPageByName("mouse"); break;
                    case 1: Snap(f, outDir, "1-mouse"); f.ShowPageByName("keys"); break;
                    case 2: Snap(f, outDir, "2-keyboard"); f.ShowPageByName("settings"); break;
                    case 3: Snap(f, outDir, "3-settings"); app.Settings.Global.MiddleMode = "action"; app.Settings.Global.MiddleAction = new ActionSpec { Kind = "keys", Mods = 9, Vk = 0x56 }; f.ReloadAll(); f.ShowPageByName("mouse"); break;
                    case 4: Snap(f, outDir, "4-mouse-action"); dlg = new InstallDialog(new Version(1, 0, 0), @"C:\Dev\LiteOptions-1.0\LiteOptions.exe"); dlg.Show(); break;
                    case 5: Snap(dlg, outDir, "5-install"); dlg.Close(); dlg = new ChooseActionDialog(3, 0x56, ActionSpec.Of("paste_plain"), () => null); dlg.Show(); break;
                    case 6: Snap(dlg, outDir, "6-choose-action"); dlg.Close(); dlg = new SetupDialog(app); dlg.Show(); break;
                    case 7: Snap(dlg, outDir, "7-setup-1"); dlg.Controls.OfType<Panel>().First(p => p.Dock == DockStyle.Bottom).Controls.OfType<FluentButton>().First(b => b.Text == "Next").PerformClick(); break;
                    case 8: Snap(dlg, outDir, "8-setup-2"); dlg.Controls.OfType<Panel>().First(p => p.Dock == DockStyle.Bottom).Controls.OfType<FluentButton>().First(b => b.Text == "Next").PerformClick(); break;
                    case 9: Snap(dlg, outDir, "9-setup-3"); dlg.Close();
                        foreach (var (k, dark, name) in new[] { (1.5f, false, "light"), (1.5f, true, "dark") })
                        {
                            using (var b = Osd.Render(k, "\uE765", "F1–F12 keys first", "Hold Fn for media keys", null, dark)) b.Save(Path.Combine(outDir, "osd-fn-" + name + ".png"));
                            using (var b = Osd.Render(k, "\uE962", "1600 DPI", "Mouse sensitivity", 0.33, dark)) b.Save(Path.Combine(outDir, "osd-dpi-" + name + ".png"));
                        }
                        using (var ic = new Icon(TrayApp.AppIcon, 24, 24))
                        {
                            foreach (var (frac, bucket, n) in new[] { (0.8, 0, "ok"), (0.25, 1, "amber"), (0.08, 2, "red") })
                                using (var b = TrayBadge.Compose(ic, frac, bucket))
                                using (var big = new Bitmap(96, 96))
                                using (var g = Graphics.FromImage(big))
                                {
                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                                    g.Clear(Color.FromArgb(32, 32, 32));
                                    g.DrawImage(b, new Rectangle(0, 0, 96, 96));
                                    big.Save(Path.Combine(outDir, "tray-" + n + ".png"));
                                }
                        }
                        t.Stop(); Application.Exit(); break;
                }
            };
            t.Start();
            Application.Run(app);
        }

        static void Snap(Form f, string dir, string name)
        {
            // Mono's DrawToBitmap skips child controls, so grab the X display instead
            f.Location = new Point(0, 0);
            Application.DoEvents();
            var r = f.Bounds;
            var psi = new System.Diagnostics.ProcessStartInfo("python3",
                $"-c \"from PIL import ImageGrab; ImageGrab.grab(bbox=({r.X},{r.Y},{r.Right},{r.Bottom}), xdisplay=':99').save('{Path.Combine(dir, name + ".png")}')\"")
            { UseShellExecute = false };
            System.Diagnostics.Process.Start(psi).WaitForExit();
            Console.WriteLine("saved " + name);
        }
    }
}
