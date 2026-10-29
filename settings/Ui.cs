using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteOptions.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace LiteOptions.SettingsApp
{
    /// <summary>Segoe Fluent Icons code points.</summary>
    static class Glyph
    {
        public const string Mouse = "", Keyboard = "", Settings = "", Up = "", Down = "", Left = "", Right = "",
            Touch = "", Edit = "", Add = "", Delete = "", Refresh = "", Copy = "", Folder = "",
            Power = "", Pause = "", Info = "", Warning = "", Check = "", Speed = "", Scroll = "",
            Swap = "", Message = "", Setup = "", Diagnostic = "", Apps = "", App = "", Forward = "",
            Record = "", Bluetooth = "", BatteryUnknown = "", Link = "", OpenFile = "", Text = "", Window = "";

        /// <summary>Battery0..Battery10 (and the charging set).</summary>
        public static string Battery(int percent, bool charging)
        {
            int n = Math.Max(0, Math.Min(10, (int)Math.Round(percent / 10.0)));
            return ((char)((charging ? 0xE85B : 0xE850) + n)).ToString();
        }
    }

    static class Ui
    {
        public static Style Style(string key) => (Style)Application.Current.Resources[key];
        public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

        public static FontIcon Icon(string glyph, double size = 16) => new FontIcon { Glyph = glyph, FontSize = size };

        /// <summary>
        /// A small drawn battery (outline, coloured fill, nub) — the font's battery glyphs look odd at small sizes.
        /// Green when fine, amber at 30% or less, red when low; accent colour while charging.
        /// </summary>
        public static FrameworkElement BatteryShape(int percent, bool charging, bool low, double height = 10)
        {
            int p = Math.Max(0, Math.Min(100, percent));
            double w = Math.Round(height * 2.1), pad = 1.5;
            string fillKey = charging ? "AccentFillColorDefaultBrush" : low ? "SystemFillColorCriticalBrush" : p <= 30 ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush";
            var outline = new Border
            {
                Width = w, Height = height,
                CornerRadius = new CornerRadius(height * 0.28),
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("TextFillColorSecondaryBrush"),
                Padding = new Thickness(pad),
            };
            double inner = w - 2 - 2 * pad;
            outline.Child = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = p == 0 ? 0 : Math.Max(1.5, inner * p / 100.0),
                CornerRadius = new CornerRadius(height * 0.12),
                Background = Brush(fillKey),
            };
            var nub = new Border
            {
                Width = Math.Max(1.5, height * 0.16), Height = height * 0.45,
                Margin = new Thickness(1, 0, 0, 0),
                CornerRadius = new CornerRadius(0, 1, 1, 0),
                Background = Brush("TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(outline);
            row.Children.Add(nub);
            ToolTipService.SetToolTip(row, p + "%" + (charging ? ", charging" : ""));
            return row;
        }

        public static TextBlock Text(string text, string style = "BodyTextBlockStyle", bool wrap = true)
        {
            var t = new TextBlock { Text = text ?? "", Style = Style(style) };
            t.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (!wrap) t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        public static TextBlock Header(string text) => new TextBlock { Text = text, Style = Style("SectionHeader") };

        public static StackPanel Row(double spacing, params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
            foreach (var i in items) if (i != null) p.Children.Add(i);
            return p;
        }

        public static StackPanel Column(double spacing, params UIElement[] items)
        {
            var p = new StackPanel { Spacing = spacing };
            foreach (var i in items) if (i != null) p.Children.Add(i);
            return p;
        }

        public static Button Button(string text, string glyph = null, bool accent = false, RoutedEventHandler click = null)
        {
            var b = new Button();
            if (glyph != null) b.Content = Row(8, Icon(glyph, 14), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            else b.Content = text;
            if (accent) b.Style = Style("AccentButtonStyle");
            if (click != null) b.Click += click;
            return b;
        }

        public static Button IconButton(string glyph, string tip, RoutedEventHandler click)
        {
            var b = new Button { Content = Icon(glyph, 14), Style = Style("SubtleButton") };
            ToolTipService.SetToolTip(b, tip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, tip);
            b.Click += click;
            return b;
        }

        /// <summary>A button with red text, for things like Uninstall.</summary>
        public static Button DangerButton(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text };
            var red = Brush("SystemFillColorCriticalBrush");
            foreach (var k in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed" }) b.Resources[k] = red;
            b.Click += click;
            return b;
        }

        public static ToggleSwitch Toggle() => new ToggleSwitch { Style = Style("WideToggle") };

        // ── key caps ──

        public static StackPanel KeyCaps(int mods, int vk, bool large = false)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var parts = new List<string>();
            if ((mods & (int)Mods.Ctrl) != 0) parts.Add("Ctrl");
            if ((mods & (int)Mods.Shift) != 0) parts.Add("Shift");
            if ((mods & (int)Mods.Alt) != 0) parts.Add("Alt");
            if ((mods & (int)Mods.Win) != 0) parts.Add("Win");
            parts.Add(KeyNames.Key(vk));
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) row.Children.Add(new TextBlock { Text = "+", VerticalAlignment = VerticalAlignment.Center, Style = Style("Secondary") });
                row.Children.Add(Cap(parts[i], large));
            }
            return row;
        }

        public static Border Cap(string label, bool large = false)
        {
            var t = new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = large ? 16 : 13 };
            var b = new Border { Style = Style("KeyCap"), Child = t };
            if (large) { b.Padding = new Thickness(14, 6, 14, 7); b.MinWidth = 44; }
            return b;
        }

        // ── dialogs ──

        public static XamlRoot Root;
        public static ElementTheme Theme = ElementTheme.Default;
        public static ContentDialog Current;

        public static ContentDialog Dialog(string title, object content, string primary, string close = "Cancel", string secondary = null)
        {
            var d = new ContentDialog
            {
                Title = title, Content = content, PrimaryButtonText = primary ?? "", CloseButtonText = close ?? "",
                SecondaryButtonText = secondary ?? "", DefaultButton = primary != null ? ContentDialogButton.Primary : ContentDialogButton.Close,
                RequestedTheme = Theme
            };
            var s = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
            if (s != null) d.Style = s;
            return d;
        }

        /// <summary>Show a dialog (WinUI allows one at a time; a second request waits for the first to close).</summary>
        public static async Task<ContentDialogResult> ShowAsync(ContentDialog d)
        {
            while (Current != null || Root == null) await Task.Delay(100);
            Current = d;
            d.XamlRoot = Root;
            try { return await d.ShowAsync(); }
            catch (Exception ex) { try { Logger.Write("dialog failed: " + ex.Message); } catch { } return ContentDialogResult.None; }
            finally { Current = null; }
        }

        public static async Task<bool> Confirm(string title, string message, string ok, bool danger = false)
        {
            var d = Dialog(title, Text(message), ok);
            if (danger)
            {
                var red = Brush("SystemFillColorCriticalBrush");
                d.Resources["AccentButtonBackground"] = red;
                d.Resources["AccentButtonBackgroundPointerOver"] = red;
            }
            return await ShowAsync(d) == ContentDialogResult.Primary;
        }

        public static Task Info(string title, string message) => ShowAsync(Dialog(title, Text(message), null, "OK"));
    }

    /// <summary>A Windows 11 settings card (WinUI's Border is sealed, so cards wrap one).</summary>
    partial class CardBox : UserControl
    {
        protected readonly Border Box;
        public CardBox()
        {
            Box = new Border { Style = Ui.Style("Card") };
            Content = Box;
            IsTabStop = false;
        }
    }

    /// <summary>Card with an icon, a title, a description and a control on the right: the Windows 11 Settings look.</summary>
    sealed partial class SettingCard : CardBox
    {
        readonly TextBlock title, desc;
        readonly FontIcon icon;
        readonly ContentPresenter right;
        readonly Grid grid;

        public SettingCard(string glyph, string title, string description, UIElement control = null, bool stackControl = false)
        {
            grid = new Grid { ColumnSpacing = 16 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            icon = new FontIcon { Glyph = glyph ?? "", FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Width = 24 };
            if (glyph == null) icon.Visibility = Visibility.Collapsed;
            grid.Children.Add(icon);

            this.title = new TextBlock { Text = title ?? "", Style = Ui.Style("BodyTextBlockStyle"), TextWrapping = TextWrapping.Wrap };
            desc = new TextBlock { Text = description ?? "", Style = Ui.Style("Secondary") };
            if (string.IsNullOrEmpty(description)) desc.Visibility = Visibility.Collapsed;
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            texts.Children.Add(this.title);
            texts.Children.Add(desc);
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            right = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Content = control };
            if (stackControl)
            {
                // narrow content (a list, a long control): put it under the text
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(right, 1);
                Grid.SetColumn(right, 1);
                Grid.SetColumnSpan(right, 2);
                right.HorizontalAlignment = HorizontalAlignment.Stretch;
                right.Margin = new Thickness(0, 12, 0, 0);
            }
            else Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            Box.Child = grid;
        }

        public string Title { get => title.Text; set => title.Text = value ?? ""; }

        public string Description
        {
            get => desc.Text;
            set { desc.Text = value ?? ""; desc.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public string IconGlyph { set { icon.Glyph = value ?? ""; icon.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible; } }
        public Brush IconBrush { set => icon.Foreground = value; }

        public UIElement Control { get => right.Content as UIElement; set => right.Content = value; }

        /// <summary>Indented look for cards that belong to the card above.</summary>
        public SettingCard Indent()
        {
            Box.Padding = new Thickness(56, 10, 16, 10);
            Box.MinHeight = 56;
            return this;
        }

        public bool Shown { get => Visibility == Visibility.Visible; set => Visibility = value ? Visibility.Visible : Visibility.Collapsed; }

        public async void Flash()
        {
            Box.Style = Ui.Style("CardFlash");
            await Task.Delay(1400);
            Box.Style = Ui.Style("Card");
        }
    }

    /// <summary>A card that only holds text (tips and notes).</summary>
    sealed partial class NoteCard : CardBox
    {
        readonly TextBlock text;
        public NoteCard(string message, string glyph = Glyph.Info)
        {
            Box.MinHeight = 0;
            var g = new Grid { ColumnSpacing = 14 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var i = new FontIcon { Glyph = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 2, 4, 0), Foreground = Ui.Brush("TextFillColorSecondaryBrush") };
            text = new TextBlock { Text = message, Style = Ui.Style("SecondaryBody") };
            Grid.SetColumn(text, 1);
            g.Children.Add(i);
            g.Children.Add(text);
            Box.Child = g;
        }
        public string Message { get => text.Text; set => text.Text = value ?? ""; }
        public bool Shown { set => Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    }

    /// <summary>Turns a slider value into the text of its thumb tooltip.</summary>
    sealed partial class FuncConverter : IValueConverter
    {
        readonly Func<double, string> f;
        public FuncConverter(Func<double, string> f) { this.f = f; }
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            try { return f(System.Convert.ToDouble(value)); } catch { return value?.ToString(); }
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }
}
