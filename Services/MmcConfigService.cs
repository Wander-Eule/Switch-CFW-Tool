using System.IO;
using System.Linq;

namespace SwitchCfwTool.Services;

public enum MmcMode
{
    SysMMC,
    EmuMMC
}

/// <summary>
/// Writes the exosphere.ini and the Atmosphère DNS-MITM host list so Nintendo's servers
/// are blocked (avoiding an online ban), based on the chosen SysMMC/EmuMMC mode.
///
/// Background:
/// - exosphere.ini's "emummc_force_disable" / blank_prodinfo_* section controls NAND
///   selection and serial blanking.
/// - Atmosphère's DNS-MITM module supports "*" as a wildcard (per the official
///   docs/features/dns_mitm.md), which is what the entries below rely on. The specific
///   host list and the 95.216.149.205 "fake success" IP for the conntest/ctest endpoints
///   (so the console still thinks it has connectivity instead of just timing out) is the
///   long-standing community-verified list used across rentry/GBAtemp CFW guides.
/// </summary>
public class MmcConfigService
{
    private static readonly string[] BlockedHosts = new[]
    {
        "127.0.0.1 *nintendo.*",
        "127.0.0.1 *nintendo-europe.com",
        "127.0.0.1 *nintendoswitch.*",
        "95.216.149.205 *conntest.nintendowifi.net",
        "95.216.149.205 *ctest.cdn.nintendo.net",
    };

    public void WriteExosphereIni(string sdRoot, MmcMode mode)
    {
        Directory.CreateDirectory(sdRoot);
        var path = Path.Combine(sdRoot, "exosphere.ini");

        // blank_prodinfo_* controls serial-number blanking (ban protection): the console
        // presents a blanked identity to software instead of its real serial. Blanking is
        // tied to whichever mode is actually selected/active, so ban protection applies no
        // matter which mode you're running CFW on. Note this affects the real NAND when
        // SysMMC is chosen and can break amiibo / local wireless play — that's an accepted
        // trade-off here since SysMMC mode means CFW runs directly on the real NAND anyway.
        var lines = new List<string>
        {
            "[exosphere]",
            "debugmode=0",
            "debugmode_user=0",
            $"emummc_force_disable={(mode == MmcMode.SysMMC ? 1 : 0)}",
            $"blank_prodinfo_sysmmc={(mode == MmcMode.SysMMC ? 1 : 0)}",
            $"blank_prodinfo_emummc={(mode == MmcMode.EmuMMC ? 1 : 0)}",
        };

        File.WriteAllLines(path, lines);
    }

    public void WriteDnsMitmBlocklist(string sdRoot, MmcMode mode)
    {
        var dir = Path.Combine(sdRoot, "atmosphere", "hosts");
        Directory.CreateDirectory(dir);

        // Atmosphère's docs call keeping default.txt untouched and using a mode-specific
        // file the desirable configuration: DNS.mitm picks exactly ONE hosts file, checking
        // emummc.txt / sysmmc.txt before falling back to default.txt. Since the MMC mode is
        // already known here, the mode-specific file is written instead of overwriting the
        // shared default. Atmosphère prepends its own built-in telemetry redirects to
        // whichever file it loads, so nothing is lost by not duplicating them here.
        var fileName = mode == MmcMode.EmuMMC ? "emummc.txt" : "sysmmc.txt";
        var path = Path.Combine(dir, fileName);

        // Existing user-added custom entries are preserved: read what's there first,
        // merge, de-duplicate, so re-running the builder doesn't wipe manual additions.
        var existing = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();

        var merged = existing
            .Concat(new[] { "# Block Nintendo Servers" })
            .Concat(BlockedHosts)
            .Distinct()
            .ToList();

        File.WriteAllLines(path, merged);
    }
}
