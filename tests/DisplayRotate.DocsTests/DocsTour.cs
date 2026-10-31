using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using DisplayRotate.Core;
using DisplayRotate.Core.Updates;

namespace DisplayRotate.DocsTests;

/// <summary>
/// The screenshots in README.md and docs/screenshots/: each test builds one of the app's windows
/// the way the app does, checks it shows what the caption says, and saves it as a PNG into
/// DOCS_SCREENSHOT_DIR. Run through scripts/docs-screenshots.sh.
/// </summary>
/// <remarks>
/// Left out: rotating a display (it changes the runner's screen) and the tray balloons (the shell
/// draws them).
/// </remarks>
[Trait("Category", "Docs")]
public class DocsTour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static DocsTour()
    {
        // What ApplicationConfiguration.Initialize() does in Program.Main.
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
    }

    [Fact]
    public void TrayMenu() => OnSta(() =>
    {
        using var menu = BuildTrayMenu(new Updater(AppSettings.CreateDefault));
        Assert.Contains(menu.Items.Cast<ToolStripItem>(), i => i.Text!.StartsWith("Display 1: ", StringComparison.Ordinal));
        Assert.Contains(menu.Items.Cast<ToolStripItem>(), i => i.Text == "Settings...");
        Save(Menu(menu), "tray-menu");
    });

    [Fact]
    public void TrayMenuWithAnUpdate() => OnSta(() =>
    {
        using var menu = BuildTrayMenu(UpdaterWithNewerRelease());
        Assert.Equal($"Install DisplayRotate {NewerRelease.ToString(3)}…", menu.Items[0].Text);
        Save(Menu(menu), "tray-menu-update");
    });

    [Fact]
    public void Settings() => OnSta(() =>
    {
        using var form = new SettingsForm(AppSettings.CreateDefault(), new Updater(AppSettings.CreateDefault));
        Show(form);
        Assert.Contains(Controls(form), c => c is Label { Text: "Never checked." });
        Assert.Equal(4, Controls(form).Count(c => c is TextBox t && t.Text.StartsWith("Ctrl+Alt+", StringComparison.Ordinal)));
        Save(Window(form), "settings");
    });

    [Fact]
    public void SettingsWithAutoRestore() => OnSta(() =>
    {
        var settings = AppSettings.CreateDefault();
        settings.AutoRestoreOrientations = true;
        using var form = new SettingsForm(settings, new Updater(AppSettings.CreateDefault));
        Show(form);
        SettingsFormTests.AssertRowsAboveHotkeys(form);
        Save(Window(form), "settings-auto-restore");
    });

    [Fact]
    public void SettingsWithAnUpdate() => OnSta(() =>
    {
        var settings = AppSettings.CreateDefault();
        settings.UpdateCheck = AppSettings.UpdateCheckDaily;
        using var form = new SettingsForm(settings, UpdaterWithNewerRelease());

        // The test host is not an installed copy, which would show "Open download page" instead.
        typeof(SettingsForm).GetField("_install", Private)!
            .SetValue(form, new InstallRecord(InstallScope.PerUser, @"C:\Users\Demo\AppData\Local\Programs\DisplayRotate", ReleaseAssets.Full));
        typeof(SettingsForm).GetMethod("RefreshUpdateControls", Private)!.Invoke(form, null);

        Show(form);
        Assert.Contains(Controls(form), c => c is Button { Visible: true } b && b.Text == $"Update to {NewerRelease.ToString(3)}");
        Save(Window(form), "settings-update");
    });

    [Fact]
    public void IdentifyOverlay() => OnSta(() =>
    {
        // A fixed 16:9 "screen", so the picture does not follow the runner's resolution.
        using var overlay = new OverlayForm(1, Screen.PrimaryScreen!, 60_000) { Bounds = new Rectangle(0, 0, 960, 540) };
        Show(overlay);
        Assert.Equal(new Size(960, 540), overlay.ClientSize);
        Save(Draw(overlay, overlay.ClientSize), "identify-overlay");
    });

    private static Version NewerRelease
    {
        get
        {
            var current = Version.Parse(ProductInfo.Version);
            return new Version(current.Major, current.Minor + 1, 0);
        }
    }

    private static Updater UpdaterWithNewerRelease()
    {
        var updater = new Updater(AppSettings.CreateDefault);
        typeof(Updater).GetProperty(nameof(Updater.Last))!
            .SetValue(updater, new UpdateCheck(CheckOutcome.Available, ProductInfo.Version, NewerRelease));
        return updater;
    }

    /// <summary>
    /// TrayApp's own menu. Its constructor registers hotkeys, restores orientations and starts
    /// timers; BuildMenu needs none of that, only the settings and the updater it reads.
    /// </summary>
    private static ContextMenuStrip BuildTrayMenu(Updater updater)
    {
        var tray = (TrayApp)RuntimeHelpers.GetUninitializedObject(typeof(TrayApp));
        typeof(TrayApp).GetField("_settings", Private)!.SetValue(tray, AppSettings.CreateDefault());
        typeof(TrayApp).GetField("_updater", Private)!.SetValue(tray, updater);
        return (ContextMenuStrip)typeof(TrayApp).GetMethod("BuildMenu", Private)!.Invoke(tray, null)!;
    }

    internal static void Show(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(40, 40);
        form.Show();
        Application.DoEvents();
    }

    private static Bitmap Menu(ContextMenuStrip menu)
    {
        menu.Show(new Point(40, 40));
        Application.DoEvents();
        return Draw(menu, menu.Size);
    }

    /// <summary>The control's own painting (WM_PRINT); right for the menu and the borderless overlay.</summary>
    private static Bitmap Draw(Control control, Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
        return bitmap;
    }

    /// <summary>
    /// The window as the desktop composes it, title bar included. DrawToBitmap would paint the
    /// frame in the classic style, which no Windows 10 or 11 desktop shows.
    /// </summary>
    private static Bitmap Window(Form form)
    {
        // Drawn as the active window whether or not the test host holds the foreground.
        NativeMethods.SendMessage(form.Handle, WM_NCACTIVATE, 1, IntPtr.Zero);
        Application.DoEvents();

        Assert.True(GetWindowRect(form.Handle, out var window));
        Assert.Equal(0, DwmGetWindowAttribute(form.Handle, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, Marshal.SizeOf<RECT>()));

        using var whole = new Bitmap(window.Right - window.Left, window.Bottom - window.Top, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(whole))
        {
            var hdc = g.GetHdc();
            try
            {
                Assert.True(PrintWindow(form.Handle, hdc, PW_RENDERFULLCONTENT));
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        // Without the invisible resize border around the visible frame.
        return whole.Clone(
            Rectangle.FromLTRB(frame.Left - window.Left, frame.Top - window.Top, frame.Right - window.Left, frame.Bottom - window.Top),
            whole.PixelFormat);
    }

    private static void Save(Bitmap bitmap, string name)
    {
        using (bitmap)
        {
            Assert.True(bitmap.Width > 100 && bitmap.Height > 100, $"{name} is only {bitmap.Width}x{bitmap.Height}");

            var dir = Environment.GetEnvironmentVariable("DOCS_SCREENSHOT_DIR");
            Assert.False(string.IsNullOrEmpty(dir), "DOCS_SCREENSHOT_DIR is not set; run scripts/docs-screenshots.sh");
            Directory.CreateDirectory(dir);
            bitmap.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
        }
    }

    internal static IEnumerable<Control> Controls(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => Controls(c).Prepend(c));

    /// <summary>
    /// WinForms wants a single-threaded apartment; xunit's threads are not one. An exception a
    /// window's message handler throws (a click, a timer, an async handler) fails the test too,
    /// instead of opening WinForms' error dialog.
    /// </summary>
    internal static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var unhandled = new List<Exception>();
            Application.ThreadException += (_, e) => unhandled.Add(e.Exception);
            try
            {
                body();
                if (unhandled.Count > 0)
                {
                    throw new AggregateException("a window's message handler threw", unhandled);
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90)))
        {
            throw new TimeoutException("the UI thread still runs after 90 s: a dialog or message box nobody closed?");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    private const int WM_NCACTIVATE = 0x0086;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint PW_RENDERFULLCONTENT = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
}
