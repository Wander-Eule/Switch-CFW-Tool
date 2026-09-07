using System;
using System.IO;

namespace SwitchCfwTool.Services;

public enum TargetKind
{
    /// <summary>A folder to assemble in; an SDMC folder is created inside it.</summary>
    PcFolder,

    /// <summary>The root of a removable drive; files go straight in.</summary>
    SdCard,

    /// <summary>A drive root that isn't reported as removable — the user has to say which it is.</summary>
    Ambiguous
}

public record TargetInfo(TargetKind Kind, string? FileSystem, long TotalBytes, string? Label);

/// <summary>
/// Works out whether a chosen path is an SD card to write into directly or a folder to build
/// in and copy across later.
///
/// Deliberately built on the drive information Windows already exposes rather than on any
/// device querying: it needs no elevation, touches nothing, and cannot be mistaken for the
/// kind of low-level disk access that security software objects to.
///
/// A path only counts as a card when it is the root of a drive Windows reports as removable.
/// A drive root that reports as fixed is reported as ambiguous rather than guessed at —
/// built-in card readers often present cards as fixed disks, and treating a second hard disk
/// as an SD card would scatter CFW files across it.
/// </summary>
public static class TargetDetection
{
    public static TargetInfo Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new TargetInfo(TargetKind.PcFolder, null, 0, null);

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);

            // Anything below a drive root is a folder, whatever it sits on.
            if (root == null || !string.Equals(
                    full.TrimEnd(Path.DirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return new TargetInfo(TargetKind.PcFolder, null, 0, null);
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return new TargetInfo(TargetKind.PcFolder, null, 0, null);

            var info = new TargetInfo(
                drive.DriveType == DriveType.Removable ? TargetKind.SdCard : TargetKind.Ambiguous,
                drive.DriveFormat,
                drive.TotalSize,
                string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel);

            // The system drive is never a card, and asking about it would only invite a
            // dangerous answer.
            var windows = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.Equals(root, windows, StringComparison.OrdinalIgnoreCase))
                return info with { Kind = TargetKind.PcFolder };

            return info;
        }
        catch
        {
            // Unreadable or exotic path: fall back to treating it as a folder, which only
            // ever creates a subfolder and can't disturb anything.
            return new TargetInfo(TargetKind.PcFolder, null, 0, null);
        }
    }

    /// <summary>True when the drive uses a file system a Switch can read.</summary>
    public static bool IsSwitchCompatible(string? fileSystem)
        => fileSystem is not null
           && (fileSystem.Equals("FAT32", StringComparison.OrdinalIgnoreCase)
               || fileSystem.Equals("exFAT", StringComparison.OrdinalIgnoreCase));
}
