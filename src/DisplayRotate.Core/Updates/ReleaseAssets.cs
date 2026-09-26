namespace DisplayRotate.Core.Updates;

/// <summary>The fixed file names every release carries (ci.yml, release job).</summary>
public static class ReleaseAssets
{
    /// <summary>Every download's SHA-256, one per line, beside the downloads.</summary>
    public const string SumsFileName = "SHA256SUMS.txt";

    public const string Full = "full";
    public const string Min = "min";

    public static bool IsVariant(string? variant) => variant is Full or Min;

    /// <summary>
    /// The installer an update fetches: the one carrying the same build as this install.
    /// </summary>
    /// <remarks>
    /// The unsuffixed installer is never chosen. It carries both builds and asks which one on a
    /// page a silent install never sees, so it would always land the full build - the wrong one
    /// for a minimal install, and a 40 MB download that the -full installer does as well.
    /// </remarks>
    public static string InstallerName(string variant, Version version)
    {
        if (!IsVariant(variant))
        {
            throw new ArgumentException($"'{variant}' is not a build this product ships.", nameof(variant));
        }

        return $"DisplayRotate-Setup-{version.ToString(3)}-{variant}.exe";
    }

    /// <summary>The path under a release's download root: <c>v1.2.3/name</c>.</summary>
    public static string DownloadPath(Version version, string assetName) =>
        $"v{version.ToString(3)}/{assetName}";
}
