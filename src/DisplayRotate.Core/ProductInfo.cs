using System.Reflection;

namespace DisplayRotate.Core;

public static class ProductInfo
{
    public const string Name = "DisplayRotate";

    /// <summary>This build's version, three-part ("1.4.0"). "0.0.0" for a local build.</summary>
    public static string Version { get; } =
        typeof(ProductInfo).Assembly.GetName().Version is { } v
            ? Normalize(v).ToString(3)
            : DevVersion;

    /// <summary>What a build without CI's -p:Version reports (Directory.Build.props).</summary>
    public const string DevVersion = "0.0.0";

    public static bool IsDevBuild => Version == DevVersion;

    /// <summary>
    /// Reads absent version components as zero.
    /// </summary>
    /// <remarks>
    /// The SDK stamps a FOUR-part version ("1.4.0.0") into the assembly, while the git tag and
    /// every string we print are three-part ("1.4.0") - and System.Version ranks an absent
    /// component (-1) BELOW 0, so "1.4.0" and "1.4.0.0" do not compare equal. pawse shipped that
    /// bug for four releases. Do not "simplify" this away - the tests pin it.
    /// </remarks>
    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    public static bool IsNewer(Version current, Version candidate) =>
        Normalize(candidate) > Normalize(current);

    public static bool IsNewer(string current, Version candidate) =>
        System.Version.TryParse(current, out var now) && IsNewer(now, candidate);
}
