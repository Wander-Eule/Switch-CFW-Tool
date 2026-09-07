namespace SwitchCfwTool.Data;

/// <summary>
/// Maps creator names as they appear in a tool's credit line to their GitHub profile, so
/// the Info window can turn each recognised name into a link. Kept separate from the tool
/// catalog because one person appears in several tools' credits (and as both original
/// author and forker), so the mapping belongs to the person, not the tool.
/// </summary>
public static class CreatorLinks
{
    public static readonly Dictionary<string, string> ByName = new()
    {
        ["CTCaer"] = "https://github.com/CTCaer",
        ["WerWolv"] = "https://github.com/WerWolv",
        ["SciresM"] = "https://github.com/SciresM",
        ["TuxSH"] = "https://github.com/TuxSH",
        ["hexkyz"] = "https://github.com/hexkyz",
        ["ITotalJustice"] = "https://github.com/ITotalJustice",
        ["impeeza"] = "https://github.com/impeeza",
        ["borntohonk"] = "https://github.com/borntohonk",
        ["ppkantorski"] = "https://github.com/ppkantorski",
        ["masagrator"] = "https://github.com/masagrator",
        ["ndeadly"] = "https://github.com/ndeadly",
        ["Souldbminer"] = "https://github.com/Souldbminer",
        ["dominatorul"] = "https://github.com/dominatorul",
        ["Lightos1"] = "https://github.com/Lightos1",
        ["shchmue"] = "https://github.com/shchmue",
        ["Kofysh"] = "https://github.com/Kofysh",
        ["suchmememanyskill"] = "https://github.com/suchmememanyskill",
        ["NaGaa95"] = "https://github.com/NaGaa95",
        ["rashevskyv"] = "https://github.com/rashevskyv",
        ["Morce3232"] = "https://github.com/Morce3232",
        ["BernardoGiordano"] = "https://github.com/BernardoGiordano",
        ["J-D-K"] = "https://github.com/J-D-K",
        ["averne"] = "https://github.com/averne",
        ["dantiicu"] = "https://github.com/dantiicu",
        ["XorTroll"] = "https://github.com/XorTroll",
        ["tomvita"] = "https://github.com/tomvita",
        ["exelix11"] = "https://github.com/exelix11",
        ["tallbl0nde"] = "https://github.com/tallbl0nde",
        ["zdm65477730"] = "https://github.com/zdm65477730",
        ["nadrino"] = "https://github.com/nadrino",
        ["rdmrocha"] = "https://github.com/rdmrocha",
        ["proconsule"] = "https://github.com/proconsule",
        ["meganukebmp"] = "https://github.com/meganukebmp",
        ["XITRIX"] = "https://github.com/XITRIX",
        ["proferabg"] = "https://github.com/proferabg",
        ["RetroNX Team"] = "https://github.com/retronx-team",
        ["NicholeMattera"] = "https://github.com/NicholeMattera",
    };
}
