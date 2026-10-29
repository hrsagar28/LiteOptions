using System;
using System.Collections.Generic;
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
    /// The device card at the top of the Mouse and Keyboard pages: name, connection, battery and firmware in a strip,
    /// over a large photo with labels pointing at its parts. Each label shows the current setting and jumps to it.
    /// In a narrow window the photo and its labels shrink together.
    /// </summary>
    sealed partial class DeviceHero : CardBox
    {
        // the drawing area and where the photo sits in it (Assets/mouse.png is 447×790, Assets/keyboard.png 1100×489)
        const double MouseBoxW = 420, MouseBoxH = 340, KeysBoxW = 700, KeysBoxH = 300;

        readonly bool mouse;
        readonly TextBlock name, caption;
        readonly StackPanel chips;
        readonly Grid header;
        readonly Image photo;
        readonly Canvas canvas;
        readonly Dictionary<string, (string title, TextBlock value, Button button, UIElement[] parts)> callouts = new Dictionary<string, (string, TextBlock, Button, UIElement[])>();
        public event Action<string> HotspotClicked;

        static BitmapImage mousePhoto, keysPhoto;

        /// <summary>The device photo (loaded once and shared).</summary>
        public static BitmapImage Photo(bool mouse)
        {
            try
            {
                if (mouse) return mousePhoto ??= new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "mouse.png")));
                return keysPhoto ??= new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "keyboard.png")));
            }
            catch { return null; }
        }

        public DeviceHero(bool mouse)
        {
            this.mouse = mouse;
            Box.Padding = new Thickness(0);
            Box.MinHeight = 0;

            name = new TextBlock { Style = Ui.Style("SubtitleTextBlockStyle"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            header = new Grid { Padding = new Thickness(20, 14, 20, 14), ColumnSpacing = 16, RowSpacing = 8 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.Children.Add(name);
            Grid.SetColumn(chips, 1);
            header.Children.Add(chips);

            double w = mouse ? MouseBoxW : KeysBoxW, h = mouse ? MouseBoxH : KeysBoxH;
            canvas = new Canvas { Width = w, Height = h };
            photo = new Image { Source = Photo(mouse), Stretch = Stretch.Uniform };
            if (mouse) { photo.Height = 300; photo.Width = 300 * 447.0 / 790; Canvas.SetLeft(photo, 125); Canvas.SetTop(photo, 20); }
            else { photo.Width = 500; photo.Height = 500 * 489.0 / 1100; Canvas.SetLeft(photo, 170); Canvas.SetTop(photo, 64); }
            canvas.Children.Add(photo);

            if (mouse)
            {
                Callout("middle", "Middle button", 205, 92, 214, 96, 66, 1, 280, 76);
                Callout("scroll", "Scroll wheel", 205, 120, 122, 124, 84, 1, 0, 104, 120);
                Callout("pointer", "Sensor", 222, 232, 231, 236, 49, 1, 280, 216);
            }
            else
            {
                Callout("toprow", "Top row", 416, 93, 420, 44, 1, 50, 356, 4, 128, center: true);
                Callout("remap", "Remapped keys", 194, 93, 130, 97, 64, 1, 0, 77, 128);
                Callout("keysoff", "Turned off", 204, 185, 130, 189, 74, 1, 0, 169, 128);
            }

            var view = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = w, MaxHeight = h, HorizontalAlignment = HorizontalAlignment.Center };
            caption = new TextBlock { Style = Ui.Style("Secondary"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(16, 0, 16, 0) };
            var stage = new Border
            {
                Background = Ui.Brush("SolidBackgroundFillColorSecondaryBrush"),
                BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(0, 0, 6, 6),
                Padding = new Thickness(12, 8, 12, 12),
                Child = Ui.Column(4, view, caption),
            };
            Box.Child = Ui.Column(0, header, stage);
            SizeChanged += (s, e) => Reflow(e.NewSize.Width);
        }

        /// <summary>A label pointing at a part of the photo: a dot on the part, a thin line, and a button with the setting.</summary>
        void Callout(string id, string title, double dotX, double dotY, double lineX, double lineY, double lineW, double lineH,
                     double labelX, double labelY, double width = double.NaN, bool center = false)
        {
            var accent = Ui.Brush("AccentFillColorDefaultBrush");
            var halo = new Ellipse { Width = 15, Height = 15, Fill = accent, Opacity = 0.25 };
            Canvas.SetLeft(halo, dotX - 3); Canvas.SetTop(halo, dotY - 3);
            var dot = new Ellipse { Width = 9, Height = 9, Fill = accent };
            Canvas.SetLeft(dot, dotX); Canvas.SetTop(dot, dotY);
            var line = new Rectangle { Width = lineW, Height = lineH, Fill = Ui.Brush("TextFillColorTertiaryBrush") };
            Canvas.SetLeft(line, lineX); Canvas.SetTop(line, lineY);

            var t = new TextBlock { Text = title, Style = Ui.Style("Secondary"), TextWrapping = TextWrapping.NoWrap };
            var v = new TextBlock { Style = Ui.Style("CaptionTextBlockStyle"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            var align = center ? HorizontalAlignment.Center : double.IsNaN(width) ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            t.HorizontalAlignment = align; v.HorizontalAlignment = align;
            var b = new Button
            {
                Content = Ui.Column(0, t, v),
                Padding = new Thickness(10, 5, 10, 6), MinWidth = 0, MinHeight = 0,
                CornerRadius = new CornerRadius(6),
                Background = Ui.Brush("CardBackgroundFillColorDefaultBrush"),
                BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"),
                HorizontalContentAlignment = align,
            };
            if (!double.IsNaN(width)) b.Width = width;
            Canvas.SetLeft(b, labelX); Canvas.SetTop(b, labelY);
            b.Click += (s, e) => HotspotClicked?.Invoke(id);
            ToolTipService.SetToolTip(b, "Go to " + title.ToLowerInvariant());
            canvas.Children.Add(line);
            canvas.Children.Add(halo);
            canvas.Children.Add(dot);
            canvas.Children.Add(b);
            callouts[id] = (title, v, b, new UIElement[] { line, halo, dot, b });
        }

        /// <summary>Set a label's value; null hides that label (for a setting the device doesn't have).</summary>
        public void SetCallout(string id, string value)
        {
            if (!callouts.TryGetValue(id, out var c)) return;
            c.value.Text = value ?? "";
            foreach (var p in c.parts) p.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(c.button, c.title + ": " + (value ?? ""));
        }

        /// <summary>Narrow: put the chips under the name instead of beside it.</summary>
        void Reflow(double width)
        {
            bool stack = width < 620;
            Grid.SetRow(chips, stack ? 1 : 0);
            Grid.SetColumn(chips, stack ? 0 : 1);
        }

        public void Fill(DeviceDto d, string missing, string missingHint, bool low)
        {
            name.Text = d?.Name ?? missing;
            photo.Opacity = d != null && d.Online ? 1.0 : 0.45; // greyed out while asleep or not found
            chips.Children.Clear();
            caption.Text = d == null ? missingHint : "Click a label to jump to its setting";
            if (d == null) { chips.Children.Add(Chip(Ui.Brush("TextFillColorDisabledBrush"), "Not found")); return; }

            chips.Children.Add(Chip(Ui.Brush(d.Online ? "SystemFillColorSuccessBrush" : "TextFillColorDisabledBrush"),
                (d.Online ? "Connected" : "Asleep — move it or press a key") + (string.IsNullOrEmpty(d.Connection) ? "" : " · " + d.Connection)));
            if (d.HasBattery)
            {
                int pct = d.BatteryPercent >= 0 ? d.BatteryPercent : LevelPercent(d.BatteryLevel);
                var ring = Ring.Make(14, 2.5, pct / 100.0, Ring.BatteryBrush(pct, d.Charging, low));
                string text = (d.BatteryPercent >= 0 ? d.BatteryPercent + "%" : d.BatteryLevel) + (d.Charging ? ", charging" : low ? " — replace soon" : "");
                chips.Children.Add(new Border { Style = Ui.Style("Chip"), Child = Ui.Row(6, ring, Ui.Text(text, "CaptionTextBlockStyle", wrap: false)) });
            }
            if (!string.IsNullOrEmpty(d.Firmware))
                chips.Children.Add(new Border { Style = Ui.Style("Chip"), Child = Ui.Text("Firmware " + d.Firmware, "CaptionTextBlockStyle", wrap: false) });
        }

        static Border Chip(Brush dot, string text) => new Border
        {
            Style = Ui.Style("Chip"),
            Child = Ui.Row(6, new Ellipse { Width = 8, Height = 8, Fill = dot, VerticalAlignment = VerticalAlignment.Center }, Ui.Text(text, "CaptionTextBlockStyle", wrap: false)),
        };

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
    }
}
