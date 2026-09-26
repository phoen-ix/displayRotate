namespace DisplayRotate.Core.Updates;

/// <summary>
/// The command line an update runs the installer with.
/// </summary>
/// <remarks>
/// Every switch is explicit, because the installer's defaults are for a person clicking through
/// it. The scope is spelled out so a silent run cannot pick the other one. /NORUNTIME because
/// the runtime was there before the update - this process is running on it - and is still
/// there after. /RESTART because nothing else brings the tray icon back once the installer has
/// asked it to quit. A test pins every switch here to the .nsi, which ignores unknown ones.
/// </remarks>
public static class InstallerArguments
{
    public static IReadOnlyList<string> For(InstallScope scope, bool restart = true)
    {
        if (scope == InstallScope.Portable)
        {
            throw new ArgumentException("A portable copy has no installer to re-run.", nameof(scope));
        }

        var arguments = new List<string>
        {
            "/S",
            scope == InstallScope.PerMachine ? "/AllUsers" : "/CurrentUser",
            "/NORUNTIME",
        };

        if (restart)
        {
            arguments.Add("/RESTART");
        }

        return arguments;
    }
}
