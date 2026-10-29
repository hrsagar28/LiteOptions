using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace LiteOptions.UI
{
    /// <summary>
    /// Small fading pop-up near the bottom of every screen (like Windows' volume flyout).
    /// One click-through layered window per monitor, with per-pixel alpha, rendered at that monitor's DPI.
    /// The windows are created when a pop-up appears and destroyed when it has faded out, so they always
    /// belong to the current virtual desktop and match the screens that are connected right now.
    /// Must be used from the UI thread.
    /// </summary>
    static class Osd
    {
        public static bool Enabled = true;

        static readonly List<Popup> windows = new List<Popup>();
        static Timer timer;
        static int alpha;
        static int phase; // 0 hidden, 1 fading in, 2 holding, 3 fading out
        static DateTime holdUntil;
        static int detailedLogs = 4; // log the first few pop-ups in full, so a "nothing appeared" report can be diagnosed
        static bool logShown;

        /// <summary>How the icon on the left is drawn.</summary>
        public enum Badge
        {
            None,    // the glyph on its own
            Filled,  // the glyph in a filled circle, for a state that should stand out (microphone muted)
            Tinted,  // the glyph on an accent-tinted square (gestures)
            KeyCap,  // "glyph" is a key label drawn on a key cap (Caps Lock: "A")
        }

        /// <param name="light">For a lock key: true draws its light on the right, false an empty one, null none.</param>
        public static void Show(string glyph, string title, string subtitle = null, double? progress = null, Badge badge = Badge.None, bool? light = null)
        {
            if (!Enabled || !Native.IsWindows) return;
            try { Display(glyph, title, subtitle, progress, badge, light); }
            catch (Exception ex) { Logger.Write("pop-up failed: " + ex.Message); }
        }

        static void Display(string glyph, string title, string subtitle, double? progress, Badge badge, bool? light)
        {
            if (timer == null) { timer = new Timer { Interval = 15 }; timer.Tick += (s, e) => Step(); }
            var screens = Screen.AllScreens;
            if (phase == 0 || windows.Count != screens.Length)
            {
                CloseAll();
                foreach (var sc in screens) windows.Add(new Popup(sc));
            }
            var notes = new List<string>();
            foreach (var w in windows)
            {
                w.SetContent(glyph, title, subtitle, progress, badge, light);
                notes.Add(w.Describe());
            }
            if (phase == 0 || phase == 3) phase = 1; // fade in (or back in, if it was fading out)
            holdUntil = DateTime.UtcNow.AddMilliseconds(1300);
            string err = null;
            foreach (var w in windows) { string e = w.Present(alpha); if (e != null) err = e; }
            timer.Start();
            if (detailedLogs > 0 || err != null)
            {
                detailedLogs--;
                logShown = true;
                Logger.Write($"Pop-up \"{title}\" on {windows.Count} screen(s): {string.Join("; ", notes)}" + (err != null ? " — " + err : ""));
            }
        }

        static void Step()
        {
            switch (phase)
            {
                case 1:
                    alpha = Math.Min(255, alpha + 45);
                    if (alpha == 255)
                    {
                        phase = 2;
                        if (logShown) { logShown = false; Logger.Write("Pop-up fully shown"); }
                    }
                    break;
                case 2: if (DateTime.UtcNow > holdUntil) phase = 3; return;
                case 3:
                    alpha = Math.Max(0, alpha - 22);
                    if (alpha == 0) { phase = 0; timer.Stop(); CloseAll(); return; }
                    break;
                default: timer.Stop(); return;
            }
            foreach (var w in windows) w.Fade(alpha);
        }

        static void CloseAll()
        {
            foreach (var w in windows) w.Dispose();
            windows.Clear();
        }

        public static void Shutdown()
        {
            CloseAll();
            timer?.Dispose(); timer = null;
            phase = 0; alpha = 0;
        }

        /// <summary>The pop-up window on one monitor.</summary>
        sealed class Popup : NativeWindow, IDisposable
        {
            readonly Screen screen;
            Bitmap frame;
            IntPtr hBitmap, memDc;
            Native.POINT pos;
            Native.SIZE size;

            public Popup(Screen screen)
            {
                this.screen = screen;
                CreateHandle(new CreateParams
                {
                    Caption = "LiteOptions pop-up",
                    Style = Native.WS_POPUP,
                    ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST,
                });
            }

            public void SetContent(string glyph, string title, string subtitle, double? progress, Badge badge, bool? light)
            {
                var b = screen.Bounds;
                int dpi = Native.DpiAt(new Point(b.Left + b.Width / 2, b.Top + b.Height / 2), Theme.SystemDpi);
                float k = dpi / 96f;
                var bmp = Render(k, glyph, title, subtitle, progress, Theme.Dark, badge, light);
                SetFrame(bmp);
                var wa = screen.WorkingArea;
                // the card's bottom edge sits where Windows' own volume pop-up sits: 14 px above the taskbar
                float cardBottom = size.cy - ShadowSize * 1.3f * k;
                pos = new Native.POINT { X = wa.Left + (wa.Width - size.cx) / 2, Y = wa.Bottom - (int)Math.Round(14 * k + cardBottom) };
            }

            public string Describe() => $"{size.cx}x{size.cy} at {pos.X},{pos.Y} on {screen.Bounds.Width}x{screen.Bounds.Height}{(screen.Primary ? " (main)" : "")}";

            /// <summary>Show it without taking focus and put it back on top. Returns an error description, or null.</summary>
            public string Present(int a)
            {
                string err = Fade(a);
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                return err;
            }

            public string Fade(int a)
            {
                if (memDc == IntPtr.Zero) return "no frame";
                IntPtr screenDc = Native.GetDC(IntPtr.Zero);
                var src = new Native.POINT();
                var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = (byte)a, AlphaFormat = Native.AC_SRC_ALPHA };
                bool ok = Native.UpdateLayeredWindow(Handle, screenDc, ref pos, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
                int code = ok ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Native.ReleaseDC(IntPtr.Zero, screenDc);
                return ok ? null : "UpdateLayeredWindow failed, error " + code;
            }

            void SetFrame(Bitmap bmp)
            {
                FreeFrame();
                frame = bmp;
                size = new Native.SIZE { cx = bmp.Width, cy = bmp.Height };
                IntPtr screenDc = Native.GetDC(IntPtr.Zero);
                memDc = Native.CreateCompatibleDC(screenDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                Native.SelectObject(memDc, hBitmap);
            }

            void FreeFrame()
            {
                if (memDc != IntPtr.Zero) { Native.DeleteDC(memDc); memDc = IntPtr.Zero; }
                if (hBitmap != IntPtr.Zero) { Native.DeleteObject(hBitmap); hBitmap = IntPtr.Zero; }
                frame?.Dispose(); frame = null;
            }

            public void Dispose()
            {
                FreeFrame();
                try { Native.ShowWindow(Handle, Native.SW_HIDE); } catch { }
                DestroyHandle();
            }
        }

        const float ShadowSize = 14;

        /// <summary>
        /// Draws the pop-up in the size and style of Windows 11's own volume and brightness pop-ups (public so tests can render it).
        /// Two layouts: icon + title (+ subtitle), or icon + bar + value when there's a level to show.
        /// </summary>
        public static Bitmap Render(float k, string glyph, string title, string subtitle, double? progress, bool dark,
                                    Badge badge = Badge.None, bool? light = null)
        {
            Color bg = dark ? Color.FromArgb(250, 44, 44, 44) : Color.FromArgb(252, 249, 249, 249);
            Color border = dark ? Color.FromArgb(255, 66, 66, 66) : Color.FromArgb(255, 222, 222, 222);
            Color text = dark ? Color.White : Color.FromArgb(27, 27, 27);
            Color muted = dark ? Color.FromArgb(206, 206, 206) : Color.FromArgb(94, 94, 94);
            Color accent = dark ? Color.FromArgb(76, 194, 255) : Color.FromArgb(0, 95, 184);
            Color track = dark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(138, 138, 138);

            bool bar = progress != null;
            using (var titleFont = new Font("Segoe UI Semibold", 14f * k, GraphicsUnit.Pixel))
            using (var valueFont = new Font("Segoe UI", 14f * k, GraphicsUnit.Pixel))
            using (var subFont = new Font("Segoe UI", 12f * k, GraphicsUnit.Pixel))
            using (var iconFont = new Font(Theme.IconFamily, (badge == Badge.None ? 16f : 14f) * k, GraphicsUnit.Pixel))
            using (var capFont = new Font("Segoe UI Semibold", 11f * k, GraphicsUnit.Pixel))
            using (var measure = new Bitmap(1, 1))
            using (var mg = Graphics.FromImage(measure))
            {
                mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float tw = mg.MeasureString(title ?? "", bar ? valueFont : titleFont).Width;
                float sw = string.IsNullOrEmpty(subtitle) || bar ? 0 : mg.MeasureString(subtitle, subFont).Width;
                float pad = 16 * k, gap = 12 * k, shadow = ShadowSize * k;
                float iconBox = (badge == Badge.None ? 20 : 28) * k;
                float lightW = light != null && !bar ? gap + 8 * k : 0;
                float minW = 192 * k; // the width of Windows' volume pop-up
                float w = bar ? Math.Max(minW, pad + iconBox + gap + 100 * k + gap + tw + pad)
                              : Math.Max(minW, Math.Min(420 * k, pad + iconBox + gap + Math.Max(tw, sw) + 2 * k + lightW + pad));
                float h = (bar || sw == 0 ? 48 : 56) * k;
                var bmp = new Bitmap((int)Math.Ceiling(w + shadow * 2), (int)Math.Ceiling(h + shadow * 2), PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    var box = new RectangleF(shadow, shadow * 0.7f, w, h);
                    // soft shadow: a few expanding translucent outlines
                    for (int i = 6; i >= 1; i--)
                    {
                        var r = RectangleF.Inflate(box, i * 1.4f * k, i * 1.4f * k);
                        r.Offset(0, 3 * k);
                        using (var p = Theme.Rounded(r, 8 * k + i * 1.4f * k))
                        using (var b = new SolidBrush(Color.FromArgb(dark ? 16 : 8, 0, 0, 0))) g.FillPath(b, p);
                    }
                    using (var p = Theme.Rounded(box, 8 * k))
                    {
                        using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                        using (var pen = new Pen(border, Math.Max(1f, k))) g.DrawPath(pen, p);
                    }

                    // the icon, in its box on the left
                    var ib = new RectangleF(box.X + pad, box.Y + (h - iconBox) / 2, iconBox, iconBox);
                    DrawBadge(g, ib, k, badge, glyph, iconFont, capFont, dark, text, accent);

                    float x = ib.Right + gap;
                    float right = box.Right - pad;
                    if (bar)
                    {
                        // icon · bar · value, like the volume pop-up
                        float valueW = tw + 2 * k;
                        float barW = Math.Max(40 * k, right - x - gap - valueW);
                        float by = box.Y + (h - 4 * k) / 2;
                        using (var tp = Theme.Rounded(new RectangleF(x, by, barW, 4 * k), 2 * k))
                        using (var tb = new SolidBrush(track)) g.FillPath(tb, tp);
                        float fw = Math.Max(4 * k, (float)(barW * Math.Max(0, Math.Min(1, progress.Value))));
                        using (var fp = Theme.Rounded(new RectangleF(x, by, fw, 4 * k), 2 * k))
                        using (var fb = new SolidBrush(accent)) g.FillPath(fb, fp);
                        using (var b = new SolidBrush(text))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                            g.DrawString(title ?? "", valueFont, b, new RectangleF(right - valueW - 2 * k, box.Y, valueW + 4 * k, h), sf);
                    }
                    else
                    {
                        float textW = right - x - lightW;
                        bool two = sw > 0;
                        float th = titleFont.GetHeight(), sh = subFont.GetHeight();
                        float ty = box.Y + (h - (two ? th + sh : th)) / 2;
                        using (var b = new SolidBrush(text))
                        using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                            g.DrawString(title ?? "", titleFont, b, new RectangleF(x, ty, textW, th + 2 * k), sf);
                        if (two)
                            using (var b = new SolidBrush(muted))
                            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                                g.DrawString(subtitle, subFont, b, new RectangleF(x, ty + th, textW, sh + 2 * k), sf);
                        if (light != null)
                        {
                            // the key's light: filled when on, an empty ring when off
                            float d = 8 * k;
                            var dot = new RectangleF(right - d, box.Y + (h - d) / 2, d, d);
                            if (light.Value) using (var b = new SolidBrush(accent)) g.FillEllipse(b, dot);
                            else using (var pen = new Pen(muted, Math.Max(1f, 1.2f * k))) g.DrawEllipse(pen, RectangleF.Inflate(dot, -0.6f * k, -0.6f * k));
                        }
                    }
                }
                return bmp;
            }
        }

        static void DrawBadge(Graphics g, RectangleF ib, float k, Badge badge, string glyph, Font iconFont, Font capFont, bool dark, Color text, Color accent)
        {
            Color fore = text;
            switch (badge)
            {
                case Badge.Filled:
                    using (var b = new SolidBrush(dark ? Color.White : Color.FromArgb(27, 27, 27))) g.FillEllipse(b, ib);
                    fore = dark ? Color.FromArgb(27, 27, 27) : Color.White;
                    break;
                case Badge.Tinted:
                    using (var p = Theme.Rounded(ib, 6 * k))
                    using (var b = new SolidBrush(dark ? Color.FromArgb(48, accent) : Color.FromArgb(235, 243, 251))) g.FillPath(b, p);
                    fore = accent;
                    break;
                case Badge.KeyCap:
                {
                    // a key: light face, thin outline and a deeper bottom edge
                    float cw = Math.Max(22 * k, g.MeasureString(glyph ?? "", capFont).Width + 8 * k), ch = 20 * k;
                    var cap = new RectangleF(ib.X + (ib.Width - cw) / 2, ib.Y + (ib.Height - ch) / 2, cw, ch);
                    Color face = dark ? Color.FromArgb(62, 62, 62) : Color.White;
                    Color edge = dark ? Color.FromArgb(110, 110, 110) : Color.FromArgb(201, 201, 201);
                    using (var p = Theme.Rounded(new RectangleF(cap.X, cap.Y, cap.Width, cap.Height + 1.5f * k), 4 * k))
                    using (var b = new SolidBrush(edge)) g.FillPath(b, p);
                    using (var p = Theme.Rounded(new RectangleF(cap.X + k, cap.Y + k, cap.Width - 2 * k, cap.Height - 1.5f * k), 3.2f * k))
                    using (var b = new SolidBrush(face)) g.FillPath(b, p);
                    using (var b = new SolidBrush(text))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(glyph ?? "", capFont, b, new RectangleF(cap.X, cap.Y, cap.Width, cap.Height - k), sf);
                    return;
                }
            }
            using (var b = new SolidBrush(fore))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(glyph ?? "", iconFont, b, new RectangleF(ib.X - 4 * k, ib.Y, ib.Width + 8 * k, ib.Height), sf);
        }

    }
}
