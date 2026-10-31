using DisplayRotate.Core;
using DisplayRotate.Core.Updates;
using static DisplayRotate.DocsTests.DocsTour;
using static DisplayRotate.DocsTests.E2E.Harness;
using static DisplayRotate.DocsTests.SettingsFormTests;

namespace DisplayRotate.DocsTests.E2E;

/// <summary>The Settings dialog, opened on seeded settings and driven control by control.</summary>
[Trait("Category", "E2E")]
public class SettingsJourneys
{
    [Fact]
    public void SaveWritesEveryChoice()
    {
        OnSta(() =>
        {
            using var state = new UserState();
            var display = DisplayManager.GetDisplays()[0];
            using (var form = Open())
            {
                Find<CheckBox>(form, "Start with Windows").Checked = true;
                Find<CheckBox>(form, "Show Identify buttons in context menu").Checked = false;
                Find<CheckBox>(form, "Auto-restore display orientations on startup").Checked = true;
                OrientationCombos(form)[0].SelectedIndex = 2;
                Controls(form).OfType<NumericUpDown>().Single().Value = 4;
                Find<RadioButton>(form, "Check once a day").Checked = true;
                InHotkeyRows<CheckBox>(form)[0].Checked = false;
                InHotkeyRows<ComboBox>(form)[3].SelectedIndex = 2;   // row 1's orientation
                InHotkeyRows<ComboBox>(form)[4].SelectedIndex = 1;   // row 2's display
                Press(InHotkeyRows<TextBox>(form)[3], Keys.Control | Keys.Shift | Keys.F9);
                Find<Button>(form, "Save").PerformClick();
            }

            var saved = UserState.Saved;
            Assert.True(saved.StartWithWindows);
            Assert.Equal($"\"{Application.ExecutablePath}\"", UserState.RunValue);
            Assert.False(saved.ShowIdentifyButtons);
            Assert.True(saved.AutoRestoreOrientations);
            Assert.Equal(2, saved.PreferredOrientations[display.DeviceName]);
            Assert.Equal(4000, saved.OverlayDurationMs);
            Assert.Equal(AppSettings.UpdateCheckDaily, saved.UpdateCheck);
            Assert.False(saved.Hotkeys[0].IsEnabled);
            Assert.Equal("Rotate Primary to 180° (Landscape flipped)", saved.Hotkeys[1].DisplayName);
            Assert.Equal((1, "Rotate Display 1 to 180° (Landscape flipped)"), (saved.Hotkeys[2].TargetDisplayIndex, saved.Hotkeys[2].DisplayName));
            Assert.Equal("Ctrl+Shift+F9", saved.Hotkeys[3].FormatHotkey());
            Assert.False(File.Exists(UserState.SettingsFile + ".tmp"), "the save left its temporary file behind");

            using (var form = Open())
            {
                Find<CheckBox>(form, "Start with Windows").Checked = false;
                Find<Button>(form, "Save").PerformClick();
            }

            Assert.Null(UserState.RunValue);
        });
    }

    [Theory]
    [InlineData(Keys.Control | Keys.Alt | Keys.F5, "Ctrl+Alt+F5")]
    [InlineData(Keys.Control | Keys.Shift | Keys.A, "Ctrl+Shift+A")]
    [InlineData(Keys.Alt | Keys.D1, "Alt+1")]
    [InlineData(Keys.Shift | Keys.Home, "Shift+Home")]
    [InlineData(Keys.Back, "(None)")]
    [InlineData(Keys.F5, "Ctrl+Alt+Up")]                        // no modifier: ignored
    [InlineData(Keys.Control | Keys.ControlKey, "Ctrl+Alt+Up")]  // a modifier alone: still waiting
    public void AHotkeyBoxTakesTheCombinationPressedInIt(Keys keys, string shown)
    {
        OnSta(() =>
        {
            using var state = new UserState();
            using var form = Open();
            var box = InHotkeyRows<TextBox>(form)[0];

            Press(box, keys);
            Find<Button>(form, "Save").PerformClick();

            Assert.Equal(shown, box.Text);
            Assert.Equal(shown, UserState.Saved.Hotkeys[0].FormatHotkey());
        });
    }

    [Fact]
    public void EscapeInAHotkeyBoxClearsItAndKeepsSettingsOpen()
    {
        OnSta(() =>
        {
            using var form = Open();
            var box = InHotkeyRows<TextBox>(form)[0];

            Press(box, Keys.Escape);

            Assert.Equal("(None)", box.Text);
            Assert.Equal(DialogResult.None, form.DialogResult);
        });
    }

    [Fact]
    public void AddAppendsAnEmptyHotkeyForThePrimaryDisplay()
    {
        OnSta(() =>
        {
            using var state = new UserState();
            using (var form = Open())
            {
                Find<Button>(form, "Add").PerformClick();

                Assert.Equal("(None)", InHotkeyRows<TextBox>(form)[4].Text);
                Assert.True(InHotkeyRows<CheckBox>(form)[4].Checked);
                Assert.Equal(["Primary", "0° (Landscape)"], InHotkeyRows<ComboBox>(form).Skip(8).Select(c => c.Text));
                Find<Button>(form, "Save").PerformClick();
            }

            var added = UserState.Saved.Hotkeys[4];
            Assert.Equal(("Rotate Primary to 0° (Landscape)", 0u), (added.DisplayName, added.VirtualKey));
        });
    }

    [Fact]
    public void AddPutsTheNewRowUnderTheLastOneWhenTheListIsScrolled()
    {
        OnSta(() =>
        {
            using var form = Open();
            var add = Find<Button>(form, "Add");
            for (var i = 0; i < 6; i++)
            {
                add.PerformClick();
            }

            var list = (Panel)InHotkeyRows<TextBox>(form)[0].Parent!;
            list.AutoScrollPosition = new Point(0, 1000);
            Application.DoEvents();
            Assert.True(list.AutoScrollPosition.Y < 0, "ten rows did not make the hotkey list scroll");

            add.PerformClick();

            var boxes = InHotkeyRows<TextBox>(form);
            Assert.Equal(30, boxes[^1].Top - boxes[^2].Top);
        });
    }

    [Fact]
    public void RemoveWithNoRowChosenRemovesNothing()
    {
        OnSta(() =>
        {
            using var form = Open();

            Find<Button>(form, "Remove").PerformClick();

            Assert.Equal(4, InHotkeyRows<TextBox>(form).Count);
        });
    }

    [Fact]
    public void ResetToDefaultsBringsBackTheFourArrowKeys()
    {
        OnSta(() =>
        {
            using var state = new UserState(s => s.Hotkeys.RemoveRange(1, 3));
            using (var form = Open(AppSettings.Load()))
            {
                Press(InHotkeyRows<TextBox>(form)[0], Keys.Control | Keys.Alt | Keys.F5);
                Find<Button>(form, "Add").PerformClick();

                Find<Button>(form, "Reset to Defaults").PerformClick();

                Assert.Equal(["Ctrl+Alt+Up", "Ctrl+Alt+Right", "Ctrl+Alt+Down", "Ctrl+Alt+Left"], InHotkeyRows<TextBox>(form).Select(t => t.Text));
                Find<Button>(form, "Save").PerformClick();
            }

            Assert.Equal(
                AppSettings.CreateDefault().Hotkeys.Select(h => (h.DisplayName, h.FormatHotkey())),
                UserState.Saved.Hotkeys.Select(h => (h.DisplayName, h.FormatHotkey())));
        });
    }

    [Fact]
    public void TheChoicesListEveryDisplay()
    {
        OnSta(() =>
        {
            var displays = DisplayManager.GetDisplays();
            var settings = AppSettings.CreateDefault();
            settings.AutoRestoreOrientations = true;
            using var form = Open(settings);

            Assert.Equal(
                displays.Select(d => $"Display {d.Index}").Prepend("Primary"),
                InHotkeyRows<ComboBox>(form)[0].Items.Cast<string>());
            Assert.Equal(
                displays.Select(d => $"Display {d.Index}: {d.FriendlyName}"),
                OrientationCombos(form)[0].Parent!.Controls.OfType<Label>().Select(l => l.Text));
            Assert.Equal(displays.Select(d => d.CurrentOrientation), OrientationCombos(form).Select(c => c.SelectedIndex));
        });
    }

    [Fact]
    public void IdentifyNextToADisplayShowsItsNumberOnIt()
    {
        OnSta(() =>
        {
            var settings = AppSettings.CreateDefault();
            settings.AutoRestoreOrientations = true;
            var display = DisplayManager.GetDisplays()[0];
            using var form = Open(settings);

            Find<Button>(form, "Identify").PerformClick();

            var overlay = Assert.Single(Application.OpenForms.OfType<OverlayForm>());
            Assert.Equal(display.Index, Get(overlay, "_displayNumber"));
            Assert.Equal(Screen.AllScreens.Single(s => s.DeviceName == display.DeviceName).Bounds, overlay.Bounds);
            overlay.Close();
        });
    }

    [Fact]
    public void TheOverlayDurationIsOneToFiveSeconds()
    {
        OnSta(() =>
        {
            var settings = AppSettings.CreateDefault();
            settings.OverlayDurationMs = 9000;
            using var form = Open(settings);
            var duration = Controls(form).OfType<NumericUpDown>().Single();

            Assert.Equal((1, 5, 5), ((int)duration.Minimum, (int)duration.Maximum, (int)duration.Value));
        });
    }

    [Fact]
    public void EnterSavesAndEscapeCancels()
    {
        OnSta(() =>
        {
            using var form = Open();

            Assert.Same(Find<Button>(form, "Save"), form.AcceptButton);
            Assert.Same(Find<Button>(form, "Cancel"), form.CancelButton);
        });
    }

    [Theory]
    [InlineData("same", "You have the newest release, {0}.", null)]
    [InlineData("none", "GitHub could not be reached. Nothing was changed.", null)]
    [InlineData("newer", "DisplayRotate {1} is available (you have {0}).", "Open download page")]
    public void CheckNowShowsTheAnswerAndStampsTheTime(string latest, string status, string? button)
    {
        OnSta(() =>
        {
            using var state = new UserState();
            var releases = new FakeReleases
            {
                Latest = latest switch
                {
                    "same" => Version.Parse(ProductInfo.Version),
                    "newer" => Newer,
                    _ => null,
                },
            };
            using var form = Open(AppSettings.Load(), Updater(releases));

            Find<Button>(form, "Check now").PerformClick();

            var expected = string.Format(status, ProductInfo.Version, latest == "newer" ? Newer.ToString(3) : "");
            Until(() => Controls(form).Any(c => c is Label { Text: var t } && t == expected), $"the status line to read '{expected}'");
            Assert.Equal(button, UpdateButton(form)?.Text);
            Assert.NotNull(UserState.Saved.LastUpdateCheckUtc);
        });
    }

    [Fact]
    public void AnInstalledCopyOffersToUpdateItself()
    {
        OnSta(() =>
        {
            using var state = new UserState();
            using var form = Open(AppSettings.Load(), Updater(new FakeReleases { Latest = Newer }));
            // The test host is a portable copy; this is what an installed one detects.
            typeof(SettingsForm).GetField("_install", Private)!
                .SetValue(form, new InstallRecord(InstallScope.PerUser, AppContext.BaseDirectory, ReleaseAssets.Full));

            Find<Button>(form, "Check now").PerformClick();

            Until(() => UpdateButton(form) is not null, "the update button");
            Assert.Equal($"Update to {Newer.ToString(3)}", UpdateButton(form)!.Text);
            Assert.True(UpdateButton(form)!.Enabled);
        });
    }

    [Fact]
    public void TheStatusLineShowsTheLastCheck()
    {
        OnSta(() =>
        {
            var settings = AppSettings.CreateDefault();
            var stamp = new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero);
            settings.LastUpdateCheckUtc = stamp;
            using var form = Open(settings);

            Assert.Contains(Controls(form), c => c is Label { Text: var t } && t == $"Last checked {stamp.ToLocalTime():yyyy-MM-dd HH:mm}.");
        });
    }

    [Fact]
    public void ChecksTurnedOffByPolicyAreSaidSoAndCannotRun()
    {
        OnSta(() =>
        {
            using var form = Open(AppSettings.CreateDefault(), Updater(new FakeReleases(), disabledByPolicy: true));

            Assert.Contains(Controls(form), c => c is Label { Text: "Update checks are disabled by policy on this computer." });
            Assert.False(Find<Button>(form, "Check now").Enabled);
        });
    }

    [Fact]
    public void WhileAnUpdateInstallsNothingElseCanStart()
    {
        OnSta(() =>
        {
            var updater = Updater(new FakeReleases());
            typeof(Updater).GetProperty(nameof(Updater.Installing))!.SetValue(updater, true);
            using var form = Open(AppSettings.CreateDefault(), updater);

            Assert.Contains(Controls(form), c => c is Label { Text: var t } && t.StartsWith("Installing… DisplayRotate closes", StringComparison.Ordinal));
            Assert.False(Find<Button>(form, "Check now").Enabled);
        });
    }

    [Fact]
    public void DownloadProgressReadsInPercentOrMegabytes()
    {
        Assert.Equal("Downloading… 50%", Updater.Progress(512, 1024));
        Assert.Equal("Downloading… 3 MB", Updater.Progress(3 * 1024 * 1024 + 5, null));
    }

    private static SettingsForm Open(AppSettings? settings = null, Updater? updater = null)
    {
        var form = new SettingsForm(settings ?? AppSettings.CreateDefault(), updater ?? Updater(new FakeReleases()));
        Show(form);
        return form;
    }

    /// <summary>The per-display orientation choices (auto-restore), one per display.</summary>
    private static List<ComboBox> OrientationCombos(Control form) =>
        Controls(form).OfType<ComboBox>().Where(c => c.Parent!.Controls.OfType<Button>().Any(b => b.Text == "Identify")).ToList();

    /// <summary>"Update to …" or "Open download page", when it shows.</summary>
    private static Button? UpdateButton(Control form) =>
        Controls(form).OfType<Button>().SingleOrDefault(b => b.Visible && (b.Text.StartsWith("Update to ", StringComparison.Ordinal) || b.Text == "Open download page"));

    /// <summary>
    /// A key pressed in the box the way the message loop delivers it: the dialog gets its turn
    /// first (Enter, Escape, Tab, arrows), and only what it leaves reaches the box.
    /// </summary>
    internal static void Press(TextBox box, Keys keys)
    {
        // Ctrl, Alt and Shift as held down: what GetKeyState, and so Control.ModifierKeys, reads.
        var held = new byte[256];
        foreach (var (modifier, key) in new[] { (Keys.Control, Keys.ControlKey), (Keys.Alt, Keys.Menu), (Keys.Shift, Keys.ShiftKey) })
        {
            if ((keys & modifier) != 0)
            {
                held[(int)key] = 0x80;
            }
        }

        Assert.True(SetKeyboardState(held));
        try
        {
            var down = (keys & Keys.Alt) != 0 ? WM_SYSKEYDOWN : WM_KEYDOWN;
            var key = (nint)(keys & Keys.KeyCode);
            var message = Message.Create(box.Handle, down, key, 1);
            if (box.PreProcessControlMessage(ref message) != PreProcessControlState.MessageProcessed)
            {
                NativeMethods.SendMessage(box.Handle, down, key, 1);
            }
        }
        finally
        {
            SetKeyboardState(new byte[256]);
        }
    }

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetKeyboardState(byte[] state);
}
