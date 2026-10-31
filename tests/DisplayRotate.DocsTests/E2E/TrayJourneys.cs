using DisplayRotate.Core;
using static DisplayRotate.DocsTests.DocsTour;
using static DisplayRotate.DocsTests.E2E.Harness;

namespace DisplayRotate.DocsTests.E2E;

/// <summary>
/// The tray app as Program.Main starts it (a real TrayApp over seeded settings): its menu, the
/// windows it opens, its balloons, its hotkeys and how it ends. Rotating a display is left out:
/// it would turn the runner's screen (tests/COVERAGE.md).
/// </summary>
[Trait("Category", "E2E")]
public class TrayJourneys
{
    private static readonly string[] Orientations =
        ["0° — Landscape", "90° — Portrait", "180° — Landscape flipped", "270° — Portrait flipped"];

    [Fact]
    public void TheMenuListsEveryDisplayWithItsOrientationsAndTicksTheCurrentOne()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();

            var expected = new List<string>();
            foreach (var d in DisplayManager.GetDisplays())
            {
                expected.Add($"Display {d.Index}: {d.FriendlyName}{(d.IsPrimary ? " (Primary)" : "")} [{d.Width}x{d.Height}]");
                expected.AddRange(Orientations);
                expected.AddRange(["Identify", "-"]);
            }

            expected.AddRange(["Identify Displays", "Settings...", "Check for updates", "-", "Exit"]);
            Assert.Equal(expected, tray.Items.Select(i => i is ToolStripSeparator ? "-" : i.Text));

            foreach (var d in DisplayManager.GetDisplays())
            {
                var header = tray.Items.FindIndex(i => i.Text!.StartsWith($"Display {d.Index}: ", StringComparison.Ordinal));
                Assert.False(tray.Items[header].Enabled);
                var ticked = tray.Items.Skip(header + 1).Take(4).Select(i => ((ToolStripMenuItem)i).Checked);
                Assert.Equal(Enumerable.Range(0, 4).Select(o => o == d.CurrentOrientation), ticked);
            }
        });
    }

    [Fact]
    public void ClickingTheTickedOrientationKeepsItAndRebuildsTheMenu()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();
            var before = tray.Menu;
            var orientations = DisplayManager.GetDisplays().Select(d => d.CurrentOrientation).ToList();

            // A failed rotation would open a message box, which the 90 s limit would catch.
            tray.Items.OfType<ToolStripMenuItem>().First(i => i.Checked).PerformClick();

            Assert.NotSame(before, tray.Menu);
            Assert.Equal(orientations, DisplayManager.GetDisplays().Select(d => d.CurrentOrientation));
        });
    }

    [Fact]
    public void IdentifyShowsTheDisplaysNumberOnItsScreenForTheSetDuration()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState(s => s.OverlayDurationMs = 500);
            using var tray = new Tray();
            var display = DisplayManager.GetDisplays()[0];

            tray.Item("Identify").PerformClick();

            var overlay = Assert.Single(Overlays());
            Assert.Equal(display.Index, Get(overlay, "_displayNumber"));
            Assert.Equal(Screen.AllScreens.Single(s => s.DeviceName == display.DeviceName).Bounds, overlay.Bounds);
            Assert.True(overlay.TopMost);
            Until(() => Overlays().Count == 0, "the overlay to go after half a second");
        });
    }

    [Fact]
    public void IdentifyDisplaysNumbersEveryScreenAndAClickOrAKeyClosesIt()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState(s => s.OverlayDurationMs = 60_000);
            using var tray = new Tray();
            var numbered = DisplayManager.GetDisplays()
                .Where(d => Screen.AllScreens.Any(s => s.DeviceName == d.DeviceName))
                .Select(d => d.Index);

            tray.Item("Identify Displays").PerformClick();
            Assert.Equal(numbered.Order(), Overlays().Select(o => (int)Get(o, "_displayNumber")!).Order());
            foreach (var overlay in Overlays())
            {
                Call(overlay, "OnClick", EventArgs.Empty);
            }

            Assert.Empty(Overlays());

            tray.Item("Identify Displays").PerformClick();
            foreach (var overlay in Overlays())
            {
                Call(overlay, "OnKeyDown", new KeyEventArgs(Keys.Escape));
            }

            Assert.Empty(Overlays());
        });
    }

    [Fact]
    public void WithoutIdentifyButtonsOnlyIdentifyDisplaysIsLeft()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState(s => s.ShowIdentifyButtons = false);
            using var tray = new Tray();

            Assert.DoesNotContain(tray.Items, i => i.Text == "Identify");
            Assert.Contains(tray.Items, i => i.Text == "Identify Displays");
        });
    }

    [Fact]
    public void SettingsSavedFromTheMenuTakeEffectAtOnce()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();
            var freeWhileOpen = false;
            OnceShown<SettingsForm>(form =>
            {
                freeWhileOpen = HotkeyIsFree(CtrlAlt, Keys.Up);
                SettingsFormTests.Find<CheckBox>(form, "Show Identify buttons in context menu").Checked = false;
                SettingsFormTests.Find<Button>(form, "Save").PerformClick();
            });

            tray.Item("Settings...").PerformClick();

            Assert.True(freeWhileOpen, "the hotkeys stay registered while Settings is open, so its key boxes cannot capture them");
            Assert.False(UserState.Saved.ShowIdentifyButtons);
            Assert.DoesNotContain(tray.Items, i => i.Text == "Identify");
            Assert.False(HotkeyIsFree(CtrlAlt, Keys.Up), "the hotkeys were not registered again");
        });
    }

    [Fact]
    public void CancelledSettingsChangeNothing()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();
            var file = File.ReadAllBytes(UserState.SettingsFile);
            OnceShown<SettingsForm>(form =>
            {
                SettingsFormTests.Find<CheckBox>(form, "Show Identify buttons in context menu").Checked = false;
                SettingsFormTests.Find<Button>(form, "Cancel").PerformClick();
            });

            tray.Item("Settings...").PerformClick();

            Assert.Equal(file, File.ReadAllBytes(UserState.SettingsFile));
            Assert.Contains(tray.Items, i => i.Text == "Identify");
            Assert.False(HotkeyIsFree(CtrlAlt, Keys.Up), "the hotkeys were not registered again");
        });
    }

    [Theory]
    [InlineData("same", "You have the newest release, {0}.", ToolTipIcon.Info)]
    [InlineData("none", "GitHub could not be reached. Nothing was changed.", ToolTipIcon.Warning)]
    [InlineData("newer", "DisplayRotate {1} is available (you have {0}). Click here to install it.", ToolTipIcon.Info)]
    public void CheckForUpdatesAnswersInABalloon(string latest, string text, ToolTipIcon icon)
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();
            tray.Releases.Latest = latest switch
            {
                "same" => Version.Parse(ProductInfo.Version),
                "newer" => Newer,
                _ => null,
            };

            tray.Item("Check for updates").PerformClick();
            Until(() => !tray.Updater.Busy, "the check");

            Assert.Equal("Updates", tray.Icon.BalloonTipTitle);
            Assert.Equal(string.Format(text, ProductInfo.Version, latest == "newer" ? Newer.ToString(3) : ""), tray.Icon.BalloonTipText);
            Assert.Equal(icon, tray.Icon.BalloonTipIcon);
            Assert.NotNull(UserState.Saved.LastUpdateCheckUtc);

            // The menu is rebuilt on every click on the icon.
            tray.ClickIcon(MouseButtons.Right);
            var first = tray.Items[0];
            Assert.Equal(latest == "newer", first.Text == $"Install DisplayRotate {Newer.ToString(3)}…");
            if (latest == "newer")
            {
                Assert.True(first.Font.Bold && first.Enabled);
            }
        });
    }

    [Fact]
    public void InstallOnACopyThatWasNotInstalledOffersTheDownloadPage()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();
            tray.Releases.Latest = Newer;
            tray.Item("Check for updates").PerformClick();
            Until(() => !tray.Updater.Busy, "the check");
            // The test host is a portable copy, as Updater.DetectInstall sees it.
            var prompt = $"DisplayRotate {Newer.ToString(3)} is available.\n\nThis copy was not installed, so it can't update itself. Open the download page?";

            Assert.Equal(prompt, AnswerMessageBox(tray.ClickBalloon, DialogResult.No));
            tray.ClickIcon(MouseButtons.Right);
            Assert.Equal(prompt, AnswerMessageBox(() => tray.Item($"Install DisplayRotate {Newer.ToString(3)}…").PerformClick(), DialogResult.No));

            Assert.False(tray.Updater.Busy || tray.Updater.Installing);
        });
    }

    [Fact]
    public void ChecksTurnedOffByPolicyGreyOutCheckForUpdates()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();
            UseReleases(tray.Updater, tray.Releases, disabledByPolicy: true);

            tray.ClickIcon(MouseButtons.Right);

            Assert.False(tray.Item("Check for updates").Enabled);
        });
    }

    [Theory]
    [InlineData(AppSettings.UpdateCheckDaily, 1)]
    [InlineData(AppSettings.UpdateCheckManual, 0)]
    public void TheScheduledCheckRunsOnlyWhenChosenAndOnlyOnceADay(string mode, int checks)
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState(s => s.UpdateCheck = mode);
            using var tray = new Tray();
            tray.Releases.Latest = Newer;

            // What the hourly timer runs; the second is not due, as the first stamped the time.
            for (var hour = 0; hour < 2; hour++)
            {
                var check = (Task)Call(tray.App, "ScheduledCheckAsync")!;
                Until(() => check.IsCompleted, "the scheduled check");
                check.GetAwaiter().GetResult();
            }

            Assert.Equal(checks, tray.Releases.Checks);
            if (checks > 0)
            {
                Assert.Equal($"DisplayRotate {Newer.ToString(3)} is available", tray.Icon.BalloonTipTitle);
                Assert.NotNull(UserState.Saved.LastUpdateCheckUtc);
            }
        });
    }

    [Fact]
    public void AnInstallThatNeverClosedTheAppIsReported()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();
            typeof(Updater).GetProperty(nameof(Updater.Installing))!.SetValue(tray.Updater, true);

            // The watchdog's three minutes, run out.
            Call(Get(tray.Updater, "_watchdog")!, "OnTick", EventArgs.Empty);

            Assert.False(tray.Updater.Installing);
            Assert.Equal("The update did not install", tray.Icon.BalloonTipTitle);
            Assert.Equal(ToolTipIcon.Warning, tray.Icon.BalloonTipIcon);
        });
    }

    [Fact]
    public void AHotkeyAnotherProgramHoldsIsNamedInABalloon()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            const int id = 0xB01;
            Assert.True(RegisterHotKey(IntPtr.Zero, id, CtrlAlt | NativeMethods.MOD_NOREPEAT, (uint)Keys.Up), "Ctrl+Alt+Up is taken already");
            try
            {
                using var state = new UserState();
                using var tray = new Tray();

                Assert.Equal("DisplayRotate", tray.Icon.BalloonTipTitle);
                Assert.Contains("Ctrl+Alt+Up — Rotate Primary to 0° (Landscape)", tray.Icon.BalloonTipText);
                Assert.DoesNotContain("Ctrl+Alt+Right", tray.Icon.BalloonTipText);
            }
            finally
            {
                UnregisterHotKey(IntPtr.Zero, id);
            }
        });
    }

    [Fact]
    public void TheEnabledHotkeysAreHeldWhileTheAppRuns()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState(s => s.Hotkeys[1].IsEnabled = false);
            using (new Tray())
            {
                Assert.False(HotkeyIsFree(CtrlAlt, Keys.Up));
                Assert.True(HotkeyIsFree(CtrlAlt, Keys.Right), "a switched-off hotkey was registered");
            }

            Assert.True(HotkeyIsFree(CtrlAlt, Keys.Up), "the hotkeys outlived the app");
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ not json")]
    public void MissingOrUnreadableSettingsStartWithTheDefaults(string? content)
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            if (content is null)
            {
                File.Delete(UserState.SettingsFile);
            }
            else
            {
                File.WriteAllText(UserState.SettingsFile, content);
            }

            using var tray = new Tray();

            Assert.Contains(tray.Items, i => i.Text == "Identify");
            Assert.All([Keys.Up, Keys.Right, Keys.Down, Keys.Left], key => Assert.False(HotkeyIsFree(CtrlAlt, key)));
        });
    }

    [Fact]
    public void StartWithWindowsIsPointedAtThisCopyAtStartup()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState(s => s.StartWithWindows = true);
            UserState.RunValue = @"""C:\Moved\DisplayRotate.exe""";

            using var tray = new Tray();

            Assert.Equal($"\"{Application.ExecutablePath}\"", UserState.RunValue);
        });
    }

    [Fact]
    public void ALeftClickOnTheIconOpensTheMenu()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();

            tray.ClickIcon(MouseButtons.Left);

            var menu = tray.Menu;
            Until(() => menu.Visible, "the menu to open");
            menu.Close();
        });
    }

    [Fact]
    public void ExitEndsTheApp()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();

            Assert.True(RunUntilExit(tray.App, () => tray.Item("Exit").PerformClick()), "Exit did not end the app");
            Assert.False(tray.Icon.Visible);
        });
    }

    [Fact]
    public void TheInstallersQuitRequestEndsTheApp()
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            using var tray = new Tray();
            using var quit = EventWaitHandle.OpenExisting(Names.QuitEvent);

            Assert.True(RunUntilExit(tray.App, () => quit.Set()), "the quit event did not end the app");
            Assert.False(tray.Icon.Visible);
        });
    }

    private static List<OverlayForm> Overlays() => Application.OpenForms.OfType<OverlayForm>().ToList();
}
