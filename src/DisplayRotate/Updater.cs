using DisplayRotate.Core;
using DisplayRotate.Core.Updates;

namespace DisplayRotate;

/// <summary>
/// The one updater the tray menu and the Settings dialog share: what the last check found,
/// whether a check or an install is running, and the watchdog that notices an install that
/// never happened.
/// </summary>
internal sealed class Updater : IDisposable
{
    /// <summary>
    /// Armed once the installer has the file. If this process is still here when it fires, the
    /// installer did not replace it: an install that works closes this app within seconds.
    /// </summary>
    private readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 180_000 };

    private readonly Func<AppSettings> _settings;

    public Updater(Func<AppSettings> settings)
    {
        _settings = settings;
        _watchdog.Tick += (_, _) =>
        {
            _watchdog.Stop();
            Installing = false;
            InstallDidNotHappen?.Invoke();
            Changed?.Invoke();
        };
    }

    public UpdateEngine Engine { get; } = UpdateEngine.CreateDefault();

    /// <summary>The most recent check's answer, or null before the first one.</summary>
    public UpdateCheck? Last { get; private set; }

    /// <summary>The newer release the last check found, if it found one.</summary>
    public Version? Available => Last is { Outcome: CheckOutcome.Available, Latest: { } v } ? v : null;

    public bool Busy { get; private set; }

    /// <summary>The installer was started and this process has not been closed by it yet.</summary>
    public bool Installing { get; private set; }

    /// <summary>Raised on the UI thread whenever <see cref="Last"/>, <see cref="Busy"/> or
    /// <see cref="Installing"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when the watchdog fires: the installer was started but this app was never closed.</summary>
    public event Action? InstallDidNotHappen;

    public static InstallRecord DetectInstall() =>
        InstallRecord.Detect(Environment.ProcessPath ?? Application.ExecutablePath);

    /// <summary>Checks now. Returns null if a check or install is already running.</summary>
    public async Task<UpdateCheck?> CheckAsync()
    {
        if (Busy || Installing)
        {
            return null;
        }

        Busy = true;
        Changed?.Invoke();

        try
        {
            var result = await Engine.CheckAsync(ProductInfo.Version).ConfigureAwait(true);

            // Stamped whether or not GitHub answered, so a daily check on an offline machine
            // waits a day rather than trying again every hour.
            if (result.Outcome != CheckOutcome.DisabledByPolicy)
            {
                _settings().StampUpdateCheck(DateTimeOffset.UtcNow);
            }

            Last = result;
            return result;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Downloads, verifies and starts the installer for <see cref="Available"/>.</summary>
    public async Task<UpdateApply?> ApplyAsync(Action<long, long?> progress)
    {
        if (Busy || Installing || Available is not { } latest)
        {
            return null;
        }

        Busy = true;
        Changed?.Invoke();

        try
        {
            var result = await Engine.ApplyAsync(latest, DetectInstall(), progress).ConfigureAwait(true);

            if (result.Outcome == ApplyOutcome.Installing)
            {
                Installing = true;
                _watchdog.Start();
            }

            return result;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    public static void OpenReleasePage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Names.LatestReleaseUrl) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    public static string Describe(UpdateCheck check) => check.Outcome switch
    {
        CheckOutcome.Available => $"{ProductInfo.Name} {check.Latest!.ToString(3)} is available (you have {check.Current}).",
        CheckOutcome.UpToDate => $"You have the newest release, {check.Current}.",
        CheckOutcome.DevBuild => $"This is a development build. The newest release is {check.Latest!.ToString(3)}.",
        CheckOutcome.DisabledByPolicy => "Update checks are disabled by policy on this computer.",
        _ => "GitHub could not be reached. Nothing was changed.",
    };

    public static string Progress(long done, long? total) =>
        total is > 0
            ? $"Downloading… {done * 100 / total.Value}%"
            : $"Downloading… {done / (1024 * 1024)} MB";

    public void Dispose() => _watchdog.Dispose();
}
