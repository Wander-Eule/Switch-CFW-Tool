using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using SwitchCfwTool.Models;

namespace SwitchCfwTool.Services;

/// <summary>
/// Thin wrapper around the GitHub REST API for fetching release metadata.
/// </summary>
/// <summary>A release together with the file to install from it. IsFallback is true when a newer release exists but has no usable file.</summary>
public sealed record InstallableRelease(GitHubRelease Release, GitHubAsset Asset, bool IsFallback);

public class GitHubService
{
    private readonly HttpClient _http;

    // Read from the assembly rather than written out, so it can't drift from <Version> in the
    // .csproj the way a hard-coded string does. GitHub requires a User-Agent but doesn't care
    // what it says, so a missing version simply falls back to something harmless.
    private static readonly string AppVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "1";

    public GitHubService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SwitchCfwTool", AppVersion));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public bool IsAuthenticated { get; private set; }

    /// <summary>
    /// Applies or clears a personal access token. Any valid token raises GitHub's rate
    /// limit from 60 to 5,000 requests/hour regardless of what scopes it has — this app
    /// only ever reads public release data, so no scopes are needed at all.
    /// </summary>
    public void UseToken(string? token)
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
        IsAuthenticated = !string.IsNullOrWhiteSpace(token);
    }

    /// <summary>Fetches the newest non-prerelease release for a given "owner/repo".</summary>
    public async Task<GitHubRelease?> GetLatestReleaseAsync(string ownerRepo, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{ownerRepo}/releases/latest";
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(await DescribeFailureAsync(resp, ct));
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GitHubRelease>(json);
    }

    /// <summary>
    /// The release a tool should be installed from: the newest published release that has a
    /// file matching the tool's pattern. Many projects publish a tag with only the source code
    /// attached, and "latest" alone would then find nothing to download. In that case the
    /// search goes back to the newest release that does have a file.
    /// </summary>
    public async Task<InstallableRelease> GetInstallableReleaseAsync(string ownerRepo, string? pattern,
        bool preferHighestVersion = false, CancellationToken ct = default)
    {
        var releases = await GetAllReleasesAsync(ownerRepo, ct);
        var skippedNewer = false;

        foreach (var release in releases)
        {
            if (release.Draft || release.Prerelease) continue;

            var asset = TrySelectAsset(release, pattern, preferHighestVersion);
            if (asset != null)
                return new InstallableRelease(release, asset, IsFallback: skippedNewer);

            skippedNewer = true;
        }

        throw new InvalidOperationException(Loc.T("Error.GitHub.NoInstallableRelease", ownerRepo));
    }

    private static GitHubAsset? TrySelectAsset(GitHubRelease release, string? pattern, bool preferHighestVersion)
    {
        if (release.Assets.Count == 0) return null;
        try
        {
            return SelectAsset(release, pattern, preferHighestVersion);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Fetches all releases (newest first) so the user can pick a specific one in "Custom" mode.</summary>
    public async Task<List<GitHubRelease>> GetAllReleasesAsync(string ownerRepo, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{ownerRepo}/releases";
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(await DescribeFailureAsync(resp, ct));
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? new();
    }

    /// <summary>
    /// Turns a failed GitHub API response into a message that actually explains what
    /// happened — in particular, GitHub's anonymous rate limit (60 requests/hour per IP)
    /// is easy to hit when building with many tools selected, and previously showed up as
    /// a generic "could not fetch" for every single tool at once with no indication why.
    /// </summary>
    private static async Task<string> DescribeFailureAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if ((resp.StatusCode == System.Net.HttpStatusCode.Forbidden || resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            && resp.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
            && remaining.FirstOrDefault() == "0"
            && resp.Headers.TryGetValues("X-RateLimit-Reset", out var resetHeader)
            && long.TryParse(resetHeader.FirstOrDefault(), out var resetUnix))
        {
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetUnix).ToLocalTime();
            return Loc.T("Error.GitHub.RateLimit", resetTime.ToString("HH:mm"));
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (body.Length > 200) body = body[..200] + "...";
        return Loc.T("Error.GitHub.RequestFailed", $"{(int)resp.StatusCode} {resp.ReasonPhrase}", body);
    }

    /// <summary>
    /// Picks the matching asset from a release using a simple wildcard pattern (only '*'
    /// is supported, e.g. "atmosphere-*.zip").
    ///
    /// Deliberately does NOT fall back to "just take the first asset" when the pattern
    /// misses: that silently downloaded the wrong file more than once (a debug zip instead
    /// of the overlay, a source archive instead of the .nro, ...) and the mistake only
    /// surfaced later as a missing or broken file on the SD card. Failing loudly with the
    /// list of assets that actually exist makes the wrong pattern immediately fixable.
    /// </summary>
    public static GitHubAsset? SelectAsset(GitHubRelease release, string? pattern, bool preferHighestVersion = false)
    {
        if (release.Assets.Count == 0)
            throw new InvalidOperationException(Loc.T("Error.GitHub.NoAssets", release.TagName));

        if (string.IsNullOrEmpty(pattern))
            return release.Assets[0];

        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*") + "$";
        var matches = release.Assets
            .Where(a => System.Text.RegularExpressions.Regex.IsMatch(a.Name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .ToList();

        if (matches.Count > 0)
        {
            // Some releases attach several builds at once — DBIPatcher, for instance, keeps
            // older versions alongside the current one. Taking whichever came first in the
            // list installed an outdated build, so the version in the filename decides.
            return preferHighestVersion
                ? matches.OrderByDescending(a => ParseVersion(a.Name), VersionOrder.Instance).First()
                : matches[0];
        }

        var available = string.Join(", ", release.Assets.Select(a => a.Name));
        throw new InvalidOperationException(
            Loc.T("Error.GitHub.PatternMismatch", release.TagName, pattern, available));
    }

    /// <summary>Pulls the version out of a filename as a list of numbers, e.g. "845.en.nro" → [845].</summary>
    public static int[] ParseVersion(string fileName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"\d+(?:\.\d+)*");
        if (!match.Success) return Array.Empty<int>();

        return match.Value.Split('.')
            .Select(part => int.TryParse(part, out var n) ? n : 0)
            .ToArray();
    }

    /// <summary>Compares versions segment by segment, so 845 sorts above 834 and 1.10 above 1.9.</summary>
    private sealed class VersionOrder : IComparer<int[]>
    {
        public static readonly VersionOrder Instance = new();

        public int Compare(int[]? x, int[]? y)
        {
            x ??= Array.Empty<int>();
            y ??= Array.Empty<int>();

            for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                var left = i < x.Length ? x[i] : 0;
                var right = i < y.Length ? y[i] : 0;
                if (left != right) return left.CompareTo(right);
            }

            return 0;
        }
    }
}
