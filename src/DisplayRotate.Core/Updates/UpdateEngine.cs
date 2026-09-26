using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace DisplayRotate.Core.Updates;

public enum CheckOutcome
{
    UpToDate,
    Available,

    /// <summary>A local build (0.0.0): the latest release is reported, never offered.</summary>
    DevBuild,

    /// <summary>GitHub did not answer with a release - offline, a proxy, or no release yet.</summary>
    Unreachable,

    DisabledByPolicy,
}

public sealed record UpdateCheck(CheckOutcome Outcome, string Current, Version? Latest);

public enum ApplyOutcome
{
    /// <summary>The installer is running. It closes this app, replaces it, and starts it again.</summary>
    Installing,

    /// <summary>Not an installed copy: there is no installer to re-run.</summary>
    Portable,

    /// <summary>The administrator prompt a per-machine install needs was declined.</summary>
    Declined,

    Failed,
    DisabledByPolicy,
}

public sealed record UpdateApply(ApplyOutcome Outcome, string Message);

public interface IReleaseSource
{
    Task<Version?> FindLatestAsync(CancellationToken cancellationToken);

    Task<string?> FetchTextAsync(Version release, string assetName, CancellationToken cancellationToken);

    Task<Download> DownloadAsync(
        Version release, string assetName, string toPath, Action<long, long?> progress, CancellationToken cancellationToken);
}

/// <summary>A finished download: its SHA-256, or why there is none.</summary>
public sealed record Download(byte[]? Digest, long Bytes, string? Problem);

public interface IInstallerLauncher
{
    LaunchResult Launch(string installer, IReadOnlyList<string> arguments, bool elevate);
}

public sealed record LaunchResult(bool Started, bool Declined, string? Problem)
{
    public static LaunchResult Ok { get; } = new(true, false, null);
}

/// <summary>
/// Reports whether a newer release exists, and installs one when asked.
/// </summary>
/// <remarks>
/// <para>
/// WinLogRotate's updater, moved in-process. The version comes from GitHub's
/// <c>releases/latest</c> redirect rather than its API, which avoids the 60-per-hour
/// unauthenticated rate limit a whole office behind one NAT would share; the download comes from
/// the same release's fixed asset names, and is checked against that release's
/// <c>SHA256SUMS.txt</c> before anything runs it.
/// </para>
/// <para>
/// Nothing here installs on its own. <see cref="ApplyAsync"/> is only ever called because
/// someone pressed a button.
/// </para>
/// </remarks>
public sealed class UpdateEngine(
    IReleaseSource releases,
    IInstallerLauncher launcher,
    Func<bool> disabledByPolicy,
    Func<bool> elevated,
    string tempRoot)
{
    /// <summary>What was downloaded earlier and never cleaned up is removed after this.</summary>
    public static readonly TimeSpan DownloadGrace = TimeSpan.FromDays(1);

    public static UpdateEngine CreateDefault() => new(
        new GitHubReleases(),
        new ShellLauncher(),
        UpdatePolicy.IsUpdateCheckDisabled,
        () => Environment.IsPrivilegedProcess,
        Path.GetTempPath());

    public bool DisabledByPolicy => disabledByPolicy();

    public async Task<UpdateCheck> CheckAsync(string current, CancellationToken cancellationToken = default)
    {
        if (disabledByPolicy())
        {
            return new UpdateCheck(CheckOutcome.DisabledByPolicy, current, null);
        }

        var latest = await releases.FindLatestAsync(cancellationToken).ConfigureAwait(false);

        if (latest is null)
        {
            return new UpdateCheck(CheckOutcome.Unreachable, current, null);
        }

        if (current == ProductInfo.DevVersion)
        {
            return new UpdateCheck(CheckOutcome.DevBuild, current, latest);
        }

        return new UpdateCheck(
            ProductInfo.IsNewer(current, latest) ? CheckOutcome.Available : CheckOutcome.UpToDate,
            current,
            latest);
    }

    public async Task<UpdateApply> ApplyAsync(
        Version latest, InstallRecord record, Action<long, long?>? progress = null, CancellationToken cancellationToken = default)
    {
        if (disabledByPolicy())
        {
            return new UpdateApply(ApplyOutcome.DisabledByPolicy, "Updates are disabled by policy on this computer.");
        }

        if (record.Scope == InstallScope.Portable)
        {
            return new UpdateApply(ApplyOutcome.Portable,
                "This copy was not installed, so there is no installer to re-run. Download the new release and replace it, "
                + "or run the release's installer to have updates handled from now on.");
        }

        // The sums first, before anything is fetched that costs bandwidth. A release whose files
        // are still being uploaded - they appear one at a time - refuses here, in a second,
        // rather than after a download it would then have to discard.
        var sumsText = await releases.FetchTextAsync(latest, ReleaseAssets.SumsFileName, cancellationToken).ConfigureAwait(false);

        if (sumsText is null)
        {
            return Failed($"The release's {ReleaseAssets.SumsFileName} could not be downloaded. "
                + "If the release was published moments ago its files may still be uploading - try again in a few minutes.");
        }

        if (!Sha256Sums.TryParse(sumsText, out var sums, out var problem))
        {
            return Failed($"The release's {ReleaseAssets.SumsFileName} could not be read ({problem}). Nothing was downloaded.");
        }

        var asset = ReleaseAssets.InstallerName(record.BuildVariant, latest);

        if (sums.DigestOf(asset) is null)
        {
            return Failed($"The release does not list {asset} in its checksums. Nothing was downloaded.");
        }

        SweepOldDownloads(tempRoot, DateTime.UtcNow);

        var directory = Path.Combine(tempRoot, Names.UpdateDirectoryPrefix + Guid.NewGuid().ToString("N")[..12]);
        var installer = Path.Combine(directory, asset);

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed($"The download folder could not be created: {e.Message}");
        }

        var download = await releases.DownloadAsync(latest, asset, installer, progress ?? ((_, _) => { }), cancellationToken)
            .ConfigureAwait(false);

        if (download.Digest is null)
        {
            Discard(directory);
            return Failed($"{asset} could not be downloaded: {download.Problem}. Nothing was changed.");
        }

        if (!sums.Matches(asset, download.Digest))
        {
            Discard(directory);
            return Failed("The download did not match the release's checksum and was discarded. Nothing was changed.");
        }

        var elevate = record.Scope == InstallScope.PerMachine && !elevated();
        var launch = launcher.Launch(installer, InstallerArguments.For(record.Scope), elevate);

        if (launch.Declined)
        {
            Discard(directory);
            return new UpdateApply(ApplyOutcome.Declined,
                "The administrator prompt was declined, so nothing was changed. DisplayRotate is installed for everyone "
                + "on this computer, so updating it needs administrator rights.");
        }

        if (!launch.Started)
        {
            Discard(directory);
            return Failed($"The installer could not be started: {launch.Problem}. Nothing was changed.");
        }

        return new UpdateApply(ApplyOutcome.Installing,
            $"Installing {ProductInfo.Name} {latest.ToString(3)}. The tray icon disappears while the files are replaced, "
            + "and comes back at the new version.");
    }

    /// <summary>Removes update folders left behind by earlier runs, once they are a day old.</summary>
    public static void SweepOldDownloads(string tempRoot, DateTime nowUtc)
    {
        try
        {
            var cutoff = nowUtc - DownloadGrace;

            foreach (var directory in Directory.EnumerateDirectories(tempRoot, Names.UpdateDirectoryPrefix + "*"))
            {
                if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                {
                    Discard(directory);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static UpdateApply Failed(string message) => new(ApplyOutcome.Failed, message);

    private static void Discard(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Whether <paramref name="location"/> - a <c>releases/latest</c> redirect - names a release
    /// tag, and which: <c>https://github.com/{owner}/{repo}/releases/tag/v1.2.3</c> is 1.2.3.
    /// </summary>
    /// <remarks>
    /// Anything shaped differently is not an answer: a repository with no release yet redirects
    /// to <c>/releases</c>, and a sign-in wall to <c>/login</c>.
    /// </remarks>
    internal static Version? VersionFromTagUrl(string? location, string requestUrl = Names.LatestReleaseUrl)
    {
        if (string.IsNullOrWhiteSpace(location)
            || !Uri.TryCreate(requestUrl, UriKind.Absolute, out var baseUri)
            || !Uri.TryCreate(baseUri, location, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 5
            || !parts[2].Equals("releases", StringComparison.OrdinalIgnoreCase)
            || !parts[3].Equals("tag", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tag = Uri.UnescapeDataString(parts[4]).TrimStart('v', 'V');

        // A two-component tag (v0.8) would make ToString(3) throw further down.
        return Version.TryParse(tag, out var version) && version.Build >= 0 ? version : null;
    }

    internal static bool IsTrustedDownloadHost(string host) =>
        host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    private sealed class GitHubReleases : IReleaseSource
    {
        private const int MaxRedirects = 3;

        /// <summary>Headers to last byte, for the whole installer. A slow line finishes fifty
        /// megabytes inside this; a stalled one does not deserve to hold the tray for longer.</summary>
        private static readonly TimeSpan DownloadBudget = TimeSpan.FromMinutes(15);

        public async Task<Version?> FindLatestAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Do not follow the redirect: its Location header carries the tag, and stopping
                // there avoids downloading a release page we have no use for.
                using var client = NewClient(TimeSpan.FromSeconds(15));
                using var response = await client.GetAsync(Names.LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                return (int)response.StatusCode is >= 300 and <= 399
                    ? VersionFromTagUrl(response.Headers.Location?.OriginalString)
                    : null;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                return null;
            }
        }

        public async Task<string?> FetchTextAsync(Version release, string assetName, CancellationToken cancellationToken)
        {
            try
            {
                using var client = NewClient(TimeSpan.FromSeconds(30));
                using var response = await GetFollowingAsync(
                    client, Names.DownloadRootUrl + ReleaseAssets.DownloadPath(release, assetName), cancellationToken).ConfigureAwait(false);

                if (response is null || !response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
            {
                return null;
            }
        }

        public async Task<Download> DownloadAsync(
            Version release, string assetName, string toPath, Action<long, long?> progress, CancellationToken cancellationToken)
        {
            var done = 0L;

            // HttpClient.Timeout stops counting once the headers are in under ResponseHeadersRead,
            // so the body gets its own budget.
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(DownloadBudget);

            try
            {
                using var client = NewClient(Timeout.InfiniteTimeSpan);
                using var response = await GetFollowingAsync(
                    client, Names.DownloadRootUrl + ReleaseAssets.DownloadPath(release, assetName), budget.Token).ConfigureAwait(false);

                if (response is null)
                {
                    return new Download(null, 0, "the release redirected somewhere that is not GitHub");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new Download(null, 0, $"the server answered {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var total = response.Content.Headers.ContentLength;

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var body = await response.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(false);
                await using var file = new FileStream(toPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

                var buffer = new byte[1 << 16];
                int read;

                progress(0, total);

                while ((read = await body.ReadAsync(buffer, budget.Token).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), budget.Token).ConfigureAwait(false);
                    done += read;
                    progress(done, total);
                }

                if (total is > 0 && done != total)
                {
                    return new Download(null, done, $"the connection closed after {done:N0} of {total.Value:N0} bytes");
                }

                return new Download(hash.GetHashAndReset(), done, null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new Download(null, done, $"it took longer than {DownloadBudget.TotalMinutes:N0} minutes");
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
            {
                return new Download(null, done, e.Message);
            }
        }

        /// <summary>Follows GitHub's redirect to its object store, and nothing further afield.</summary>
        private static async Task<HttpResponseMessage?> GetFollowingAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var current = new Uri(url);

            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.MovedPermanently
                    or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                {
                    return response;
                }

                var location = response.Headers.Location;
                response.Dispose();

                if (location is null)
                {
                    return null;
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);

                if (current.Scheme != Uri.UriSchemeHttps || !IsTrustedDownloadHost(current.Host))
                {
                    return null;
                }
            }

            return null;
        }

        private static HttpClient NewClient(TimeSpan timeout)
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            var client = new HttpClient(handler) { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"{ProductInfo.Name}/{ProductInfo.Version}");
            return client;
        }
    }

    /// <summary>Starts the installer and does not wait for it.</summary>
    private sealed class ShellLauncher : IInstallerLauncher
    {
        private const int ErrorCancelled = 1223;

        public LaunchResult Launch(string installer, IReadOnlyList<string> arguments, bool elevate)
        {
            // Through the shell and without pipes: nothing is read back from it, and this process
            // is one of the files it is about to replace. "runas" only for a per-machine install
            // this process cannot write to - a per-user installer needs no prompt at all.
            var info = new ProcessStartInfo(installer)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installer),
                Verb = elevate ? "runas" : "",
            };

            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(info);
                return process is null ? new LaunchResult(false, false, "the system did not start it") : LaunchResult.Ok;
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
                return new LaunchResult(false, true, "the administrator prompt was declined");
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                return new LaunchResult(false, false, e.Message);
            }
        }
    }
}

public static class UpdatePolicy
{
    /// <summary>
    /// <c>HKLM\SOFTWARE\Policies\DisplayRotate\DisableUpdateCheck</c> = 1 turns every update check
    /// off - for a fleet whose software arrives some other way.
    /// </summary>
    public static bool IsUpdateCheckDisabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Names.PolicyKey);
            return key?.GetValue("DisableUpdateCheck") is int and 1;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}
