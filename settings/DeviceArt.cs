using System;
using System.IO;
using LiteOptions.Ipc;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace LiteOptions.SettingsApp
{
    /// <summary>
    /// The device card at the top of the Mouse and Keyboard pages: a photo of the device with clickable parts,
    /// next to its name, connection, battery and firmware.
    /// </summary>
    sealed partial class DeviceHero : CardBox
    {
        // photo sizes (Assets/mouse.png is 447×790, Assets/keyboard.png 1300×580, backgrounds already cut away)
        const double MouseH = 168, MouseW = MouseH * 447 / 790, KeysW = 330, KeysH = KeysW * 580 / 1300;

        readonly bool mouse;
        readonly Grid art;
        readonly TextBlock name, status, battery, firmware, hint;
        readonly FontIcon batteryIcon;
        readonly Ellipse dot;
        readonly StackPanel batteryRow;
        public event Action<string> HotspotClicked;

        public DeviceHero(bool mouse)
        {
            this.mouse = mouse;
            Box.Padding = new Thickness(24, 18, 24, 18);

            var g = new Grid { ColumnSpacing = 32 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            double w = mouse ? MouseW : KeysW, h = mouse ? MouseH : KeysH;
            art = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(mouse ? 24 : 0, 0, mouse ? 24 : 0, 0) };
            var photo = new Image { Stretch = Stretch.Fill, Width = w, Height = h };
            try { photo.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", mouse ? "mouse.png" : "keyboard.png"))); } catch { }
            art.Children.Add(photo);
            var spots = new Canvas { Width = w, Height = h };
            if (mouse)
            {
                Hotspot(spots, 0.0268, 0.0038, 0.9485, 0.9747, "pointer", "Pointer & scrolling", w * 0.47);
                Hotspot(spots, 0.3333, 0.1684, 0.3356, 0.2051, "middle", "Middle button (press the wheel)", 10);
            }
            else
            {
                Hotspot(spots, 0.02, 0.2106, 0.9587, 0.7374, "remap", "Remap keys", 8);
                Hotspot(spots, 0.02, 0.1056, 0.9587, 0.1001, "toprow", "Top row (media keys / F1–F12)", 8);
            }
            art.Children.Add(spots);
            g.Children.Add(art);

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
        }

        public void Fill(DeviceDto d, string missing, string missingHint, bool low)
        {
            name.Text = d?.Name ?? missing;
            art.Opacity = d != null && d.Online ? 1.0 : 0.45; // greyed out while asleep or not found
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
            hint.Text = mouse ? "Click the wheel or the mouse to jump to its settings." : "Click the top row or the keys to jump to their settings.";
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

        /// <summary>A clickable part of the photo (positions are fractions of the picture); it lights up in the accent colour under the pointer.</summary>
        void Hotspot(Canvas c, double fx, double fy, double fw, double fh, string id, string label, double radius)
        {
            double cw = c.Width, ch = c.Height;
            var area = new Rectangle
            {
                Width = fw * cw, Height = fh * ch, RadiusX = radius, RadiusY = radius,
                Fill = new SolidColorBrush(Colors.Transparent), StrokeThickness = 2
            };
            Canvas.SetLeft(area, fx * cw);
            Canvas.SetTop(area, fy * ch);
            ToolTipService.SetToolTip(area, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(area, label);
            area.PointerEntered += (s, e) =>
            {
                var accent = Ui.Brush("AccentFillColorDefaultBrush") as SolidColorBrush;
                area.Stroke = accent;
                area.Fill = new SolidColorBrush(accent?.Color ?? Colors.DodgerBlue) { Opacity = 0.14 };
            };
            area.PointerExited += (s, e) => { area.Stroke = null; area.Fill = new SolidColorBrush(Colors.Transparent); };
            area.Tapped += (s, e) => HotspotClicked?.Invoke(id);
            c.Children.Add(area);
        }
    }
}
