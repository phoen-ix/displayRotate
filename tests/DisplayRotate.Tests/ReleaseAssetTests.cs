using DisplayRotate.Core.Updates;

namespace DisplayRotate.Tests;

public class ReleaseAssetTests
{
    private const string A = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string B = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    [Fact]
    public void ReadsTheFileCiWrites()
    {
        // CI writes it on Windows: lower-case hex, two spaces, CRLF.
        var text = $"{A}  DisplayRotate-Setup-0.1.0-full.exe\r\n{B}  DisplayRotate-Setup-0.1.0-min.exe\r\n";

        Assert.True(Sha256Sums.TryParse(text, out var sums, out _));
        Assert.Equal(2, sums.Count);
        Assert.Equal(A, sums.DigestOf("DisplayRotate-Setup-0.1.0-full.exe"));
        Assert.True(sums.Matches("DisplayRotate-Setup-0.1.0-min.exe", Convert.FromHexString(B)));
        Assert.False(sums.Matches("DisplayRotate-Setup-0.1.0-min.exe", Convert.FromHexString(A)));
        Assert.Null(sums.DigestOf("DisplayRotate-Setup-0.1.0.exe"));
    }

    [Fact]
    public void AcceptsUpperCaseAndTheBinaryMarker()
    {
        Assert.True(Sha256Sums.TryParse("﻿" + A.ToUpperInvariant() + " *file.exe\n", out var sums, out _));
        Assert.Equal(A, sums.DigestOf("file.exe"));
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html>Not Found</html>")]
    [InlineData("")]
    [InlineData("   \n\r\n")]
    [InlineData("0123  file.exe")]
    [InlineData(A)]
    [InlineData(A + "x file.exe")]
    public void RefusesAnythingThatIsNotAChecksumFile(string text) =>
        Assert.False(Sha256Sums.TryParse(text, out _, out _));

    [Fact]
    public void RefusesANameListedTwiceWithDifferentDigests()
    {
        Assert.False(Sha256Sums.TryParse($"{A}  file.exe\n{B}  file.exe\n", out _, out var problem));
        Assert.Contains("twice", problem);
    }

    [Theory]
    [InlineData("full", "DisplayRotate-Setup-0.1.0-full.exe")]
    [InlineData("min", "DisplayRotate-Setup-0.1.0-min.exe")]
    public void AnUpdateFetchesTheSameBuild(string variant, string expected) =>
        Assert.Equal(expected, ReleaseAssets.InstallerName(variant, new Version(0, 1, 0)));

    [Fact]
    public void ThereIsNoThirdBuild() =>
        Assert.Throws<ArgumentException>(() => ReleaseAssets.InstallerName("", new Version(0, 1, 0)));

    [Fact]
    public void AnUpdateRunsSilentlyInTheSameScopeAndRestarts()
    {
        Assert.Equal(["/S", "/CurrentUser", "/NORUNTIME", "/RESTART"], InstallerArguments.For(InstallScope.PerUser));
        Assert.Equal(["/S", "/AllUsers", "/NORUNTIME", "/RESTART"], InstallerArguments.For(InstallScope.PerMachine));
        Assert.Throws<ArgumentException>(() => InstallerArguments.For(InstallScope.Portable));
    }

    [Theory]
    [InlineData("/home/u/Programs/DisplayRotate", "/home/u/Programs/DisplayRotate", null, InstallScope.PerUser)]
    [InlineData("/home/u/Programs/DisplayRotate", "/home/u/Programs/DisplayRotate/", "/opt/DisplayRotate", InstallScope.PerUser)]
    [InlineData("/opt/DisplayRotate", "/home/u/Programs/DisplayRotate", "/opt/DisplayRotate", InstallScope.PerMachine)]
    [InlineData("/opt/displayrotate", null, "/opt/DisplayRotate", InstallScope.PerMachine)]
    [InlineData("/home/u/Downloads/DisplayRotate", "/home/u/Programs/DisplayRotate", "/opt/DisplayRotate", InstallScope.Portable)]
    [InlineData("/home/u/Downloads", null, null, InstallScope.Portable)]
    [InlineData("/home/u/Downloads", "", "  ", InstallScope.Portable)]
    public void OnlyTheFolderAnInstallerRecordedCountsAsInstalled(string exeFolder, string? user, string? machine, InstallScope expected) =>
        Assert.Equal(expected, InstallRecord.ScopeOf(exeFolder, user, machine));

    [Theory]
    [InlineData("full", 100L, "full")]
    [InlineData("min", 100L * 1024 * 1024, "min")]
    [InlineData(null, 60L * 1024 * 1024, "full")]
    [InlineData(null, 300L * 1024, "min")]
    [InlineData("banana", 300L * 1024, "min")]
    [InlineData(null, null, "min")]
    public void TheRecordedBuildWinsOverTheExeSize(string? recorded, long? bytes, string expected) =>
        Assert.Equal(expected, InstallRecord.ResolveVariant(recorded, bytes));
}
