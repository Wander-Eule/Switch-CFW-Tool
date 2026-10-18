using System;
using System.Collections.Generic;
using System.IO;

namespace SwitchCfwTool.Services;

public record IntegrityProblem(string Message, bool IsFatal);

/// <summary>
/// Verifies that the files a console actually needs to boot really made it onto the card.
///
/// Worth doing because the failure mode is so unhelpful: Hekate loads its menu from files
/// that are almost certainly fine, but every boot entry then fails against files that are
/// missing or truncated — and it does so without printing anything, which looks identical to
/// a broken console or a bad SD card. Checking here turns that into a plain message.
///
/// Hekate's own documentation marks bootloader/sys/ as important; it holds the emuMMC
/// module, the sleep-mode and DRAM modules, Nyx itself and the Tsec keygen. Atmosphère's
/// package3 carries the kernel, exosphere and warmboot, so a partial copy of it breaks every
/// entry at once, stock included.
/// </summary>
public static class BootIntegrityCheck
{
    private static readonly string[] HekateSysFiles =
    {
        "emummc.kipm",
        "libsys_lp0.bso",
        "libsys_minerva.bso",
        "nyx.bin",
        "res.pak",
    };

    /// <summary>package3 carries a whole firmware stack; anything this small cannot be one.</summary>
    private const long MinimumPackage3Bytes = 1_000_000;

    public static List<IntegrityProblem> Run(string sdRoot, bool hekateInstalled, bool atmosphereInstalled)
    {
        var problems = new List<IntegrityProblem>();

        if (hekateInstalled)
        {
            var sysDir = Path.Combine(sdRoot, "bootloader", "sys");

            if (!Directory.Exists(sysDir))
            {
                problems.Add(new IntegrityProblem(Loc.T("Boot.HekateSysMissing"), true));
            }
            else
            {
                var missing = new List<string>();
                foreach (var file in HekateSysFiles)
                {
                    if (!File.Exists(Path.Combine(sysDir, file)))
                        missing.Add(file);
                }

                if (missing.Count > 0)
                {
                    problems.Add(new IntegrityProblem(
                        Loc.T("Boot.HekateSysIncomplete", string.Join(", ", missing)), true));
                }
            }

            if (!File.Exists(Path.Combine(sdRoot, "payload.bin")))
            {
                problems.Add(new IntegrityProblem(Loc.T("Boot.PayloadMissing"), false));
            }
        }

        if (atmosphereInstalled)
        {
            var package3 = Path.Combine(sdRoot, "atmosphere", "package3");

            if (!File.Exists(package3))
            {
                problems.Add(new IntegrityProblem(Loc.T("Boot.Package3Missing"), true));
            }
            else
            {
                var size = new FileInfo(package3).Length;
                if (size < MinimumPackage3Bytes)
                {
                    problems.Add(new IntegrityProblem(Loc.T("Boot.Package3TooSmall", size / 1024), true));
                }
            }

            if (!Directory.Exists(Path.Combine(sdRoot, "atmosphere", "contents")))
            {
                problems.Add(new IntegrityProblem(Loc.T("Boot.ContentsMissing"), false));
            }
        }

        return problems;
    }
}
