using System;
using System.Linq;
using System.Threading.Tasks;
using LiteOptions.Ipc;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LiteOptions.SettingsApp
{
    /// <summary>
    /// The Aula F108 Pro: where it's connected, its backlight and its screen clock. The keyboard can't report its
    /// settings, so the page shows what was last chosen here; changes go to the keyboard only over the USB cable.
    /// </summary>
    sealed partial class AulaPage : PageBase
    {
        static readonly (string name, Color c)[] Swatches =
        {
            ("Red", Color.FromArgb(255, 255, 0, 0)), ("Orange", Color.FromArgb(255, 255, 110, 0)), ("Amber", Color.FromArgb(255, 255, 170, 0)),
            ("Green", Color.FromArgb(255, 0, 255, 0)), ("Cyan", Color.FromArgb(255, 0, 255, 255)), ("Blue", Color.FromArgb(255, 0, 60, 255)),
            ("Purple", Color.FromArgb(255, 140, 0, 255)), ("Pink", Color.FromArgb(255, 255, 0, 140)), ("White", Color.FromArgb(255, 255, 255, 255)),
        };

        readonly SettingCard statusCard, effectCard, colourCard, rainbowCard, brightCard, speedCard, dirCard, syncCard;
        readonly NoteCard errorNote, cableNote, batteryNote;
        readonly SettingsGroup lightGroup, clockGroup;
        readonly ComboBox effect;
        readonly ToggleSwitch rainbow, sync;
        readonly Slider bright, speed;
        readonly TextBlock brightValue, speedValue;
        readonly Segmented dirLR, dirUD;
        readonly StackPanel swatchRow;
        readonly Border customSwatch;
        readonly ColorPicker picker;
        readonly Button syncNow;
        readonly Debouncer send = new Debouncer(350);
        AulaLighting light = new AulaLighting { Mode = 1 };

        public override string Title => "Aula F108 Pro";
        public override bool UsesProfiles => false;

        public AulaPage(Store store) : base(store)
        {
            var status = Add(new SettingsGroup());
            statusCard = status.Add(new SettingCard(Glyph.Keyboard, "Aula F108 Pro", ""));
            errorNote = Add(new NoteCard("", Glyph.Warning));
            errorNote.Shown = false;

            // ── backlight ──
            Section("Backlight", Note("Fn shortcuts on the keyboard change it too"));
            cableNote = Add(new NoteCard("The keyboard takes lighting changes only over its USB-C cable. Plug it in to change these."));
            cableNote.Margin = new Thickness(0, 0, 0, 4);
            lightGroup = Group();

            effect = new ComboBox { MinWidth = 220, ItemsSource = AulaLighting.Names.ToList() };
            effect.SelectionChanged += (s, e) => { if (loading || effect.SelectedIndex < 0) return; light.Mode = effect.SelectedIndex; UpdateVisibility(); Changed(); };
            effectCard = Find(lightGroup.Add(new SettingCard(Glyph.Light, "Effect", "", effect)), "aula lighting effect backlight rgb mode static breathing off");

            swatchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var (name, c) in Swatches)
            {
                var col = c;
                var b = SwatchButton(new SolidColorBrush(col), name);
                b.Click += (s, e) => SetColour(col);
                swatchRow.Children.Add(b);
            }
            picker = new ColorPicker { IsAlphaEnabled = false, IsColorPreviewVisible = true, IsMoreButtonVisible = false, ColorSpectrumShape = ColorSpectrumShape.Ring };
            picker.ColorChanged += (s, e) => { if (!loading) SetColour(e.NewColor, fromPicker: true); };
            customSwatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("ControlStrokeColorDefaultBrush") };
            var custom = new DropDownButton
            {
                Content = Ui.Row(8, customSwatch, new TextBlock { Text = "Custom", VerticalAlignment = VerticalAlignment.Center }),
                Flyout = new Flyout { Content = picker, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight },
                Margin = new Thickness(8, 0, 0, 0),
            };
            ToolTipService.SetToolTip(custom, "Pick any colour");
            swatchRow.Children.Add(custom);
            colourCard = Find(lightGroup.Add(new SettingCard(Glyph.Palette, "Colour", "", swatchRow, stackControl: true)), "aula colour color backlight");

            rainbow = Ui.Toggle();
            rainbow.Toggled += (s, e) => { if (loading) return; light.Rainbow = rainbow.IsOn; UpdateVisibility(); Changed(); };
            rainbowCard = Find(lightGroup.Add(new SettingCard(Glyph.Palette, "Rainbow colours", "Instead of one colour", rainbow)), "aula rainbow colourful");

            bright = new Slider { Minimum = 0, Maximum = 5, StepFrequency = 1, Width = 200, VerticalAlignment = VerticalAlignment.Center };
            brightValue = Value();
            bright.ValueChanged += (s, e) => { brightValue.Text = ((int)bright.Value).ToString(); if (loading) return; light.Brightness = (int)bright.Value; Changed(); };
            brightCard = Find(lightGroup.Add(new SettingCard(Glyph.Brightness, "Brightness", "0 to 5", Ui.Row(10, bright, brightValue))), "aula brightness backlight");

            speed = new Slider { Minimum = 0, Maximum = 5, StepFrequency = 1, Width = 200, VerticalAlignment = VerticalAlignment.Center };
            speedValue = Value();
            speed.ValueChanged += (s, e) => { speedValue.Text = ((int)speed.Value).ToString(); if (loading) return; light.Speed = (int)speed.Value; Changed(); };
            speedCard = Find(lightGroup.Add(new SettingCard(Glyph.Speed, "Speed", "0 to 5", Ui.Row(10, speed, speedValue))), "aula effect speed");

            dirLR = new Segmented("Direction", "Left", "Right");
            dirUD = new Segmented("Direction", "Up", "Down");
            dirLR.SelectionChanged += i => { if (loading || i < 0) return; light.Direction = i; Changed(); };
            dirUD.SelectionChanged += i => { if (loading || i < 0) return; light.Direction = i; Changed(); };
            dirCard = lightGroup.Add(new SettingCard(Glyph.Swap, "Direction", "", new Grid { Children = { dirLR, dirUD } }));

            // ── clock ──
            Section("Screen clock");
            clockGroup = Group();
            sync = Ui.Toggle();
            sync.Toggled += (s, e) => { if (loading) return; store.Settings.AulaClockSync = sync.IsOn; store.Save(); };
            syncCard = Find(clockGroup.Add(new SettingCard(Glyph.Clock, "Set the clock when the cable is plugged in", "", sync)), "aula clock time date screen sync");
            syncNow = Ui.Button("Set it now", Glyph.Refresh, click: async (s, e) => await SyncNow());
            clockGroup.Add(new SettingCard(null, "Set the clock to this PC's time now", "Needs the USB cable", syncNow).Indent());

            // ── battery ──
            Section("Battery");
            batteryNote = Add(new NoteCard("The keyboard's screen shows its battery level. LiteOptions is learning how the dongle reports it, so it can show it here and warn you when it's low."));
        }

        static TextBlock Value() => new TextBlock { Width = 24, VerticalAlignment = VerticalAlignment.Center, Style = Ui.Style("BodyStrongTextBlockStyle") };

        static Button SwatchButton(Brush fill, string name)
        {
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = fill, BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("ControlStrokeColorDefaultBrush") };
            var b = new Button { Content = dot, Padding = new Thickness(5), MinWidth = 0, CornerRadius = new CornerRadius(17) };
            ToolTipService.SetToolTip(b, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, name);
            return b;
        }

        void SetColour(Color c, bool fromPicker = false)
        {
            light.R = c.R; light.G = c.G; light.B = c.B;
            if (light.Rainbow) { light.Rainbow = false; loading = true; rainbow.IsOn = false; loading = false; }
            ShowColour(fromPicker);
            UpdateVisibility();
            Changed();
        }

        void ShowColour(bool fromPicker = false)
        {
            var c = Color.FromArgb(255, (byte)light.R, (byte)light.G, (byte)light.B);
            customSwatch.Background = new SolidColorBrush(c);
            colourCard.Description = light.Rainbow ? "Rainbow" : $"#{light.R:X2}{light.G:X2}{light.B:X2}";
            if (!fromPicker) { bool was = loading; loading = true; picker.Color = c; loading = was; }
        }

        /// <summary>Save the choice and send it to the keyboard once the sliders have settled.</summary>
        void Changed()
        {
            store.Settings.AulaLight = light.Clone();
            send.Run(async () =>
            {
                await store.SaveNowAsync();
                var r = await store.Send("aulaLight");
                if (r.Error != null) ShowError(r.Error);
            });
        }

        async Task SyncNow()
        {
            syncNow.IsEnabled = false;
            var r = await store.Send("aulaClock");
            if (r.Error != null) ShowError(r.Error);
            await Task.Delay(800);
            await store.RefreshAsync();
            syncNow.IsEnabled = true;
        }

        void ShowError(string msg)
        {
            errorNote.Message = msg;
            errorNote.Shown = true;
        }

        void UpdateVisibility()
        {
            int m = light.Mode;
            effectCard.Description = m == 0 ? "The backlight is off" : "";
            colourCard.Shown = AulaLighting.HasColour(m) && !light.Rainbow;
            rainbowCard.Shown = AulaLighting.HasColour(m);
            brightCard.Shown = m != 0;
            speedCard.Shown = AulaLighting.HasSpeed(m);
            int dk = AulaLighting.DirectionKind(m);
            dirCard.Shown = dk != 0;
            Show(dirLR, dk == 1);
            Show(dirUD, dk == 2);
            lightGroup.Refresh();
        }

        public override void LoadSettings()
        {
            loading = true;
            var s = store.Settings;
            light = s.AulaLight?.Clone() ?? new AulaLighting { Mode = 0 };
            light.Normalize();
            effect.SelectedIndex = light.Mode;
            rainbow.IsOn = light.Rainbow;
            bright.Value = light.Brightness;
            speed.Value = light.Speed;
            dirLR.SelectedIndex = light.Direction;
            dirUD.SelectedIndex = light.Direction;
            brightValue.Text = light.Brightness.ToString();
            speedValue.Text = light.Speed.ToString();
            sync.IsOn = s.AulaClockSync;
            ShowColour();
            UpdateVisibility();
            loading = false;
            LoadDevices();
        }

        public override void LoadDevices()
        {
            var d = store.Aula;
            bool cable = d != null && d.CanConfigure;
            if (d == null)
            {
                statusCard.Title = "Aula F108 Pro: not found";
                statusCard.Description = "Plug in its 2.4 GHz dongle or its USB-C cable. LiteOptions can't see it over Bluetooth.";
            }
            else
            {
                statusCard.Title = "Aula F108 Pro";
                statusCard.Description = d.Connection == "USB cable"
                    ? "Connected by USB cable" + (d.Firmware != "" ? " · " + d.Firmware : "")
                    : "Dongle plugged in. Lighting and the clock change over the USB cable.";
            }
            cableNote.Shown = !cable;
            lightGroup.IsEnabled = cable;
            lightGroup.Opacity = cable ? 1 : 0.6;
            syncNow.IsEnabled = cable;
            syncCard.Description = d != null && d.ClockSynced != "" ? "Last set " + d.ClockSynced : "Keeps the date and time on its screen right";
            batteryNote.Shown = d != null && !cable;
            if (d != null && d.LastError != "") ShowError(d.LastError);
            else if (d == null || cable) errorNote.Shown = false;
        }
    }
}
