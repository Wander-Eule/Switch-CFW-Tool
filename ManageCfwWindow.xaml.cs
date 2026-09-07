using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using System.Threading.Tasks;
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

    public Visibility InstallVisibility => IsInstalled ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ManageVisibility => IsInstalled ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The Update/Reload button specifically: hidden until the version check resolves, so it
    /// never shows a placeholder guess. Uninstall isn't affected — it uses ManageVisibility.
    /// </summary>
    public Visibility UpdateButtonVisibility => IsInstalled && !ActionPending ? Visibility.Visible : Visibility.Collapsed;
}

public partial class ManageCfwWindow : Window
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x4A, 0xDE, 0x9E));
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0xFF, 0xC8, 0x6B));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xFF, 0x6B, 0x6B));

    private readonly List<InstalledEntry> _entries = new();

    // Latest-release version per tool id, fetched once per window and reused across rescans —
    // re-checking every installed tool after every single install/uninstall would burn through
    // GitHub's anonymous rate limit (60/hour) fast.
    private readonly Dictionary<string, string> _latestVersionCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _updateCheckCts;

    public string SdPath => SdPathBox.Text.Trim();

    private readonly GitHubService _github;
    private readonly Action<string, LogKind> _log;

    public ManageCfwWindow(string? initialPath, GitHubService github, Action<string, LogKind> log)
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);

        _github = github;
        _log = log;

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            SdPathBox.Text = initialPath;
            if (Directory.Exists(initialPath)) Rescan();
        }

        // A path typed by hand is picked up when the field loses focus, so there's still a
        // way in without the folder picker.
        SdPathBox.LostFocus += (_, _) =>
        {
            if (Directory.Exists(SdPath)) Rescan();
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the root of your SD card" };
        if (dialog.ShowDialog() != true) return;

        SdPathBox.Text = dialog.FolderName;
        Rescan();   // picking a folder already states the intent — no second click needed
    }



    private void Rescan()
    {
        BuildEntries();

        // Fire-and-forget: the list above is already correct and shown, this only fills in
        // "Update" vs "Reload" once the newest release for each tool comes back.
        _ = CheckForUpdatesAsync();
    }

    private void BuildEntries()
    {
        _entries.Clear();

        // Picks up anything copied onto the card by hand since the last scan.
        InstallManifest.MergeImports(SdPath);

        var records = InstallManifest.Load(SdPath)
            .ToDictionary(r => r.ToolId, StringComparer.OrdinalIgnoreCase);

        // Scanning the card as well as reading the record: a card set up elsewhere has no
        // record at all, and even one of ours can have tools added by hand afterwards.
        var detected = ToolDetector.Scan(SdPath)
            .ToDictionary(d => d.ToolId, StringComparer.OrdinalIgnoreCase);

        // Every tool is listed, so the card can be filled out from here as well as tidied up.
        foreach (var tool in ToolCatalog.All)
        {
            records.TryGetValue(tool.Id, out var record);
            detected.TryGetValue(tool.Id, out var trace);

            // Only files that are actually still there, so a hand-deleted tool doesn't linger
            // in the list as if it were installed.
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
                    ? "not installed"
                    : known
                        ? $"{version ?? "unknown version"}  ·  installed {record!.InstalledAt:yyyy-MM-dd}"
                        : "detected on card  ·  version unknown"
            });
        }

        InstalledList.ItemsSource = null;
        InstalledList.ItemsSource = _entries;

        var haveCard = Directory.Exists(SdPath);
        ListScroller.Visibility = haveCard ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = haveCard ? Visibility.Collapsed : Visibility.Visible;

        if (haveCard)
        {
            var count = _entries.Count(e => e.IsInstalled);
            StatusText.Foreground = SuccessBrush;
            StatusText.Text = count == 0
                ? "Nothing installed on this card yet."
                : $"{count} tool(s) installed.";
        }
    }

    /// <summary>
    /// Resolves the Update/Reload button's state for one tool. Pending (button hidden) unless
    /// this is a case that can never be resolved — no recorded version to compare from, or no
    /// GitHub repo to check against — in which case the combined label shows immediately
    /// instead of waiting on a check that will never happen.
    /// </summary>
    private (bool Pending, string Label) ResolveAction(CfwTool tool, string? recordedVersion)
    {
        if (string.IsNullOrEmpty(recordedVersion) || tool.GitHubRepo == null)
            return (false, "Update/Reload");

        if (!_latestVersionCache.TryGetValue(tool.Id, out var latest))
            return (true, "Update/Reload");

        var label = string.Equals(recordedVersion, latest, StringComparison.OrdinalIgnoreCase)
            ? "Reload"
            : "Update";
        return (false, label);
    }

    /// <summary>
    /// Looks up the newest release for every installed tool that has a recorded version and
    /// hasn't been checked yet this session, then rebuilds the list once so "Update"/"Reload"
    /// can replace the generic label. A previous run still in flight is cancelled first, so
    /// rapid rescans (switching cards, one install after another) don't pile up requests.
    /// </summary>
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
                var release = await _github.GetLatestReleaseAsync(tool.GitHubRepo, cts.Token);
                if (release == null) continue;

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
                _log($"[{tool.DisplayName}] Could not check for updates: {ex.Message}", LogKind.Info);
            }
        }

        if (changed && !cts.IsCancellationRequested) BuildEntries();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is InstalledEntry entry) await InstallAsync(entry, reinstall: false);
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is InstalledEntry entry) await InstallAsync(entry, reinstall: true);
    }

    /// <summary>
    /// Installs or reinstalls one tool straight onto the card, through the same code the main
    /// window's build uses, so a tool added here ends up laid out exactly as it would there.
    /// </summary>
    private async Task InstallAsync(InstalledEntry entry, bool reinstall)
    {
        if (!Directory.Exists(SdPath))
        {
            MessageDialog.Show(this, "Manage CFW", "No SD card selected",
                "Choose your SD card at the top of this window first.");
            return;
        }

        var tool = ToolCatalog.All.FirstOrDefault(t => t.Id == entry.ToolId);
        if (tool == null) return;

        IsEnabled = false;
        StatusText.Foreground = InfoBrush;
        StatusText.Text = $"{(reinstall ? "Reinstalling" : "Installing")} {tool.DisplayName}...";

        // A single tool has no sub-steps to count, unlike the main window's per-tool build
        // progress — so the bar is shown full-width rather than stuck at 0%, and the animated
        // gradient flowing across it is what actually signals "still working".
        ActionProgress.Minimum = 0;
        ActionProgress.Maximum = 1;
        ActionProgress.Value = 1;
        ActionProgress.Visibility = Visibility.Visible;
        ProgressBarAnimation.SetIsAnimating(ActionProgress, true);

        try
        {
            var staging = Path.Combine(Path.GetTempPath(), "SwitchCfwTool_manage");
            var downloader = new DownloadService(staging);
            var builder = new SdCardBuilder(SdPath);

            // Writing onto the card itself, so the record is merged straight away rather than
            // handed over as an import file.
            var installer = new ToolInstaller(_github, downloader, builder, SdPath, SdPath,
                asImport: false, _log);

            var ok = await installer.InstallAsync(tool, null, null, null,
                downloadOptionalExtra: false);

            if (ok)
            {
                // A tool arriving can bring a KIP with it, which changes the boot entries.
                var ini = new HekateIniService();
                ini.SyncKipUsage(SdPath, InstallManifest.HasKipTool(SdPath));
                ini.SyncSecmonLines(SdPath);

                StatusText.Foreground = SuccessBrush;
                StatusText.Text = $"{tool.DisplayName} {(reinstall ? "reinstalled" : "installed")}.";
                _log($"{tool.DisplayName} {(reinstall ? "reinstalled" : "installed")} on {SdPath}.", LogKind.Success);
            }
            else
            {
                StatusText.Foreground = ErrorBrush;
                StatusText.Text = $"{tool.DisplayName} could not be installed — see the log.";
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
            ActionProgress.Visibility = Visibility.Collapsed;
            IsEnabled = true;
            Rescan();
        }
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InstalledEntry entry) return;

        // Destructive, so it still asks — but only to confirm, not to negotiate over which
        // files go: everything the tool installed is removed.
        var leftovers = ToolLeftovers.FindConfigFolders(SdPath, entry.ToolId);

        var reason = "All files this tool installed will be deleted from the card.";
        if (leftovers.Count > 0)
        {
            var names = leftovers.Select(d => "config/" + Path.GetFileName(d));
            reason += $" Its settings folder will go too ({string.Join(", ", names)}).";
        }

        var confirm = new ChoiceDialog(
            $"Remove {entry.DisplayName}?",
            reason + " Save backups and other data you created are not touched.",
            "Remove it completely?",
            "Remove",
            "Cancel") { Owner = this };

        if (confirm.ShowDialog() != true) return;

        var toDelete = entry.Files;
        var removed = 0;
        var failed = 0;

        _log($"Removing {entry.DisplayName}...", LogKind.Normal);

        // Entries can be single files or whole folders — a title-ID directory under
        // atmosphere/contents is one item but many files.
        foreach (var full in toDelete)
        {
            var shown = Relative(full);
            try
            {
                if (File.Exists(full))
                {
                    File.Delete(full);
                    removed++;
                    _log($"  deleted {shown}", LogKind.Normal);
                }
                else if (Directory.Exists(full))
                {
                    var count = Directory.GetFiles(full, "*", SearchOption.AllDirectories).Length;
                    Directory.Delete(full, recursive: true);
                    removed++;
                    _log($"  deleted folder {shown} ({count} file(s))", LogKind.Normal);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _log($"  could not delete {shown}: {ex.Message}", LogKind.Error);
            }
        }

        foreach (var folder in leftovers)
        {
            var shown = "config/" + Path.GetFileName(folder);
            try
            {
                Directory.Delete(folder, recursive: true);
                removed++;
                _log($"  deleted settings folder {shown}", LogKind.Normal);
            }
            catch (Exception ex)
            {
                failed++;
                _log($"  could not delete {shown}: {ex.Message}", LogKind.Error);
            }
        }

        RemoveEmptyFolders(toDelete);
        PruneEmptyFolders(Path.Combine(SdPath, "config"));
        PruneEmptyFolders(Path.Combine(SdPath, "switch"));
        PruneEmptyFolders(Path.Combine(SdPath, "atmosphere"));

        InstallManifest.Remove(SdPath, entry.ToolId);

        // Removing a tool can invalidate parts of the boot menu: a kip1 line with no KIP
        // behind it, a NO KIP entry with nothing to fall back from, or a secmon line whose
        // exosphere.bin just went with the tool. All of that is corrected here.
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
            _log($"{entry.DisplayName} removed completely — {removed} item(s) deleted, no leftovers.", LogKind.Success);
        else
            _log($"{entry.DisplayName} removed with {failed} item(s) left behind — see the lines above.", LogKind.Info);

        StatusText.Foreground = failed == 0 ? SuccessBrush : InfoBrush;
        StatusText.Text = $"{entry.DisplayName}: {removed} item(s) removed.";
        Rescan();
    }

    /// <summary>Path as it reads on the card, for log output.</summary>
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

    /// <summary>
    /// Walks a directory bottom-up and removes every folder that ended up empty, so an
    /// uninstall doesn't leave a trail of hollow directories behind. The starting folder
    /// itself is kept, since it is shared with everything else on the card.
    /// </summary>
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

    /// <summary>
    /// Deletes folders left behind once their files are gone, deepest first so parents empty
    /// out too. Shared directories like switch/ or atmosphere/ still contain other tools'
    /// files, so they simply won't be empty and are left alone by the same check.
    /// </summary>
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
