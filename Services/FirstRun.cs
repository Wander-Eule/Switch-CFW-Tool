using System;
using System.IO;

namespace SwitchCfwTool.Services;

/// <summary>
/// Remembers whether the Quick Tour has already been offered.
///
/// A program cannot remember anything across restarts without writing somewhere — the
/// process keeps nothing once it exits, and its own executable can't be modified while it
/// runs. So a marker is unavoidable; the only real choice is where it lives.
///
/// It goes in AppData, with the GitHub token and the language choice, so that the program's
/// own folder stays nothing but the exe and the licence. The trade-off is deliberate: having
/// already seen the tour is a fact about the person, not about one copy of the program, so
/// updating or re-extracting doesn't replay it. Help → Quick Tour replays it on demand, which
/// is a better way to get it back than deleting a file.
/// </summary>
public static class FirstRun
{
    private static readonly string MarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Switch-CFW-Tool", "quicktour.done");

    public static bool ShouldOfferTour()
    {
        try
        {
            return !File.Exists(MarkerPath);
        }
        catch
        {
            // If the check itself fails, don't nag: treat it as already offered.
            return false;
        }
    }

    public static void MarkOffered()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, string.Join(Environment.NewLine, new[]
            {
                "Switch-CFW-Tool",
                "",
                "This file only records that the Quick Tour has already been offered.",
                "Delete it to be asked again on the next start, or just pick",
                "Help > Quick Tour in the program.",
            }));
        }
        catch
        {
            // Failing to write it only means the prompt comes back next start. Annoying,
            // but not worth interrupting the launch over.
        }
    }
}
