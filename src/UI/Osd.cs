using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace LiteOptions.UI
{
    /// <summary>
    /// Small fading pop-up near the bottom of the screen (like Windows' volume flyout).
    /// A click-through layered window with per-pixel alpha, rendered at the DPI of the monitor it appears on.
    /// Must be used from the UI thread.
    /// </summary>
    sealed class Osd : NativeWindow, IDisposable
    {
        static Osd instance;
        public static bool Enabled = true;

        readonly Timer timer = new Timer { Interval = 15 };
        Bitmap frame;
        IntPtr hBitmap, memDc;
        Native.POINT pos;
        Native.SIZE size;
        int alpha;
        int phase; // 0 hidden, 1 fading in, 2 holding, 3 fading out
        DateTime holdUntil;

        public static void Show(string glyph, string title, string subtitle = null, double? progress = null)
        {
            if (!Enabled || !Native.IsWindows) return;
            try
            {
                if (instance == null) instance = new Osd();
                instance.Display(glyph, title, subtitle, progress);
            }
            catch (Exception ex) { Logger.Write("pop-up failed: " + ex.Message); }
        }

        Osd()
        {
            CreateHandle(new CreateParams
            {
                Caption = "LiteOptions pop-up",
                Style = Native.WS_POPUP,
                ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST,
            });
            timer.Tick += (s, e) => Step();
        }

        void Display(string glyph, string title, string subtitle, double? progress)
        {
            var screen = Screen.FromPoint(Cursor.Position);
            int dpi = Native.DpiAt(new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2), Theme.SystemDpi);
            var bmp = Render(dpi / 96f, glyph, title, subtitle, progress, Theme.Dark);
            SetFrame(bmp);
            var wa = screen.WorkingArea;
            pos = new Native.POINT { X = wa.Left + (wa.Width - size.cx) / 2, Y = wa.Bottom - size.cy - (int)(36 * dpi / 96f) };
            if (phase == 0 || phase == 3) phase = 1; // fade in (or back in, if it was fading out)
            holdUntil = DateTime.UtcNow.AddMilliseconds(1300);
            Apply();
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            timer.Start();
        }

        void Step()
        {
            switch (phase)
            {
                case 1: alpha = Math.Min(255, alpha + 45); if (alpha == 255) phase = 2; break;
                case 2: if (DateTime.UtcNow > holdUntil) phase = 3; return;
                case 3:
                    alpha = Math.Max(0, alpha - 22);
                    if (alpha == 0) { phase = 0; timer.Stop(); Native.ShowWindow(Handle, Native.SW_HIDE); FreeFrame(); return; }
                    break;
                default: timer.Stop(); return;
            }
            Apply();
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

        void Apply()
        {
            if (memDc == IntPtr.Zero) return;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = (byte)alpha, AlphaFormat = Native.AC_SRC_ALPHA };
            Native.UpdateLayeredWindow(Handle, screenDc, ref pos, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }

        /// <summary>Draws the pop-up (public so the preview can render it on Linux).</summary>
        public static Bitmap Render(float k, string glyph, string title, string subtitle, double? progress, bool dark)
        {
            Color bg = dark ? Color.FromArgb(246, 44, 44, 44) : Color.FromArgb(250, 249, 249, 249);
            Color border = dark ? Color.FromArgb(255, 64, 64, 64) : Color.FromArgb(255, 222, 222, 222);
            Color text = dark ? Color.White : Color.FromArgb(26, 26, 26);
            Color muted = dark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(93, 93, 93);
            Color accent = dark ? Color.FromArgb(76, 194, 255) : Color.FromArgb(0, 95, 184);
            Color track = dark ? Color.FromArgb(90, 90, 90) : Color.FromArgb(205, 205, 205);

            using (var titleFont = new Font("Segoe UI Semibold", 15f * k, GraphicsUnit.Pixel))
            using (var subFont = new Font("Segoe UI", 12.5f * k, GraphicsUnit.Pixel))
            using (var iconFont = new Font(Theme.IconFamily, 22f * k, GraphicsUnit.Pixel))
            using (var measure = new Bitmap(1, 1))
            using (var mg = Graphics.FromImage(measure))
            {
                mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float tw = mg.MeasureString(title, titleFont).Width;
                float sw = string.IsNullOrEmpty(subtitle) ? 0 : mg.MeasureString(subtitle, subFont).Width;
                float pad = 18 * k, icon = 40 * k, shadow = 14 * k;
                float w = Math.Max(240 * k, Math.Min(460 * k, pad + icon + Math.Max(tw, sw) + pad + 4 * k));
                float h = (string.IsNullOrEmpty(subtitle) && progress == null ? 58 : 74) * k;
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
                        var r = RectangleF.Inflate(box, i * 1.6f * k, i * 1.6f * k);
                        r.Offset(0, 3 * k);
                        using (var p = Theme.Rounded(r, 10 * k + i * 1.6f * k))
                        using (var b = new SolidBrush(Color.FromArgb(dark ? 16 : 9, 0, 0, 0))) g.FillPath(b, p);
                    }
                    using (var p = Theme.Rounded(box, 10 * k))
                    {
                        using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                        using (var pen = new Pen(border, Math.Max(1f, k))) g.DrawPath(pen, p);
                    }
                    var iconRect = new RectangleF(box.X + pad - 4 * k, box.Y, icon, h);
                    using (var b = new SolidBrush(accent))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(glyph ?? "", iconFont, b, iconRect, sf);
                    float x = box.X + pad + icon;
                    float textW = box.Right - pad - x;
                    bool two = !string.IsNullOrEmpty(subtitle) || progress != null;
                    float ty = two ? box.Y + 13 * k : box.Y + (h - titleFont.Height) / 2;
                    using (var b = new SolidBrush(text))
                    using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                        g.DrawString(title, titleFont, b, new RectangleF(x, ty, textW, titleFont.Height + 4), sf);
                    if (progress != null)
                    {
                        float by = box.Y + 46 * k;
                        using (var tp = Theme.Rounded(new RectangleF(x, by, textW, 4 * k), 2 * k))
                        using (var tb = new SolidBrush(track)) g.FillPath(tb, tp);
                        float fw = Math.Max(4 * k, (float)(textW * Math.Max(0, Math.Min(1, progress.Value))));
                        using (var fp = Theme.Rounded(new RectangleF(x, by, fw, 4 * k), 2 * k))
                        using (var fb = new SolidBrush(accent)) g.FillPath(fb, fp);
                        if (!string.IsNullOrEmpty(subtitle))
                            using (var b = new SolidBrush(muted))
                                g.DrawString(subtitle, subFont, b, x + textW - mg.MeasureString(subtitle, subFont).Width + 4 * k, box.Y + 13 * k + 2 * k);
                    }
                    else if (!string.IsNullOrEmpty(subtitle))
                        using (var b = new SolidBrush(muted))
                        using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                            g.DrawString(subtitle, subFont, b, new RectangleF(x, box.Y + 39 * k, textW, subFont.Height + 4), sf);
                }
                return bmp;
            }
        }

        public void Dispose()
        {
            timer.Dispose();
            FreeFrame();
            DestroyHandle();
        }

        public static void Shutdown() { instance?.Dispose(); instance = null; }
    }
}
