using System.Security.Cryptography;
using System.Text;
using DisplayRotate.Core;
using DisplayRotate.Core.Updates;

namespace DisplayRotate.Tests;

public sealed class UpdateEngineTests : IDisposable
{
    private static readonly Version Latest = new(0, 2, 0);
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("pretend this is an NSIS installer");
    private static readonly InstallRecord PerUser = new(InstallScope.PerUser, "/somewhere", ReleaseAssets.Full);
    private static readonly InstallRecord PerMachine = new(InstallScope.PerMachine, "/somewhere", ReleaseAssets.Min);

    private readonly string _temp = Directory.CreateTempSubdirectory("dr-tests-").FullName;
    private readonly FakeReleases _releases = new();
    private readonly FakeLauncher _launcher = new();
    private bool _policy;
    private bool _elevated;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private UpdateEngine Engine() => new(_releases, _launcher, () => _policy, () => _elevated, _temp);

    public UpdateEngineTests()
    {
        _releases.Latest = Latest;
        _releases.Files["DisplayRotate-Setup-0.2.0-full.exe"] = Installer;
        _releases.Files["DisplayRotate-Setup-0.2.0-min.exe"] = Installer;
        _releases.Files[ReleaseAssets.SumsFileName] = Encoding.UTF8.GetBytes(
            $"{Hex(Installer)}  DisplayRotate-Setup-0.2.0-full.exe\r\n{Hex(Installer)}  DisplayRotate-Setup-0.2.0-min.exe\r\n");
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static string Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private string[] UpdateFolders() => Directory.GetDirectories(_temp, Names.UpdateDirectoryPrefix + "*");

    [Theory]
    [InlineData("0.1.0", CheckOutcome.Available)]
    [InlineData("0.2.0", CheckOutcome.UpToDate)]
    [InlineData("0.3.0", CheckOutcome.UpToDate)]
    [InlineData(ProductInfo.DevVersion, CheckOutcome.DevBuild)]
    public async Task ACheckComparesAgainstTheLatestRelease(string current, CheckOutcome expected)
    {
        var result = await Engine().CheckAsync(current, Ct);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(Latest, result.Latest);
    }

    [Fact]
    public async Task NoAnswerFromGitHubIsUnreachable()
    {
        _releases.Latest = null;
        Assert.Equal(CheckOutcome.Unreachable, (await Engine().CheckAsync("0.1.0", Ct)).Outcome);
    }

    [Fact]
    public async Task ThePolicyStopsTheCheckBeforeAnythingGoesOnline()
    {
        _policy = true;
        Assert.Equal(CheckOutcome.DisabledByPolicy, (await Engine().CheckAsync("0.1.0", Ct)).Outcome);
        Assert.Equal(ApplyOutcome.DisabledByPolicy, (await Engine().ApplyAsync(Latest, PerUser, cancellationToken: Ct)).Outcome);
        Assert.Equal(0, _releases.Requests);
    }

    [Fact]
    public async Task APerUserInstallRunsTheVerifiedInstallerSilentlyWithoutElevation()
    {
        var result = await Engine().ApplyAsync(Latest, PerUser, cancellationToken: Ct);

        Assert.Equal(ApplyOutcome.Installing, result.Outcome);
        var launch = Assert.Single(_launcher.Launches);
        Assert.Equal("DisplayRotate-Setup-0.2.0-full.exe", Path.GetFileName(launch.Installer));
        Assert.Equal(Installer, File.ReadAllBytes(launch.Installer));
        Assert.Equal(["/S", "/CurrentUser", "/NORUNTIME", "/RESTART"], launch.Arguments);
        Assert.False(launch.Elevate);
    }

    [Fact]
    public async Task APerMachineInstallAsksForElevationUnlessItHasIt()
    {
        await Engine().ApplyAsync(Latest, PerMachine, cancellationToken: Ct);
        _elevated = true;
        await Engine().ApplyAsync(Latest, PerMachine, cancellationToken: Ct);

        Assert.Collection(_launcher.Launches,
            first =>
            {
                Assert.Equal("DisplayRotate-Setup-0.2.0-min.exe", Path.GetFileName(first.Installer));
                Assert.Equal(["/S", "/AllUsers", "/NORUNTIME", "/RESTART"], first.Arguments);
                Assert.True(first.Elevate);
            },
            second => Assert.False(second.Elevate));
    }

    [Fact]
    public async Task APortableCopyIsNeverUpdatedThroughAnInstaller()
    {
        var result = await Engine().ApplyAsync(Latest, InstallRecord.Portable(ReleaseAssets.Full), cancellationToken: Ct);

        Assert.Equal(ApplyOutcome.Portable, result.Outcome);
        Assert.Empty(_launcher.Launches);
        Assert.Equal(0, _releases.Requests);
    }

    [Fact]
    public async Task ADownloadThatDoesNotMatchIsDiscardedAndNeverRun()
    {
        _releases.Files["DisplayRotate-Setup-0.2.0-full.exe"] = Encoding.UTF8.GetBytes("tampered");

        var result = await Engine().ApplyAsync(Latest, PerUser, cancellationToken: Ct);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Contains("checksum", result.Message);
        Assert.Empty(_launcher.Launches);
        Assert.Empty(UpdateFolders());
    }

    [Fact]
    public async Task MissingChecksumsRefuseBeforeTheInstallerIsDownloaded()
    {
        _releases.Files.Remove(ReleaseAssets.SumsFileName);

        var result = await Engine().ApplyAsync(Latest, PerUser, cancellationToken: Ct);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Equal(1, _releases.Requests);
        Assert.Empty(_launcher.Launches);
    }

    [Fact]
    public async Task AMalformedChecksumFileRefuses()
    {
        _releases.Files[ReleaseAssets.SumsFileName] = Encoding.UTF8.GetBytes("<html>rate limited</html>");

        var result = await Engine().ApplyAsync(Latest, PerUser, cancellationToken: Ct);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Empty(_launcher.Launches);
    }

    [Fact]
    public async Task AnInstallerTheChecksumsDoNotListIsNotDownloaded()
    {
        _releases.Files[ReleaseAssets.SumsFileName] = Encoding.UTF8.GetBytes($"{Hex(Installer)}  DisplayRotate-Setup-0.2.0-min.exe\n");

        var result = await Engine().ApplyAsync(Latest, PerUser, cancellationToken: Ct);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Contains("does not list", result.Message);
        Assert.Equal(1, _releases.Requests);
    }

    [Fact]
    public async Task ADeclinedPromptChangesNothing()
    {
        _launcher.Result = new LaunchResult(false, true, "declined");

        var result = await Engine().ApplyAsync(Latest, PerMachine, cancellationToken: Ct);

        Assert.Equal(ApplyOutcome.Declined, result.Outcome);
        Assert.Empty(UpdateFolders());
    }

    [Fact]
    public async Task AnInstallerThatWillNotStartIsReported()
    {
        _launcher.Result = new LaunchResult(false, false, "blocked by policy");

        var result = await Engine().ApplyAsync(Latest, PerUser, cancellationToken: Ct);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Contains("blocked by policy", result.Message);
        Assert.Empty(UpdateFolders());
    }

    [Fact]
    public void OnlyOldDownloadsAreSwept()
    {
        var old = Directory.CreateDirectory(Path.Combine(_temp, Names.UpdateDirectoryPrefix + "old"));
        var fresh = Directory.CreateDirectory(Path.Combine(_temp, Names.UpdateDirectoryPrefix + "fresh"));
        var unrelated = Directory.CreateDirectory(Path.Combine(_temp, "SomethingElse-update-old"));
        var now = DateTime.UtcNow;
        Directory.SetLastWriteTimeUtc(old.FullName, now.AddDays(-2));
        Directory.SetLastWriteTimeUtc(unrelated.FullName, now.AddDays(-2));

        UpdateEngine.SweepOldDownloads(_temp, now);

        Assert.False(old.Exists && Directory.Exists(old.FullName));
        Assert.True(Directory.Exists(fresh.FullName));
        Assert.True(Directory.Exists(unrelated.FullName));
    }

    private sealed class FakeReleases : IReleaseSource
    {
        public Version? Latest { get; set; }
        public Dictionary<string, byte[]> Files { get; } = new();
        public int Requests { get; private set; }

        public Task<Version?> FindLatestAsync(CancellationToken cancellationToken) => Task.FromResult(Latest);

        public Task<string?> FetchTextAsync(Version release, string assetName, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(Files.TryGetValue(assetName, out var data) ? Encoding.UTF8.GetString(data) : null);
        }

        public async Task<Download> DownloadAsync(
            Version release, string assetName, string toPath, Action<long, long?> progress, CancellationToken cancellationToken)
        {
            Requests++;
            if (!Files.TryGetValue(assetName, out var data))
            {
                return new Download(null, 0, "404");
            }

            await File.WriteAllBytesAsync(toPath, data, cancellationToken);
            progress(data.Length, data.Length);
            return new Download(SHA256.HashData(data), data.Length, null);
        }
    }

    private sealed class FakeLauncher : IInstallerLauncher
    {
        public List<(string Installer, IReadOnlyList<string> Arguments, bool Elevate)> Launches { get; } = [];
        public LaunchResult Result { get; set; } = LaunchResult.Ok;

        public LaunchResult Launch(string installer, IReadOnlyList<string> arguments, bool elevate)
        {
            if (Result.Started)
            {
                // Read before the test's Dispose removes the temp folder.
                Assert.True(File.Exists(installer));
            }

            Launches.Add((installer, arguments, elevate));
            return Result;
        }
    }
}
