using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwitchCfwTool.Data;
using SwitchCfwTool.Models;

namespace SwitchCfwTool.Services;

/// <summary>What was found on the card for one tool.</summary>
public record DetectedTool(string ToolId, List<string> Paths);

/// <summary>
/// Recognises installed tools from the card itself, without relying on the install record.
///
/// Four kinds of trace are checked, because no single one covers every tool:
///
///   • declared paths — title-ID folders under atmosphere/contents above all, since nothing
///     about "0100000000000352" can be derived from a tool's name
///   • the folder under switch/ a homebrew app is installed into
///   • a loose .nro directly in switch/, which is how these were laid out for years and how
///     other installers still do it
///   • overlays and boot payloads, matched on filename
///
/// Name matching ignores case and punctuation, so "NX-FanControl" also finds "nxfancontrol".
/// It is deliberately conservative: a miss only means a tool shows as not installed, whereas
/// a false hit would offer to delete files belonging to something else.
/// </summary>
public static class ToolDetector
{
    public static List<DetectedTool> Scan(string sdRoot)
    {
        var results = new List<DetectedTool>();
        if (!Directory.Exists(sdRoot)) return results;

        var switchDir = Path.Combine(sdRoot, "switch");
        var overlayDir = Path.Combine(switchDir, ".overlays");
        var payloadDir = Path.Combine(sdRoot, "bootloader", "payloads");

        var looseNros = SafeFiles(switchDir, "*.nro", SearchOption.TopDirectoryOnly);
        var overlays = SafeFiles(overlayDir, "*.ovl", SearchOption.TopDirectoryOnly);
        var payloads = SafeFiles(payloadDir, "*.bin", SearchOption.TopDirectoryOnly);

        // A Footprint path declared by more than one tool (e.g. Horizon-OC deliberately
        // overriding sys-clk's title ID) proves only that ONE of them is on the card, not
        // which — crediting it to both would show two tools as installed from a single
        // folder. Such a path only counts for a tool that also has its own ConfigFolders
        // present as corroboration; left uncorroborated, it counts for neither.
        var footprintOwners = ToolCatalog.All
            .SelectMany(t => t.Footprint)
            .GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in ToolCatalog.All)
        {
            var found = new List<string>();

            var configFound = tool.ConfigFolders
                .Select(c => Path.Combine(sdRoot, c.Replace('/', Path.DirectorySeparatorChar)))
                .Where(Directory.Exists)
                .ToList();
            found.AddRange(configFound);

            foreach (var declared in tool.Footprint)
            {
                var full = Path.Combine(sdRoot, declared.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full) && !Directory.Exists(full)) continue;
                if (footprintOwners.Contains(declared) && configFound.Count == 0) continue;
                found.Add(full);
            }

            // The folder this program installs homebrew apps into.
            var ownFolder = Path.Combine(switchDir, tool.DisplayName);
            if (Directory.Exists(ownFolder)) found.Add(ownFolder);

            found.AddRange(looseNros.Where(f => NameMatches(tool, Path.GetFileNameWithoutExtension(f))));
            found.AddRange(overlays.Where(f => NameMatches(tool, Path.GetFileNameWithoutExtension(f))));
            found.AddRange(payloads.Where(f => NameMatches(tool, Path.GetFileNameWithoutExtension(f))));

            if (found.Count > 0)
                results.Add(new DetectedTool(tool.Id, found.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
        }

        return results;
    }

    /// <summary>Loose .nro files in switch/ that belong to a tool now installed in its own folder.</summary>
    public static List<string> FindLegacyLooseFiles(string sdRoot, CfwTool tool)
    {
        var switchDir = Path.Combine(sdRoot, "switch");

        return SafeFiles(switchDir, "*.nro", SearchOption.TopDirectoryOnly)
            .Where(f => NameMatches(tool, Path.GetFileNameWithoutExtension(f)))
            .ToList();
    }

    /// <summary>
    /// Whether a loose file's name (without extension) belongs to this tool: an exact match
    /// against its id/display name/asset filename, or — only for tools that declare
    /// <see cref="CfwTool.NameContains"/> — a substring match against one of those hints.
    /// </summary>
    private static bool NameMatches(CfwTool tool, string fileNameNoExt)
    {
        var normalised = Normalise(fileNameNoExt);
        if (NamesFor(tool).Contains(normalised)) return true;

        return tool.NameContains
            .Select(Normalise)
            .Where(hint => hint.Length > 0)
            .Any(normalised.Contains);
    }

    /// <summary>The spellings a tool's files might use.</summary>
    private static HashSet<string> NamesFor(CfwTool tool)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Normalise(tool.Id),
            Normalise(tool.DisplayName)
        };

        // A pattern like "FPSLocker.ovl" names the file outright; anything with a wildcard
        // says nothing useful about the name.
        if (tool.AssetNamePattern is { } pattern && !pattern.Contains('*'))
            names.Add(Normalise(Path.GetFileNameWithoutExtension(pattern)));

        names.RemoveWhere(string.IsNullOrEmpty);
        return names;
    }

    private static List<string> SafeFiles(string dir, string pattern, SearchOption option)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, pattern, option).ToList()
                : new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>Letters and digits only, lower-cased — shared with <see cref="ToolLeftovers"/>,
    /// which needs the exact same "ignore case and punctuation" name comparison.</summary>
    internal static string Normalise(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
