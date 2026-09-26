namespace DisplayRotate.Core.Updates;

public enum InstallScope
{
    /// <summary>Not where any installer put it: an unzipped copy, or a build run in place.</summary>
    Portable,

    /// <summary>Installed for the current user (HKCU uninstall entry, %LOCALAPPDATA%\Programs).</summary>
    PerUser,

    /// <summary>Installed for everyone (HKLM uninstall entry, Program Files). Updating needs elevation.</summary>
    PerMachine,
}
