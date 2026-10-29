using System;
using System.Collections.Generic;
using System.Linq;
using LiteOptions.Ipc;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace LiteOptions.SettingsApp
{
    /// <summary>
    /// One card holding several rows with thin lines between them, like the groups in Windows 11 Settings.
    /// Rows that are hidden take their line with them.
    /// </summary>
    sealed partial class SettingsGroup : UserControl
    {
        readonly Border box;
        readonly StackPanel rows;
        readonly List<(Border wrap, UIElement row)> items = new List<(Border, UIElement)>();

        public SettingsGroup()
        {
            IsTabStop = false;
            rows = new StackPanel();
            box = new Border { Style = Ui.Style("Group"), Child = rows };
            Content = box;
        }

        public T Add<T>(T row) where T : UIElement
        {
            if (row is SettingCard c) c.AsRow();
            var wrap = new Border { Child = row, BorderBrush = Ui.Brush("DividerStrokeColorDefaultBrush") };
            items.Add((wrap, row));
            rows.Children.Add(wrap);
            row.RegisterPropertyChangedCallback(VisibilityProperty, (o, dp) => Refresh());
            Refresh();
            return row;
        }

        public void Clear()
        {
            items.Clear();
            rows.Children.Clear();
            Refresh();
        }

        public void Refresh()
        {
            bool first = true;
            foreach (var (wrap, row) in items)
            {
                bool shown = row.Visibility == Visibility.Visible;
                wrap.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                if (!shown) continue;
                wrap.BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0);
                first = false;
            }
            Visibility = first ? Visibility.Collapsed : Visibility.Visible; // nothing to show: hide the whole card
        }
    }

    /// <summary>A row of two to four options where exactly one is chosen (Windows 11 style segmented buttons).</summary>
    sealed partial class Segmented : UserControl
    {
        readonly StackPanel panel;
        readonly List<ToggleButton> buttons = new List<ToggleButton>();
        int selected = -1;
        public event Action<int> SelectionChanged;

        public Segmented(string label, params string[] options)
        {
            IsTabStop = false;
            panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            Content = new Border
            {
                Background = Ui.Brush("ControlAltFillColorSecondaryBrush"),
                BorderBrush = Ui.Brush("ControlStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                Child = panel,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(panel, label);
            SetOptions(options);
        }

        public void SetOptions(params string[] options)
        {
            panel.Children.Clear();
            buttons.Clear();
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var b = new ToggleButton { Content = options[i], Padding = new Thickness(14, 4, 14, 5), MinHeight = 28, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
                Brush clear = new SolidColorBrush(Colors.Transparent);
                b.Resources["ToggleButtonBackground"] = clear;
                b.Resources["ToggleButtonBackgroundPointerOver"] = Ui.Brush("SubtleFillColorSecondaryBrush");
                b.Resources["ToggleButtonBackgroundPressed"] = Ui.Brush("SubtleFillColorTertiaryBrush");
                b.Resources["ToggleButtonBackgroundChecked"] = Ui.Brush("ControlFillColorDefaultBrush");
                b.Resources["ToggleButtonBackgroundCheckedPointerOver"] = Ui.Brush("ControlFillColorSecondaryBrush");
                b.Resources["ToggleButtonBackgroundCheckedPressed"] = Ui.Brush("ControlFillColorTertiaryBrush");
                foreach (var k in new[] { "ToggleButtonForeground", "ToggleButtonForegroundPointerOver", "ToggleButtonForegroundPressed", "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed" })
                    b.Resources[k] = Ui.Brush("TextFillColorPrimaryBrush");
                foreach (var k in new[] { "ToggleButtonBorderBrush", "ToggleButtonBorderBrushPointerOver", "ToggleButtonBorderBrushPressed" })
                    b.Resources[k] = clear;
                foreach (var k in new[] { "ToggleButtonBorderBrushChecked", "ToggleButtonBorderBrushCheckedPointerOver", "ToggleButtonBorderBrushCheckedPressed" })
                    b.Resources[k] = Ui.Brush("ControlElevationBorderBrush");
                b.Click += (s, e) => { Select(index); SelectionChanged?.Invoke(index); };
                buttons.Add(b);
                panel.Children.Add(b);
            }
            Select(selected);
        }

        public int SelectedIndex { get => selected; set => Select(value); }

        void Select(int index)
        {
            selected = index;
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].IsChecked = i == index;
                buttons[i].FontWeight = i == index ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            }
        }
    }

    static class Ring
    {
        /// <summary>Theme brush for a battery level: green, amber at 30% or less, red when low, accent while charging.</summary>
        public static Brush BatteryBrush(int percent, bool charging, bool low) =>
            Ui.Brush(charging ? "AccentFillColorDefaultBrush" : low ? "SystemFillColorCriticalBrush" : percent <= 30 ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush");

        /// <summary>A circular gauge: a faint full circle with an arc over it from 12 o'clock, clockwise.</summary>
        public static Grid Make(double size, double thickness, double fraction, Brush stroke)
        {
            var g = new Grid { Width = size, Height = size };
            double r = (size - thickness) / 2, c = size / 2;
            g.Children.Add(new Ellipse { Width = size, Height = size, Stroke = Ui.Brush("ControlStrongStrokeColorDisabledBrush"), StrokeThickness = thickness, Opacity = 0.55 });
            double f = Math.Max(0, Math.Min(1, fraction));
            if (f >= 0.999)
                g.Children.Add(new Ellipse { Width = size, Height = size, Stroke = stroke, StrokeThickness = thickness });
            else if (f > 0.005)
            {
                double a = f * 2 * Math.PI;
                var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false, IsFilled = false };
                fig.Segments.Add(new ArcSegment
                {
                    Point = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)),
                    Size = new Size(r, r),
                    IsLargeArc = f > 0.5,
                    SweepDirection = SweepDirection.Clockwise,
                });
                var geo = new PathGeometry();
                geo.Figures.Add(fig);
                g.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
                {
                    Data = geo, Stroke = stroke, StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                });
            }
            return g;
        }

        /// <summary>The device's photo inside a ring showing how full its battery is (just the photo when the battery is unknown).</summary>
        public static Grid Device(DeviceDto d, bool mouse, double size, bool low)
        {
            int pct = d == null || !d.HasBattery ? -1 : d.BatteryPercent >= 0 ? d.BatteryPercent : DeviceHero.LevelPercent(d.BatteryLevel);
            double thickness = Math.Max(2.5, size / 20);
            var g = pct >= 0 && d.Online ? Make(size, thickness, pct / 100.0, BatteryBrush(pct, d.Charging, low))
                                         : Make(size, thickness, 0, Ui.Brush("TextFillColorDisabledBrush"));
            var img = new Image
            {
                Source = DeviceHero.Photo(mouse),
                Stretch = Stretch.Uniform,
                Width = mouse ? size * 0.42 : size * 0.68,
                Height = mouse ? size * 0.62 : size * 0.4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = d != null && d.Online ? 1.0 : 0.45,
            };
            g.Children.Add(img);
            string name = d?.Name ?? (mouse ? "Mouse" : "Keyboard");
            ToolTipService.SetToolTip(g, pct >= 0 ? name + ": " + pct + "% battery" + (d.Charging ? ", charging" : "") : name);
            return g;
        }
    }

    /// <summary>Easy-Switch channels as three tiles; this computer's channel is outlined in the accent colour.</summary>
    static class Channels
    {
        public static Grid Make() => new Grid { ColumnSpacing = 8 };

        public static void Fill(Grid grid, DeviceDto d, bool mouse)
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            var hosts = d?.Hosts;
            if (hosts == null || hosts.Count == 0) return;
            int col = 0;
            foreach (var h in hosts.OrderBy(h => h.Number))
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var t = Tile(h, h.Number - 1 == d.CurrentHost, mouse);
                Grid.SetColumn(t, col++);
                grid.Children.Add(t);
            }
        }

        static FrameworkElement Tile(HostDto h, bool current, bool mouse)
        {
            string name = h.Name ?? "";
            bool empty = !h.Paired && !h.Unknown && !current;
            var badgeText = new TextBlock
            {
                Text = h.Number.ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ui.Brush(current ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"),
            };
            var badge = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Child = badgeText, HorizontalAlignment = HorizontalAlignment.Left,
                Background = current ? Ui.Brush("AccentFillColorDefaultBrush") : new SolidColorBrush(Colors.Transparent),
                BorderBrush = Ui.Brush("TextFillColorSecondaryBrush"), BorderThickness = new Thickness(current ? 0 : 1),
            };
            bool phone = name.IndexOf("phone", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("pixel", StringComparison.OrdinalIgnoreCase) >= 0
                      || name.IndexOf("galaxy", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("ipad", StringComparison.OrdinalIgnoreCase) >= 0;
            var icon = new FontIcon
            {
                Glyph = phone ? "" : "", FontSize = 18,
                Foreground = Ui.Brush(current ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"),
                Visibility = empty ? Visibility.Collapsed : Visibility.Visible,
            };
            var top = new Grid();
            top.Children.Add(badge);
            icon.HorizontalAlignment = HorizontalAlignment.Right;
            top.Children.Add(icon);

            string title = name.Length > 0 ? name : current ? "This computer" : h.Unknown ? "Channel " + h.Number : empty ? "Nothing paired" : "Paired";
            string sub = current ? "This computer"
                       : empty ? (mouse ? "Hold the button underneath to pair" : "Hold F" + h.Number + " to pair")
                       : h.Unknown ? "This device doesn't say" : phone ? "Phone · Bluetooth" : "Bluetooth";
            var titleText = Ui.Text(title, empty ? "SecondaryBody" : "BodyStrongTextBlockStyle", wrap: false);
            var subText = Ui.Text(sub, "Secondary", wrap: false);
            if (current) subText.Foreground = Ui.Brush("AccentTextFillColorPrimaryBrush");
            var tile = new Border
            {
                Style = Ui.Style("Tile"),
                Child = Ui.Column(10, top, Ui.Column(0, titleText, subText)),
            };
            if (current) { tile.BorderBrush = Ui.Brush("AccentFillColorDefaultBrush"); tile.BorderThickness = new Thickness(2); tile.Padding = new Thickness(15, 13, 15, 13); }
            if (empty) tile.Background = new SolidColorBrush(Colors.Transparent);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, "Channel " + h.Number + ": " + title + (current ? ", this computer" : ""));
            return tile;
        }
    }

    /// <summary>Tick boxes for the keys the keyboard can switch off itself, each with a drawing of the key.</summary>
    sealed partial class KeysOff : UserControl
    {
        static readonly (int bit, string cap, string name)[] all =
        {
            (0x01, "caps lock", "Caps Lock"), (0x08, "ins", "Insert"), (0x10, "start", "Windows key"), (0x04, "scr lk", "Scroll Lock"), (0x02, "num lock", "Num Lock"),
        };

        readonly Grid grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        readonly List<(int bit, CheckBox box, Border tile)> boxes = new List<(int, CheckBox, Border)>();
        bool loading;
        public event Action<int> Changed;

        public KeysOff() { IsTabStop = false; Content = grid; }

        /// <summary>Show the keys this keyboard supports, ticked where they're switched off.</summary>
        public void Load(int capabilities, int disabled)
        {
            loading = true;
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            boxes.Clear();
            var keys = all.Where(k => (capabilities & k.bit) != 0).ToList();
            int cols = Math.Max(1, Math.Min(4, keys.Count));
            for (int i = 0; i < cols; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Clear();
            for (int i = 0; i < (keys.Count + cols - 1) / cols; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                var cap = Ui.Cap(k.cap);
                var box = new CheckBox { IsChecked = disabled >= 0 && (disabled & k.bit) != 0, MinWidth = 0, Padding = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
                var label = Ui.Text(k.name, "BodyTextBlockStyle", wrap: false);
                label.VerticalAlignment = VerticalAlignment.Center;
                cap.VerticalAlignment = VerticalAlignment.Center;
                var row = Ui.Row(10, cap, label);
                row.VerticalAlignment = VerticalAlignment.Center;
                box.Content = row;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "Turn off " + k.name);
                var tile = new Border { Style = Ui.Style("Tile"), Padding = new Thickness(12, 8, 12, 8), Child = box };
                box.Checked += (s, e) => Toggled();
                box.Unchecked += (s, e) => Toggled();
                Grid.SetColumn(tile, i % cols);
                Grid.SetRow(tile, i / cols);
                grid.Children.Add(tile);
                boxes.Add((k.bit, box, tile));
            }
            Paint();
            loading = false;
        }

        void Toggled()
        {
            Paint();
            if (loading) return;
            int mask = 0;
            foreach (var b in boxes) if (b.box.IsChecked == true) mask |= b.bit;
            Changed?.Invoke(mask);
        }

        void Paint()
        {
            foreach (var b in boxes)
            {
                bool on = b.box.IsChecked == true;
                b.tile.BorderBrush = Ui.Brush(on ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush");
            }
        }

        public string Summary(int mask)
        {
            var names = all.Where(k => (mask & k.bit) != 0).Select(k => k.name).ToList();
            return names.Count == 0 ? "None" : names.Count == 1 ? names[0] : names.Count + " keys";
        }
    }
}
