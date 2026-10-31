using DisplayRotate.Core;
using static DisplayRotate.DocsTests.DocsTour;
using static DisplayRotate.DocsTests.E2E.Harness;

namespace DisplayRotate.DocsTests.E2E;

/// <summary>
/// Breadth: every window the app opens, in each state it can open in, comes up showing what it
/// is, and no handler of its throws (DocsTour.OnSta fails the test on that).
/// </summary>
[Trait("Category", "E2E")]
public class Sweep
{
    public static TheoryData<string> Windows =>
    [
        "tray menu",
        "tray menu, update available",
        "Settings",
        "Settings, auto-restore on",
        "Settings, update available",
        "Settings, checks off by policy",
        "Settings, update installing",
        "Identify overlay",
    ];

    [Theory]
    [MemberData(nameof(Windows))]
    public void EveryWindowOpensAndSaysWhatItIs(string window)
    {
        NoOtherCopy();
        OnSta(() =>
        {
            using var state = new UserState();
            switch (window)
            {
                case "tray menu":
                case "tray menu, update available":
                {
                    using var tray = new Tray();
                    if (window.EndsWith("available", StringComparison.Ordinal))
                    {
                        tray.Releases.Latest = Newer;
                        tray.Item("Check for updates").PerformClick();
                        tray.ClickIcon(MouseButtons.Right);
                    }

                    var menu = tray.Menu;
                    menu.Show(new Point(40, 40));
                    Until(() => menu.Visible, "the menu to open");
                    Assert.StartsWith(window.EndsWith("available", StringComparison.Ordinal) ? "Install DisplayRotate " : "Display 1: ", menu.Items[0].Text);
                    menu.Close();
                    break;
                }

                case "Identify overlay":
                {
                    using var overlay = new OverlayForm(1, Screen.PrimaryScreen!, 60_000);
                    Show(overlay);
                    Assert.True(overlay.Visible && overlay.TopMost);
                    Assert.Equal(1, Get(overlay, "_displayNumber"));
                    break;
                }

                default:
                {
                    var settings = AppSettings.CreateDefault();
                    settings.AutoRestoreOrientations = window.EndsWith("auto-restore on", StringComparison.Ordinal);
                    var releases = new FakeReleases { Latest = Newer };
                    var updater = Updater(releases, disabledByPolicy: window.EndsWith("policy", StringComparison.Ordinal));
                    if (window.EndsWith("available", StringComparison.Ordinal))
                    {
                        updater.CheckAsync().GetAwaiter().GetResult();
                    }

                    if (window.EndsWith("installing", StringComparison.Ordinal))
                    {
                        typeof(Updater).GetProperty(nameof(Updater.Installing))!.SetValue(updater, true);
                    }

                    using var form = new SettingsForm(settings, updater);
                    Show(form);
                    Assert.Equal($"DisplayRotate Settings — v{ProductInfo.Version}", form.Text);
                    Assert.All(["General", "Hotkeys", "Updates"], heading => Assert.True(SettingsFormTests.Find<Label>(form, heading).Visible));
                    break;
                }
            }
        });
    }
}
