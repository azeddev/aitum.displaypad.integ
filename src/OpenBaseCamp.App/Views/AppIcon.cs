using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace OpenBaseCamp.App.Views;

/// <summary>
/// Draws the window and tray icon at runtime, so the repository carries no binary assets.
/// It is a miniature DisplayPad: a 4x3 grid of rounded keys.
/// </summary>
public static class AppIcon
{
    public static byte[] RenderPng(int size = 64)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            var scale = size / 64f;
            using var body = new SKPaint { IsAntialias = true, Color = new SKColor(0x1C, 0x1C, 0x24) };
            canvas.DrawRoundRect(new SKRect(1 * scale, 16 * scale, 63 * scale, 48 * scale), 6 * scale, 6 * scale, body);

            var colors = new[]
            {
                new SKColor(0x4C, 0x9A, 0xFF), new SKColor(0x3D, 0xD5, 0x8C),
                new SKColor(0xFF, 0xB0, 0x3A), new SKColor(0xFF, 0x5C, 0x8A),
            };

            using var key = new SKPaint { IsAntialias = true };
            for (var row = 0; row < 2; row++)
            {
                for (var column = 0; column < 6; column++)
                {
                    var x = (4 + column * 10) * scale;
                    var y = (20 + row * 12) * scale;
                    key.Color = colors[(row + column) % colors.Length].WithAlpha(row == 0 ? (byte)255 : (byte)190);
                    canvas.DrawRoundRect(new SKRect(x, y, x + 8 * scale, y + 8 * scale), 2f * scale, 2f * scale, key);
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static WindowIcon CreateWindowIcon()
    {
        using var stream = new MemoryStream(RenderPng(128));
        return new WindowIcon(new Bitmap(stream));
    }
}
