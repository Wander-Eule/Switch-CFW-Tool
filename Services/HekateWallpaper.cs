using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SwitchCfwTool.Services;

/// <summary>
/// Writes a Nyx background in exactly the shape Hekate expects: 1280x720, 32-bit BMP with a
/// fully opaque alpha channel.
///
/// The bytes are written by hand rather than through BmpBitmapEncoder because the encoder
/// gives no guarantee about the bit depth or whether an alpha channel survives, and Hekate
/// documents both explicitly ("must be a 1280 x 720 32-bit BMP", alpha 0xFF for a solid
/// background). Producing the file directly removes that uncertainty.
/// </summary>
public static class HekateWallpaper
{
    public const int Width = 1280;
    public const int Height = 720;

    /// <summary>Scales and centre-crops the source to 1280x720, then writes the BMP.</summary>
    public static void Save(BitmapSource source, string destinationPath)
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
    private static byte[] Rasterize(BitmapSource source)
    {
        var scale = Math.Max((double)Width / source.PixelWidth, (double)Height / source.PixelHeight);
        var scaledWidth = source.PixelWidth * scale;
        var scaledHeight = source.PixelHeight * scale;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new System.Windows.Rect(0, 0, Width, Height));
            context.DrawImage(source, new System.Windows.Rect(
                (Width - scaledWidth) / 2,
                (Height - scaledHeight) / 2,
                scaledWidth,
                scaledHeight));
        }

        var target = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        var buffer = new byte[Width * Height * 4];
        converted.CopyPixels(buffer, Width * 4, 0);
        return buffer;
    }
}
