using System.Linq;
using LiteOptions.Ipc;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LiteOptions.SettingsApp
{
    /// <summary>
    /// The device's Easy-Switch channels (1, 2, 3) and what is paired on each, with this computer marked.
    /// Read-only, like Options+: the device's own Easy-Switch button does the switching.
    /// </summary>
    static class EasySwitch
    {
        public static SettingCard Card(bool mouse) => new SettingCard("", "Paired computers and phones",
            mouse ? "The Easy-Switch button underneath moves the mouse between the computers and phones paired with it."
                  : "The keyboard's Easy-Switch keys (F1, F2 and F3 on the K380) move it between the computers and phones paired with it.",
            new StackPanel { Spacing = 6 }, stackControl: true);

        /// <summary>Fill the card from the device; hidden when the device isn't connected or has no channels.</summary>
        public static void Fill(SettingCard card, DeviceDto d)
        {
            var list = (StackPanel)card.Control;
            list.Children.Clear();
            var hosts = d?.Hosts;
            card.Shown = d != null && hosts != null && hosts.Count > 0;
            if (!card.Shown) return;
            foreach (var h in hosts.OrderBy(h => h.Number))
                list.Children.Add(Row(h, h.Number - 1 == d.CurrentHost));
        }

        static UIElement Row(HostDto h, bool current)
        {
            var g = new Grid { ColumnSpacing = 12, MinHeight = 28 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var number = new TextBlock
            {
                Text = h.Number.ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ui.Brush(current ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorSecondaryBrush")
            };
            var badge = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(current ? 0 : 1), BorderBrush = Ui.Brush("TextFillColorSecondaryBrush"),
                Background = current ? Ui.Brush("AccentFillColorDefaultBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Child = number
            };

            string hostName = h.Name ?? "";
            string text = hostName.Length > 0 ? hostName
                        : current ? "This computer"
                        : h.Unknown ? "Not shown by this device"
                        : h.Paired ? "Paired (no name given)"
                        : "Nothing paired";
            var name = Ui.Text(text, hostName.Length > 0 || current ? "BodyTextBlockStyle" : "SecondaryBody", wrap: false);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 1);

            g.Children.Add(badge);
            g.Children.Add(name);
            if (current && hostName.Length > 0)
            {
                var tag = Ui.Text("This computer", "Secondary", wrap: false);
                tag.Foreground = Ui.Brush("AccentTextFillColorPrimaryBrush");
                tag.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(tag, 2);
                g.Children.Add(tag);
            }
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(g, "Channel " + h.Number + ": " + text + (current ? ", this computer" : ""));
            return g;
        }
    }
}
