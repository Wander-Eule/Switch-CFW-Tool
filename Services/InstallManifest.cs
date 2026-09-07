using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwitchCfwTool.Services;

/// <summary>What one tool put on the card, recorded at build time.</summary>
public class InstallRecord
{
    [JsonPropertyName("toolId")] public string ToolId { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("installedAt")] public DateTime InstalledAt { get; set; }

    /// <summary>Paths relative to the SD root, using forward slashes.</summary>
    [JsonPropertyName("files")] public List<string> Files { get; set; } = new();
}

/// <summary>
/// Tracks which files belong to which tool, stored on the card itself.
///
/// This exists because an SD card can't be read backwards: a folder named after a title ID
/// gives no hint which homebrew created it, and a stray .nro says nothing about the config
/// and sysmodule files installed alongside it. Guessing would mean either missing files on
/// uninstall or deleting someone else's. Recording the file list while installing is the
/// only way to remove a tool completely and safely later.
/// </summary>
public static class InstallManifest
{
    // Kept under config/ with the other tools' settings rather than under switch/, which is
    // reserved for things the console can launch.
    private const string RelativePath = "config/s-cfw-t/Switch-CFW-Tool-DB.json";

    // Builds aimed at a folder on the PC write here instead. Copying such a build onto a
    // card by hand would otherwise drop its short record straight on top of the card's own
    // and erase everything installed before it. Under a separate name the two land side by
    // side, and Manage CFW folds this one into the real record the next time it scans.
    private const string ImportRelativePath = "config/s-cfw-t/Switch-CFW-Tool-DB-import.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string PathFor(string sdRoot) => Path.Combine(sdRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ImportPathFor(string sdRoot) => Path.Combine(sdRoot, ImportRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public static List<InstallRecord> Load(string sdRoot) => LoadFrom(PathFor(sdRoot));

    private static List<InstallRecord> LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return new List<InstallRecord>();
            return JsonSerializer.Deserialize<List<InstallRecord>>(File.ReadAllText(path)) ?? new List<InstallRecord>();
        }
        catch
        {
            // A damaged manifest shouldn't block the app — treat it as "nothing recorded".
            return new List<InstallRecord>();
        }
    }

    /// <summary>
    /// Folds a hand-copied build's record into the card's own and removes it afterwards, so
    /// tools installed earlier keep showing up in Manage CFW.
    /// </summary>
    public static void MergeImports(string sdRoot)
    {
        var importPath = ImportPathFor(sdRoot);
        if (!File.Exists(importPath)) return;

        // Only entries whose files are genuinely on the card are taken over. Without this a
        // leftover import file — say, the same PC folder copied across a second time after
        // the tool had been uninstalled — would put the tool back into the record even
        // though nothing of it is installed any more.
        var incoming = LoadFrom(importPath)
            .Where(record => record.Files.Any(f =>
                File.Exists(Path.Combine(sdRoot, f.Replace('/', Path.DirectorySeparatorChar)))))
            .ToList();

        if (incoming.Count > 0)
        {
            var all = Load(sdRoot);
            foreach (var record in incoming)
            {
                all.RemoveAll(r => r.ToolId.Equals(record.ToolId, StringComparison.OrdinalIgnoreCase));
                all.Add(record);
            }

            Save(sdRoot, all.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());
        }

        try { File.Delete(importPath); } catch { /* leaving it costs nothing */ }
    }

    public static void Save(string sdRoot, List<InstallRecord> records)
    {
        var path = PathFor(sdRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records, Options));
    }

    /// <summary>Adds or replaces the entry for one tool, keeping the rest untouched.</summary>
    /// <param name="asImport">
    /// True for builds written to a folder on the PC, which are meant to be copied onto a
    /// card later and must not overwrite what is already recorded there.
    /// </param>
    public static void Upsert(string sdRoot, InstallRecord record, bool asImport = false)
    {
        var path = asImport ? ImportPathFor(sdRoot) : PathFor(sdRoot);

        var all = LoadFrom(path);
        all.RemoveAll(r => r.ToolId.Equals(record.ToolId, StringComparison.OrdinalIgnoreCase));
        all.Add(record);

        var sorted = all.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(sorted, Options));
    }

    /// <summary>
    /// Whether a tool that installs a KIP is currently on the card. Read from the manifest
    /// rather than from the current selection, so removing such a tool later is noticed too.
    /// </summary>
    public static bool HasKipTool(string sdRoot)
        => Load(sdRoot).Concat(LoadFrom(ImportPathFor(sdRoot)))
               .Any(record => Data.ToolCatalog.All.Any(tool => tool.Id == record.ToolId && tool.LoadsKip));

    public static void Remove(string sdRoot, string toolId)
    {
        var all = Load(sdRoot);
        all.RemoveAll(r => r.ToolId.Equals(toolId, StringComparison.OrdinalIgnoreCase));
        Save(sdRoot, all);
    }
}
