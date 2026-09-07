using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwitchCfwTool.Data;

namespace SwitchCfwTool.Services;

/// <summary>
/// Finds what a tool leaves behind beyond the files that were installed.
///
/// Most tools write their settings the first time they run on the console, so those files
/// exist in no install record and survive an uninstall that only removes what was copied.
/// Two sources are combined: folders declared for the tool, and a name match inside config/.
///
/// The automatic name match is confined to config/ on purpose: folders elsewhere on the card
/// — save backups, dumps, cheat collections — can carry work the user would not want removed
/// along with a program, and no automatic rule can tell those apart. Folders outside config/
/// are only removed when a tool declares them explicitly, as Tico does for the folder it
/// creates in the card root on first launch.
/// </summary>
public static class ToolLeftovers
{
    private const string ConfigRoot = "config";

    public static List<string> FindConfigFolders(string sdRoot, string toolId)
    {
        var tool = ToolCatalog.All.FirstOrDefault(t => t.Id.Equals(toolId, StringComparison.OrdinalIgnoreCase));
        if (tool == null) return new List<string>();

        var found = new List<string>();

        foreach (var declared in tool.ConfigFolders)
        {
            var full = Path.Combine(sdRoot, declared.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(full)) found.Add(full);
        }

        // Fallback for tools with no declared folder: a directory under config/ whose name
        // matches the tool once punctuation and case are ignored.
        var configDir = Path.Combine(sdRoot, ConfigRoot);
        if (Directory.Exists(configDir))
        {
            var wanted = new[] { ToolDetector.Normalise(tool.Id), ToolDetector.Normalise(tool.DisplayName) };

            foreach (var dir in Directory.GetDirectories(configDir))
            {
                if (!wanted.Contains(ToolDetector.Normalise(Path.GetFileName(dir)))) continue;
                if (found.Any(f => string.Equals(f, dir, StringComparison.OrdinalIgnoreCase))) continue;

                found.Add(dir);
            }
        }

        return found;
    }
}
