using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SwitchCfwTool.Services;

public record ExistingCfwInfo(bool Found, List<string> Parts);

/// <summary>
/// Spots a CFW that is already on a card.
///
/// Only the few landmarks every install has are checked — the bootloader, the firmware
/// package and the sysmodule folder. That is enough to tell "this card is already set up"
/// from "this card is empty", which is all the warning needs. Working out which tool owns
/// which file is a different problem, and one the install record exists to solve.
/// </summary>
public static class ExistingCfw
{
    private static readonly (string Path, bool IsDirectory, string Name)[] Landmarks =
    {
        ("atmosphere/package3", false, "Atmosphère"),
        ("bootloader/sys", true, "Hekate"),
        ("atmosphere/contents", true, "sysmodules"),
        ("payload.bin", false, "a boot payload"),
    };

    public static ExistingCfwInfo Detect(string cardRoot)
    {
        var found = new List<string>();

        foreach (var (relative, isDirectory, name) in Landmarks)
        {
            var full = Path.Combine(cardRoot, relative.Replace('/', Path.DirectorySeparatorChar));

            var present = isDirectory
                ? Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()
                : File.Exists(full);

            if (present) found.Add(name);
        }

        return new ExistingCfwInfo(found.Count > 0, found);
    }
}
