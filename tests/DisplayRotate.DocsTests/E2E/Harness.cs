using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DisplayRotate.Core;
using DisplayRotate.Core.Updates;
using Microsoft.Win32;
using static DisplayRotate.DocsTests.E2E.Harness;

// One test at a time: the e2e tests share the user's settings file, the autostart value, the
// global hotkeys and the desktop.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace DisplayRotate.DocsTests.E2E;

/// <summary>What the e2e tests drive the app with. Run them through scripts/e2e.sh.</summary>
internal static class Harness
{
    public const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>A release one minor above this build (scripts/e2e.sh builds 9.9.9).</summary>
    public static Version Newer
    {
        get
        {
            Assert.False(ProductInfo.IsDevBuild, "a 0.0.0 build is never offered an update; run scripts/e2e.sh");
            var current = Version.Parse(ProductInfo.Version);
            return new Version(current.Major, current.Minor + 1, 0);
        }
    }

    public static object? Get(object owner, string field) =>
        owner.GetType().GetField(field, Private)!.GetValue(owner);

    public static object? Call(object owner, string method, params object?[] args) =>
        owner.GetType().GetMethod(method, Private)!.Invoke(owner, args);

    /// <summary>Waits for <paramref name="condition"/>, handling the thread's window messages meanwhile.</summary>
    public static void Until(Func<bool> condition, string what, double seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"waited {seconds} s for {what}");
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Runs <paramref name="act"/> on the dialog once ShowDialog has it on screen: a timer, so it
    /// runs inside the dialog's own message loop. If it throws, the dialog is cancelled, so the
    /// ShowDialog that opened it returns and the test fails instead of hanging.
    /// </summary>
    public static void OnceShown<T>(Action<T> act) where T : Form
    {
        var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            if (Application.OpenForms.OfType<T>().FirstOrDefault(f => f.Visible) is not { } form)
            {
                return;
            }

            timer.Dispose();
            try
            {
                act(form);
            }
            catch
            {
                form.DialogResult = DialogResult.Cancel;
                throw;
            }
        };
        timer.Start();
    }

    /// <summary>
    /// Runs <paramref name="app"/>'s message loop as Program.Main does, starting with
    /// <paramref name="first"/>. True if the app ended the loop itself within the time.
    /// </summary>
    public static bool RunUntilExit(ApplicationContext app, Action first, int seconds = 10)
    {
        var timedOut = false;
        var start = new System.Windows.Forms.Timer { Interval = 1 };
        start.Tick += (_, _) =>
        {
            start.Dispose();
            first();
        };
        var limit = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
        limit.Tick += (_, _) =>
        {
            limit.Dispose();
            timedOut = true;
            Application.ExitThread();
        };
        start.Start();
        limit.Start();
        Application.Run(app);
        limit.Dispose();
        return !timedOut;
    }

    /// <summary>An updater whose GitHub is <paramref name="releases"/>.</summary>
    public static Updater Updater(FakeReleases releases, bool disabledByPolicy = false)
    {
        var updater = new Updater(AppSettings.Load);
        UseReleases(updater, releases, disabledByPolicy);
        return updater;
    }

    public static void UseReleases(Updater updater, FakeReleases releases, bool disabledByPolicy = false) =>
        typeof(Updater).GetField("<Engine>k__BackingField", Private)!.SetValue(
            updater, new UpdateEngine(releases, releases, () => disabledByPolicy, () => false, Path.GetTempPath()));

    /// <summary>
    /// A running copy holds the hotkeys and listens on the installer's quit event too, so the
    /// tests that need them to themselves skip while one runs.
    /// </summary>
    public static void NoOtherCopy()
    {
        if (Mutex.TryOpenExisting(Names.SingleInstanceMutex, out var running))
        {
            running.Dispose();
            Assert.Skip("DisplayRotate is running on this desktop; quit it to run this test");
        }
    }

    /// <summary>Whether this thread could take the combination: false while someone holds it.</summary>
    public static bool HotkeyIsFree(uint modifiers, Keys key)
    {
        const int id = 0xB00;
        if (!RegisterHotKey(IntPtr.Zero, id, modifiers | NativeMethods.MOD_NOREPEAT, (uint)key))
        {
            return false;
        }

        UnregisterHotKey(IntPtr.Zero, id);
        return true;
    }

    public const uint CtrlAlt = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;

    /// <summary>
    /// Runs <paramref name="act"/>, which opens a message box on this thread, answers the box with
    /// <paramref name="answer"/> from a timer (it runs inside the box's own message loop) and
    /// returns the box's text.
    /// </summary>
    public static string AnswerMessageBox(Action act, DialogResult answer)
    {
        string? text = null;
        var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            var box = IntPtr.Zero;
            EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
            {
                var name = new StringBuilder(16);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() != "#32770" || !IsWindowVisible(window))
                {
                    return true;
                }

                box = window;
                return false;
            }, IntPtr.Zero);
            if (box == IntPtr.Zero)
            {
                return;
            }

            timer.Stop();
            var label = new StringBuilder(1024);
            GetWindowText(GetDlgItem(box, 0xFFFF), label, label.Capacity);
            text = label.ToString();
            PostMessage(box, WM_COMMAND, (nint)answer, IntPtr.Zero);
        };
        timer.Start();
        try
        {
            act();
        }
        finally
        {
            timer.Dispose();
        }

        Assert.True(text is not null, "no message box opened");
        return text.ReplaceLineEndings("\n");
    }

    private const int WM_COMMAND = 0x0111;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr param);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr dialog, int id);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int message, nint wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>A real TrayApp, its update checks answered by <see cref="Releases"/>.</summary>
internal sealed class Tray : IDisposable
{
    public Tray()
    {
        App = new TrayApp();
        UseReleases(Updater, Releases);
    }

    public TrayApp App { get; }

    public FakeReleases Releases { get; } = new();

    public Updater Updater => (Updater)Get(App, "_updater")!;

    public NotifyIcon Icon => (NotifyIcon)Get(App, "_trayIcon")!;

    public ContextMenuStrip Menu => Icon.ContextMenuStrip!;

    public List<ToolStripItem> Items => Menu.Items.Cast<ToolStripItem>().ToList();

    public ToolStripMenuItem Item(string text) => Menu.Items.OfType<ToolStripMenuItem>().First(i => i.Text == text);

    /// <summary>A click on the tray icon, as the shell reports it.</summary>
    public void ClickIcon(MouseButtons button) =>
        (typeof(NotifyIcon).GetMethod("OnMouseClick", Private) ?? throw new MissingMethodException(nameof(NotifyIcon), "OnMouseClick"))
            .Invoke(Icon, [new MouseEventArgs(button, 1, 0, 0, 0)]);

    /// <summary>A click on the balloon showing now, as the shell reports it.</summary>
    public void ClickBalloon() =>
        (typeof(NotifyIcon).GetMethod("OnBalloonTipClicked", Private) ?? throw new MissingMethodException(nameof(NotifyIcon), "OnBalloonTipClicked"))
            .Invoke(Icon, null);

    public void Dispose() => App.Dispose();
}

/// <summary>GitHub as the update tests want it: <see cref="Latest"/> null is "not reachable".</summary>
internal sealed class FakeReleases : IReleaseSource, IInstallerLauncher
{
    public Version? Latest { get; set; }

    public int Checks { get; private set; }

    public Task<Version?> FindLatestAsync(CancellationToken cancellationToken)
    {
        Checks++;
        return Task.FromResult(Latest);
    }

    public Task<string?> FetchTextAsync(Version release, string assetName, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("downloads are UpdateEngineTests' business");

    public Task<Download> DownloadAsync(
        Version release, string assetName, string toPath, Action<long, long?> progress, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("downloads are UpdateEngineTests' business");

    public LaunchResult Launch(string installer, IReadOnlyList<string> arguments, bool elevate) =>
        throw new InvalidOperationException("installs are UpdateEngineTests' business");
}

/// <summary>
/// The account's settings file and autostart value, seeded for a test and put back as they were
/// when it ends. The app has one settings path (%APPDATA%\DisplayRotate), so the tests use it.
/// </summary>
internal sealed class UserState : UserBackup
{
    public UserState(Action<AppSettings>? seed = null)
    {
        var settings = AppSettings.CreateDefault();
        seed?.Invoke(settings);
        settings.Save();
    }
}

/// <summary>
/// The account's settings file and autostart value as they are now, put back on Dispose.
/// DocsTour.OnSta takes one too, so a test its time limit stops still puts them back.
/// </summary>
internal class UserBackup : IDisposable
{
    private static readonly string SettingsPath = AppSettings.GetSettingsPath();
    private readonly byte[]? _settings = File.Exists(SettingsPath) ? File.ReadAllBytes(SettingsPath) : null;
    private readonly string? _run = RunValue;

    public static AppSettings Saved => AppSettings.Load();

    public static string SettingsFile => SettingsPath;

    public static string? RunValue
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Names.RunKey);
            return key?.GetValue(Names.RunValue) as string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(Names.RunKey);
            if (value is null)
            {
                key.DeleteValue(Names.RunValue, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(Names.RunValue, value);
            }
        }
    }

    public void Dispose()
    {
        if (_settings is null)
        {
            // File.Delete throws when the folder is missing too.
            if (File.Exists(SettingsPath))
            {
                File.Delete(SettingsPath);
            }
        }
        else
        {
            File.WriteAllBytes(SettingsPath, _settings);
        }

        RunValue = _run;
    }
}
