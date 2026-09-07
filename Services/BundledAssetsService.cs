using System.Collections.Generic;
using System.IO;

namespace SwitchCfwTool.Services;

/// <summary>
/// Extracts bundled assets (embedded into the exe itself, no external files needed) into
/// the SDMC output folder: the hekate_ipl.ini template + bootloader/res icons for Hekate.
/// They are never overwritten once present, so the user's own boot
/// entries and edits stay intact — the same "don't touch existing user config" rule the
/// SdCardBuilder applies elsewhere.
/// </summary>
public class BundledAssetsService
{
    private static readonly string[] ResIcons =
    {
        "icon_payload.bmp",
        "icon_switch.bmp",
        "icon_payload_nokip.bmp",
        "lockpick.bmp",
        "tegra.bmp",
    };

    /// <summary>
    /// Writes the bundled boot configuration and icons.
    /// </summary>
    /// <returns>
    /// The ini files this call actually created. Only those may be filtered afterwards —
    /// an ini the user already had stays exactly as they left it.
    /// </returns>
    public List<string> ExtractHekateAssets(string sdRoot)
    {
        var created = new List<string>();

        var bootloaderDir = Path.Combine(sdRoot, "bootloader");
        var resDir = Path.Combine(bootloaderDir, "res");
        Directory.CreateDirectory(resDir);

        var iniPath = Path.Combine(bootloaderDir, "hekate_ipl.ini");
        if (!File.Exists(iniPath))
        {
            ExtractResourceToFile("hekate_ipl.ini", iniPath);
            created.Add(iniPath);
        }

        var extraIniDir = Path.Combine(bootloaderDir, "ini");
        Directory.CreateDirectory(extraIniDir);
        var extraIniPath = Path.Combine(extraIniDir, "hekate_ipl.ini");
        if (!File.Exists(extraIniPath))
        {
            ExtractResourceToFile("hekate_ipl_extra.ini", extraIniPath);
            created.Add(extraIniPath);
        }

        foreach (var icon in ResIcons)
        {
            var destPath = Path.Combine(resDir, icon);
            if (!File.Exists(destPath))
                ExtractResourceToFile(icon, destPath);
        }

        return created;
    }

    /// <summary>Reads a bundled text resource, used to restore a boot entry that was removed.</summary>
    public static string ReadTextResource(string logicalName)
    {
        using var stream = EmbeddedResource.Open(logicalName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void ExtractResourceToFile(string logicalName, string destPath)
    {
        using var stream = EmbeddedResource.Open(logicalName);
        using var fileStream = File.Create(destPath);
        stream.CopyTo(fileStream);
    }
}
