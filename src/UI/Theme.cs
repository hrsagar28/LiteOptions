using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiteOptions.UI
{
    /// <summary>Windows 11-style palette, type ramp and drawing helpers. Follows the Windows light/dark app setting.</summary>
    static class Theme
    {
        public static bool Dark { get; private set; }

        public static Color WindowBg, CardBg, CardBorder, Divider, Text, Muted, Faint,
            Accent, AccentHover, AccentPressed, OnAccent,
            ControlBg, ControlHover, ControlPressed, ControlBorder, SubtleHover, SubtlePressed,
            NavSelected, KeyCapBg, KeyCapBorder, KeyCapShadow, Good, Warn, Bad, BadHover, MenuBg, ThumbBg, TrackOff;

        public static Font Body, BodyStrong, Caption, CaptionStrong, Subtitle, PageTitle, Mono;
        public static string IconFamily = "Segoe MDL2 Assets";
        static readonly Dictionary<float, Font> iconFonts = new Dictionary<float, Font>();

        // Segoe Fluent Icons / MDL2 code points
        public const string IcMouse = "", IcKeyboard = "", IcSettings = "", IcAdd = "", IcDelete = "",
            IcEdit = "", IcChevronDown = "", IcChevronRight = "", IcRight = "", IcLeft = "", IcUp = "",
            IcDown = "", IcRefresh = "", IcCopy = "", IcFolder = "", IcInfo = "", IcWarning = "",
            IcCheck = "", IcPower = "", IcPause = "", IcTouch = "", IcDiagnostic = "", IcApps = "",
            IcBattery = "", IcSync = "", IcClose = "";

        static bool initialised;

        public static void Init()
        {
            Dark = ReadDarkMode();
            if (Dark)
            {
                WindowBg = C(0x202020); CardBg = C(0x2B2B2B); CardBorder = C(0x1F1F1F); Divider = C(0x1D1D1D);
                Text = C(0xFFFFFF); Muted = C(0xC8C8C8); Faint = C(0x9D9D9D);
                Accent = C(0x4CC2FF); AccentHover = C(0x47B1E8); AccentPressed = C(0x42A1D2); OnAccent = C(0x000000);
                ControlBg = C(0x373737); ControlHover = C(0x3D3D3D); ControlPressed = C(0x323232); ControlBorder = C(0x454545);
                SubtleHover = C(0x353535); SubtlePressed = C(0x303030); NavSelected = C(0x2D2D2D);
                KeyCapBg = C(0x3A3A3A); KeyCapBorder = C(0x4A4A4A); KeyCapShadow = C(0x1E1E1E);
                Good = C(0x6CCB5F); Warn = C(0xFCE100); Bad = C(0xFF99A4); BadHover = C(0xFFB3BB);
                MenuBg = C(0x2C2C2C); ThumbBg = C(0x454545); TrackOff = C(0x9A9A9A);
            }
            else
            {
                WindowBg = C(0xF3F3F3); CardBg = C(0xFBFBFB); CardBorder = C(0xE5E5E5); Divider = C(0xEAEAEA);
                Text = C(0x1A1A1A); Muted = C(0x5D5D5D); Faint = C(0x8A8A8A);
                Accent = C(0x005FB8); AccentHover = C(0x1A6FC0); AccentPressed = C(0x3380CC); OnAccent = C(0xFFFFFF);
                ControlBg = C(0xFFFFFF); ControlHover = C(0xF6F6F6); ControlPressed = C(0xF0F0F0); ControlBorder = C(0xD9D9D9);
                SubtleHover = C(0xEDEDED); SubtlePressed = C(0xE6E6E6); NavSelected = C(0xEAEAEA);
                KeyCapBg = C(0xFFFFFF); KeyCapBorder = C(0xD4D4D4); KeyCapShadow = C(0xBDBDBD);
                Good = C(0x0F7B0F); Warn = C(0x9D5D00); Bad = C(0xC42B1C); BadHover = C(0xB02617);
                MenuBg = C(0xF9F9F9); ThumbBg = C(0xFFFFFF); TrackOff = C(0x8A8A8A);
            }

            if (!initialised)
            {
                initialised = true;
                CreateFonts(1f);
                try
                {
                    using (var fonts = new InstalledFontCollection())
                    {
                        var names = new HashSet<string>(fonts.Families.Select(f => f.Name));
                        if (names.Contains("Segoe Fluent Icons")) IconFamily = "Segoe Fluent Icons";
                    }
                }
                catch { }
            }
        }

        static float fontFactor = 1f;

        static void CreateFonts(float f)
        {
            fontFactor = f;
            Body = new Font("Segoe UI", 10f * f);
            BodyStrong = new Font("Segoe UI Semibold", 10f * f);
            Caption = new Font("Segoe UI", 8.75f * f);
            CaptionStrong = new Font("Segoe UI Semibold", 8.75f * f);
            Subtitle = new Font("Segoe UI Semibold", 12.5f * f);
            PageTitle = new Font("Segoe UI Semibold", 21f * f);
            Mono = new Font("Consolas", 8.75f * f);
            lock (iconFonts) iconFonts.Clear();
        }

        /// <summary>
        /// The process is per-monitor DPI aware. Windows still converts font points using the system DPI,
        /// so for another monitor we scale pixel sizes by dpi/96 and font sizes by dpi/systemDpi.
        /// </summary>
        public static void ApplyDpi(int dpi)
        {
            if (!initialised) Init();
            if (dpi <= 0) dpi = SystemDpi;
            if (dpi == CurrentDpi) return;
            CurrentDpi = dpi;
            scale = dpi / 96f;
            CreateFonts(dpi / (float)SystemDpi);
        }

        static int systemDpi;
        public static int SystemDpi
        {
            get
            {
                if (systemDpi == 0)
                {
                    try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) systemDpi = (int)Math.Round(g.DpiX); } catch { systemDpi = 96; }
                    if (systemDpi < 96) systemDpi = 96;
                }
                return systemDpi;
            }
        }
        public static int CurrentDpi { get; private set; }
        public static int DpiAtCursor() => Native.IsWindows ? Native.DpiAt(Cursor.Position, SystemDpi) : SystemDpi;

        static Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        static bool ReadDarkMode()
        {
            string forced = Environment.GetEnvironmentVariable("LITEOPTIONS_THEME");
            if (forced == "dark") return true;
            if (forced == "light") return false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        public static Font Icon(float pt)
        {
            lock (iconFonts)
            {
                if (!iconFonts.TryGetValue(pt, out var f)) iconFonts[pt] = f = new Font(IconFamily, pt * fontFactor);
                return f;
            }
        }

        // ── DPI ──
        static float scale = -1;
        public static float Scale
        {
            get
            {
                if (scale < 0) scale = SystemDpi / 96f;
                return scale;
            }
        }
        public static int S(int px) => (int)Math.Round(px * Scale);
        public static float Sf(float px) => px * Scale;
        public static Padding P(int all) => new Padding(S(all));
        public static Padding P(int l, int t, int r, int b) => new Padding(S(l), S(t), S(r), S(b));

        // ── drawing helpers ──
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Color fill, RectangleF r, float radius, Color? border = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(r, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.HasValue) using (var p = new Pen(border.Value)) g.DrawPath(p, path);
            }
        }

        public static void Glyph(Graphics g, string glyph, float pt, Color color, Rectangle r)
        {
            TextRenderer.DrawText(g, glyph, Icon(pt), r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        public static Color Blend(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        public static Color BatteryColor(int? pct, bool low) =>
            low || (pct.HasValue && pct.Value <= 15) ? Bad : (pct.HasValue && pct.Value <= 30 ? Warn : Good);

        /// <summary>Small horizontal battery icon with fill.</summary>
        public static void DrawBattery(Graphics g, Rectangle r, double fraction, Color fill, Color outline)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float nub = Math.Max(2f, r.Height * 0.18f);
            var body = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - nub - 1.5f, r.Height - 1f);
            using (var p = new Pen(outline, Math.Max(1f, Sf(1.2f))))
            using (var path = Rounded(body, Sf(3)))
                g.DrawPath(p, path);
            using (var b = new SolidBrush(outline))
                g.FillRectangle(b, body.Right + Sf(1), r.Y + r.Height * 0.32f, nub, r.Height * 0.36f);
            float inset = Sf(2.5f);
            var inner = new RectangleF(body.X + inset, body.Y + inset, (body.Width - inset * 2) * (float)Math.Max(0.06, Math.Min(1, fraction)), body.Height - inset * 2);
            using (var path = Rounded(inner, Sf(1.5f)))
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        }

        public static double BatteryFraction(Hid.BatteryInfo b)
        {
            if (b == null) return 0;
            if (b.Percent.HasValue) return b.Percent.Value / 100.0;
            switch (b.Level) { case "Full": return 1; case "Good": return 0.65; case "Low": return 0.2; case "Critical": return 0.08; }
            return 0.5;
        }

        // ── window chrome (Windows 10 1809+/11) ──
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static void ApplyWindowChrome(Form f)
        {
            try
            {
                int dark = Dark ? 1 : 0;
                DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);            // immersive dark title bar
                int caption = WindowBg.R | (WindowBg.G << 8) | (WindowBg.B << 16);
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);         // title bar colour = window colour (Win11)
            }
            catch { }
        }

        public static void RoundCorners(IntPtr hwnd)
        {
            try { int pref = 3; DwmSetWindowAttribute(hwnd, 33, ref pref, 4); } catch { } // DWMWCP_ROUNDSMALL for menus
        }

        public static void DarkScrollbars(Control c)
        {
            if (!Dark) return;
            void apply() { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
            if (c.IsHandleCreated) apply(); else c.HandleCreated += (s, e) => apply();
        }
    }
}
