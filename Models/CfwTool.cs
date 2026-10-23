using System;

namespace SwitchCfwTool.Models;

/// <summary>
/// Static description of a CFW / homebrew tool that can be offered in the UI.
/// </summary>
public class CfwTool
{
    /// <summary>Unique internal id, e.g. "hekate".</summary>
    public required string Id { get; init; }

    /// <summary>Display name shown next to the checkbox.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Tooltip text shown on hover, describing what the tool does. Lives in the language files
    /// under "Tool.&lt;Id&gt;.Description" so it can be translated like everything else.
    /// </summary>
    public string Description => Loc.T($"Tool.{Id}.Description");

    /// <summary>Developer/maintainer credit shown in the Help → Info window.</summary>
    public required string Author { get; init; }

    /// <summary>GitHub "owner/repo" the tool lives in (used for GitHubRelease source).</summary>
    public string? GitHubRepo { get; init; }

    /// <summary>
    /// Optional filter to pick the correct asset out of a release when there are multiple
    /// files attached (e.g. pick "*.zip" and skip "*-debug.zip"). Simple substring/wildcard
    /// matched against the asset file name. Null = take the first asset.
    /// </summary>
    public string? AssetNamePattern { get; init; }

    /// <summary>
    /// Whether this tool is installed as an Atmosphère sysmodule under
    /// atmosphere/contents/&lt;title id&gt;/, vs. a plain file drop (e.g. a .nro).
    /// Purely informational for now, used by SdCardBuilder to decide unpack behaviour.
    /// </summary>
    public bool IsSysmodule { get; init; }

    /// <summary>Whether this tool should be listed under the "requires EmuMMC/SysMMC choice" group.</summary>
    public bool MmcRelevant { get; init; }

    /// <summary>
    /// Paths that identify this tool on a card, relative to the SD root — title-ID folders
    /// under atmosphere/contents in particular, which nothing else can be derived from.
    /// Used both to recognise an install and to remove it completely.
    /// </summary>
    public string[] Footprint { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Folders the tool keeps its settings in, relative to the SD root. Needed because a
    /// tool usually creates these on the console at first launch, so they appear in no
    /// install record and would otherwise survive an uninstall.
    /// </summary>
    public string[] ConfigFolders { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Extra substrings (case-insensitive) that also count as a match when scanning loose
    /// .nro/.ovl/.bin files by name, checked as a "contains" rather than the normal exact
    /// match against the tool's id/display name. For tools whose real release filename is
    /// versioned rather than named after the tool — DBI's asset is e.g. "834.en.nro", so a
    /// hand-placed copy is often renamed to something that merely contains "DBI" rather than
    /// matching it outright. Left empty for everything else: a "contains" match is looser
    /// than the default and only worth the false-positive risk where it's actually needed.
    /// </summary>
    public string[] NameContains { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Read the version out of the asset's filename instead of the release tag. Some repos
    /// tag a release one version and attach a file built from another; the filename is what
    /// actually lands on the card, so that is what gets shown.
    /// </summary>
    public bool VersionFromAssetName { get; init; }

    /// <summary>
    /// True when the tool installs a KIP that Hekate loads at boot. Decides whether the
    /// "(NO KIP)" fallback boot entries are worth offering at all.
    /// </summary>
    public bool LoadsKip { get; init; }

    /// <summary>
    /// Ids of tools that shouldn't be installed alongside this one. Checked in both
    /// directions, so the relationship only needs declaring on one of the two tools.
    /// </summary>
    public string[] ConflictsWith { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Shown in the warning dialog when a conflict is hit, from "Tool.&lt;Id&gt;.ConflictReason"
    /// in the language files. Null (no such key) uses a generic message.
    /// </summary>
    public string? ConflictReason => Loc.TryT($"Tool.{Id}.ConflictReason");

    /// <summary>
    /// Extra downloads pulled in automatically whenever this tool is selected, for pieces
    /// that live in a separate repository but are effectively part of using the tool.
    /// </summary>
    public CompanionDownload[] Companions { get; init; } = Array.Empty<CompanionDownload>();

    /// <summary>An extra the user is asked about before it is downloaded. Null = nothing to ask.</summary>
    public OptionalExtra? OptionalExtra { get; init; }

    /// <summary>
    /// Shown when the tool is ticked, for tools that need an account before they can be used.
    /// Null = no notice.
    /// </summary>
    public AccountNotice? AccountNotice { get; init; }

    /// <summary>
    /// Where an archive's contents belong when it doesn't already contain SD-root folders —
    /// e.g. NXMP ships a bare "nxmp" folder that has to end up under switch/. Null lets the
    /// generic routing decide.
    /// </summary>
    public string? ArchiveSubfolder { get; init; }
}

/// <summary>A second repository that is fetched alongside its parent tool.</summary>
public class CompanionDownload
{
    public required string DisplayName { get; init; }
    public required string GitHubRepo { get; init; }
    public required string AssetNamePattern { get; init; }

    /// <summary>
    /// SD-card folder the archive's contents go into as a whole (e.g. "config/ultrahand/lang").
    /// Null = routed by file type like any other download.
    /// </summary>
    public string? ArchiveSubfolder { get; init; }

    /// <summary>
    /// Only these files are taken from the archive, each to the same relative path on the SD card
    /// (e.g. "atmosphere/contents/.../exefs.nsp"). Null = take what the archive offers.
    /// </summary>
    public string[]? IncludePaths { get; init; }

    /// <summary>Credit line for the Info window, same format as CfwTool.Author.</summary>
    public required string Author { get; init; }
}

/// <summary>A notice that a tool needs an account, with a link to where one is made.</summary>
public class AccountNotice
{
    public required string Url { get; init; }

    /// <summary>
    /// Prefix of the texts in the language files, e.g. "Tool.nextendo.Account" for
    /// ".Headline", ".Reason", ".Question", ".Register" and ".HaveOne".
    /// </summary>
    public required string TextKey { get; init; }

    public string Headline => Loc.T(TextKey + ".Headline");
    public string Reason => Loc.T(TextKey + ".Reason");
    public string Question => Loc.T(TextKey + ".Question");
    public string RegisterLabel => Loc.T(TextKey + ".Register");
    public string HaveAccountLabel => Loc.T(TextKey + ".HaveOne");
}

/// <summary>
/// An optional companion file the user is prompted about, for things that aren't needed by
/// everyone and shouldn't be downloaded silently (e.g. Goldleaf's desktop-side Quark.jar).
/// </summary>
public class OptionalExtra
{
    public required string GitHubRepo { get; init; }
    public required string AssetNamePattern { get; init; }

    /// <summary>
    /// Prefix of the prompt's texts in the language files, e.g. "Tool.goldleaf.Extra" for
    /// "Tool.goldleaf.Extra.Headline", ".Reason", ".Question", ".Yes" and ".No".
    /// </summary>
    public required string TextKey { get; init; }

    public string Headline => Loc.T(TextKey + ".Headline");
    public string Reason => Loc.T(TextKey + ".Reason");
    public string Question => Loc.T(TextKey + ".Question");
    public string YesLabel => Loc.T(TextKey + ".Yes");
    public string NoLabel => Loc.T(TextKey + ".No");

    /// <summary>True = save beside the SDMC folder instead of inside it (PC-side files).</summary>
    public bool PlaceNextToSdmc { get; init; }
}
