using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwitchCfwTool.Models;

namespace SwitchCfwTool.Services;

/// <summary>
/// Places a downloaded tool into the SDMC folder.
///
/// Design note: destinations are derived from what was ACTUALLY downloaded, not from a
/// per-tool assumption about what the release "should" contain. Hardcoding both the asset
/// pattern and the destination per tool meant every upstream repo that packaged its release
/// differently than assumed produced a file in the wrong place (or none at all), and each
/// one had to be discovered by hand. Now:
///
///   - a raw file is routed by its extension (.ovl → overlays, .bin → payloads, .nro → switch)
///   - an archive is inspected: if it already contains SD-root folders (atmosphere/, switch/,
///     bootloader/, config/, ...) it is merged as-is; if those sit inside a single wrapper
///     folder (SdOut/, dist/, a versioned name, ...) the wrapper is transparently skipped;
///     if it only holds loose files, those are routed by extension like a raw download.
///
/// Only genuine one-off quirks remain as per-tool cases below.
/// </summary>
public class SdCardBuilder
{
    private readonly string _sdRoot;

    /// <summary>Folder names that identify a directory as "the root of an SD card layout".</summary>
    private static readonly string[] SdRootFolders =
    {
        "atmosphere", "bootloader", "switch", "config",
        "saltysd", "emuiibo", "themes", "nintendo", "warmboot_mariko",
    };

    /// <summary>Files that never carry payload and shouldn't stop wrapper-folder detection.</summary>
    private static readonly string[] IgnorableExtensions = { ".txt", ".md", ".pdf", ".html", ".url" };

    // Relative paths (from SDMC root) that must NOT be overwritten if they already exist,
    // because they hold user-specific settings rather than program files.
    // Kept as-is when already present. Deliberately short: everything else a tool installs
    // is program data that may be replaced on update without consequence, and preserving
    // more than necessary meant updates silently kept stale files around.
    private static readonly string[] PreserveIfExists =
    {
        "bootloader/hekate_ipl.ini",
        "bootloader/ini/hekate_ipl.ini",
        "bootloader/res",              // whole folder: boot icons and the Nyx background are the user's
    };

    // Never wanted, no matter which tool's archive happens to ship them.
    private static readonly string[] GlobalExcludeFileNames =
    {
        "reboot_to_payload.nro",
        "reboot_to_hekate.nro",
    };

    // Hekate ships placeholder versions of these; the bundled ones belong there instead.
    private static readonly string[] HekateResIconsToSkip =
    {
        "bootloader/res/icon_payload.bmp",
        "bootloader/res/icon_switch.bmp",
    };

    // Every file written since the last hand-off, so the caller can record which tool owns
    // what (see InstallManifest) and remove exactly those files again later.
    private readonly List<string> _written = new();

    public SdCardBuilder(string sdRoot)
    {
        _sdRoot = sdRoot;
        Directory.CreateDirectory(_sdRoot);
    }

    /// <summary>Returns the files written since the previous call and starts a fresh list.</summary>
    public List<string> TakeWrittenFiles()
    {
        var copy = _written.ToList();
        _written.Clear();
        return copy;
    }

    private void Record(string absolutePath)
    {
        var rel = Path.GetRelativePath(_sdRoot, absolutePath).Replace('\\', '/');
        if (!_written.Contains(rel, StringComparer.OrdinalIgnoreCase))
            _written.Add(rel);
    }

    public void MergeTool(CfwTool tool, string stagedPath, string stagedFileName, Action<string> log)
    {
        var isArchive = stagedFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (!isArchive)
        {
            // Raw single-file download (.ovl / .bin / .nro / .kip).
            var file = Path.Combine(stagedPath, stagedFileName);
            if (!File.Exists(file))
                throw new FileNotFoundException($"Downloaded file '{stagedFileName}' not found in staging folder.");

            RouteLooseFile(file, tool, log);
            return;
        }

        // Archive: work out where the actual SD-card layout starts inside it.
        // When a target subfolder is declared, the archive's own top level IS the payload
        // (e.g. NXMP's "nxmp" directory), so unwrapping would copy that folder's contents
        // into switch/ instead of the folder itself.
        var root = tool.ArchiveSubfolder != null ? stagedPath : ResolveArchiveRoot(stagedPath, log);

        if (tool.ArchiveSubfolder != null)
        {
            // The archive holds a self-contained folder (e.g. NXMP's "nxmp", which carries
            // assets besides the .nro) that belongs under a known SD directory as a whole.
            var target = Path.Combine(_sdRoot, tool.ArchiveSubfolder);
            Directory.CreateDirectory(target);
            MergeDirectory(root, target);
            log($"merged archive into {tool.ArchiveSubfolder}/");
        }
        else if (LooksLikeSdRoot(root))
        {
            var excludePaths = tool.Id == "hekate" ? HekateResIconsToSkip : null;
            MergeDirectory(root, _sdRoot, excludePaths);
        }
        else
        {
            // No recognizable SD layout — treat whatever is inside as loose files.
            log("archive has no SD-root folders; routing its files by type");
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                RouteLooseFile(file, tool, log);
        }

        ApplyToolSpecificFixups(tool, log);
    }

    /// <summary>Genuine per-tool quirks that can't be derived from the archive layout.</summary>
    private void ApplyToolSpecificFixups(CfwTool tool, Action<string> log)
    {
        switch (tool.Id)
        {
            case "hekate":
                // Hekate's zip has a loose "hekate_ctcaer_x.x.x.bin" at its root. Modchip
                // consoles need it named exactly "payload.bin" in the SD root, and only one
                // copy should remain — so it's renamed in place rather than copied.
                var payloadBin = Directory
                    .GetFiles(_sdRoot, "hekate_ctcaer_*.bin", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (payloadBin != null)
                {
                    var target = Path.Combine(_sdRoot, "payload.bin");
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(payloadBin, target);
                    Record(target);
                    log("renamed Hekate payload to payload.bin");
                }
                break;

            case "horizon-oc":
                // Horizon-OC ships its optional custom Secure Monitor as "exosphere.bin" in
                // the archive root, but Hekate expects it at atmosphere/exosphere.bin
                // (that's what a "secmon=" line points at).
                var stray = Path.Combine(_sdRoot, "exosphere.bin");
                if (File.Exists(stray))
                {
                    var atmoDir = Path.Combine(_sdRoot, "atmosphere");
                    Directory.CreateDirectory(atmoDir);
                    var exoTarget = Path.Combine(atmoDir, "exosphere.bin");
                    if (File.Exists(exoTarget)) File.Delete(exoTarget);
                    File.Move(stray, exoTarget);
                    Record(exoTarget);
                    log("moved exosphere.bin to atmosphere/");
                }
                break;
        }
    }

    /// <summary>Routes one loose file to the folder its type belongs in.</summary>
    private void RouteLooseFile(string file, CfwTool tool, Action<string> log)
    {
        var name = Path.GetFileName(file);

        if (GlobalExcludeFileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return;

        var ext = Path.GetExtension(file).ToLowerInvariant();
        string relativeDir = ext switch
        {
            ".ovl" => Path.Combine("switch", ".overlays"),
            ".bin" => Path.Combine("bootloader", "payloads"),
            ".nro" => Path.Combine("switch", tool.DisplayName),
            ".kip" or ".kip1" => Path.Combine("atmosphere", "kips"),
            _ => ""
        };

        if (relativeDir == "")
        {
            log($"skipped '{name}' (unrecognized file type)");
            return;
        }

        var targetDir = Path.Combine(_sdRoot, relativeDir);
        Directory.CreateDirectory(targetDir);
        var dest = Path.Combine(targetDir, name);
        File.Copy(file, dest, overwrite: true);
        Record(dest);
        log($"placed '{name}' in {relativeDir.Replace('\\', '/')}/");

        // Older layouts — and other installers — drop the .nro straight into switch/. Leaving
        // that copy behind would show the tool twice in the console's menu, one of them stale.
        if (ext == ".nro") RemoveLegacyCopies(tool, name, log);
    }

    /// <summary>Deletes a loose copy of this tool sitting directly in switch/.</summary>
    private void RemoveLegacyCopies(CfwTool tool, string installedName, Action<string> log)
    {
        foreach (var stale in ToolDetector.FindLegacyLooseFiles(_sdRoot, tool))
        {
            try
            {
                File.Delete(stale);
                log($"removed the older copy at switch/{Path.GetFileName(stale)}");
            }
            catch
            {
                // Locked file: the new copy is in place either way.
            }
        }

        // Also catches an identically named file, which name matching alone would miss.
        var sameName = Path.Combine(_sdRoot, "switch", installedName);
        try
        {
            if (File.Exists(sameName))
            {
                File.Delete(sameName);
                log($"removed the older copy at switch/{installedName}");
            }
        }
        catch
        {
            // Same as above.
        }
    }

    /// <summary>
    /// Walks down through wrapper folders until the directory that actually holds the SD
    /// card layout is found (e.g. emuiibo's "SdOut", or a versioned folder inside the zip).
    /// </summary>
    private static string ResolveArchiveRoot(string extractedRoot, Action<string> log)
    {
        var current = extractedRoot;

        for (var depth = 0; depth < 4; depth++)
        {
            if (LooksLikeSdRoot(current))
                return current;

            var dirs = Directory.GetDirectories(current);

            // A wrapper whose single child holds the layout.
            var sdLike = dirs.Where(LooksLikeSdRoot).ToArray();
            if (sdLike.Length == 1)
            {
                log($"unwrapped '{Path.GetFileName(sdLike[0])}' folder from archive");
                return sdLike[0];
            }

            // A pure wrapper: one folder, nothing else of substance next to it.
            var meaningfulFiles = Directory.GetFiles(current)
                .Where(f => !IgnorableExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())
                            && !Path.GetFileNameWithoutExtension(f).Equals("LICENSE", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (dirs.Length == 1 && meaningfulFiles.Length == 0)
            {
                current = dirs[0];
                log($"unwrapped '{Path.GetFileName(current)}' folder from archive");
                continue;
            }

            break;
        }

        return current;
    }

    private static bool LooksLikeSdRoot(string dir)
        => Directory.GetDirectories(dir)
            .Any(d => SdRootFolders.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase));

    private void MergeDirectory(string sourceDir, string targetDir,
        IEnumerable<string>? excludeRelPaths = null, IEnumerable<string>? excludeFileNames = null)
    {
        var exclude = excludeRelPaths?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excludeNames = GlobalExcludeFileNames
            .Concat(excludeFileNames ?? Enumerable.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(targetDir, rel));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var relUnix = rel.Replace('\\', '/');
            var destFile = Path.Combine(targetDir, rel);

            if (exclude != null && exclude.Contains(relUnix))
                continue;

            if (excludeNames.Contains(Path.GetFileName(file)))
                continue;

            if (ShouldPreserve(relUnix) && File.Exists(destFile))
                continue; // keep the user's existing version untouched

            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
            Record(destFile);
        }
    }

    private static bool ShouldPreserve(string relUnixPath)
        => PreserveIfExists.Any(p =>
            relUnixPath.Equals(p, StringComparison.OrdinalIgnoreCase) ||
            relUnixPath.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));
}
