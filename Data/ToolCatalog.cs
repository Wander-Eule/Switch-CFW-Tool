using System;
using System.Collections.Generic;
using System.Linq;
using SwitchCfwTool.Models;

namespace SwitchCfwTool.Data;

/// <summary>
/// Single source of truth for every tool offered in the UI. Add new tools here only —
/// the order entries are written in doesn't matter, <see cref="All"/> sorts them.
/// </summary>
public static class ToolCatalog
{
    /// <summary>
    /// Every tool, sorted alphabetically by display name with digit-led names (e.g. "90DNS
    /// Tester") pushed to the end, since a list that opens on a number reads as an oddity
    /// rather than as the start of an alphabet.
    /// </summary>
    // Computed on first access rather than in a field initialiser: static initialisers run
    // in source order, so sorting eagerly here would read the list below before it exists.
    private static List<CfwTool>? _sorted;

    public static IReadOnlyList<CfwTool> All => _sorted ??= Unsorted
        .OrderBy(t => char.IsDigit(t.DisplayName[0]) ? 1 : 0)
        .ThenBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static readonly List<CfwTool> Unsorted = new()
    {
        new CfwTool
        {
            Id = "ovl-sysmodules",
            ConfigFolders = new[] { "config/ovl-sysmodules" },
            Author = "WerWolv (fork by ppkantorski)",
            DisplayName = "SYS-Modules",
            Description = "Overlay for starting and stopping sysmodules on the fly",
            GitHubRepo = "ppkantorski/ovl-sysmodules",
            AssetNamePattern = "*.ovl"
        },
        new CfwTool
        {
            Id = "goldleaf",
            Author = "XorTroll",
            DisplayName = "Goldleaf",
            Description = "Multipurpose file and content manager, installs NSP/NRO over USB or from the SD card",
            GitHubRepo = "XorTroll/Goldleaf",
            AssetNamePattern = "Goldleaf.nro",
            OptionalExtra = new OptionalExtra
            {
                GitHubRepo = "XorTroll/Goldleaf",
                AssetNamePattern = "Quark.jar",
                Headline = "Goldleaf can use a PC-side companion",
                Reason = "Quark.jar runs on your computer and is required for Goldleaf's USB features, "
                       + "such as browsing PC folders and installing files directly over USB. "
                       + "Goldleaf itself works without it, just with those features unavailable.",
                Question = "Download Quark.jar? It will be saved next to the SDMC folder.",
                YesLabel = "Download Quark",
                NoLabel = "Don't Download",
                PlaceNextToSdmc = true
            }
        },
        new CfwTool
        {
            Id = "moonlight",
            Author = "XITRIX",
            DisplayName = "Moonlight",
            Description = "Game streaming client for NVIDIA GameStream and Sunshine hosts",
            GitHubRepo = "XITRIX/Moonlight-Switch",
            AssetNamePattern = "*.nro"
        },
        new CfwTool
        {
            Id = "dns90tester",
            Author = "meganukebmp",
            DisplayName = "90DNS Tester",
            Description = "Checks whether Nintendo's servers are correctly blocked on the console",
            GitHubRepo = "meganukebmp/switch_90DNS_tester",
            AssetNamePattern = "*.nro"
        },
        new CfwTool
        {
            Id = "hekate",
            // Neither lives under switch/ nor has a name-matching file, so without a
            // Footprint Hekate never showed as installed on a hand-set-up card. Both paths
            // are exclusive to Hekate: payload.bin is its renamed boot payload (see the
            // "hekate" case in SdCardBuilder.ApplyToolSpecificFixups), bootloader/sys is the
            // folder Hekate's own release ships (nyx.bin, the emuMMC/sleep/DRAM modules —
            // nothing else in the catalog writes there).
            Footprint = new[] { "payload.bin", "bootloader/sys" },
            Author = "CTCaer",
            DisplayName = "Hekate",
            Description = "Custom Bootloader for the Nintendo Switch",
            GitHubRepo = "CTCaer/hekate",
            AssetNamePattern = "*.zip"
        },
        new CfwTool
        {
            Id = "hekate-toolbox",
            Author = "WerWolv",
            DisplayName = "Hekate-Toolbox",
            Description = "Homebrew app for managing Hekate configs, backups, and tools",
            GitHubRepo = "WerWolv/Hekate-Toolbox",
            AssetNamePattern = "*.nro"
        },
        new CfwTool
        {
            Id = "atmosphere",
            // atmosphere/package3 is what every boot entry points at (see BootIntegrityCheck)
            // and belongs to Atmosphère alone — unlike atmosphere/contents, which sysmodules
            // from other tools also populate, so it wouldn't reliably mean Atmosphère itself.
            Footprint = new[] { "atmosphere/package3" },
            Author = "SciresM, TuxSH & hexkyz",
            DisplayName = "Atmosphère",
            Description = "Custom Firmware (CFW) for the Nintendo Switch",
            GitHubRepo = "Atmosphere-NX/Atmosphere",
            AssetNamePattern = "atmosphere-*.zip",
            MmcRelevant = true
        },
        new CfwTool
        {
            Id = "sys-patch",
            Footprint = new[] { "atmosphere/contents/420000000000000B" },
            ConfigFolders = new[] { "config/sys-patch" },
            Author = "ITotalJustice (forks by impeeza & borntohonk)",
            DisplayName = "SYS-Patch",
            Description = "Dynamically patches system modules (e.g. ES, NIFM) without modifying NSPs",
            GitHubRepo = "borntohonk/sys-patch",
            AssetNamePattern = "*.zip",
            IsSysmodule = true
        },
        new CfwTool
        {
            Id = "ultrahand-overlay",
            Footprint = new[] { "atmosphere/contents/420000000007E51A", "switch/.overlays/ovlmenu.ovl" },
            ConfigFolders = new[] { "config/ultrahand" },
            Author = "ppkantorski",
            DisplayName = "Ultrahand-Overlay",
            Description = "Overlay menu for custom overlays that can get accessed everytime",
            GitHubRepo = "ppkantorski/Ultrahand-Overlay",
            AssetNamePattern = "sdout.zip",
            IsSysmodule = true
        },
        new CfwTool
        {
            Id = "saltynx",
            Author = "masagrator",
            DisplayName = "SaltyNX",
            Description = "Sysmodule for injecting custom codes into retail Nintendo Switch games. SaltyNX-Tool is downloaded automatically alongside it",
            GitHubRepo = "masagrator/SaltyNX",
            AssetNamePattern = "*.zip",
            Footprint = new[] { "atmosphere/contents/0000000000534C56" },
            IsSysmodule = true,
            Companions = new[]
            {
                new CompanionDownload
                {
                    DisplayName = "SaltyNX-Tool",
                    GitHubRepo = "masagrator/SaltyNX-Tool",
                    AssetNamePattern = "*.nro",
                    Author = "masagrator"
                }
            }
        },
        new CfwTool
        {
            Id = "missioncontrol",
            Footprint = new[] { "atmosphere/contents/010000000000BD00", "atmosphere/exefs_patches/bluetooth_patches", "atmosphere/exefs_patches/btm_patches", "atmosphere/exefs_patches/hid_patches" },
            Author = "ndeadly",
            DisplayName = "Mission Control",
            Description = "A Homebrew tool, to connect non Nintendo Controllers to the switch like Playstation or XBox Controllers",
            GitHubRepo = "ndeadly/MissionControl",
            AssetNamePattern = "*.zip",
            IsSysmodule = true
        },
        new CfwTool
        {
            Id = "horizon-oc",
            // Shares sys-clk's title ID on purpose: hoc-clk is a drop-in replacement that
            // overrides sys-clk rather than running alongside it (confirmed by Horizon-OC's
            // developer, see ConflictReason below). ToolDetector treats a Footprint shared by
            // more than one tool as ambiguous and only credits it to whichever one also has
            // its own ConfigFolders present, so this never shows both as installed at once.
            Footprint = new[] { "atmosphere/contents/00FF0000636C6BFF" },
            ConfigFolders = new[] { "config/HOC" },
            Author = "Souldbminer",
            DisplayName = "Horizon-OC",
            Description = "Overclocking/underclocking configuration profiles for the Nintendo Switch CPU, GPU, and memory",
            GitHubRepo = "Horizon-OC/Horizon-OC",
            AssetNamePattern = "dist.zip",
            IsSysmodule = true,
            LoadsKip = true,
            ConflictsWith = new[] { "sys-clk" },
            ConflictReason = "Horizon-OC ships its own clock module (hoc-clk) which overrides sys-clk. "
                           + "Its developer recommends replacing sys-clk with it rather than running both."
        },
        new CfwTool
        {
            Id = "sys-clk",
            Footprint = new[] { "atmosphere/contents/00FF0000636C6BFF" },
            ConfigFolders = new[] { "config/sys-clk" },
            Author = "RetroNX Team (fork by ppkantorski)",
            DisplayName = "SYS-CLK",
            Description = "Sysmodule for setting custom CPU, GPU and memory clock speeds per game",
            GitHubRepo = "ppkantorski/sys-clk",
            AssetNamePattern = "*.zip",
            IsSysmodule = true
        },
        new CfwTool
        {
            Id = "nx-fancontrol",
            Footprint = new[] { "atmosphere/contents/00FF0000B378D640" },
            ConfigFolders = new[] { "config/nx-fancontrol" },
            Author = "dominatorul (fork by Lightos1)",
            DisplayName = "NX-FanControl",
            Description = "Sysmodule for custom fan curve control, with an overlay to adjust the curve",
            // ppkantorski's fork only attaches the overlay, which is just the interface —
            // without the sysmodule behind it nothing actually drives the fan. This repo
            // ships the full package in its SD-card layout.
            GitHubRepo = "dominatorul/NX-FanControl",
            AssetNamePattern = "*.zip",
            IsSysmodule = true
        },
        new CfwTool
        {
            Id = "fpslocker",
            Author = "masagrator",
            DisplayName = "FPS Locker",
            Description = "Unlocks or locks the framerate in supported games",
            GitHubRepo = "masagrator/FPSLocker",
            AssetNamePattern = "FPSLocker.ovl",
            IsSysmodule = false
        },
        new CfwTool
        {
            Id = "reversenx-rt",
            Author = "masagrator",
            DisplayName = "ReverseNX-RT",
            Description = "Switch games between docked or handheld mode, no matter if console is docked or not",
            GitHubRepo = "masagrator/ReverseNX-RT",
            AssetNamePattern = "*.ovl",
            IsSysmodule = false
        },
        new CfwTool
        {
            Id = "lockpick_rcm",
            Author = "shchmue (fork by Kofysh)",
            DisplayName = "Lockpick RCM",
            Description = "Extracts encryption keys from the console",
            GitHubRepo = "Kofysh/Lockpick_RCM",
            AssetNamePattern = "Lockpick_RCM.bin"
        },
        new CfwTool
        {
            Id = "tegraexplorer",
            Author = "suchmememanyskill",
            DisplayName = "TegraExplorer",
            Description = "File explorer and NAND/partition tool for the Nintendo Switch, usable as a boot payload",
            GitHubRepo = "suchmememanyskill/TegraExplorer",
            AssetNamePattern = "TegraExplorer.bin"
        },
        new CfwTool
        {
            Id = "sphaira",
            Author = "ITotalJustice (fork by NaGaa95)",
            DisplayName = "Sphaira",
            Description = "All-in-one homebrew file manager, forwarder, and installer for the Nintendo Switch",
            GitHubRepo = "NaGaa95/sphaira",
            AssetNamePattern = "*.zip"
        },
        new CfwTool
        {
            Id = "dbi",
            // The actual release asset is versioned ("834.en.nro"), which is what a hand
            // install typically drops straight into switch/ — that filename doesn't equal
            // "dbi" outright, so the normal exact-name match never catches it. NameContains
            // still requires "dbi" to appear somewhere in the filename, so a stray unrelated
            // .nro isn't picked up.
            NameContains = new[] { "dbi" },
            Author = "rashevskyv (fork by Morce3232)",
            DisplayName = "DBI",
            Description = "Installer for NSP/XCI files via USB or network",
            GitHubRepo = "Morce3232/DBIPatcher",
            AssetNamePattern = "*.en.nro",
            VersionFromAssetName = true
        },
        new CfwTool
        {
            Id = "linkalho",
            Author = "rdmrocha (fork by impeeza)",
            DisplayName = "Linkalho",
            Description = "Links or unlinks Nintendo accounts on the console offline",
            GitHubRepo = "impeeza/linkalho",
            AssetNamePattern = "*.zip"
        },
        new CfwTool
        {
            Id = "checkpoint",
            Author = "BernardoGiordano",
            DisplayName = "Checkpoint",
            Description = "Backup/restore manager for save data",
            GitHubRepo = "BernardoGiordano/Checkpoint",
            AssetNamePattern = "*.nro"
        },
        new CfwTool
        {
            Id = "jksv",
            Author = "J-D-K",
            DisplayName = "JKSV",
            Description = "Save data backup and restore manager for the Nintendo Switch",
            GitHubRepo = "J-D-K/JKSV",
            AssetNamePattern = "*.nro"
        },
        new CfwTool
        {
            Id = "nxmp",
            Author = "proconsule",
            DisplayName = "NXMP",
            Description = "Media player for local files and network shares, based on MPV",
            GitHubRepo = "proconsule/nxmp",
            AssetNamePattern = "*.zip",
            ArchiveSubfolder = "switch"
        },
        new CfwTool
        {
            Id = "fizeau",
            Footprint = new[] { "atmosphere/contents/0100000000000F12" },
            ConfigFolders = new[] { "config/Fizeau" },
            Author = "averne",
            DisplayName = "Fizeau",
            Description = "Adjusts screen color temperature/gamma (like f.lux)",
            GitHubRepo = "averne/Fizeau",
            AssetNamePattern = "*.zip",
            IsSysmodule = true
        },
        new CfwTool
        {
            Id = "tico",
            ConfigFolders = new[] { "tico" },
            Author = "dantiicu",
            DisplayName = "Tico",
            Description = "The first custom emulation frontend for Nintendo Switch. A controller-first interface built for performance and simplicity",
            GitHubRepo = "ticohq/tico",
            AssetNamePattern = "*.nro"
        },
        new CfwTool
        {
            Id = "emuiibo",
            Footprint = new[] { "atmosphere/contents/0100000000000352" },
            Author = "XorTroll",
            DisplayName = "Emuiibo",
            Description = "Sysmodule that emulates amiibo without needing physical figures",
            GitHubRepo = "XorTroll/emuiibo",
            AssetNamePattern = "*.zip",
            IsSysmodule = true
        },
        new CfwTool
        {
            Id = "edizon-se",
            ConfigFolders = new[] { "config/EdiZon" },
            Author = "WerWolv (fork by tomvita)",
            DisplayName = "EdiZon-SE",
            Description = "Cheat code and save manager for the Nintendo Switch. The matching EdiZon overlay by proferabg is downloaded automatically alongside it",
            GitHubRepo = "tomvita/EdiZon-SE",
            AssetNamePattern = "*.zip",
            IsSysmodule = true,
            Companions = new[]
            {
                new CompanionDownload
                {
                    DisplayName = "EdiZon-Overlay",
                    GitHubRepo = "proferabg/EdiZon-Overlay",
                    AssetNamePattern = "ovlEdiZon.ovl",
                    Author = "proferabg"
                }
            }
        },
        new CfwTool
        {
            Id = "nxthemesinstaller",
            Author = "exelix11",
            DisplayName = "NX-Themes Installer",
            Description = "Installs and manages custom Nintendo Switch home menu themes",
            GitHubRepo = "exelix11/SwitchThemeInjector",
            AssetNamePattern = "*.nro"
        },
        new CfwTool
        {
            Id = "nxactivitylog",
            Author = "tallbl0nde (fork by zdm65477730)",
            DisplayName = "NX-Activity-Log",
            Description = "Tracks and displays play-time activity and statistics for games",
            GitHubRepo = "zdm65477730/NX-Activity-Log",
            AssetNamePattern = "*.nro"
        },
        new CfwTool
        {
            Id = "simplemodmanager",
            Author = "nadrino",
            DisplayName = "Simple Mod Manager",
            Description = "Manages and toggles game mods without manually editing LayeredFS folders",
            GitHubRepo = "nadrino/SimpleModManager",
            AssetNamePattern = "*.nro"
        },
    };
}
