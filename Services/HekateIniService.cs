using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SwitchCfwTool.Services;

/// <summary>
/// Keeps the "secmon=" lines in hekate_ipl.ini consistent with what was actually built.
///
/// A "secmon=" line tells Hekate to load a specific Secure Monitor binary instead of taking
/// it out of package3. That's what Horizon-OC's optional custom exosphere.bin is for. The
/// catch: if the referenced file isn't on the card, Hekate aborts the boot and drops back
/// to its own menu with no readable error — a confusing failure that looks like a broken
/// CFW install or a bad SD card.
///
/// Rather than making the user manage that by hand, the decision is derived from the card
/// itself: the line is kept exactly when its target exists, and removed when it doesn't.
/// That covers both directions automatically — selecting Horizon-OC puts exosphere.bin in
/// place so the lines stay (and get re-added if a previous build stripped them), while
/// building without it removes them again.
/// </summary>
public class HekateIniService
{
    private const string SecmonTarget = "atmosphere/exosphere.bin";

    /// <summary>
    /// Keeps the KIP-dependent parts of the boot menu in step with what is installed.
    ///
    /// A "kip1=" line pointing at an empty kips folder makes Hekate fail the entry, and the
    /// "(NO KIP)" fallback only means anything while something actually installs a KIP. Both
    /// therefore have to appear when such a tool arrives and disappear again when it is
    /// removed — which is why this runs after every build and every uninstall rather than
    /// once when the files are created.
    /// </summary>
    public void SyncKipUsage(string sdRoot, bool anyKipTool)
    {
        var mainIni = Path.Combine(sdRoot, "bootloader", "hekate_ipl.ini");
        var extraIni = Path.Combine(sdRoot, "bootloader", "ini", "hekate_ipl.ini");

        var mode = DetectMode(mainIni);

        if (File.Exists(mainIni))
            ApplyKipLine(mainIni, anyKipTool);

        if (File.Exists(extraIni) && mode != null)
            ApplyNoKipSection(extraIni, mode.Value, anyKipTool);
    }

    /// <summary>Reads the configured MMC mode back out of the boot entries themselves.</summary>
    private static MmcMode? DetectMode(string mainIni)
    {
        if (!File.Exists(mainIni)) return null;

        foreach (var line in File.ReadAllLines(mainIni))
        {
            var t = line.Trim();
            if (!t.StartsWith("[") || t.Contains("NO KIP", StringComparison.OrdinalIgnoreCase)) continue;
            if (t.Contains("Stock", StringComparison.OrdinalIgnoreCase)) continue;

            if (t.Contains("EmuMMC", StringComparison.OrdinalIgnoreCase)) return MmcMode.EmuMMC;
            if (t.Contains("SysMMC", StringComparison.OrdinalIgnoreCase)) return MmcMode.SysMMC;
        }

        return null;
    }

    private static void ApplyKipLine(string iniPath, bool anyKipTool)
    {
        var blocks = SplitIntoBlocks(File.ReadAllLines(iniPath));
        var changed = false;

        foreach (var block in blocks)
        {
            var header = block.FirstOrDefault(l => l.TrimStart().StartsWith("["))?.Trim() ?? "";
            var isBootEntry = block.Any(l => l.TrimStart().StartsWith("pkg3=", StringComparison.OrdinalIgnoreCase));
            var isStock = block.Any(l => l.Trim().Equals("stock=1", StringComparison.OrdinalIgnoreCase));
            var isNoKip = header.Contains("NO KIP", StringComparison.OrdinalIgnoreCase);

            if (!isBootEntry || isStock || isNoKip) continue;

            var kipIndex = block.FindIndex(l => l.TrimStart().StartsWith("kip1=", StringComparison.OrdinalIgnoreCase));

            if (!anyKipTool && kipIndex >= 0)
            {
                block.RemoveAt(kipIndex);
                changed = true;
            }
            else if (anyKipTool && kipIndex < 0)
            {
                // Goes after the mode line, keeping the pkg3 → mode → kip1 → secmon order.
                var anchor = -1;
                for (var i = 0; i < block.Count; i++)
                {
                    var key = block[i].TrimStart();
                    if (key.StartsWith("pkg3=", StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith("emummcforce=", StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith("emummc_force_disable=", StringComparison.OrdinalIgnoreCase))
                    {
                        anchor = i;
                    }
                }

                block.Insert(anchor + 1, "kip1=atmosphere/kips/*");
                changed = true;
            }
        }

        if (changed) WriteBlocks(iniPath, blocks);
    }

    private static void ApplyNoKipSection(string iniPath, MmcMode mode, bool anyKipTool)
    {
        var blocks = SplitIntoBlocks(File.ReadAllLines(iniPath));
        var wanted = $"[Atmosphere {(mode == MmcMode.EmuMMC ? "EmuMMC" : "SysMMC")} (NO KIP)]";

        // Anything NO KIP that isn't the wanted one goes, plus all of them when no KIP tool
        // is installed any more.
        var removed = blocks.RemoveAll(b =>
        {
            var header = b.FirstOrDefault(l => l.TrimStart().StartsWith("["))?.Trim() ?? "";
            if (!header.Contains("NO KIP", StringComparison.OrdinalIgnoreCase)) return false;
            return !anyKipTool || !header.Equals(wanted, StringComparison.OrdinalIgnoreCase);
        });

        var changed = removed > 0;

        if (anyKipTool && !blocks.Any(b => b.Any(l => l.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase))))
        {
            var template = ExtractSection(BundledAssetsService.ReadTextResource("hekate_ipl_extra.ini"), wanted);
            if (template.Count > 0)
            {
                blocks.Add(template);
                changed = true;
            }
        }

        if (changed) WriteBlocks(iniPath, blocks);
    }

    private static List<string> ExtractSection(string ini, string header)
    {
        foreach (var block in SplitIntoBlocks(ini.Replace("\r\n", "\n").Split('\n')))
        {
            var found = block.FirstOrDefault(l => l.TrimStart().StartsWith("["))?.Trim();
            if (string.Equals(found, header, StringComparison.OrdinalIgnoreCase))
                return block;
        }

        return new List<string>();
    }

    /// <summary>
    /// Splits an ini into one list per section, without the blank separators. Used by the
    /// KIP-line/NO-KIP-section maintenance below, which only ever adds or removes whole
    /// lines/sections — never needs to preserve a section's original blank-line layout, so
    /// normalising it away here is harmless and keeps <see cref="WriteBlocks"/> simple.
    ///
    /// Deliberately NOT the same splitter <see cref="ProcessIni"/> uses for secmon-line
    /// syncing: that one preserves the file's exact blank-line layout verbatim, because it
    /// only ever edits inside an existing block and must not reformat lines it isn't
    /// touching. This one is free to normalise, since every caller only inspects or replaces
    /// whole sections.
    ///
    /// Any content before the first section header (a hand-added comment, say) becomes its
    /// own leading, header-less block instead of being silently dropped on the next rewrite —
    /// none of this class's block handling can mistake it for a real section, since it never
    /// starts with "[", carries a boot-entry marker, or equals a section header verbatim.
    /// </summary>
    private static List<List<string>> SplitIntoBlocks(IEnumerable<string> lines)
    {
        var blocks = new List<List<string>>();
        List<string>? current = null;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("["))
            {
                current = new List<string>();
                blocks.Add(current);
            }
            else if (current == null && blocks.Count == 0)
            {
                current = new List<string>();
                blocks.Add(current);
            }

            if (current == null) continue;
            if (string.IsNullOrWhiteSpace(line)) continue;

            current.Add(line);
        }

        return blocks;
    }

    private static void WriteBlocks(string iniPath, List<List<string>> blocks)
    {
        var output = new List<string>();

        foreach (var block in blocks.Where(b => b.Count > 0))
        {
            if (output.Count > 0) output.Add("");
            output.AddRange(block);
        }

        File.WriteAllLines(iniPath, output);
    }

    /// <summary>
    /// Strips boot entries that don't apply to this build.</summary>
    public void FilterSections(string iniPath, MmcMode mode, bool anyKipTool)
    {
        if (!File.Exists(iniPath)) return;

        var kept = new List<string>();
        var lines = File.ReadAllLines(iniPath);

        var keepingCurrent = true;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                keepingCurrent = ShouldKeep(trimmed, mode, anyKipTool);

                // Drop the blank line that separated a removed section from the one before.
                if (keepingCurrent && kept.Count > 0 && !string.IsNullOrWhiteSpace(kept[^1]))
                    kept.Add("");
            }

            if (keepingCurrent) kept.Add(line);
        }

        // Normalise: no leading or trailing blank lines.
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[0])) kept.RemoveAt(0);
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1])) kept.RemoveAt(kept.Count - 1);

        File.WriteAllLines(iniPath, kept);
    }

    private static bool ShouldKeep(string header, MmcMode mode, bool anyKipTool)
    {
        var isNoKip = header.Contains("NO KIP", StringComparison.OrdinalIgnoreCase);
        if (isNoKip && !anyKipTool) return false;

        var mentionsEmu = header.Contains("EmuMMC", StringComparison.OrdinalIgnoreCase);
        var mentionsSys = header.Contains("SysMMC", StringComparison.OrdinalIgnoreCase);

        // "Stock sysMMC" names a mode but isn't one of the two Atmosphère entries, so it is
        // matched by name rather than by the mode word it happens to contain.
        if (header.Contains("Stock", StringComparison.OrdinalIgnoreCase)) return true;

        if (mentionsEmu && mode != MmcMode.EmuMMC) return false;
        if (mentionsSys && mode != MmcMode.SysMMC) return false;

        return true;
    }

    public record SyncResult(int LinesRemoved, int LinesAdded, bool ExospherePresent);

    public SyncResult SyncSecmonLines(string sdRoot)
    {
        var exospherePath = Path.Combine(sdRoot, "atmosphere", "exosphere.bin");
        var exospherePresent = File.Exists(exospherePath);

        int removed = 0, added = 0;

        foreach (var iniPath in new[]
                 {
                     Path.Combine(sdRoot, "bootloader", "hekate_ipl.ini"),
                     Path.Combine(sdRoot, "bootloader", "ini", "hekate_ipl.ini"),
                 })
        {
            if (!File.Exists(iniPath)) continue;

            var lines = File.ReadAllLines(iniPath).ToList();
            var result = ProcessIni(lines, sdRoot, exospherePresent);

            if (result.Removed > 0 || result.Added > 0)
            {
                File.WriteAllLines(iniPath, result.Lines);
                removed += result.Removed;
                added += result.Added;
            }
        }

        return new SyncResult(removed, added, exospherePresent);
    }

    private record ProcessResult(List<string> Lines, int Removed, int Added);

    private static ProcessResult ProcessIni(List<string> lines, string sdRoot, bool exospherePresent)
    {
        var output = new List<string>();
        int removed = 0, added = 0;

        // Split into a header (anything before the first [section]) plus one block per section,
        // so each boot entry can be inspected and fixed on its own.
        var blocks = new List<List<string>>();
        var current = new List<string>();

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("[") && current.Any(l => l.TrimStart().StartsWith("[")))
            {
                blocks.Add(current);
                current = new List<string>();
            }
            current.Add(line);
        }
        if (current.Count > 0) blocks.Add(current);

        foreach (var block in blocks)
        {
            var work = new List<string>(block);

            // 1) Drop any secmon= line whose target file isn't actually on the card.
            for (int i = work.Count - 1; i >= 0; i--)
            {
                var trimmed = work[i].Trim();
                if (!trimmed.StartsWith("secmon=", StringComparison.OrdinalIgnoreCase)) continue;

                var target = trimmed["secmon=".Length..].Trim();
                var full = Path.Combine(sdRoot, target.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    work.RemoveAt(i);
                    removed++;
                }
            }

            // 2) If the custom exosphere IS present, make sure every Atmosphère boot entry
            //    uses it. Stock/OFW entries are deliberately left alone — they're meant to
            //    boot as close to unmodified as possible.
            if (exospherePresent)
            {
                bool isBootEntry = work.Any(l => l.TrimStart().StartsWith("pkg3=", StringComparison.OrdinalIgnoreCase));
                bool isStock = work.Any(l => l.Trim().Equals("stock=1", StringComparison.OrdinalIgnoreCase));
                bool hasSecmon = work.Any(l => l.TrimStart().StartsWith("secmon=", StringComparison.OrdinalIgnoreCase));

                if (isBootEntry && !isStock && !hasSecmon)
                {
                    // Placed after the mode line rather than straight after pkg3, so
                    // emummcforce keeps its position directly beneath pkg3.
                    var anchor = -1;
                    for (var i = 0; i < work.Count; i++)
                    {
                        var key = work[i].TrimStart();
                        if (key.StartsWith("pkg3=", StringComparison.OrdinalIgnoreCase)
                            || key.StartsWith("kip1=", StringComparison.OrdinalIgnoreCase)
                            || key.StartsWith("emummcforce=", StringComparison.OrdinalIgnoreCase)
                            || key.StartsWith("emummc_force_disable=", StringComparison.OrdinalIgnoreCase))
                        {
                            anchor = i;
                        }
                    }

                    work.Insert(anchor + 1, $"secmon={SecmonTarget}");
                    added++;
                }
            }

            output.AddRange(work);
        }

        return new ProcessResult(output, removed, added);
    }
}
