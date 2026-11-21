using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SwitchCfwTool.Data;
using SwitchCfwTool.Models;
using SwitchCfwTool.Services;

namespace SwitchCfwTool;

/// <summary>One row in the tool list — installed or not.</summary>
public class InstalledEntry
{
    public required string ToolId { get; init; }
    public required string DisplayName { get; init; }
    public required string Details { get; init; }
    public required List<string> Files { get; init; }
    public required bool IsInstalled { get; init; }

    /// <summary>The version on record, or null when the tool was only detected on the card
    /// (no install record) — that case can't be compared against the latest release.</summary>
    public required string? RecordedVersion { get; init; }

    /// <summary>
    /// True while a GitHub check that could resolve "Update" vs "Reload" is still outstanding
    /// (or hasn't been tried yet this session) for this tool. The Update/Reload button stays
    /// hidden for as long as this is true, rather than showing a guess.
    /// </summary>
    public required bool ActionPending { get; init; }

    /// <summary>"Update" or "Reload" once resolved; meaningless while <see cref="ActionPending"/>.</summary>
    public required string ActionLabel { get; init; }

    public bool InstallVisibility => !IsInstalled;
    public bool ManageVisibility => IsInstalled;

    /// <summary>
    /// The Update/Reload button specifically: hidden until the version check resolves, so it
    /// never shows a placeholder guess. Uninstall isn't affected — it uses ManageVisibility.
    /// </summary>
    public bool UpdateButtonVisibility => IsInstalled && !ActionPending;
}

public partial class ManageCfwWindow : Window
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x4A, 0xDE, 0x9E));
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0xFF, 0xC8, 0x6B));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xFF, 0x6B, 0x6B));

    private readonly List<InstalledEntry> _entries = new();
    private readonly Dictionary<string, string> _latestVersionCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _updateCheckCts;

    public string SdPath => (SdPathBox.Text ?? "").Trim();

    private readonly GitHubService _github = null!;
    private readonly Action<string, LogKind> _log = null!;

    public ManageCfwWindow()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
        Closed += (_, _) => _updateCheckCts?.Cancel();
    }

    public ManageCfwWindow(string? initialPath, GitHubService github, Action<string, LogKind> log) : this()
    {
        _github = github;
        _log = log;

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            SdPathBox.Text = initialPath;
            if (Directory.Exists(initialPath)) Rescan();
        }

        SdPathBox.LostFocus += (_, _) =>
        {
            if (Directory.Exists(SdPath)) Rescan();
        };
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.T("Common.SelectSdRoot"),
            AllowMultiple = false
        });
        if (folders.Count == 0) return;

        SdPathBox.Text = folders[0].Path.LocalPath;
        Rescan();
    }

    private void Rescan()
    {
        BuildEntries();
        _ = CheckForUpdatesAsync();
    }

    private void BuildEntries()
    {
        _entries.Clear();

        InstallManifest.MergeImports(SdPath);

        var records = InstallManifest.Load(SdPath)
            .ToDictionary(r => r.ToolId, StringComparer.OrdinalIgnoreCase);

        var detected = ToolDetector.Scan(SdPath)
            .ToDictionary(d => d.ToolId, StringComparer.OrdinalIgnoreCase);

        foreach (var tool in ToolCatalog.All)
        {
            records.TryGetValue(tool.Id, out var record);
            detected.TryGetValue(tool.Id, out var trace);

            var recorded = record?.Files
                .Select(f => Path.Combine(SdPath, f.Replace('/', Path.DirectorySeparatorChar)))
                .Where(File.Exists)
                .ToList() ?? new List<string>();

            var all = recorded.Concat(trace?.Paths ?? new List<string>())
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .ToList();

            var installed = all.Count > 0;
            var known = recorded.Count > 0;
            var version = string.IsNullOrWhiteSpace(record?.Version) ? null : record!.Version;
            var (actionPending, actionLabel) = ResolveAction(tool, version);

            _entries.Add(new InstalledEntry
            {
                ToolId = tool.Id,
                DisplayName = tool.DisplayName,
                IsInstalled = installed,
                Files = all,
                RecordedVersion = version,
                ActionPending = actionPending,
                ActionLabel = actionLabel,
                Details = !installed
                    ? Loc.T("Manage.Details.NotInstalled")
                    : known
                        ? Loc.T("Manage.Details.Installed", version ?? Loc.T("Manage.Details.UnknownVersion"), record!.InstalledAt)
                        : Loc.T("Manage.Details.Detected")
            });
        }

        InstalledList.ItemsSource = null;
        InstalledList.ItemsSource = _entries;

        var haveCard = Directory.Exists(SdPath);
        ListScroller.IsVisible = haveCard;
        EmptyHint.IsVisible = !haveCard;

        if (haveCard)
        {
            var count = _entries.Count(e => e.IsInstalled);
            StatusText.Foreground = SuccessBrush;
            StatusText.Text = count == 0
                ? Loc.T("Manage.Status.NothingInstalled")
                : Loc.T("Manage.Status.Count", count);
        }
    }

    private (bool Pending, string Label) ResolveAction(CfwTool tool, string? recordedVersion)
    {
        if (string.IsNullOrEmpty(recordedVersion) || tool.GitHubRepo == null)
            return (false, Loc.T("Manage.UpdateOrReload"));

        if (!_latestVersionCache.TryGetValue(tool.Id, out var latest))
            return (true, Loc.T("Manage.UpdateOrReload"));

        var label = string.Equals(recordedVersion, latest, StringComparison.OrdinalIgnoreCase)
            ? Loc.T("Manage.Reload")
            : Loc.T("Manage.Update");
        return (false, label);
    }

    private async Task CheckForUpdatesAsync()
    {
        _updateCheckCts?.Cancel();
        var cts = new CancellationTokenSource();
        _updateCheckCts = cts;

        var toCheck = _entries
            .Where(e => e.IsInstalled
                     && !string.IsNullOrEmpty(e.RecordedVersion)
                     && !_latestVersionCache.ContainsKey(e.ToolId))
            .ToList();
        if (toCheck.Count == 0) return;

        var changed = false;
        foreach (var entry in toCheck)
        {
            if (cts.IsCancellationRequested) return;

            var tool = ToolCatalog.All.FirstOrDefault(t => t.Id == entry.ToolId);
            if (tool?.GitHubRepo == null) continue;

            try
            {
                var release = (await _github.GetInstallableReleaseAsync(tool.GitHubRepo, tool.AssetNamePattern, tool.VersionFromAssetName, cts.Token)).Release;

                string latest;
                if (tool.VersionFromAssetName)
                {
                    var asset = GitHubService.SelectAsset(release, tool.AssetNamePattern, preferHighestVersion: true);
                    latest = ToolInstaller.DescribeVersion(tool, release, asset?.Name ?? "");
                }
                else
                {
                    latest = release.TagName;
                }

                _latestVersionCache[tool.Id] = latest;
                changed = true;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log(Loc.T("Log.Manage.UpdateCheckFailed", tool.DisplayName, ex.Message), LogKind.Info);
            }
        }

        if (changed && !cts.IsCancellationRequested) BuildEntries();
    }

    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is InstalledEntry entry) await InstallAsync(entry, reinstall: false);
    }

    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is InstalledEntry entry) await InstallAsync(entry, reinstall: true);
    }

    private async Task InstallAsync(InstalledEntry entry, bool reinstall)
    {
        if (!Directory.Exists(SdPath))
        {
            await MessageDialog.Show(this, Loc.T("Manage.Title"), Loc.T("Common.NoSdCard.Headline"),
                Loc.T("Manage.NoSdCard.Message"));
            return;
        }

        var tool = ToolCatalog.All.FirstOrDefault(t => t.Id == entry.ToolId);
        if (tool == null) return;

        IsEnabled = false;
        StatusText.Foreground = InfoBrush;
        StatusText.Text = Loc.T(reinstall ? "Manage.Status.Reinstalling" : "Manage.Status.Installing", tool.DisplayName);

        ActionProgress.Minimum = 0;
        ActionProgress.Maximum = 1;
        ActionProgress.Value = 1;
        ActionProgress.IsVisible = true;
        ProgressBarAnimation.SetIsAnimating(ActionProgress, true);

        try
        {
            var staging = Path.Combine(Path.GetTempPath(), "SwitchCfwTool_manage");
            var downloader = new DownloadService(staging);
            var builder = new SdCardBuilder(SdPath);

            var installer = new ToolInstaller(_github, downloader, builder, SdPath, SdPath,
                asImport: false, _log);

            var ok = await installer.InstallAsync(tool, null, null, null,
                downloadOptionalExtra: false);

            if (ok)
            {
                var ini = new HekateIniService();
                ini.SyncKipUsage(SdPath, InstallManifest.HasKipTool(SdPath));
                ini.SyncSecmonLines(SdPath);

                StatusText.Foreground = SuccessBrush;
                StatusText.Text = Loc.T(reinstall ? "Manage.Status.Reinstalled" : "Manage.Status.Installed", tool.DisplayName);
                _log(Loc.T(reinstall ? "Log.Manage.Reinstalled" : "Log.Manage.Installed", tool.DisplayName, SdPath), LogKind.Success);
            }
            else
            {
                StatusText.Foreground = ErrorBrush;
                StatusText.Text = Loc.T("Manage.Status.InstallFailed", tool.DisplayName);
            }
        }
        catch (Exception ex)
        {
            StatusText.Foreground = ErrorBrush;
            StatusText.Text = ex.Message;
            _log($"{tool.DisplayName}: {ex.Message}", LogKind.Error);
        }
        finally
        {
            ProgressBarAnimation.SetIsAnimating(ActionProgress, false);
            ActionProgress.IsVisible = false;
            IsEnabled = true;
            Rescan();
        }
    }

    private async void Uninstall_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InstalledEntry entry) return;

        var leftovers = ToolLeftovers.FindConfigFolders(SdPath, entry.ToolId);

        var reason = Loc.T("Manage.Remove.Files");
        if (leftovers.Count > 0)
        {
            var names = leftovers.Select(d => "config/" + Path.GetFileName(d));
            reason += " " + Loc.T("Manage.Remove.Settings", string.Join(", ", names));
        }

        var confirm = new ChoiceDialog(
            Loc.T("Manage.Remove.Headline", entry.DisplayName),
            reason + " " + Loc.T("Manage.Remove.Untouched"),
            Loc.T("Manage.Remove.Question"),
            Loc.T("Manage.Remove.Yes"),
            Loc.T("Common.Cancel"));

        if (await confirm.ShowDialog<bool>(this) != true) return;

        var toDelete = entry.Files;
        var removed = 0;
        var failed = 0;

        _log(Loc.T("Log.Manage.Removing", entry.DisplayName), LogKind.Normal);

        foreach (var full in toDelete)
        {
            var shown = Relative(full);
            try
            {
                if (File.Exists(full))
                {
                    File.Delete(full);
                    removed++;
                    _log("  " + Loc.T("Log.Manage.DeletedFile", shown), LogKind.Normal);
                }
                else if (Directory.Exists(full))
                {
                    var count = Directory.GetFiles(full, "*", SearchOption.AllDirectories).Length;
                    Directory.Delete(full, recursive: true);
                    removed++;
                    _log("  " + Loc.T("Log.Manage.DeletedFolder", shown, count), LogKind.Normal);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _log("  " + Loc.T("Log.Manage.DeleteFailed", shown, ex.Message), LogKind.Error);
            }
        }

        foreach (var folder in leftovers)
        {
            var shown = "config/" + Path.GetFileName(folder);
            try
            {
                Directory.Delete(folder, recursive: true);
                removed++;
                _log("  " + Loc.T("Log.Manage.DeletedSettings", shown), LogKind.Normal);
            }
            catch (Exception ex)
            {
                failed++;
                _log("  " + Loc.T("Log.Manage.DeleteFailed", shown, ex.Message), LogKind.Error);
            }
        }

        RemoveEmptyFolders(toDelete);
        PruneEmptyFolders(Path.Combine(SdPath, "config"));
        PruneEmptyFolders(Path.Combine(SdPath, "switch"));
        PruneEmptyFolders(Path.Combine(SdPath, "atmosphere"));

        InstallManifest.Remove(SdPath, entry.ToolId);

        try
        {
            var ini = new HekateIniService();
            ini.SyncKipUsage(SdPath, InstallManifest.HasKipTool(SdPath));
            ini.SyncSecmonLines(SdPath);
        }
        catch
        {
            // Cosmetic clean-up of the boot menu; never let it undo a successful removal.
        }

        if (failed == 0)
            _log(Loc.T("Log.Manage.RemovedClean", entry.DisplayName, removed), LogKind.Success);
        else
            _log(Loc.T("Log.Manage.RemovedPartly", entry.DisplayName, failed), LogKind.Info);

        StatusText.Foreground = failed == 0 ? SuccessBrush : InfoBrush;
        StatusText.Text = Loc.T("Manage.Status.Removed", entry.DisplayName, removed);
        Rescan();
    }

    private string Relative(string full)
    {
        try
        {
            return Path.GetRelativePath(SdPath, full).Replace('\\', '/');
        }
        catch
        {
            return full;
        }
    }

    private static void PruneEmptyFolders(string root)
    {
        if (!Directory.Exists(root)) return;

        try
        {
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                                         .OrderByDescending(d => d.Length))
            {
                if (!Directory.Exists(dir)) continue;
                if (Directory.EnumerateFileSystemEntries(dir).Any()) continue;

                try { Directory.Delete(dir); } catch { /* in use — leave it */ }
            }
        }
        catch
        {
            // Unreadable tree: tidying up is optional, never worth failing over.
        }
    }

    private void RemoveEmptyFolders(IEnumerable<string> removedFiles)
    {
        var dirs = removedFiles
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .OrderByDescending(d => d!.Length);

        foreach (var dir in dirs)
        {
            var current = dir;
            while (!string.IsNullOrEmpty(current)
                   && current.StartsWith(SdPath, StringComparison.OrdinalIgnoreCase)
                   && !current.Equals(SdPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(current)) break;
                    if (Directory.EnumerateFileSystemEntries(current).Any()) break;
                    Directory.Delete(current);
                    current = Path.GetDirectoryName(current);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
