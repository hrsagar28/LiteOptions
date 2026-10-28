using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiteOptions.SettingsApp
{
    /// <summary>The three first-run choices: check for Options+, the K380's top row, and the middle button.</summary>
    static class SetupFlow
    {
        /// <summary>Lets the screenshot tour move between steps.</summary>
        internal static Action<int> GoTo;

        public static async Task RunAsync(Store store)
        {
            var s = store.Settings;
            string fnChoice = s.FnMode == "unchanged" ? "standard" : s.FnMode;
            string midChoice = s.Global.MiddleMode == "default" ? "gestures" : s.Global.MiddleMode;

            var step = new TextBlock { Style = Ui.Style("StepLabel") };
            var title = new TextBlock { Style = Ui.Style("SubtitleTextBlockStyle"), Margin = new Thickness(0, 2, 0, 12) };
            var host = new Border { MinHeight = 300 };
            var panel = Ui.Column(0, step, title, host);
            panel.Width = 540;

            // step 1: welcome, and make sure Options+ is gone
            var optionsCard = new SettingCard(Glyph.Check, "", "", Ui.Button("Installed apps", null, false, (o, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true })?.Dispose(); } catch { }
            }));
            void showOptions()
            {
                bool running = !string.IsNullOrEmpty(store.System.OptionsPlus);
                optionsCard.Title = running ? "Logi Options+ is still running" : "Logi Options+ isn't running";
                optionsCard.Description = running
                    ? "Uninstall it from Installed apps, otherwise the two will fight over your mouse and keyboard."
                    : "Good. Nothing will compete with LiteOptions for your devices.";
                optionsCard.IconGlyph = running ? Glyph.Warning : Glyph.Check;
                optionsCard.IconBrush = Ui.Brush(running ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush");
            }
            showOptions();
            var check = Ui.Button("Check again", Glyph.Refresh, false, async (o, e) => { await store.RefreshAsync(); showOptions(); });
            check.Margin = new Thickness(0, 10, 0, 0);
            var p1 = Ui.Column(12,
                Ui.Text("LiteOptions replaces Logi Options+ for your Pebble mouse and K380 keyboard. It works offline, needs no account and stays light. Three quick choices and you're done."),
                optionsCard, check);

            // step 2: the top row
            var media = Choice("fn", "Media keys first", "Volume, play/pause and the like. Hold Fn for F1–F12. This is how the keyboard comes.", fnChoice == "media");
            var fkeys = Choice("fn", "F1–F12 first", "Best for Excel and Tally: F2, F4 and the rest work directly. Hold Fn for the media keys.", fnChoice != "media");
            ((RadioButton)media.Child).Checked += (o, e) => fnChoice = "media";
            ((RadioButton)fkeys.Child).Checked += (o, e) => fnChoice = "standard";
            var p2 = Ui.Column(8,
                Ui.Text("What should the K380's top row do when you press a key on its own? The other set is always one Fn press away."),
                media, fkeys,
                Ui.Text("You can switch any time by pressing Ctrl + Esc.", "Secondary"));

            // step 3: the middle button
            var picker = new ActionPicker(store, "Middle click", true) { HorizontalAlignment = HorizontalAlignment.Stretch };
            picker.Value = s.Global.MiddleAction ?? ActionSpec.Of("back");
            var editor = new CustomEditor(store) { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
            picker.InlineCustom = kind =>
            {
                var v = picker.Value;
                editor.Load(kind, v.Kind == kind ? v : null);
                editor.Visibility = Visibility.Visible;
                picker.ShowLabel(CustomEditor.TitleOf(kind) + "…");
            };
            picker.Changed += a => { editor.Stop(); editor.Visibility = Visibility.Collapsed; };
            var pickerBox = Ui.Column(0, picker, editor);
            pickerBox.Margin = new Thickness(32, 0, 0, 8);
            var normal = Choice("mid", "Normal middle click", "Opens links in a new tab, closes tabs, and so on. Same as without LiteOptions.", midChoice == "default" || midChoice == "inherit");
            var gest = Choice("mid", "Gestures", "Hold the wheel and nudge the mouse: up for Task View, down for the desktop, left and right to switch desktops. A plain click still middle-clicks.", midChoice == "gestures");
            var one = Choice("mid", "One action of my choice", "For example Back, Copy, or a keyboard shortcut.", midChoice == "action");
            void pickMid(string v) { midChoice = v; pickerBox.Visibility = v == "action" ? Visibility.Visible : Visibility.Collapsed; }
            ((RadioButton)normal.Child).Checked += (o, e) => pickMid("default");
            ((RadioButton)gest.Child).Checked += (o, e) => pickMid("gestures");
            ((RadioButton)one.Child).Checked += (o, e) => pickMid("action");
            pickMid(midChoice == "inherit" ? "default" : midChoice);
            var p3 = Ui.Column(8, Ui.Text("What should pressing the scroll wheel do?"), normal, gest, one, pickerBox);

            var pages = new UIElement[] { p1, p2, p3 };
            var titles = new[] { "Welcome to LiteOptions", "The top row of your K380", "The middle button" };
            var d = Ui.Dialog(null, new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, "Next", "Skip setup", "Back");
            int page = 0;
            void go(int n)
            {
                page = Math.Max(0, Math.Min(2, n));
                host.Child = pages[page];
                step.Text = $"STEP {page + 1} OF 3";
                title.Text = titles[page];
                d.PrimaryButtonText = page == 2 ? "Finish" : "Next";
                d.IsSecondaryButtonEnabled = page > 0;
            }
            d.PrimaryButtonClick += (o, e) => { if (page < 2) { e.Cancel = true; go(page + 1); } };
            d.SecondaryButtonClick += (o, e) => { e.Cancel = true; go(page - 1); };
            go(0);
            GoTo = go;

            var r = await Ui.ShowAsync(d);
            GoTo = null;
            editor.Stop();
            s = store.Settings; // may have been replaced while the dialog was open
            if (r == ContentDialogResult.Primary)
            {
                s.FnMode = fnChoice;
                s.Global.MiddleMode = midChoice;
                if (midChoice == "action")
                {
                    var a = editor.Visibility == Visibility.Visible ? editor.Result : picker.Value;
                    if (a != null) s.Global.MiddleAction = a;
                }
            }
            s.SetupDone = true;
            await store.SaveNowAsync();
            if (r == ContentDialogResult.Primary) await store.Send("setFnMode", text: fnChoice);
            await store.RefreshAsync();
        }

        static Border Choice(string group, string title, string description, bool selected)
        {
            var texts = Ui.Column(2, Ui.Text(title, "BodyStrongTextBlockStyle"), Ui.Text(description, "Secondary"));
            var radio = new RadioButton { GroupName = group, Content = texts, IsChecked = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
            return new Border { Style = Ui.Style("Card"), MinHeight = 0, Padding = new Thickness(14, 8, 14, 10), Child = radio };
        }
    }
}
