using static DisplayRotate.DocsTests.DocsTour;

namespace DisplayRotate.DocsTests;

/// <summary>The Settings dialog's behaviour. WinForms needs Windows, so these live beside the tour.</summary>
public class SettingsFormTests
{
    [Fact]
    public void TickingAutoRestoreMakesRoomForTheDisplayRows() => OnSta(() =>
    {
        using var form = new SettingsForm(AppSettings.CreateDefault(), new Updater(AppSettings.CreateDefault));
        Show(form);
        var hotkeys = Find<Label>(form, "Hotkeys");
        var (top, height) = (hotkeys.Top, form.Height);

        Find<CheckBox>(form, "Auto-restore display orientations on startup").Checked = true;
        AssertRowsAboveHotkeys(form);
        var lastLine = Find<Label>(form, "Never checked.");
        Assert.True(lastLine.Bottom <= lastLine.Parent!.ClientSize.Height, "the update status is cut off");

        Find<CheckBox>(form, "Auto-restore display orientations on startup").Checked = false;
        Assert.Equal((top, height), (hotkeys.Top, form.Height));
    });

    internal static void AssertRowsAboveHotkeys(SettingsForm form)
    {
        var rows = Find<Button>(form, "Identify").Parent!;
        var hotkeys = Find<Label>(form, "Hotkeys");
        Assert.True(rows.Visible);
        Assert.True(rows.Bottom <= hotkeys.Top, $"the display rows end at {rows.Bottom}, the Hotkeys heading starts at {hotkeys.Top}");
    }

    private static T Find<T>(Control form, string text) where T : Control =>
        Controls(form).OfType<T>().First(c => c.Text == text);
}
