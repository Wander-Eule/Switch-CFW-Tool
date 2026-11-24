using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SwitchCfwTool.Services;

/// <summary>
/// Handles the bundled Ultrahand wallpapers.
///
/// Ultrahand expects 448x720 raw .rgba files. Two locations matter: everything in
/// config/ultrahand/wallpapers/ shows up in the on-console Wallpaper Manager, while
/// config/ultrahand/wallpaper.rgba is the one actually displayed. Both are written, so the
/// pick takes effect immediately and the others stay switchable from the console.
/// </summary>
public static class UltrahandWallpaper
{
    public const int Width = 448;
    public const int Height = 720;

    private const string LibraryFolder = "config/ultrahand/wallpapers";
    private const string ActiveFile = "config/ultrahand/wallpaper.rgba";

    /// <summary>The bundled files, each with the language key of the name shown in the picker.</summary>
    public static readonly (string Resource, string LabelKey)[] Bundled =
    {
        ("wallpaper_circles.rgba", "Theming.Wallpaper.Circles"),
        ("wallpaper_squares.rgba", "Theming.Wallpaper.Squares"),
        ("wallpaper_triangles.rgba", "Theming.Wallpaper.Triangles"),
    };

    /// <summary>Turns the raw pixel data into something Avalonia can show.</summary>
    public static Bitmap LoadPreview(string resourceName)
    {
        var raw = ReadResource(resourceName);
        var bitmap = new WriteableBitmap(
            new PixelSize(Width, Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using var fb = bitmap.Lock();
        var srcStride = Width * 4;
        if (fb.RowBytes == srcStride)
        {
            Marshal.Copy(raw, 0, fb.Address, raw.Length);
        }
        else
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(raw, y * srcStride, fb.Address + y * fb.RowBytes, srcStride);
        }
        return bitmap;
    }

    /// <summary>
    /// Installs exactly one wallpaper. Any previously applied bundled wallpaper is removed
    /// again, so switching between them doesn't quietly leave the earlier picks behind on
    /// the card — only the current selection is ever present.
    /// </summary>
    public static void Apply(string sdRoot, string resourceName)
    {
        var libraryDir = Path.Combine(sdRoot, LibraryFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(libraryDir);

        foreach (var (resource, _) in Bundled)
        {
            var stale = Path.Combine(libraryDir, resource);
            if (resource != resourceName && File.Exists(stale))
                File.Delete(stale);
        }

        var raw = ReadResource(resourceName);
        File.WriteAllBytes(Path.Combine(libraryDir, resourceName), raw);

        var active = Path.Combine(sdRoot, ActiveFile.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(active)!);
        File.WriteAllBytes(active, raw);
    }

    /// <summary>Removes the active wallpaper along with the bundled copies it came from.</summary>
    public static void Clear(string sdRoot)
    {
        var active = Path.Combine(sdRoot, ActiveFile.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(active)) File.Delete(active);

        var libraryDir = Path.Combine(sdRoot, LibraryFolder.Replace('/', Path.DirectorySeparatorChar));
        foreach (var (resource, _) in Bundled)
        {
            var file = Path.Combine(libraryDir, resource);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>Which bundled wallpaper is currently active, if any.</summary>
    public static string? DetectActive(string sdRoot)
    {
        var active = Path.Combine(sdRoot, ActiveFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(active)) return null;

        var current = File.ReadAllBytes(active);
        foreach (var (resource, _) in Bundled)
        {
            if (current.AsSpan().SequenceEqual(ReadResource(resource)))
                return resource;
        }

        return null;   // a wallpaper the user supplied themselves
    }

    private static byte[] ReadResource(string logicalName)
    {
        using var stream = EmbeddedResource.Open(logicalName);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
