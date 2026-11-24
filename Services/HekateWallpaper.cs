using System;
using System.IO;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace SwitchCfwTool.Services;

/// <summary>
/// Writes a Nyx background in exactly the shape Hekate expects: 1280x720, 32-bit BMP with a
/// fully opaque alpha channel.
///
/// The bytes are written by hand rather than through an image encoder because encoders give
/// no guarantee about the bit depth or whether an alpha channel survives, and Hekate
/// documents both explicitly ("must be a 1280 x 720 32-bit BMP", alpha 0xFF for a solid
/// background). Producing the file directly removes that uncertainty.
/// </summary>
public static class HekateWallpaper
{
    public const int Width = 1280;
    public const int Height = 720;

    /// <summary>Scales and centre-crops the source to 1280x720, then writes the BMP.</summary>
    public static void Save(Bitmap source, string destinationPath)
    {
        var pixels = Rasterize(source);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var stream = File.Create(destinationPath);
        using var writer = new BinaryWriter(stream);

        const int headerSize = 14 + 40;
        var imageSize = Width * Height * 4;

        // BITMAPFILEHEADER
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(headerSize + imageSize);
        writer.Write(0);                 // reserved
        writer.Write(headerSize);        // pixel data offset

        // BITMAPINFOHEADER
        writer.Write(40);
        writer.Write(Width);
        writer.Write(Height);            // positive: rows stored bottom-up
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);                 // BI_RGB
        writer.Write(imageSize);
        writer.Write(2835);              // ~72 DPI
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        // Pixel data, bottom row first, BGRA order
        for (var y = Height - 1; y >= 0; y--)
        {
            var row = y * Width * 4;
            for (var x = 0; x < Width; x++)
            {
                var i = row + x * 4;
                writer.Write(pixels[i]);       // B
                writer.Write(pixels[i + 1]);   // G
                writer.Write(pixels[i + 2]);   // R
                writer.Write((byte)0xFF);      // A — opaque, as Hekate expects for a solid image
            }
        }
    }

    /// <summary>Fills the 1280x720 frame, cropping the overhanging side rather than distorting.</summary>
    private static byte[] Rasterize(Bitmap source)
    {
        using var mem = new MemoryStream();
        source.Save(mem);
        mem.Position = 0;
        using var skSource = SKBitmap.Decode(mem)
            ?? throw new InvalidOperationException("Failed to decode image.");

        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Failed to create drawing surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        var scale = Math.Max((float)Width / skSource.Width, (float)Height / skSource.Height);
        var scaledWidth = skSource.Width * scale;
        var scaledHeight = skSource.Height * scale;
        var destRect = SKRect.Create(
            (Width - scaledWidth) / 2f,
            (Height - scaledHeight) / 2f,
            scaledWidth,
            scaledHeight);

        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(skSource, destRect, paint);

        var buffer = new byte[Width * Height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var dstInfo = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            surface.ReadPixels(dstInfo, handle.AddrOfPinnedObject(), Width * 4, 0, 0);
        }
        finally
        {
            handle.Free();
        }
        return buffer;
    }
}
