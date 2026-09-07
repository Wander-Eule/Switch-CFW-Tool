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
/// It is written next to the executable rather than in AppData on purpose: deleting the
/// program folder then also removes the marker, so a fresh download offers the tour again,
/// which is the behaviour asked for. AppData is used only as a fallback for the case where
/// the program sits somewhere unwritable (a read-only share, Program Files without rights),
/// since failing to write there would otherwise mean the prompt returns on every start.
/// </summary>
public static class FirstRun
{
    private const string MarkerName = "quicktour.done";

    private static string? NextToExecutable
    {
        get
        {
            try
            {
                var exe = Environment.ProcessPath;
                var dir = exe == null ? null : Path.GetDirectoryName(exe);
                return dir == null ? null : Path.Combine(dir, MarkerName);
            }
            catch
            {
                return null;
            }
        }
    }

    private static string InAppData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Switch-CFW-Tool", MarkerName);

    public static bool ShouldOfferTour()
    {
        try
        {
            var local = NextToExecutable;
            if (local != null && File.Exists(local)) return false;
            return !File.Exists(InAppData);
        }
        catch
        {
            // If the check itself fails, don't nag: treat it as already offered.
            return false;
        }
    }

    public static void MarkOffered()
    {
        var local = NextToExecutable;
        if (local != null && TryWrite(local)) return;

        TryWrite(InAppData);
    }

    private static bool TryWrite(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join(Environment.NewLine, new[]
            {
                "Switch-CFW-Tool",
                "",
                "This file only records that the Quick Tour has already been offered.",
                "Delete it to be asked again on the next start.",
                "",
                "Nothing else is stored here. A GitHub token entered under Debug > GitHub Token",
                "is kept separately in:",
                @"    %AppData%\Switch-CFW-Tool\github_token.bin",
                "encrypted for the current Windows account, so it never travels with the",
                "program if you pass the .exe on to someone else.",
            }));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
