using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DisplayRotate.Core.Updates;

/// <summary>
/// What the installer recorded about the copy that is running: for whom it was installed, where,
/// and which build.
/// </summary>
/// <remarks>
/// Read from the Add/Remove Programs entry the installer writes. An entry only counts if its
/// InstallLocation is the folder this exe runs from - an unzipped copy on a machine that also has
/// an installed one is still portable, and updating "it" through the installer would update the
/// other copy instead. HKLM is read in both registry views: the installer is a 32-bit program, so
/// its per-machine entry lands under WOW6432Node, which a 64-bit reader does not see by default.
/// </remarks>
public sealed record InstallRecord(InstallScope Scope, string? Location, string BuildVariant)
{
    /// <summary>A self-contained single-file build is tens of MB; the framework-dependent one is
    /// well under 1 MB. Only consulted when the entry does not say (an install made by hand).</summary>
    public const long FullBuildMinimumBytes = 20L * 1024 * 1024;

    public static InstallRecord Portable(string buildVariant) => new(InstallScope.Portable, null, buildVariant);

    /// <summary>The record for the exe at <paramref name="exePath"/>.</summary>
    public static InstallRecord Detect(string exePath)
    {
        var folder = Path.GetDirectoryName(exePath) ?? exePath;
        var size = FileSize(exePath);

        if (!OperatingSystem.IsWindows())
        {
            return Portable(InferVariant(size));
        }

        var user = ReadEntry(RegistryHive.CurrentUser, RegistryView.Default);
        var machine = ReadEntry(RegistryHive.LocalMachine, RegistryView.Registry64)
            is { Location: not null } native ? native : ReadEntry(RegistryHive.LocalMachine, RegistryView.Registry32);

        var scope = ScopeOf(folder, user?.Location, machine?.Location);
        var recorded = scope switch
        {
            InstallScope.PerUser => user?.Variant,
            InstallScope.PerMachine => machine?.Variant,
            _ => null,
        };

        return new InstallRecord(
            scope,
            scope == InstallScope.Portable ? null : folder,
            ResolveVariant(recorded, size));
    }

    /// <summary>HKCU first, the same order the installer's own previous-install probe uses.</summary>
    public static InstallScope ScopeOf(string exeFolder, string? userLocation, string? machineLocation)
    {
        if (!string.IsNullOrWhiteSpace(userLocation) && SameFolder(userLocation, exeFolder))
        {
            return InstallScope.PerUser;
        }

        if (!string.IsNullOrWhiteSpace(machineLocation) && SameFolder(machineLocation, exeFolder))
        {
            return InstallScope.PerMachine;
        }

        return InstallScope.Portable;
    }

    public static bool SameFolder(string a, string b)
    {
        try
        {
            return string.Equals(Canonical(a), Canonical(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        // Both separators: the registry holds Windows paths, and the tests run on Linux too.
        static string Canonical(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');
    }

    public static string InferVariant(long? exeBytes) =>
        exeBytes is >= FullBuildMinimumBytes ? ReleaseAssets.Full : ReleaseAssets.Min;

    public static string ResolveVariant(string? recorded, long? exeBytes) =>
        ReleaseAssets.IsVariant(recorded) ? recorded! : InferVariant(exeBytes);

    private sealed record Entry(string? Location, string? Variant);

    [SupportedOSPlatform("windows")]
    private static Entry? ReadEntry(RegistryHive hive, RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(Names.UninstallKey);

            if (key is null)
            {
                return null;
            }

            return new Entry(
                key.GetValue("InstallLocation") as string,
                key.GetValue(Names.BuildVariantValue) as string);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static long? FileSize(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
