using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SwitchCfwTool.Data;
using SwitchCfwTool.Models;
using SwitchCfwTool.Services;
using SwitchCfwTool.ViewModels;

namespace SwitchCfwTool;

public partial class MainWindow : Window
{
    private readonly List<ToolSelectionItem> _items = new();
    private readonly GitHubService _github = new();

    // Which of the two ways to build is in effect. This is not about detecting an SD card —
    // it records whether the output path is an existing card layout to write into (set by
    // Manage CFW), or a plain folder to assemble and copy across later (the Output folder
    // field, which always creates an SDMC subfolder inside it). The distinction decides both
    // whether to append that subfolder and whether the install record can be merged straight
    // away or has to be handed over as an import file.
    private bool _writingIntoExistingCard;

    public MainWindow()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);

        foreach (var tool in ToolCatalog.All)
        {
            _items.Add(new ToolSelectionItem(tool));
        }

        // In Custom mode a tool's version list is only fetched once it's actually checked
        // (see FetchVersionsForSelectedAsync), so checking one later has to trigger it too.
        foreach (var item in _items)
        {
            item.PropertyChanged += async (_, args) =>
            {
                if (args.PropertyName != nameof(ToolSelectionItem.IsSelected)) return;

                // Switching modes flips IsSelected on every item as a side effect. Those
                // notifications aren't user actions, and reacting to them mid-switch reads
                // the list while half the items still report the old mode's tick state —
                // which fetched versions for tools that aren't selected in the new mode.
                if (_switchingMode) return;

                if (RadioCustom?.IsChecked == true)
                    await FetchVersionsForSelectedAsync();
                else
                    await FetchLatestLabelsAsync();

                // Deferred so the checkbox binding has finished updating before a modal
                // dialog can interrupt it. Deliberately not awaited — the handler shouldn't
                // stay alive waiting on a dialog the user may leave open; the discard makes
                // that intent explicit instead of leaving a compiler warning behind.
                if (item.IsSelected)
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        CheckConflicts(item);
                        AskAboutOptionalExtra(item);
                        AskAboutAccountNotice(item);
                    }), DispatcherPriority.Background);
            };
        }

        ToolsList.ItemsSource = _items;

        OutputPathBox.TextChanged += (_, _) => RefreshTarget();
        RefreshTarget();

        var savedToken = GitHubTokenStore.Load();
        if (savedToken != null)
            _github.UseToken(savedToken);

        BuildLanguageMenu();
        Loc.LanguageChanged += OnLanguageChanged;

        // Deferred to Loaded so the window is laid out before the tour measures anything.
        Loaded += (_, _) => OfferQuickTourOnFirstRun();
    }

    /// <summary>
    /// One entry per language file. Each is listed under its own name ("Português (Brasil)"
    /// rather than a translation of it), so the way back is recognisable from any language.
    /// </summary>
    private void BuildLanguageMenu()
    {
        MenuLanguage.Items.Clear();

        foreach (var language in Loc.Available)
        {
            var entry = new MenuItem
            {
                Header = language.Name,
                Tag = language.Code,
                IsCheckable = true,
                IsChecked = language.Code == Loc.Current
            };
            entry.Click += (_, _) =>
            {
                Loc.SetLanguage(language.Code);

                // A checkable item toggles itself on click, so clicking the active language
                // would untick it; the ticks are always re-derived from what is in effect.
                foreach (var item in MenuLanguage.Items.OfType<MenuItem>())
                    item.IsChecked = (string)item.Tag == Loc.Current;
            };
            MenuLanguage.Items.Add(entry);
        }
    }

    /// <summary>
    /// Everything bound in XAML follows a switch by itself; this covers the few texts that are
    /// set from code. Log lines already written stay as they are.
    /// </summary>
    private void OnLanguageChanged()
    {
        TargetKindLabel.Text = Loc.T(_writingIntoExistingCard ? "Main.Target.SdCard" : "Main.Target.PcFolder");

        if (StatusText.Visibility == Visibility.Visible)
            ShowBuildResult();
    }

    private async void VersionMode_Changed(object sender, RoutedEventArgs e)
    {
        // Fires once during InitializeComponent() itself (RadioLatest starts IsChecked="True"),
        // at which point RadioCustom hasn't been created by the XAML loader yet -> guard against that.
        if (RadioCustom == null || RadioLatest == null) return;

        bool custom = RadioCustom.IsChecked == true;

        _switchingMode = true;
        foreach (var item in _items)
        {
            item.CustomMode = custom;

            // Guard against a version lingering on a tool that isn't ticked in this mode,
            // which would otherwise sit behind the "select this tool" placeholder.
            if (custom && !item.IsSelected)
                item.SelectedVersion = null;
        }
        _switchingMode = false;

        if (!custom)
        {
            await FetchLatestLabelsAsync();
            return;
        }
        await FetchVersionsForSelectedAsync();
    }

    /// <summary>
    /// Looks up the version "Latest" would actually install, for display next to each
    /// selected tool. Restricted to selected tools so switching modes doesn't fire two
    /// dozen GitHub requests at once and exhaust the anonymous rate limit.
    /// </summary>
    private async Task FetchLatestLabelsAsync()
    {
        foreach (var item in _items.Where(i => i.IsSelected && string.IsNullOrEmpty(i.LatestLabel)))
        {
            try
            {
                var release = await _github.GetLatestReleaseAsync(item.Tool.GitHubRepo!);
                if (release != null)
                    item.LatestLabel = DescribeVersion(item.Tool, release);
            }
            catch (Exception ex)
            {
                Log(Loc.T("Log.Main.LatestVersionFailed", item.Tool.DisplayName, ex.Message), LogKind.Error);
            }
        }
    }

    /// <summary>
    /// The version to show for a release. Normally the tag, but for tools whose tag and
    /// attached file can disagree it is taken from the filename, so the number on screen is
    /// the one that actually gets installed. The actual filename → version parsing lives in
    /// <see cref="ToolInstaller.DescribeVersion"/>; this only resolves which asset to read it
    /// from, so the two don't drift apart into two slightly different regexes.
    /// </summary>
    private static string DescribeVersion(CfwTool tool, GitHubRelease release)
    {
        if (!tool.VersionFromAssetName) return release.TagName;

        try
        {
            var asset = GitHubService.SelectAsset(release, tool.AssetNamePattern, tool.VersionFromAssetName);
            return asset != null ? ToolInstaller.DescribeVersion(tool, release, asset.Name) : release.TagName;
        }
        catch
        {
            // No usable asset in this release — fall back to the tag.
            return release.TagName;
        }
    }

    // Set while a conflict dialog is undoing a selection, so clearing the checkbox doesn't
    // immediately re-enter the conflict check for the tool being switched off.
    private bool _resolvingConflict;

    // Set while Latest/Custom is being applied to every item, see the handler above.
    private bool _switchingMode;

    private void CheckConflicts(ToolSelectionItem item)
    {
        if (_resolvingConflict || !item.IsSelected) return;

        // Declared on either side, so both orders of selection are covered.
        var clash = _items.FirstOrDefault(other =>
            other != item && other.IsSelected &&
            (item.Tool.ConflictsWith.Contains(other.Tool.Id) ||
             other.Tool.ConflictsWith.Contains(item.Tool.Id)));

        if (clash == null) return;

        var reason = item.Tool.ConflictReason ?? clash.Tool.ConflictReason
            ?? Loc.T("Main.Conflict.GenericReason");

        var dialog = new ChoiceDialog(
            Loc.T("Main.Conflict.Headline", item.Tool.DisplayName, clash.Tool.DisplayName),
            reason,
            Loc.T("Main.Conflict.Question", item.Tool.DisplayName),
            Loc.T("Main.Conflict.Remove"),
            Loc.T("Main.Conflict.KeepBoth")) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            _resolvingConflict = true;
            item.IsSelected = false;
            _resolvingConflict = false;
            Log(Loc.T("Log.Main.ConflictRemoved", item.Tool.DisplayName, clash.Tool.DisplayName), LogKind.Info);
        }
        else
        {
            Log(Loc.T("Log.Main.ConflictKept", item.Tool.DisplayName, clash.Tool.DisplayName), LogKind.Info);
        }
    }

    /// <summary>
    /// Fetches release lists for Custom mode. Deliberately limited to CHECKED tools:
    /// fetching all 24 up front burned most of GitHub's anonymous 60-requests-per-hour
    /// budget in one click, which then made the actual build fail for every tool at once.
    /// </summary>
    private async Task FetchVersionsForSelectedAsync()
    {
        foreach (var item in _items.Where(i =>
                     i.IsSelected && i.AvailableVersions.Count == 0))
        {
            await FetchVersionsForAsync(item);
        }
    }

    private async Task FetchVersionsForAsync(ToolSelectionItem item)
    {
            try
            {
                var releases = await _github.GetAllReleasesAsync(item.Tool.GitHubRepo!);
                var options = new List<VersionOption>();
                foreach (var r in releases)
                {
                    // A release whose assets don't match this tool's pattern isn't
                    // selectable — skip it rather than letting SelectAsset throw here.
                    try
                    {
                        var asset = GitHubService.SelectAsset(r, item.Tool.AssetNamePattern, item.Tool.VersionFromAssetName);
                        if (asset != null && !string.IsNullOrEmpty(asset.BrowserDownloadUrl))
                        {
                            options.Add(new VersionOption
                            {
                                TagName = DescribeVersion(item.Tool, r),
                                DownloadUrl = asset.BrowserDownloadUrl,
                                AssetFileName = asset.Name
                            });
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // no usable asset in this particular release — ignore it
                    }
                }

                item.AvailableVersions = options;
                item.SelectedVersion ??= options.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Log(Loc.T("Log.Main.ReleaseListFailed", item.Tool.DisplayName, ex.Message), LogKind.Error);
            }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("Main.Browse.Title")
        };

        if (dialog.ShowDialog() == true)
            OutputPathBox.Text = dialog.FolderName;
    }

    /// <summary>
    /// Works out what the chosen path is and says so. Writing straight onto a card and
    /// building a folder to copy across differ in where files land, so it is shown rather
    /// than left for the user to infer.
    /// </summary>
    private void RefreshTarget()
    {
        var path = OutputPathBox.Text.Trim();
        TargetHint.Visibility = string.IsNullOrEmpty(path) ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrEmpty(path))
        {
            TargetKindLabel.Text = Loc.T("Main.Target.PcFolder");
            _writingIntoExistingCard = false;
            _askedAbout = null;
            return;
        }

        var info = TargetDetection.Inspect(path);

        switch (info.Kind)
        {
            case TargetKind.SdCard:
                SetCardTarget(true, info);
                break;

            case TargetKind.Ambiguous when !string.Equals(_askedAbout, path, StringComparison.OrdinalIgnoreCase):
                // A drive root that Windows doesn't report as removable. Built-in card
                // readers often look like this, but so does a second hard disk — so it is
                // asked rather than assumed.
                _askedAbout = path;

                var size = info.TotalBytes > 0 ? $"{info.TotalBytes / 1024 / 1024 / 1024} GB, " : "";
                var answer = new ChoiceDialog(
                    Loc.T("Main.AskCard.Headline"),
                    $"{path}  ({size}{info.FileSystem})\n\n" + Loc.T("Main.AskCard.Reason"),
                    Loc.T("Main.AskCard.Question"),
                    Loc.T("Main.AskCard.SdCard"),
                    Loc.T("Main.AskCard.PcFolder")) { Owner = this };

                SetCardTarget(answer.ShowDialog() == true, info);
                break;

            case TargetKind.Ambiguous:
                break;   // already answered for this path

            default:
                SetCardTarget(false, info);
                break;
        }
    }

    private void SetCardTarget(bool isCard, TargetInfo info)
    {
        _writingIntoExistingCard = isCard;
        TargetKindLabel.Text = Loc.T(isCard ? "Main.Target.SdCard" : "Main.Target.PcFolder");

        if (!isCard) return;

        if (!TargetDetection.IsSwitchCompatible(info.FileSystem))
        {
            Log(Loc.T("Log.Main.FileSystemWarning", info.FileSystem), LogKind.Info);
        }

        WarnIfCardAlreadySetUp(OutputPathBox.Text.Trim());
    }

    /// <summary>
    /// Says so when the chosen card already carries a CFW. Building writes over the files it
    /// installs, and finding that out afterwards is a poor way to learn it — especially on a
    /// card set up elsewhere, where the existing install can't be managed from here.
    /// </summary>
    private void WarnIfCardAlreadySetUp(string cardRoot)
    {
        if (string.Equals(_warnedAboutCard, cardRoot, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            var existing = ExistingCfw.Detect(cardRoot);
            if (!existing.Found) return;

            _warnedAboutCard = cardRoot;

            var known = InstallManifest.Load(cardRoot).Count > 0;

            new MessageDialog(
                Loc.T("Main.ExistingCfw.Title"),
                Loc.T("Main.ExistingCfw.Headline"),
                Loc.T("Main.ExistingCfw.Found", string.Join(", ", existing.Parts)) + "\n\n"
                + Loc.T(known ? "Main.ExistingCfw.Known" : "Main.ExistingCfw.Unknown")
                ) { Owner = this }.ShowDialog();
        }
        catch
        {
            // Only a courtesy check — never let it get in the way of choosing a card.
        }
    }

    // The card the "already set up" notice was last shown for, so it appears once per card
    // rather than on every keystroke in the path box.
    private string? _warnedAboutCard;

    // The path the removable-or-not question was last answered for. Keyed by path rather
    // than a plain flag so switching to a different drive asks again, while typing in the
    // box does not re-ask about the same one.
    private string? _askedAbout;

    private void Theming_Click(object sender, RoutedEventArgs e)
    {
        // The window carries its own SD Card field, so it opens straight away; a path is
        // only pre-filled when one is already known from a build aimed at a card.
        var suggested = _writingIntoExistingCard ? OutputPathBox.Text.Trim() : null;
        new ThemingWindow(suggested) { Owner = this }.ShowDialog();
    }

    private QuickTour? _tour;

    private void QuickTour_Click(object sender, RoutedEventArgs e) => StartQuickTour();

    private void StartQuickTour()
    {
        _tour ??= new QuickTour(this, TourHost);
        _tour.Start(BuildTourSteps());
    }

    private void StartQuickTourAsFirstRun()
    {
        _tour ??= new QuickTour(this, TourHost);

        // Recorded only once the tour is actually seen through to the end. Closing the window
        // or pressing Esc part-way leaves nothing behind, so the offer returns next start.
        void OnCompleted()
        {
            _tour!.Completed -= OnCompleted;
            FirstRun.MarkOffered();
        }

        _tour.Completed += OnCompleted;
        _tour.Start(BuildTourSteps());
    }

    /// <summary>
    /// The walkthrough, in the order someone would actually work through the window: the two
    /// mode choices, the tool list, where it goes, the build button, and finally what lives
    /// behind each menu.
    /// </summary>
    private List<TourStep> BuildTourSteps() => new()
    {
        new TourStep
        {
            Target = () => VersionPanel,
            Text = Loc.T("Tour.Version")
        },
        new TourStep
        {
            Target = () => MmcPanel,
            Text = Loc.T("Tour.Mmc")
        },
        new TourStep
        {
            // Narrowed to the checkbox column and the first few rows, so the highlight sits
            // on the controls being described instead of on the whole list.
            Target = () => ToolsList,
            MaxHeight = 178,
            MaxWidth = 46,
            PaddingX = -3,
            Text = Loc.T("Tour.Tools")
        },
        new TourStep
        {
            Target = () => OutputRow,
            Text = Loc.T("Tour.Output")
        },
        new TourStep
        {
            Target = () => BuildButton,
            Text = Loc.T("Tour.Build")
        },
        new TourStep
        {
            Target = () => MenuFile,
            Text = Loc.T("Tour.File")
        },
        new TourStep
        {
            Target = () => MenuInfo,
            Text = Loc.T("Tour.Info")
        },
        new TourStep
        {
            Target = () => MenuExtras,
            Text = Loc.T("Tour.Extras")
        },
        new TourStep
        {
            Target = () => MenuHelp,
            Text = Loc.T("Tour.Help")
        },
    };

    /// <summary>Offered once, on the very first start.</summary>
    private void OfferQuickTourOnFirstRun()
    {
        if (!FirstRun.ShouldOfferTour()) return;

        var ask = new ChoiceDialog(
            Loc.T("Main.Welcome.Headline"),
            Loc.T("Main.Welcome.Reason"),
            Loc.T("Main.Welcome.Question"),
            Loc.T("Common.Yes"),
            Loc.T("Common.No")) { Owner = this };

        if (ask.ShowDialog() == true)
            Dispatcher.BeginInvoke(new Action(StartQuickTourAsFirstRun), DispatcherPriority.Background);
        else
            FirstRun.MarkOffered();   // declined outright — don't ask again
    }

    private void ManageCfw_Click(object sender, RoutedEventArgs e)
    {
        // Installing and removing happen inside that window now, so nothing is handed back
        // here; its own log lines go into this window's log.
        var suggested = _writingIntoExistingCard ? OutputPathBox.Text.Trim() : null;
        new ManageCfwWindow(suggested, _github, Log) { Owner = this }.ShowDialog();
    }

    private void Info_Click(object sender, RoutedEventArgs e)
    {
        new InfoWindow { Owner = this }.ShowDialog();
    }

    private void ToggleLog_Click(object sender, RoutedEventArgs e)
    {
        LogRow.Height = new GridLength(LogMenuItem.IsChecked ? 180 : 0);
    }

    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageDialog.Show(this, Loc.T("Main.NoTools.Title"), Loc.T("Main.NoTools.Headline"),
                Loc.T("Main.NoTools.Message"));
            return;
        }

        var basePath = OutputPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            MessageDialog.Show(this, Loc.T("Main.NoOutput.Title"), Loc.T("Main.NoOutput.Headline"),
                Loc.T("Main.NoOutput.Message"));
            return;
        }

        // Asked last, so the user isn't made to confirm a warning only to hit a validation
        // error straight afterwards.
        if (!ConfirmSysmoduleLoad(selected)) return;

        // Direct SD card mode: basePath IS the SD root already. Otherwise create/use a
        // "SDMC" subfolder inside the chosen output folder.
        var sdRoot = _writingIntoExistingCard ? basePath : Path.Combine(basePath, "SDMC");

        BuildButton.IsEnabled = false;
        BuildProgress.Value = 0;
        BuildProgress.Maximum = selected.Count;
        LogBox.Document.Blocks.Clear();
        StatusText.Visibility = Visibility.Collapsed;
        _failedTools = 0;
        _bootIntegrityProblems = 0;
        _fatalError = null;
        ProgressBarAnimation.SetIsAnimating(BuildProgress, true);

        var stagingRoot = Path.Combine(Path.GetTempPath(), "SwitchCfwTool_staging");

        // Wipe leftovers from previous runs: a stale file with a matching extension could
        // otherwise be picked up by the archive fallback search in SdCardBuilder and end
        // up on the SD card instead of the freshly downloaded one.
        try
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, true);
        }
        catch (Exception ex)
        {
            Log(Loc.T("Log.Main.TempNotCleared", ex.Message), LogKind.Info);
        }

        var downloader = new DownloadService(stagingRoot);
        var sdBuilder = new SdCardBuilder(sdRoot);
        var mmcMode = RadioSysMmc.IsChecked == true ? MmcMode.SysMMC : MmcMode.EmuMMC;
        var mmcService = new MmcConfigService();
        var bundledAssets = new BundledAssetsService();

        try
        {
            var installer = new ToolInstaller(_github, downloader, sdBuilder, sdRoot, basePath,
                asImport: !_writingIntoExistingCard, Log);

            foreach (var item in selected)
            {
                var pinned = RadioCustom.IsChecked == true ? item.SelectedVersion : null;

                var ok = await installer.InstallAsync(
                    item.Tool,
                    pinned?.DownloadUrl,
                    pinned?.AssetFileName,
                    pinned?.Label ?? item.LatestLabel,
                    downloadOptionalExtra: item.OptionalExtraAccepted);

                if (!ok) _failedTools++;

                BuildProgress.Value += 1;
            }

            // Only relevant if Hekate was selected — the ini/icons are meaningless without it,
            // and this must run after Hekate's own merge so it doesn't touch anything Hekate ships.
            if (selected.Any(i => i.Tool.Id == "hekate"))
            {
                Log(Loc.T("Log.Main.AddingHekateAssets"));
                var createdInis = bundledAssets.ExtractHekateAssets(sdRoot);
                var iniService = new HekateIniService();

                // Read from the manifest, not from this run's selection: a KIP tool installed
                // by an earlier build is still on the card and its entries must stay.
                var anyKipTool = InstallManifest.HasKipTool(sdRoot);

                // Mode trimming only touches files just written, so an ini the user already
                // edited keeps every entry they put in it.
                foreach (var ini in createdInis)
                    iniService.FilterSections(ini, mmcMode, anyKipTool);

                // The KIP parts are maintained on every build, since a tool may have been
                // added or removed since the ini was first written.
                iniService.SyncKipUsage(sdRoot, anyKipTool);

                Log(Loc.T(anyKipTool ? "Log.Main.BootEntriesWithKip" : "Log.Main.BootEntriesNoKip", mmcMode),
                    LogKind.Info);
            }

            // Only relevant if at least one MMC-relevant (Atmosphère-based) tool was selected.
            if (selected.Any(i => i.Tool.MmcRelevant))
            {
                Log(Loc.T("Log.Main.WritingExosphere", mmcMode));
                mmcService.WriteExosphereIni(sdRoot, mmcMode);
                Log(Loc.T("Log.Main.WritingDnsMitm", mmcMode == MmcMode.EmuMMC ? "emummc.txt" : "sysmmc.txt"));
                mmcService.WriteDnsMitmBlocklist(sdRoot, mmcMode);
            }

            SyncSecmonLines(sdRoot);
            LogSysmoduleNote(selected);
            CheckBootIntegrity(sdRoot, selected);

            Log("");
            Log(_writingIntoExistingCard
                    ? Loc.T("Log.Main.DoneCard")
                    : Loc.T("Log.Main.DoneFolder", sdRoot),
                LogKind.Success);
        }
        catch (Exception ex)
        {
            Log(Loc.T("Log.Common.Error", ex.Message), LogKind.Error);
            _fatalError = ex.Message;
        }
        finally
        {
            BuildButton.IsEnabled = true;
            ProgressBarAnimation.SetIsAnimating(BuildProgress, false);
            ShowBuildResult();

            // Tied to the file actually existing rather than to Hekate being ticked: without
            // a payload.bin the advice has nothing to refer to and would only be noise.
            if (_fatalError == null && File.Exists(Path.Combine(sdRoot, "payload.bin")))
                new PayloadNoticeDialog { Owner = this }.ShowDialog();
        }
    }

    /// <summary>
    /// Brings the secmon= lines in hekate_ipl.ini in line with what this build actually
    /// put on the card (see HekateIniService for why this matters), and reports what
    /// changed so the log makes the reasoning visible rather than silently rewriting
    /// the user's boot config.
    /// </summary>
    private void SyncSecmonLines(string sdRoot)
    {
        try
        {
            var result = new HekateIniService().SyncSecmonLines(sdRoot);

            if (result.LinesRemoved == 0 && result.LinesAdded == 0)
                return;

            if (result.LinesRemoved > 0)
                Log(Loc.T("Log.Main.SecmonRemoved", result.LinesRemoved), LogKind.Info);

            if (result.LinesAdded > 0)
                Log(Loc.T("Log.Main.SecmonAdded", result.LinesAdded), LogKind.Info);
        }
        catch (Exception ex)
        {
            Log(Loc.T("Log.Main.SecmonFailed", ex.Message), LogKind.Info);
        }
    }

    // Outcome of the last build, summarised in StatusText so the result is visible even
    // when the log panel is hidden. Kept as two separate counters rather than one shared
    // total: a download failure and a fatal boot-integrity problem are different kinds of
    // trouble with different fixes, and lumping them into one number produced a wrong
    // explanation whenever the real cause was a boot-integrity problem, not a download.
    private int _failedTools;
    private int _bootIntegrityProblems;
    private string? _fatalError;



    /// <summary>
    /// Summarises how the build went next to the progress bar. Only genuine problems change
    /// the wording or colour — routine notes (an adjusted secmon line, a cleared temp
    /// folder) still count as a clean run, so a normal build always reads simply "DONE!".
    /// </summary>
    private void ShowBuildResult()
    {
        StatusText.Visibility = Visibility.Visible;

        if (_fatalError != null)
        {
            StatusText.Foreground = ErrorBrush;
            StatusText.Text = Loc.T("Main.Status.Error");
        }
        else if (_bootIntegrityProblems > 0)
        {
            // Worth its own branch and its own colour: this means the card likely won't boot,
            // which is a more serious outcome than a package that simply failed to download.
            StatusText.Foreground = ErrorBrush;
            StatusText.Text = _failedTools > 0
                ? Loc.T("Main.Status.BootAndDownloadProblems", _failedTools)
                : Loc.T("Main.Status.BootProblems");
        }
        else if (_failedTools > 0)
        {
            StatusText.Foreground = InfoBrush;
            StatusText.Text = _failedTools == 1
                ? Loc.T("Main.Status.OneDownloadFailed")
                : Loc.T("Main.Status.DownloadsFailed", _failedTools);
        }
        else
        {
            StatusText.Foreground = SuccessBrush;
            StatusText.Text = Loc.T("Main.Status.Done");
        }
    }

    /// <summary>
    /// Asks once, at selection time, whether an optional companion file should be fetched.
    /// Asking here rather than mid-build keeps the build itself uninterrupted.
    /// </summary>
    private void AskAboutOptionalExtra(ToolSelectionItem item)
    {
        var extra = item.Tool.OptionalExtra;
        if (extra == null || item.OptionalExtraAsked) return;

        item.OptionalExtraAsked = true;

        var dialog = new ChoiceDialog(extra.Headline, extra.Reason, extra.Question,
            extra.YesLabel, extra.NoLabel) { Owner = this };

        item.OptionalExtraAccepted = dialog.ShowDialog() == true;
    }

    /// <summary>
    /// Tells the user a tool needs an account before it can be used, and offers to open the
    /// sign-up page. Shown once per selection, like the optional extras.
    /// </summary>
    private void AskAboutAccountNotice(ToolSelectionItem item)
    {
        var notice = item.Tool.AccountNotice;
        if (notice == null || item.AccountNoticeAsked) return;

        item.AccountNoticeAsked = true;

        var dialog = new AccountNoticeDialog(notice.Headline, notice.Reason, notice.Question,
            notice.RegisterLabel, notice.HaveAccountLabel) { Owner = this };

        if (dialog.ShowDialog() == true)
            Links.Open(notice.Url);
    }

    private void Faq_Click(object sender, RoutedEventArgs e)
    {
        new FaqWindow { Owner = this }.ShowDialog();
    }

    private void GitHubLogin_Click(object sender, RoutedEventArgs e)
    {
        new GitHubLoginWindow(_github) { Owner = this }.ShowDialog();
    }

    // Tuned for the dark log panel: brighter, desaturated variants stay legible on #1C1C36,
    // whereas the darker shades used on a light background would disappear into it.
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0xFF, 0xC8, 0x6B));
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x4A, 0xDE, 0x9E));
    private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(0xC9, 0xC9, 0xDE));

    private const int SysmoduleWarningThreshold = 7;

    /// <summary>
    /// Sysmodules auto-start at boot via their boot2.flag. Atmosphère's release notes for
    /// firmware 21.0.0 record that roughly 10 MB less memory is available for custom system
    /// modules than before, and stacking many of them can push boot2 past what's left — at
    /// which point HOS dies immediately and simply lands back in Hekate with nothing on
    /// screen to explain it. Asked before the build rather than only logged afterwards,
    /// since by then the SD card is already written.
    /// </summary>
    private bool ConfirmSysmoduleLoad(List<ToolSelectionItem> selected)
    {
        var sysmodules = selected.Where(i => i.Tool.IsSysmodule).Select(i => i.Tool.DisplayName).ToList();
        if (sysmodules.Count < SysmoduleWarningThreshold) return true;

        var dialog = new ChoiceDialog(
            Loc.T("Main.Sysmodules.Headline", sysmodules.Count),
            $"{string.Join(", ", sysmodules)}\n\n" + Loc.T("Main.Sysmodules.Reason"),
            Loc.T("Main.Sysmodules.Question"),
            Loc.T("Main.Sysmodules.Continue"),
            Loc.T("Main.Sysmodules.Reselect")) { Owner = this };

        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// Reports anything missing that the console needs to boot. Silent failures at boot are
    /// hard to tell apart from a broken card or console, so it is worth saying plainly here
    /// while the folder is still on the PC.
    /// </summary>
    private void CheckBootIntegrity(string sdRoot, List<ToolSelectionItem> selected)
    {
        try
        {
            var problems = BootIntegrityCheck.Run(
                sdRoot,
                selected.Any(i => i.Tool.Id == "hekate"),
                selected.Any(i => i.Tool.Id == "atmosphere"));

            if (problems.Count == 0) return;

            Log("");
            foreach (var problem in problems)
            {
                Log(Loc.T(problem.IsFatal ? "Log.Main.BootProblem" : "Log.Main.BootNote", problem.Message),
                    problem.IsFatal ? LogKind.Error : LogKind.Info);

                if (problem.IsFatal) _bootIntegrityProblems++;
            }
        }
        catch (Exception ex)
        {
            Log(Loc.T("Log.Main.BootCheckFailed", ex.Message), LogKind.Info);
        }
    }

    private void LogSysmoduleNote(List<ToolSelectionItem> selected)
    {
        var sysmodules = selected.Where(i => i.Tool.IsSysmodule).Select(i => i.Tool.DisplayName).ToList();
        if (sysmodules.Count < SysmoduleWarningThreshold) return;

        Log("");
        Log(Loc.T("Log.Main.SysmoduleNote", sysmodules.Count, string.Join(", ", sysmodules)), LogKind.Info);
        Log(Loc.T("Log.Main.SysmoduleHint"), LogKind.Info);
    }

    private void Log(string message, LogKind level = LogKind.Normal)
    {
        var brush = level switch
        {
            LogKind.Error => ErrorBrush,
            LogKind.Info => InfoBrush,
            LogKind.Success => SuccessBrush,
            _ => NormalBrush
        };

        var paragraph = new Paragraph(new Run(message) { Foreground = brush })
        {
            Margin = new Thickness(0)   // RichTextBox spaces paragraphs by default; keep log lines tight
        };

        LogBox.Document.Blocks.Add(paragraph);
        LogBox.ScrollToEnd();
    }
}
