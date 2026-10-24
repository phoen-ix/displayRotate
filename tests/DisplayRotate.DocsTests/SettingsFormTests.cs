using System.Reflection;
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

    [Fact]
    public void CancelKeepsTheHotkeys() => OnSta(() =>
    {
        // Not the defaults, so Reset to Defaults changes something; and Reset first, so it can't
        // undo what the others did.
        var settings = AppSettings.CreateDefault();
        settings.Hotkeys.RemoveAt(3);
        var before = Describe(settings.Hotkeys);
        using var form = new SettingsForm(settings, new Updater(AppSettings.CreateDefault));
        Show(form);

        Find<Button>(form, "Reset to Defaults").PerformClick();
        PressCtrlAltF5(form, 0);
        Find<Button>(form, "Add").PerformClick();
        RemoveRow(form, 1);
        Find<Button>(form, "Cancel").PerformClick();

        Assert.Equal(before, Describe(settings.Hotkeys));
    });

    [Fact]
    public void RemoveDeletesTheRowLastFocused() => OnSta(() =>
    {
        using var form = new SettingsForm(AppSettings.CreateDefault(), new Updater(AppSettings.CreateDefault));
        Show(form);

        RemoveRow(form, 2);

        Assert.Equal(new[] { "Ctrl+Alt+Up", "Ctrl+Alt+Right", "Ctrl+Alt+Left" }, HotkeyBoxes(form).Select(t => t.Text));
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

    // Not every TextBox: the NumericUpDown has one inside.
    private static List<TextBox> HotkeyBoxes(Control form) =>
        Controls(form).OfType<TextBox>().Where(t => t.Parent is Panel { BorderStyle: BorderStyle.FixedSingle }).ToList();

    /// <summary>What a mouse click on Remove does: the button takes the focus, then clicks.</summary>
    private static void RemoveRow(SettingsForm form, int index)
    {
        Assert.True(HotkeyBoxes(form)[index].Focus());
        var remove = Find<Button>(form, "Remove");
        Assert.True(remove.Focus());
        remove.PerformClick();
    }

    /// <summary>Ctrl+Alt+F5 pressed in a row's hotkey box.</summary>
    private static void PressCtrlAltF5(SettingsForm form, int index)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        var rows = (System.Collections.IList)typeof(SettingsForm).GetField("_hotkeyRows", Private)!.GetValue(form)!;
        var row = rows[index]!;
        row.GetType().GetMethod("OnHotkeyKeyDown", Private)!.Invoke(row, [null, new KeyEventArgs(Keys.Control | Keys.Alt | Keys.F5)]);
        Assert.Equal("Ctrl+Alt+F5", HotkeyBoxes(form)[index].Text);
    }

    private static List<string> Describe(List<HotkeyBinding> hotkeys) =>
        hotkeys.Select(h => $"{h.IsEnabled} {h.TargetDisplayIndex} {h.TargetOrientation} {h.FormatHotkey()}").ToList();
}
