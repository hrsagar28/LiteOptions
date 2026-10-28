using System;
using LiteOptions.Ipc;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace LiteOptions.SettingsApp
{
    /// <summary>
    /// The device card at the top of the Mouse and Keyboard pages: a drawing of the device whose parts you can click,
    /// next to its name, connection, battery and firmware.
    /// </summary>
    sealed class DeviceHero : CardBox
    {
        readonly bool mouse;
        readonly Canvas art;
        readonly TextBlock name, status, battery, firmware, hint;
        readonly FontIcon batteryIcon;
        readonly Ellipse dot;
        readonly StackPanel batteryRow;
        public event Action<string> HotspotClicked;

        public DeviceHero(bool mouse)
        {
            this.mouse = mouse;
            Box.Padding = new Thickness(20, 18, 24, 18);

            var g = new Grid { ColumnSpacing = 28 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            art = new Canvas { Width = mouse ? 150 : 270, Height = mouse ? 170 : 120 };
            var box = new Viewbox { Child = art, Height = mouse ? 150 : 108, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(box);

            name = new TextBlock { Style = Ui.Style("SubtitleTextBlockStyle"), TextWrapping = TextWrapping.Wrap };
            dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
            status = new TextBlock { Style = Ui.Style("SecondaryBody"), VerticalAlignment = VerticalAlignment.Center };
            batteryIcon = new FontIcon { FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
            battery = new TextBlock { Style = Ui.Style("BodyTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
            batteryRow = Ui.Row(8, batteryIcon, battery);
            firmware = new TextBlock { Style = Ui.Style("Secondary") };
            hint = new TextBlock { Style = Ui.Style("Secondary"), Margin = new Thickness(0, 6, 0, 0) };
            var info = Ui.Column(6, name, Ui.Row(8, dot, status), batteryRow, firmware, hint);
            info.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(info, 1);
            g.Children.Add(info);
            Box.Child = g;

            ActualThemeChanged += (s, e) => Draw();
            Loaded += (s, e) => Draw();
        }

        public void Fill(DeviceDto d, string missing, string missingHint, bool low)
        {
            name.Text = d?.Name ?? missing;
            if (d == null)
            {
                dot.Fill = Ui.Brush("TextFillColorDisabledBrush");
                status.Text = "Not found";
                batteryRow.Visibility = Visibility.Collapsed;
                firmware.Visibility = Visibility.Collapsed;
                hint.Text = missingHint;
                return;
            }
            dot.Fill = d.Online ? Ui.Brush("SystemFillColorSuccessBrush") : Ui.Brush("TextFillColorDisabledBrush");
            status.Text = (d.Online ? "Connected" : "Asleep — move it or press a key to wake it") + (string.IsNullOrEmpty(d.Connection) ? "" : " · " + d.Connection);
            batteryRow.Visibility = d.HasBattery ? Visibility.Visible : Visibility.Collapsed;
            if (d.HasBattery)
            {
                int pct = d.BatteryPercent >= 0 ? d.BatteryPercent : LevelPercent(d.BatteryLevel);
                batteryIcon.Glyph = Glyph.Battery(pct, d.Charging);
                batteryIcon.Foreground = low ? Ui.Brush("SystemFillColorCriticalBrush") : Ui.Brush("TextFillColorPrimaryBrush");
                battery.Text = (d.BatteryPercent >= 0 ? d.BatteryPercent + "%" : d.BatteryLevel + " battery") + (d.Charging ? ", charging" : low ? " — replace soon" : "");
            }
            firmware.Visibility = string.IsNullOrEmpty(d.Firmware) ? Visibility.Collapsed : Visibility.Visible;
            firmware.Text = "Firmware " + d.Firmware;
            hint.Text = "Click a part of the picture to jump to its settings.";
        }

        public static int LevelPercent(string level)
        {
            switch (level)
            {
                case "Full": return 100;
                case "Good": return 70;
                case "Low": return 20;
                case "Critical": return 5;
            }
            return 50;
        }

        // ───────────── drawing ─────────────

        bool Dark => ActualTheme == ElementTheme.Dark;

        static Color C(byte a, byte r, byte g, byte b) => ColorHelper.FromArgb(a, r, g, b);
        static Color C(uint argb) => ColorHelper.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        static Brush Vertical(Color top, Color bottom)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            b.GradientStops.Add(new GradientStop { Color = top, Offset = 0 });
            b.GradientStops.Add(new GradientStop { Color = bottom, Offset = 1 });
            return b;
        }

        static T Place<T>(T e, double x, double y) where T : UIElement
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            return e;
        }

        void Draw()
        {
            art.Children.Clear();
            if (mouse) DrawMouse(); else DrawKeyboard();
        }

        void DrawMouse()
        {
            bool dark = Dark;
            var shell = dark ? Vertical(C(0xFF5A5A62), C(0xFF34343A)) : Vertical(C(0xFFF7F7F9), C(0xFFD9D9DF));
            var edge = new SolidColorBrush(dark ? C(0x40FFFFFF) : C(0x26000000));

            art.Children.Add(Place(new Ellipse { Width = 112, Height = 22, Fill = new SolidColorBrush(C(dark ? (byte)0x55 : (byte)0x22, 0, 0, 0)) }, 19, 146));
            var body = Place(new Ellipse { Width = 104, Height = 156, Fill = shell, Stroke = edge, StrokeThickness = 1.2 }, 23, 4);
            art.Children.Add(body);
            // gloss
            art.Children.Add(Place(new Ellipse { Width = 46, Height = 52, Fill = new SolidColorBrush(C(dark ? (byte)0x18 : (byte)0x70, 255, 255, 255)) }, 36, 16));
            // split between the two buttons, and the curve where the buttons end
            art.Children.Add(new Line { X1 = 75, Y1 = 5, X2 = 75, Y2 = 62, Stroke = edge, StrokeThickness = 1.2 });
            var curve = new Microsoft.UI.Xaml.Shapes.Path
            {
                Stroke = edge, StrokeThickness = 1.2,
                Data = new PathGeometry
                {
                    Figures =
                    {
                        new PathFigure
                        {
                            StartPoint = new Point(28, 58), IsClosed = false,
                            Segments = { new BezierSegment { Point1 = new Point(52, 70), Point2 = new Point(98, 70), Point3 = new Point(122, 58) } }
                        }
                    }
                }
            };
            art.Children.Add(curve);
            // the wheel
            var wheel = Place(new Rectangle { Width = 11, Height = 26, RadiusX = 5.5, RadiusY = 5.5, Fill = Vertical(dark ? C(0xFF1C1C20) : C(0xFF4A4A52), dark ? C(0xFF0E0E10) : C(0xFF2A2A30)) }, 69.5, 24);
            art.Children.Add(wheel);
            for (int i = 0; i < 4; i++)
                art.Children.Add(new Line { X1 = 71.5, X2 = 78.5, Y1 = 29 + i * 5, Y2 = 29 + i * 5, Stroke = new SolidColorBrush(C(0x40, 255, 255, 255)), StrokeThickness = 1 });

            Hotspot(new Rect(23, 62, 104, 98), "pointer", "Pointer & scrolling", body);
            Hotspot(new Rect(58, 14, 34, 46), "middle", "Middle button (press the wheel)", wheel);
        }

        void DrawKeyboard()
        {
            bool dark = Dark;
            var shell = dark ? Vertical(C(0xFF4C4C54), C(0xFF2E2E34)) : Vertical(C(0xFFF2F2F5), C(0xFFD6D6DC));
            var keyFill = dark ? Vertical(C(0xFF2A2A30), C(0xFF1E1E22)) : Vertical(C(0xFFFFFFFF), C(0xFFF0F0F3));
            var keyEdge = new SolidColorBrush(dark ? C(0x30FFFFFF) : C(0x22000000));

            art.Children.Add(Place(new Rectangle { Width = 262, Height = 16, RadiusX = 8, RadiusY = 8, Fill = new SolidColorBrush(C(dark ? (byte)0x55 : (byte)0x1E, 0, 0, 0)) }, 4, 102));
            var body = Place(new Rectangle { Width = 262, Height = 106, RadiusX = 16, RadiusY = 16, Fill = shell, Stroke = new SolidColorBrush(dark ? C(0x40FFFFFF) : C(0x26000000)), StrokeThickness = 1.2 }, 4, 2);
            art.Children.Add(body);

            double x0 = 14, y = 9, pitch = 17.4;
            // top row: Esc, F1–F12, Del as small round keys
            var top = new System.Collections.Generic.List<Shape>();
            for (int i = 0; i < 14; i++)
            {
                var k = Place(new Ellipse { Width = 13, Height = 10, Fill = keyFill, Stroke = keyEdge, StrokeThickness = 0.8 }, x0 + i * pitch + 1, y);
                art.Children.Add(k);
                top.Add(k);
            }
            y += 14;
            // four rows of round keys, with wider keys at the ends
            double[][] rows =
            {
                new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1.2 },
                new double[] { 1.3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
                new double[] { 1.6, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1.75 },
                new double[] { 2.1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2.3 },
            };
            foreach (var row in rows)
            {
                double x = x0;
                foreach (var w in row)
                {
                    double width = w * pitch - 3.4;
                    art.Children.Add(Place(new Rectangle { Width = width, Height = 14, RadiusX = 7, RadiusY = 7, Fill = keyFill, Stroke = keyEdge, StrokeThickness = 0.8 }, x, y));
                    x += w * pitch;
                }
                y += 16.5;
            }
            // bottom row with the space bar and arrows
            double bx = x0;
            foreach (var w in new double[] { 1, 1, 1, 1.2, 5.6, 1.2, 1, 1, 1, 1 })
            {
                double width = w * pitch - 3.4;
                art.Children.Add(Place(new Rectangle { Width = width, Height = 14, RadiusX = 7, RadiusY = 7, Fill = keyFill, Stroke = keyEdge, StrokeThickness = 0.8 }, bx, y));
                bx += w * pitch;
            }

            Hotspot(new Rect(8, 22, 254, 84), "remap", "Remap keys", body);
            Hotspot(new Rect(8, 4, 254, 18), "toprow", "Top row (media keys / F1–F12)", null, top);
        }

        /// <summary>A clickable area over part of the drawing; the part lights up in the accent colour while the pointer is on it.</summary>
        void Hotspot(Rect r, string id, string label, Shape highlight, System.Collections.Generic.List<Shape> many = null)
        {
            var area = Place(new Rectangle { Width = r.Width, Height = r.Height, Fill = new SolidColorBrush(Colors.Transparent), RadiusX = 8, RadiusY = 8 }, r.X, r.Y);
            ToolTipService.SetToolTip(area, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(area, label);
            var accent = Ui.Brush("AccentFillColorDefaultBrush");
            Brush oldStroke = highlight?.Stroke;
            double oldThick = highlight?.StrokeThickness ?? 0;
            area.PointerEntered += (s, e) =>
            {
                if (highlight != null) { highlight.Stroke = accent; highlight.StrokeThickness = 2.2; }
                if (many != null) foreach (var m in many) m.Stroke = accent;
            };
            area.PointerExited += (s, e) =>
            {
                if (highlight != null) { highlight.Stroke = oldStroke; highlight.StrokeThickness = oldThick; }
                if (many != null) foreach (var m in many) m.Stroke = new SolidColorBrush(Dark ? C(0x30FFFFFF) : C(0x22000000));
            };
            area.Tapped += (s, e) => HotspotClicked?.Invoke(id);
            art.Children.Add(area);
        }
    }
}
