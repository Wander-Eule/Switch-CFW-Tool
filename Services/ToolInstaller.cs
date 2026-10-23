using System;
using System.IO;
using System.Threading.Tasks;
using SwitchCfwTool.Models;

namespace SwitchCfwTool.Services;

/// <summary>
/// Installs one tool onto a card or into a build folder.
///
/// Pulled out of the main window so Manage CFW can install and reinstall through exactly the
/// same path. Two copies of this would drift apart, and the consequences of drift here are
/// files landing in the wrong place on someone's SD card.
/// </summary>
public class ToolInstaller
{
    private readonly GitHubService _github;
    private readonly DownloadService _downloader;
    private readonly SdCardBuilder _builder;
    private readonly string _sdRoot;
    private readonly string _basePath;
    private readonly bool _asImport;
    private readonly Action<string, LogKind> _log;

    /// <param name="basePath">Where PC-side extras go; usually the folder holding the SDMC folder.</param>
    /// <param name="asImport">
    /// True while building a folder that still has to be copied onto a card, so the install
    /// record is handed over separately instead of overwriting the card's own.
    /// </param>
    public ToolInstaller(GitHubService github, DownloadService downloader, SdCardBuilder builder,
        string sdRoot, string basePath, bool asImport, Action<string, LogKind> log)
    {
        _github = github;
        _downloader = downloader;
        _builder = builder;
        _sdRoot = sdRoot;
        _basePath = basePath;
        _asImport = asImport;
        _log = log;
    }

    /// <param name="pinnedUrl">A specific release chosen by the user, or null for the newest.</param>
    public async Task<bool> InstallAsync(CfwTool tool, string? pinnedUrl, string? pinnedFileName,
        string? pinnedVersion, bool downloadOptionalExtra)
    {
        _log(Loc.T("Log.Install.Resolving", tool.DisplayName), LogKind.Normal);

        try
        {
            string url;
            string fileName;
            string version;

            if (pinnedUrl != null && pinnedFileName != null)
            {
                url = pinnedUrl;
                fileName = pinnedFileName;
                version = pinnedVersion ?? "";
            }
            else
            {
                var pick = await _github.GetInstallableReleaseAsync(tool.GitHubRepo!, tool.AssetNamePattern, tool.VersionFromAssetName);
                var release = pick.Release;
                var asset = pick.Asset;
                if (pick.IsFallback)
                    _log(Loc.T("Log.Install.OlderRelease", tool.DisplayName, release.TagName), LogKind.Info);

                url = asset.BrowserDownloadUrl;
                fileName = asset.Name;
                version = DescribeVersion(tool, release, asset.Name);
            }

            var progress = new Progress<DownloadProgress>(p =>
                _log($"[{tool.DisplayName}] {Loc.T("Log.Stage." + p.Stage)}: {p.Message}", LogKind.Normal));

            var staged = await _downloader.DownloadAndStageAsync(tool.Id, url, fileName, progress);
            _builder.MergeTool(tool, staged, fileName, detail => _log($"[{tool.DisplayName}] {detail}", LogKind.Normal));

            _log(Loc.T("Log.Install.Merged", tool.DisplayName), LogKind.Success);

            InstallManifest.Upsert(_sdRoot, new InstallRecord
            {
                ToolId = tool.Id,
                DisplayName = tool.DisplayName,
                Version = version,
                InstalledAt = DateTime.Now,
                Files = _builder.TakeWrittenFiles()
            }, _asImport);

            await DownloadCompanionsAsync(tool);

            if (downloadOptionalExtra)
                await DownloadOptionalExtraAsync(tool);

            return true;
        }
        catch (Exception ex)
        {
            _log(Loc.T("Log.Install.ToolFailed", tool.DisplayName, ex.Message), LogKind.Error);
            return false;
        }
    }

    /// <summary>Extra repositories a tool always brings with it.</summary>
    private async Task DownloadCompanionsAsync(CfwTool tool)
    {
        foreach (var companion in tool.Companions)
        {
            try
            {
                _log(Loc.T("Log.Install.ResolvingCompanion", companion.DisplayName), LogKind.Normal);

                var pick = await _github.GetInstallableReleaseAsync(companion.GitHubRepo, companion.AssetNamePattern);
                var asset = pick.Asset;
                if (pick.IsFallback)
                    _log(Loc.T("Log.Install.OlderRelease", companion.DisplayName, pick.Release.TagName), LogKind.Info);

                var staged = await _downloader.DownloadAndStageAsync(
                    tool.Id + "-" + companion.DisplayName, asset.BrowserDownloadUrl, asset.Name);

                // Routed like any other tool, so it lands in the folder its file type implies.
                var pseudo = new CfwTool
                {
                    Id = "companion-" + companion.DisplayName,
                    Author = companion.Author,
                    DisplayName = companion.DisplayName,
                    GitHubRepo = companion.GitHubRepo
                };

                _builder.MergeTool(pseudo, staged, asset.Name,
                    detail => _log($"[{companion.DisplayName}] {detail}", LogKind.Normal));

                _log(Loc.T("Log.Install.CompanionAdded", companion.DisplayName, tool.DisplayName), LogKind.Success);
            }
            catch (Exception ex)
            {
                _log(Loc.T("Log.Install.CompanionFailed", companion.DisplayName, ex.Message), LogKind.Error);
            }
        }
    }

    private async Task DownloadOptionalExtraAsync(CfwTool tool)
    {
        var extra = tool.OptionalExtra;
        if (extra == null) return;

        try
        {
            var pick = await _github.GetInstallableReleaseAsync(extra.GitHubRepo, extra.AssetNamePattern);
            var asset = pick.Asset;
            if (pick.IsFallback)
                _log(Loc.T("Log.Install.OlderRelease", tool.DisplayName, pick.Release.TagName), LogKind.Info);

            var staged = await _downloader.DownloadAndStageAsync(
                tool.Id + "-extra", asset.BrowserDownloadUrl, asset.Name);

            // PC-side companions belong beside the card's contents, not on the card itself.
            var targetDir = extra.PlaceNextToSdmc ? _basePath : _sdRoot;
            Directory.CreateDirectory(targetDir);
            File.Copy(Path.Combine(staged, asset.Name), Path.Combine(targetDir, asset.Name), overwrite: true);

            _log(Loc.T("Log.Install.ExtraSaved", tool.DisplayName, asset.Name, targetDir), LogKind.Success);
        }
        catch (Exception ex)
        {
            _log(Loc.T("Log.Install.ExtraFailed", tool.DisplayName, ex.Message), LogKind.Error);
        }
    }

    /// <summary>
    /// The version to record. Normally the release tag, but taken from the filename for tools
    /// whose tag and attached file can disagree, so the number stored is the one installed.
    /// </summary>
    public static string DescribeVersion(CfwTool tool, GitHubRelease release, string assetName)
    {
        if (!tool.VersionFromAssetName) return release.TagName;

        var match = System.Text.RegularExpressions.Regex.Match(assetName, @"\d+(?:\.\d+)*");
        return match.Success ? match.Value : release.TagName;
    }
}
