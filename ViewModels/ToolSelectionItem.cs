using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using SwitchCfwTool.Models;

namespace SwitchCfwTool.ViewModels;

/// <summary>One selectable version entry shown in the "Custom" mode dropdown.</summary>
public class VersionOption
{
    public required string TagName { get; init; }
    public required string DownloadUrl { get; init; }
    public required string AssetFileName { get; init; }

    // Shown as the repo tags it, with no added "v": some repos already carry one and some
    // don't, so prefixing produced inconsistent labels like "vv1.2" next to "v0.9".
    public string Label => TagName;

    // The custom ComboBox template doesn't reliably pick up the template WPF generates from
    // DisplayMemberPath for the *closed* box, which made it fall back to ToString() and show
    // the raw type name. Overriding ToString() makes that fallback show the right thing.
    public override string ToString() => Label;
}

public class ToolSelectionItem : INotifyPropertyChanged
{
    public CfwTool Tool { get; }

    // Latest and Custom keep their own tick state: the two modes are separate builds in the
    // user's head, so switching tabs to compare shouldn't quietly carry a selection across.
    private bool _selectedInLatest;
    private bool _selectedInCustom;

    public bool IsSelected
    {
        get => CustomMode ? _selectedInCustom : _selectedInLatest;
        set
        {
            if (IsSelected == value) return;

            if (CustomMode) _selectedInCustom = value;
            else _selectedInLatest = value;

            // An unselected tool shouldn't keep showing a version, so the dropdown is
            // blanked out — but the fetched list is kept, so re-selecting restores the
            // newest entry without another GitHub request.
            if (CustomMode)
            {
                if (!value) SelectedVersion = null;
                else SelectedVersion ??= AvailableVersions.FirstOrDefault();
            }

            // Asked again on every fresh selection, so the choice can be revised.
            if (!value) OptionalExtraAsked = false;

            OnPropertyChanged();
            OnPropertyChanged(nameof(LatestVersionVisibility));
            OnPropertyChanged(nameof(VersionHintVisibility));
            OnPropertyChanged(nameof(CustomVersionVisibility));
        }
    }

    private bool _customMode;
    public bool CustomMode
    {
        get => _customMode;
        set
        {
            _customMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSelected));          // each mode has its own tick state
            OnPropertyChanged(nameof(CustomVersionVisibility));
            OnPropertyChanged(nameof(LatestVersionVisibility));
            OnPropertyChanged(nameof(VersionHintVisibility));
        }
    }

    /// <summary>The version that "Latest" would install, shown as fixed text (not a picker).</summary>
    private string? _latestLabel;
    public string? LatestLabel
    {
        get => _latestLabel;
        set
        {
            _latestLabel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LatestVersionVisibility));
        }
    }

    // Only meaningful once the tool is actually selected and its version has been looked up.
    public Visibility LatestVersionVisibility =>
        (!CustomMode && IsSelected && !string.IsNullOrEmpty(LatestLabel))
            ? Visibility.Visible
            : Visibility.Collapsed;

    // The dropdown and the hint below are mutually exclusive rather than stacked, so a
    // leftover selection can never show through the placeholder text.
    public Visibility CustomVersionVisibility =>
        (CustomMode && IsSelected) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Replaces the dropdown while the tool isn't selected. Version lists are only fetched
    /// for selected tools — each fetch is a GitHub request, and pulling one for all 31 tools
    /// would burn half the hourly budget before a build even starts.
    /// </summary>
    public Visibility VersionHintVisibility =>
        (CustomMode && !IsSelected) ? Visibility.Visible : Visibility.Collapsed;

    private List<VersionOption> _availableVersions = new();
    public List<VersionOption> AvailableVersions
    {
        get => _availableVersions;
        set { _availableVersions = value; OnPropertyChanged(); }
    }

    private VersionOption? _selectedVersion;
    public VersionOption? SelectedVersion
    {
        get => _selectedVersion;
        set { _selectedVersion = value; OnPropertyChanged(); }
    }

    /// <summary>Whether the optional-extra prompt has already been shown for this tool.</summary>
    public bool OptionalExtraAsked { get; set; }

    /// <summary>The user's answer to that prompt, read at build time.</summary>
    public bool OptionalExtraAccepted { get; set; }

    public ToolSelectionItem(CfwTool tool)
    {
        Tool = tool;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
