using DisplayRotate.Core;
using DisplayRotate.Core.Updates;

namespace DisplayRotate.Tests;

public class VersionTests
{
    [Fact]
    public void NormalizeTreatsMissingComponentsAsZero()
    {
        // The SDK stamps 1.4.0.0 while the tag says 1.4.0; System.Version alone ranks them apart.
        Assert.NotEqual(new Version(1, 4, 0), new Version(1, 4, 0, 0));
        Assert.Equal(ProductInfo.Normalize(new Version(1, 4, 0)), ProductInfo.Normalize(new Version(1, 4, 0, 0)));
    }

    [Theory]
    [InlineData("0.1.0", "0.1.1", true)]
    [InlineData("0.1.0", "0.2.0", true)]
    [InlineData("0.9.0", "0.10.0", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.0.0", "0.1.0", false)]
    [InlineData("0.2.0", "0.1.9", false)]
    [InlineData("not a version", "9.9.9", false)]
    public void IsNewerComparesNumerically(string current, string candidate, bool expected) =>
        Assert.Equal(expected, ProductInfo.IsNewer(current, Version.Parse(candidate)));

    [Fact]
    public void ALocalBuildReportsTheDevVersion()
    {
        // Directory.Build.props stamps 0.0.0-dev; only CI's -p:Version makes a real one.
        Assert.Equal(ProductInfo.DevVersion, ProductInfo.Version);
        Assert.True(ProductInfo.IsDevBuild);
    }

    [Theory]
    [InlineData("https://github.com/phoen-ix/displayRotate/releases/tag/v0.1.0", "0.1.0")]
    [InlineData("https://github.com/phoen-ix/displayRotate/releases/tag/v12.3.45", "12.3.45")]
    [InlineData("https://github.com/phoen-ix/displayRotate/releases/tag/0.2.1", "0.2.1")]
    [InlineData("/phoen-ix/displayRotate/releases/tag/v1.0.0", "1.0.0")]
    public void TheLatestRedirectNamesTheRelease(string location, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateEngine.VersionFromTagUrl(location));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://github.com/phoen-ix/displayRotate/releases")]
    [InlineData("https://github.com/login?return_to=x")]
    [InlineData("https://github.com/phoen-ix/displayRotate/releases/tag/v0.8")]
    [InlineData("https://github.com/phoen-ix/displayRotate/releases/tag/nightly")]
    [InlineData("http://github.com/phoen-ix/displayRotate/releases/tag/v1.0.0")]
    [InlineData("https://evil.example/phoen-ix/displayRotate/releases/tag/v1.0.0")]
    public void AnythingElseIsNotAnAnswer(string? location) =>
        Assert.Null(UpdateEngine.VersionFromTagUrl(location));

    [Theory]
    [InlineData("github.com", true)]
    [InlineData("objects.githubusercontent.com", true)]
    [InlineData("release-assets.githubusercontent.com", true)]
    [InlineData("codeload.github.com", true)]
    [InlineData("github.com.evil.example", false)]
    [InlineData("notgithub.com", false)]
    [InlineData("githubusercontent.com.evil.example", false)]
    public void DownloadsOnlyFollowGitHub(string host, bool trusted) =>
        Assert.Equal(trusted, UpdateEngine.IsTrustedDownloadHost(host));

    [Theory]
    [InlineData(null, true)]
    [InlineData(-25, true)]
    [InlineData(-24, true)]
    [InlineData(-23, false)]
    [InlineData(0, false)]
    [InlineData(5, true)] // a stamp in the future: the clock went backwards
    public void TheDailyCheckIsDueAfterADay(int? hoursAgo, bool due)
    {
        var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset? last = hoursAgo is { } h ? now.AddHours(h) : null;
        Assert.Equal(due, UpdateSchedule.IsCheckDue(last, now));
    }
}
