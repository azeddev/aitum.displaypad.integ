using OpenBaseCamp.Core.Model;
using SkiaSharp;

namespace OpenBaseCamp.Core.Rendering;

/// <summary>
/// Paints a key face. The same output is used for the on-screen preview and for the
/// bitmap uploaded to the pad, so what you see in the editor is what the LCD shows.
/// </summary>
public static class KeyImageRenderer
{
    private static readonly SKColor Accent = new(0x4C, 0x9A, 0xFF);

    public static SKBitmap Render(KeyRenderRequest request)
    {
        var size = Math.Max(16, request.Size);
        var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        Render(canvas, size, request);
        canvas.Flush();
        return bitmap;
    }

    public static byte[] RenderPng(KeyRenderRequest request)
    {
        using var bitmap = Render(request);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void Render(SKCanvas canvas, int size, KeyRenderRequest request)
    {
        var appearance = request.Appearance;
        var scale = size / (float)DisplayPadLayout.KeyPixels;

        canvas.Clear(ParseColor(appearance.BackgroundColor, new SKColor(0x10, 0x10, 0x14)));

        var title = request.TitleOverride ?? appearance.Title;
        var hasTitle = appearance.ShowTitle && !string.IsNullOrWhiteSpace(title);
        var hasValue = !string.IsNullOrWhiteSpace(request.ValueText);

        var content = new SKRect(0, 0, size, size);

        // The band has to be measured from the wrapped lines, otherwise a title that wraps
        // onto a second line is drawn past the bottom edge of the key.
        List<string>? lines = null;
        var lineHeight = 0f;
        var titleHeight = 0f;

        if (hasTitle)
        {
            using var measure = new SKPaint
            {
                Typeface = ResolveTypeface(appearance.TitleFont, appearance.TitleBold),
                TextSize = (float)appearance.TitleSize * scale,
            };

            lines = WrapText(title!, measure, size * 0.94f, maxLines: 2);
            lineHeight = measure.TextSize * 1.12f;
            titleHeight = Math.Min(size * 0.55f, lineHeight * lines.Count + measure.TextSize * 0.45f);

            content = appearance.TitlePosition switch
            {
                TitlePosition.Top => new SKRect(0, titleHeight, size, size),
                TitlePosition.Bottom => new SKRect(0, 0, size, size - titleHeight),
                _ => content,
            };
        }

        DrawIcon(canvas, content, request, scale, hasValue);

        if (hasValue)
        {
            DrawValue(canvas, content, request.ValueText!, scale, appearance);
        }

        if (request.Gauge is { } gauge)
        {
            DrawGauge(canvas, size, gauge, scale);
        }

        if (hasTitle && lines is not null)
        {
            DrawTitle(canvas, size, lines, appearance, scale, titleHeight, lineHeight);
        }

        if (request.IsActive)
        {
            DrawActiveBorder(canvas, size, scale);
        }
    }

    private static void DrawIcon(SKCanvas canvas, SKRect content, KeyRenderRequest request, float scale, bool hasValue)
    {
        var appearance = request.Appearance;
        var iconScale = (float)Math.Clamp(appearance.IconScale, 0.05, 1.0);

        // When a big live value is drawn the glyph shrinks and moves up out of its way.
        var box = Inset(content, iconScale * (hasValue ? 0.55f : 1f));
        if (hasValue)
        {
            box = new SKRect(box.Left, content.Top + content.Height * 0.06f, box.Right, content.Top + content.Height * 0.42f);
            var side = Math.Min(box.Width, box.Height);
            box = new SKRect(box.MidX - side / 2f, box.MidY - side / 2f, box.MidX + side / 2f, box.MidY + side / 2f);
        }

        var bitmap = LoadUserImage(request);
        if (bitmap is not null)
        {
            using (bitmap)
            {
                DrawBitmap(canvas, bitmap, box, content, appearance.IconFit);
            }

            return;
        }

        if (!string.IsNullOrEmpty(appearance.IconId))
        {
            var tint = ParseColor(appearance.IconTint, SKColors.White);
            BuiltInIcons.Draw(canvas, appearance.IconId!, box, tint);
        }
    }

    private static SKBitmap? LoadUserImage(KeyRenderRequest request)
    {
        if (request.ImageOverride is { Length: > 0 } bytes)
        {
            try
            {
                if (SKBitmap.Decode(bytes) is { } decoded)
                {
                    return decoded;
                }
            }
            catch (Exception)
            {
                // Fall through to the configured image.
            }
        }

        var file = request.Appearance.ImageFile;
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        var path = request.ResolveImagePath?.Invoke(file) ?? file;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return SKBitmap.Decode(stream);
        }
        catch (Exception)
        {
            // A missing or corrupt image must never take a key (or the whole pad) down.
            return null;
        }
    }

    private static void DrawBitmap(SKCanvas canvas, SKBitmap bitmap, SKRect box, SKRect content, IconFit fit)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return;
        }

        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };

        switch (fit)
        {
            case IconFit.Stretch:
                canvas.DrawBitmap(bitmap, box, paint);
                return;

            case IconFit.Cover:
            {
                var scale = Math.Max(content.Width / bitmap.Width, content.Height / bitmap.Height);
                var w = bitmap.Width * scale;
                var h = bitmap.Height * scale;
                var dest = new SKRect(content.MidX - w / 2f, content.MidY - h / 2f, content.MidX + w / 2f, content.MidY + h / 2f);
                canvas.Save();
                canvas.ClipRect(content);
                canvas.DrawBitmap(bitmap, dest, paint);
                canvas.Restore();
                return;
            }

            case IconFit.Center:
            {
                var dest = new SKRect(
                    box.MidX - bitmap.Width / 2f,
                    box.MidY - bitmap.Height / 2f,
                    box.MidX + bitmap.Width / 2f,
                    box.MidY + bitmap.Height / 2f);
                canvas.Save();
                canvas.ClipRect(content);
                canvas.DrawBitmap(bitmap, dest, paint);
                canvas.Restore();
                return;
            }

            default:
            {
                var scale = Math.Min(box.Width / bitmap.Width, box.Height / bitmap.Height);
                var w = bitmap.Width * scale;
                var h = bitmap.Height * scale;
                var dest = new SKRect(box.MidX - w / 2f, box.MidY - h / 2f, box.MidX + w / 2f, box.MidY + h / 2f);
                canvas.DrawBitmap(bitmap, dest, paint);
                return;
            }
        }
    }

    private static void DrawValue(SKCanvas canvas, SKRect content, string value, float scale, KeyAppearance appearance)
    {
        var color = ParseColor(appearance.TitleColor, SKColors.White);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Typeface = ResolveTypeface(appearance.TitleFont, bold: true),
            TextAlign = SKTextAlign.Center,
        };

        var maxWidth = content.Width * 0.92f;
        paint.TextSize = 34f * scale;
        while (paint.TextSize > 10f * scale && paint.MeasureText(value) > maxWidth)
        {
            paint.TextSize -= 1f * scale;
        }

        var baseline = content.Top + content.Height * 0.78f;

        using var outline = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 190),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.5f, 2.6f * scale),
            Typeface = paint.Typeface,
            TextSize = paint.TextSize,
            TextAlign = SKTextAlign.Center,
        };

        canvas.DrawText(value, content.MidX, baseline, outline);
        canvas.DrawText(value, content.MidX, baseline, paint);
    }

    private static void DrawTitle(
        SKCanvas canvas,
        int size,
        List<string> lines,
        KeyAppearance appearance,
        float scale,
        float bandHeight,
        float lineHeight)
    {
        var color = ParseColor(appearance.TitleColor, SKColors.White);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Typeface = ResolveTypeface(appearance.TitleFont, appearance.TitleBold),
            TextSize = (float)appearance.TitleSize * scale,
            TextAlign = SKTextAlign.Center,
        };

        var block = lineHeight * lines.Count;

        var top = appearance.TitlePosition switch
        {
            TitlePosition.Top => size * 0.02f,
            TitlePosition.Bottom => size - bandHeight + (bandHeight - block) / 2f,
            _ => (size - block) / 2f,
        };

        // Never let the last baseline fall outside the key.
        var maxTop = size - block - size * 0.02f;
        top = Math.Clamp(top, size * 0.02f, Math.Max(size * 0.02f, maxTop));

        using var outline = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 200),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.4f, 2.4f * scale),
            Typeface = paint.Typeface,
            TextSize = paint.TextSize,
            TextAlign = SKTextAlign.Center,
        };

        for (var i = 0; i < lines.Count; i++)
        {
            var baseline = top + lineHeight * i + paint.TextSize * 0.86f;
            if (appearance.TitleOutline)
            {
                canvas.DrawText(lines[i], size / 2f, baseline, outline);
            }

            canvas.DrawText(lines[i], size / 2f, baseline, paint);
        }
    }

    private static void DrawGauge(SKCanvas canvas, int size, double value, float scale)
    {
        var v = (float)Math.Clamp(value, 0, 1);
        var inset = 3.5f * scale;
        var rect = new SKRect(inset, inset, size - inset, size - inset);

        using var track = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f * scale,
            Color = new SKColor(255, 255, 255, 40),
            StrokeCap = SKStrokeCap.Round,
        };

        using var arc = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f * scale,
            Color = GaugeColor(v),
            StrokeCap = SKStrokeCap.Round,
        };

        canvas.DrawArc(rect, 135f, 270f, false, track);
        if (v > 0.001f)
        {
            canvas.DrawArc(rect, 135f, 270f * v, false, arc);
        }
    }

    private static SKColor GaugeColor(float v) => v switch
    {
        >= 0.9f => new SKColor(0xFF, 0x5C, 0x5C),
        >= 0.7f => new SKColor(0xFF, 0xB0, 0x3A),
        _ => Accent,
    };

    private static void DrawActiveBorder(SKCanvas canvas, int size, float scale)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f * scale,
            Color = Accent,
        };

        var inset = 2f * scale;
        canvas.DrawRoundRect(
            new SKRect(inset, inset, size - inset, size - inset),
            10f * scale,
            10f * scale,
            paint);
    }

    private static List<string> WrapText(string text, SKPaint paint, float maxWidth, int maxLines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (paint.MeasureText(candidate) <= maxWidth || current.Length == 0)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
                if (lines.Count == maxLines)
                {
                    break;
                }
            }
        }

        if (lines.Count < maxLines && current.Length > 0)
        {
            lines.Add(current);
        }

        if (lines.Count == 0)
        {
            lines.Add(text);
        }

        // Ellipsise anything that still overflows.
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (paint.MeasureText(line) <= maxWidth)
            {
                continue;
            }

            while (line.Length > 1 && paint.MeasureText(line + "…") > maxWidth)
            {
                line = line[..^1];
            }

            lines[i] = line + "…";
        }

        return lines;
    }

    private static readonly Dictionary<(string, bool), SKTypeface> TypefaceCache = new();

    private static SKTypeface ResolveTypeface(string family, bool bold)
    {
        lock (TypefaceCache)
        {
            var key = (family ?? string.Empty, bold);
            if (TypefaceCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            var typeface = SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                           ?? SKTypeface.Default;
            TypefaceCache[key] = typeface;
            return typeface;
        }
    }

    private static SKRect Inset(SKRect rect, float factor)
    {
        var w = rect.Width * factor;
        var h = rect.Height * factor;
        return new SKRect(rect.MidX - w / 2f, rect.MidY - h / 2f, rect.MidX + w / 2f, rect.MidY + h / 2f);
    }

    /// <summary>Parses <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    public static SKColor ParseColor(string? value, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var text = value.Trim().TrimStart('#');
        try
        {
            return text.Length switch
            {
                6 => new SKColor(
                    Convert.ToByte(text[..2], 16),
                    Convert.ToByte(text.Substring(2, 2), 16),
                    Convert.ToByte(text.Substring(4, 2), 16)),
                8 => new SKColor(
                    Convert.ToByte(text.Substring(2, 2), 16),
                    Convert.ToByte(text.Substring(4, 2), 16),
                    Convert.ToByte(text.Substring(6, 2), 16),
                    Convert.ToByte(text[..2], 16)),
                _ => fallback,
            };
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
