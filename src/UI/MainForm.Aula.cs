using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LiteOptions.Hid;

namespace LiteOptions.UI
{
    /// <summary>The classic window's Aula F108 Pro page: the same settings as the WinUI page.</summary>
    sealed partial class MainForm
    {
        Panel pageAula;
        NavItem navAula;
        SettingRow aulaStatus, aulaEffectRow, aulaColourRow, aulaRainbowRow, aulaBrightRow, aulaSpeedRow, aulaDirRow, aulaSyncRow;
        NoteRow aulaCableNote, aulaError;
        CardPanel aulaLightCard;
        DropDown aulaEffect, aulaColour;
        ToggleSwitch aulaRainbow, aulaSync;
        Slider aulaBright, aulaSpeed;
        Label aulaBrightValue, aulaSpeedValue;
        Segmented aulaDir;
        FluentButton aulaSyncNow;
        AulaLighting aulaLight = new AulaLighting();

        static readonly (string name, Color c)[] AulaSwatches =
        {
            ("Red", Color.FromArgb(255, 0, 0)), ("Orange", Color.FromArgb(255, 110, 0)), ("Amber", Color.FromArgb(255, 170, 0)),
            ("Green", Color.FromArgb(0, 255, 0)), ("Cyan", Color.FromArgb(0, 255, 255)), ("Blue", Color.FromArgb(0, 60, 255)),
            ("Purple", Color.FromArgb(140, 0, 255)), ("Pink", Color.FromArgb(255, 0, 140)), ("White", Color.FromArgb(255, 255, 255)),
        };

        void BuildAulaPage()
        {
            var status = new CardPanel();
            aulaStatus = status.Add(new SettingRow(Theme.IcKeyboard, "Aula F108 Pro", "", null));
            aulaError = status.Add(new NoteRow("", Theme.IcWarning));
            Add(pageAula, status);

            Add(pageAula, UIx.Section("Backlight"));
            aulaLightCard = new CardPanel();
            aulaCableNote = aulaLightCard.Add(new NoteRow("Over Bluetooth the keyboard doesn't take changes. Switch it to 2.4G (the dongle) or plug in its USB-C cable to change these."));

            aulaEffect = new DropDown { Width = Theme.S(240) };
            aulaEffect.SetItems(AulaLighting.Names.Select((n, i) => new KeyValuePair<string, object>(n, i)));
            aulaEffect.Picked += v => { aulaLight.Mode = (int)v; UpdateAulaVisibility(); AulaChanged(); };
            aulaEffectRow = aulaLightCard.Add(new SettingRow(Theme.IcBrightness, "Effect", "Fn shortcuts on the keyboard change it too", aulaEffect));

            aulaColour = new DropDown { Width = Theme.S(180) };
            var colours = AulaSwatches.Select(s => new KeyValuePair<string, object>(s.name, s.c)).ToList();
            colours.Add(new KeyValuePair<string, object>("Custom…", "custom"));
            aulaColour.SetItems(colours);
            aulaColour.Picked += v =>
            {
                Color c;
                if (v is Color col) c = col;
                else
                {
                    using (var d = new ColorDialog { FullOpen = true, Color = Color.FromArgb(aulaLight.R, aulaLight.G, aulaLight.B) })
                    {
                        if (d.ShowDialog(this) != DialogResult.OK) { ShowAulaColour(); return; }
                        c = d.Color;
                    }
                }
                aulaLight.R = c.R; aulaLight.G = c.G; aulaLight.B = c.B;
                ShowAulaColour();
                AulaChanged();
            };
            aulaColourRow = aulaLightCard.Add(new SettingRow(null, "Colour", "", aulaColour));

            aulaRainbow = new ToggleSwitch();
            aulaRainbow.CheckedChanged += (s, e) => { if (loading) return; aulaLight.Rainbow = aulaRainbow.Checked; UpdateAulaVisibility(); AulaChanged(); };
            aulaRainbowRow = aulaLightCard.Add(new SettingRow(null, "Rainbow colours", "Instead of one colour", aulaRainbow));

            aulaBright = new Slider { Minimum = 0, Maximum = 5, Width = Theme.S(200) };
            aulaBrightValue = UIx.Label("", Theme.BodyStrong);
            aulaBright.ValueChanged += (s, e) => aulaBrightValue.Text = aulaBright.Value.ToString();
            aulaBright.ValueCommitted += (s, e) => { if (loading) return; aulaLight.Brightness = aulaBright.Value; AulaChanged(); };
            aulaBrightRow = aulaLightCard.Add(new SettingRow(null, "Brightness", "0 to 5", UIx.Row(aulaBright, aulaBrightValue)));

            aulaSpeed = new Slider { Minimum = 0, Maximum = 5, Width = Theme.S(200) };
            aulaSpeedValue = UIx.Label("", Theme.BodyStrong);
            aulaSpeed.ValueChanged += (s, e) => aulaSpeedValue.Text = aulaSpeed.Value.ToString();
            aulaSpeed.ValueCommitted += (s, e) => { if (loading) return; aulaLight.Speed = aulaSpeed.Value; AulaChanged(); };
            aulaSpeedRow = aulaLightCard.Add(new SettingRow(null, "Speed", "0 to 5", UIx.Row(aulaSpeed, aulaSpeedValue)));

            aulaDir = new Segmented();
            aulaDir.SetItems("Left", "Right");
            aulaDir.SelectedIndexChanged += (s, e) => { if (loading || aulaDir.SelectedIndex < 0) return; aulaLight.Direction = aulaDir.SelectedIndex; AulaChanged(); };
            aulaDirRow = aulaLightCard.Add(new SettingRow(null, "Direction", "", aulaDir));
            Add(pageAula, aulaLightCard);

            Add(pageAula, UIx.Section("Screen clock"));
            var clock = new CardPanel();
            aulaSync = new ToggleSwitch();
            aulaSync.CheckedChanged += (s, e) => { if (loading) return; app.Settings.AulaClockSync = aulaSync.Checked; app.Commit(); };
            aulaSyncRow = clock.Add(new SettingRow(Theme.IcSync, "Set the clock when the keyboard connects", "", aulaSync));
            aulaSyncNow = new FluentButton("Set it now", ButtonKind.Standard, Theme.IcRefresh, (s, e) =>
            {
                if (!app.Aula.Snapshot().CanConfigure) { ShowAulaError("Over Bluetooth the keyboard doesn't take changes. Switch it to 2.4G (the dongle) or plug in its USB-C cable."); return; }
                app.Aula.SyncClock();
            });
            clock.Add(new SettingRow(null, "Set the clock to this PC's time now", "Over the cable or the dongle", aulaSyncNow));
            Add(pageAula, clock);

        }

        void AulaChanged()
        {
            app.Settings.AulaLight = aulaLight.Clone();
            app.Commit();
            if (!app.Aula.Snapshot().CanConfigure) { ShowAulaError("Over Bluetooth the keyboard doesn't take changes. Switch it to 2.4G (the dongle) or plug in its USB-C cable."); return; }
            app.Aula.SetLighting(app.Settings.AulaLight);
        }

        void ShowAulaError(string msg) { aulaError.Text2 = msg; aulaError.Visible = true; }

        void ShowAulaColour()
        {
            var c = Color.FromArgb(aulaLight.R, aulaLight.G, aulaLight.B);
            var hit = AulaSwatches.FirstOrDefault(s => s.c.ToArgb() == c.ToArgb());
            if (hit.name != null) aulaColour.Select(hit.c);
            else { aulaColour.Select("custom"); aulaColour.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }
            aulaColourRow.Description = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        void UpdateAulaVisibility()
        {
            int m = aulaLight.Mode;
            aulaColourRow.Visible = AulaLighting.HasColour(m) && !aulaLight.Rainbow;
            aulaRainbowRow.Visible = AulaLighting.HasColour(m);
            aulaBrightRow.Visible = m != 0;
            aulaSpeedRow.Visible = AulaLighting.HasSpeed(m);
            int dk = AulaLighting.DirectionKind(m);
            aulaDirRow.Visible = dk != 0;
            if (dk == 1) aulaDir.SetItems("Left", "Right");
            else if (dk == 2) aulaDir.SetItems("Up", "Down");
            aulaDir.SelectedIndex = aulaLight.Direction;
        }

        void RefreshAula()
        {
            bool was = loading;
            loading = true;
            aulaLight = app.Settings.AulaLight?.Clone() ?? new AulaLighting { Mode = 0 };
            aulaLight.Normalize();
            aulaEffect.Select(aulaLight.Mode);
            aulaRainbow.Checked = aulaLight.Rainbow;
            aulaBright.Value = aulaLight.Brightness; aulaBrightValue.Text = aulaLight.Brightness.ToString();
            aulaSpeed.Value = aulaLight.Speed; aulaSpeedValue.Text = aulaLight.Speed.ToString();
            aulaSync.Checked = app.Settings.AulaClockSync;
            ShowAulaColour();
            UpdateAulaVisibility();

            var a = app.Aula.Snapshot();
            aulaStatus.Title.Text = a.Present ? "Aula F108 Pro" : "Aula F108 Pro: not found";
            aulaStatus.Description = !a.Present
                ? "Not connected. Switch it on, or plug in its 2.4 GHz dongle or USB-C cable."
                : a.Connection == "USB cable" ? "Connected by USB cable" + (a.Version != 0 ? " · v" + a.Version.ToString("X4") : "")
                : a.Connection == "Bluetooth" ? "Connected by Bluetooth" + (a.Battery >= 0 ? " · battery " + a.Battery + "%" : "")
                : a.DongleAwake ? "Connected by 2.4 GHz dongle" + (a.Battery >= 0 ? " · battery " + a.Battery + "%" : "")
                : "Dongle plugged in, but the keyboard isn't answering. Press a key to wake it, or check it's switched to 2.4G.";
            aulaCableNote.Visible = !a.CanConfigure;
            foreach (var r in new Control[] { aulaEffectRow, aulaColourRow, aulaRainbowRow, aulaBrightRow, aulaSpeedRow, aulaDirRow }) r.Enabled = a.CanConfigure;
            aulaSyncNow.Enabled = a.CanConfigure;
            aulaSyncRow.Description = a.ClockSyncedAt != null ? "Last set " + a.ClockSyncedAt.Value.ToString("d MMM 'at' HH:mm") : "Keeps the date and time on its screen right";
            aulaError.Text2 = a.LastError ?? "";
            aulaError.Visible = !string.IsNullOrEmpty(a.LastError);
            loading = was;
        }
    }
}
