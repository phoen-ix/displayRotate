namespace DisplayRotate.Core;

/// <summary>
/// Names the app and its installer must agree on.
/// </summary>
/// <remarks>
/// The mutex, the quit event and the uninstall key are duplicated in
/// packaging/displayrotate.nsi, and a test pins the two files together: renaming one alone
/// fails silently - the installer would stop finding a running copy, or stop asking it to quit.
/// </remarks>
public static class Names
{
    /// <summary>Held by the tray instance for as long as it runs. The installer polls it to
    /// know when the app has actually gone.</summary>
    public const string SingleInstanceMutex = @"Local\DisplayRotate-single-instance-4d9e21";

    /// <summary>Signalled by the installer to ask a running tray instance to exit.</summary>
    public const string QuitEvent = @"Local\DisplayRotate-quit-4d9e21";

    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayRotate";
    public const string BuildVariantValue = "BuildVariant";

    /// <summary>HKLM. <c>DisableUpdateCheck</c> = 1 (DWORD) turns every update check off.</summary>
    public const string PolicyKey = @"SOFTWARE\Policies\DisplayRotate";

    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValue = "DisplayRotate";

    public const string RepoUrl = "https://github.com/phoen-ix/displayRotate";
    public const string LatestReleaseUrl = RepoUrl + "/releases/latest";
    public const string DownloadRootUrl = RepoUrl + "/releases/download/";

    /// <summary>Update downloads go to %TEMP%\{prefix}{12 hex}\. The uninstaller sweeps them too.</summary>
    public const string UpdateDirectoryPrefix = "DisplayRotate-update-";
}
