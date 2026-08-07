using SkiaSharp;

namespace OpenBaseCamp.Core.Rendering;

public sealed record IconDefinition(string Id, string Name, string Category);

/// <summary>
/// The bundled icon set. Icons are drawn procedurally on a 24x24 canvas rather than shipped
/// as bitmaps, so the app has no binary assets and icons stay crisp at any size.
/// </summary>
public static class BuiltInIcons
{
    private const float G = 24f;

    private static readonly Dictionary<string, Action<SKCanvas, SKPaint, SKPaint>> Drawers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IconDefinition> Definitions = new();

    public static IReadOnlyList<IconDefinition> All => Definitions;

    public static bool Contains(string id) => Drawers.ContainsKey(id);

    static BuiltInIcons() => Register();

    /// <summary>Draws icon <paramref name="id"/> scaled into <paramref name="bounds"/>.</summary>
    public static bool Draw(SKCanvas canvas, string id, SKRect bounds, SKColor color)
    {
        if (!Drawers.TryGetValue(id, out var drawer))
        {
            return false;
        }

        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = color,
        };

        var scale = Math.Min(bounds.Width, bounds.Height) / G;
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        var count = canvas.Save();
        canvas.Translate(bounds.MidX - G * scale / 2f, bounds.MidY - G * scale / 2f);
        canvas.Scale(scale);
        drawer(canvas, fill, stroke);
        canvas.RestoreToCount(count);
        return true;
    }

    private static void Add(string id, string name, string category, Action<SKCanvas, SKPaint, SKPaint> drawer)
    {
        Drawers[id] = drawer;
        Definitions.Add(new IconDefinition(id, name, category));
    }

    private static void RoundRect(SKCanvas c, SKPaint p, float x, float y, float w, float h, float r) =>
        c.DrawRoundRect(new SKRect(x, y, x + w, y + h), r, r, p);

    private static void Triangle(SKCanvas c, SKPaint p, SKPoint a, SKPoint b, SKPoint d)
    {
        using var path = new SKPath();
        path.MoveTo(a);
        path.LineTo(b);
        path.LineTo(d);
        path.Close();
        c.DrawPath(path, p);
    }

    private static void Chevron(SKCanvas c, SKPaint stroke, float cx, float cy, float size, float rotationDegrees)
    {
        var count = c.Save();
        c.Translate(cx, cy);
        c.RotateDegrees(rotationDegrees);
        using var path = new SKPath();
        path.MoveTo(size / 2f, -size);
        path.LineTo(-size / 2f, 0);
        path.LineTo(size / 2f, size);
        c.DrawPath(path, stroke);
        c.RestoreToCount(count);
    }

    private static void Bars(SKCanvas c, SKPaint fill, params float[] heights)
    {
        var width = 3.2f;
        var gap = (G - 4 - heights.Length * width) / Math.Max(1, heights.Length - 1);
        var x = 2f;
        foreach (var h in heights)
        {
            RoundRect(c, fill, x, 20 - h, width, h, 1.2f);
            x += width + gap;
        }
    }

    private static void Register()
    {
        // ---- Navigation -------------------------------------------------------
        Add("folder", "Folder", "Navigation", (c, fill, _) =>
        {
            using var path = new SKPath();
            path.MoveTo(2.5f, 6f);
            path.LineTo(9.5f, 6f);
            path.LineTo(11.5f, 8.5f);
            path.LineTo(21.5f, 8.5f);
            path.LineTo(21.5f, 19f);
            path.LineTo(2.5f, 19f);
            path.Close();
            c.DrawPath(path, fill);
        });

        Add("back", "Back", "Navigation", (c, _, stroke) =>
        {
            stroke.StrokeWidth = 2.4f;
            c.DrawLine(20f, 12f, 5f, 12f, stroke);
            Chevron(c, stroke, 8f, 12f, 5f, 0f);
        });

        Add("home", "Home", "Navigation", (c, fill, _) =>
        {
            using var roof = new SKPath();
            roof.MoveTo(12f, 2.5f);
            roof.LineTo(22.5f, 11.5f);
            roof.LineTo(1.5f, 11.5f);
            roof.Close();
            c.DrawPath(roof, fill);
            RoundRect(c, fill, 4.5f, 11f, 15f, 10.5f, 1.5f);
        });

        Add("arrow-up", "Arrow Up", "Navigation", (c, _, stroke) => Chevron(c, stroke, 12f, 12f, 5f, 90f));
        Add("arrow-down", "Arrow Down", "Navigation", (c, _, stroke) => Chevron(c, stroke, 12f, 12f, 5f, -90f));
        Add("arrow-left", "Arrow Left", "Navigation", (c, _, stroke) => Chevron(c, stroke, 12f, 12f, 5f, 0f));
        Add("arrow-right", "Arrow Right", "Navigation", (c, _, stroke) => Chevron(c, stroke, 12f, 12f, 5f, 180f));

        Add("profile", "Profile", "Navigation", (c, fill, stroke) =>
        {
            c.DrawCircle(12f, 9f, 4f, fill);
            using var path = new SKPath();
            path.AddArc(new SKRect(4f, 12f, 20f, 26f), 180f, 180f);
            stroke.StrokeWidth = 3f;
            c.DrawPath(path, stroke);
        });

        // ---- Media ------------------------------------------------------------
        Add("play", "Play", "Media", (c, fill, _) =>
            Triangle(c, fill, new SKPoint(7f, 4.5f), new SKPoint(20f, 12f), new SKPoint(7f, 19.5f)));

        Add("pause", "Pause", "Media", (c, fill, _) =>
        {
            RoundRect(c, fill, 6.5f, 5f, 4f, 14f, 1.2f);
            RoundRect(c, fill, 13.5f, 5f, 4f, 14f, 1.2f);
        });

        Add("play-pause", "Play / Pause", "Media", (c, fill, _) =>
        {
            Triangle(c, fill, new SKPoint(3f, 5f), new SKPoint(13f, 12f), new SKPoint(3f, 19f));
            RoundRect(c, fill, 15f, 5f, 3f, 14f, 1f);
            RoundRect(c, fill, 20f, 5f, 3f, 14f, 1f);
        });

        Add("stop", "Stop", "Media", (c, fill, _) => RoundRect(c, fill, 5.5f, 5.5f, 13f, 13f, 2f));

        Add("next", "Next Track", "Media", (c, fill, _) =>
        {
            Triangle(c, fill, new SKPoint(5f, 5f), new SKPoint(15f, 12f), new SKPoint(5f, 19f));
            RoundRect(c, fill, 16.5f, 5f, 3f, 14f, 1f);
        });

        Add("previous", "Previous Track", "Media", (c, fill, _) =>
        {
            Triangle(c, fill, new SKPoint(19f, 5f), new SKPoint(9f, 12f), new SKPoint(19f, 19f));
            RoundRect(c, fill, 4.5f, 5f, 3f, 14f, 1f);
        });

        Add("record", "Record", "Media", (c, fill, _) => c.DrawCircle(12f, 12f, 7f, fill));

        Add("volume-up", "Volume Up", "Media", (c, fill, stroke) =>
        {
            SpeakerBody(c, fill);
            stroke.StrokeWidth = 1.8f;
            c.DrawArc(new SKRect(9f, 6f, 19f, 18f), -55f, 110f, false, stroke);
            c.DrawArc(new SKRect(11f, 3f, 24f, 21f), -55f, 110f, false, stroke);
        });

        Add("volume-down", "Volume Down", "Media", (c, fill, stroke) =>
        {
            SpeakerBody(c, fill);
            stroke.StrokeWidth = 1.8f;
            c.DrawArc(new SKRect(9f, 6f, 19f, 18f), -55f, 110f, false, stroke);
        });

        Add("volume-mute", "Mute", "Media", (c, fill, stroke) =>
        {
            SpeakerBody(c, fill);
            stroke.StrokeWidth = 2f;
            c.DrawLine(13.5f, 8.5f, 21f, 15.5f, stroke);
            c.DrawLine(21f, 8.5f, 13.5f, 15.5f, stroke);
        });

        Add("microphone", "Microphone", "Media", (c, fill, stroke) =>
        {
            RoundRect(c, fill, 9.5f, 3f, 5f, 11f, 2.5f);
            stroke.StrokeWidth = 1.8f;
            c.DrawArc(new SKRect(6.5f, 7.5f, 17.5f, 17.5f), 0f, 180f, false, stroke);
            c.DrawLine(12f, 17.5f, 12f, 21f, stroke);
        });

        Add("microphone-off", "Microphone Muted", "Media", (c, fill, stroke) =>
        {
            RoundRect(c, fill, 9.5f, 3f, 5f, 11f, 2.5f);
            stroke.StrokeWidth = 1.8f;
            c.DrawArc(new SKRect(6.5f, 7.5f, 17.5f, 17.5f), 0f, 180f, false, stroke);
            c.DrawLine(12f, 17.5f, 12f, 21f, stroke);
            stroke.StrokeWidth = 2.2f;
            c.DrawLine(4f, 3.5f, 20f, 20.5f, stroke);
        });

        Add("camera", "Camera", "Media", (c, fill, _) =>
        {
            RoundRect(c, fill, 2.5f, 7f, 13f, 10f, 2f);
            using var path = new SKPath();
            path.MoveTo(17f, 12f);
            path.LineTo(21.5f, 8f);
            path.LineTo(21.5f, 16f);
            path.Close();
            c.DrawPath(path, fill);
        });

        Add("stream", "Stream", "Media", (c, fill, stroke) =>
        {
            c.DrawCircle(12f, 12f, 3f, fill);
            stroke.StrokeWidth = 1.8f;
            c.DrawArc(new SKRect(5f, 5f, 19f, 19f), 30f, 120f, false, stroke);
            c.DrawArc(new SKRect(5f, 5f, 19f, 19f), 210f, 120f, false, stroke);
            c.DrawArc(new SKRect(1.5f, 1.5f, 22.5f, 22.5f), 30f, 120f, false, stroke);
            c.DrawArc(new SKRect(1.5f, 1.5f, 22.5f, 22.5f), 210f, 120f, false, stroke);
        });

        Add("scene", "Scene", "Media", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            RoundRect(c, stroke, 2.5f, 4.5f, 19f, 15f, 2f);
            using var path = new SKPath();
            path.MoveTo(4.5f, 17.5f);
            path.LineTo(9.5f, 10.5f);
            path.LineTo(13.5f, 15f);
            path.LineTo(16f, 12f);
            path.LineTo(19.5f, 17.5f);
            path.Close();
            c.DrawPath(path, fill);
        });

        // ---- System monitoring ------------------------------------------------
        Add("cpu", "CPU", "Monitoring", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            RoundRect(c, stroke, 5.5f, 5.5f, 13f, 13f, 2f);
            RoundRect(c, fill, 9.5f, 9.5f, 5f, 5f, 1f);
            for (var i = 0; i < 3; i++)
            {
                var o = 8.5f + i * 3.5f;
                c.DrawLine(o, 5.5f, o, 2.5f, stroke);
                c.DrawLine(o, 18.5f, o, 21.5f, stroke);
                c.DrawLine(5.5f, o, 2.5f, o, stroke);
                c.DrawLine(18.5f, o, 21.5f, o, stroke);
            }
        });

        Add("gpu", "GPU", "Monitoring", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            RoundRect(c, stroke, 2.5f, 6.5f, 19f, 11f, 2f);
            c.DrawCircle(9f, 12f, 3.2f, fill);
            RoundRect(c, fill, 14f, 9.5f, 5f, 5f, 1f);
        });

        Add("ram", "Memory", "Monitoring", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            RoundRect(c, stroke, 2.5f, 8f, 19f, 8f, 1.5f);
            for (var i = 0; i < 4; i++)
            {
                RoundRect(c, fill, 5f + i * 4.2f, 10f, 2.6f, 4f, 0.6f);
            }
        });

        Add("disk", "Disk", "Monitoring", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            c.DrawCircle(12f, 12f, 9f, stroke);
            c.DrawCircle(12f, 12f, 2.4f, fill);
        });

        Add("network", "Network", "Monitoring", (c, fill, stroke) =>
        {
            Bars(c, fill, 4f, 7f, 10f, 13f, 16f);
            stroke.StrokeWidth = 0f;
        });

        Add("chart", "Chart", "Monitoring", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 2f;
            using var path = new SKPath();
            path.MoveTo(3f, 17f);
            path.LineTo(9f, 10f);
            path.LineTo(13f, 14f);
            path.LineTo(21f, 5f);
            c.DrawPath(path, stroke);
            c.DrawCircle(21f, 5f, 1.6f, fill);
        });

        Add("clock", "Clock", "Monitoring", (c, _, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            c.DrawCircle(12f, 12f, 9f, stroke);
            c.DrawLine(12f, 12f, 12f, 6.5f, stroke);
            c.DrawLine(12f, 12f, 16f, 14f, stroke);
        });

        Add("calendar", "Calendar", "Monitoring", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            RoundRect(c, stroke, 3.5f, 5f, 17f, 15f, 2f);
            RoundRect(c, fill, 3.5f, 5f, 17f, 4f, 2f);
            c.DrawLine(8f, 3f, 8f, 6f, stroke);
            c.DrawLine(16f, 3f, 16f, 6f, stroke);
        });

        // ---- Actions ----------------------------------------------------------
        Add("keyboard", "Keyboard", "Actions", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            RoundRect(c, stroke, 2f, 6.5f, 20f, 11f, 2f);
            for (var row = 0; row < 2; row++)
            {
                for (var col = 0; col < 5; col++)
                {
                    RoundRect(c, fill, 4.4f + col * 3.3f, 9f + row * 3f, 2f, 2f, 0.5f);
                }
            }

            RoundRect(c, fill, 7.5f, 15f, 9f, 1.8f, 0.8f);
        });

        Add("mouse", "Mouse", "Actions", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            RoundRect(c, stroke, 7f, 3f, 10f, 18f, 5f);
            RoundRect(c, fill, 11.2f, 6.5f, 1.6f, 4f, 0.8f);
        });

        Add("macro", "Macro", "Actions", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            c.DrawCircle(6f, 12f, 2.4f, fill);
            c.DrawCircle(12f, 12f, 2.4f, fill);
            c.DrawCircle(18f, 12f, 2.4f, fill);
            c.DrawLine(8.4f, 12f, 9.6f, 12f, stroke);
            c.DrawLine(14.4f, 12f, 15.6f, 12f, stroke);
        });

        Add("text", "Text", "Actions", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 2.2f;
            c.DrawLine(4f, 6f, 20f, 6f, stroke);
            c.DrawLine(12f, 6f, 12f, 19f, stroke);
            RoundRect(c, fill, 8f, 18f, 8f, 1.8f, 0.8f);
        });

        Add("app", "Application", "Actions", (c, fill, _) =>
        {
            RoundRect(c, fill, 3f, 3f, 8f, 8f, 1.6f);
            RoundRect(c, fill, 13f, 3f, 8f, 8f, 1.6f);
            RoundRect(c, fill, 3f, 13f, 8f, 8f, 1.6f);
            RoundRect(c, fill, 13f, 13f, 8f, 8f, 1.6f);
        });

        Add("file", "File", "Actions", (c, fill, _) =>
        {
            using var path = new SKPath();
            path.MoveTo(5.5f, 2.5f);
            path.LineTo(14f, 2.5f);
            path.LineTo(19f, 7.5f);
            path.LineTo(19f, 21.5f);
            path.LineTo(5.5f, 21.5f);
            path.Close();
            c.DrawPath(path, fill);
        });

        Add("globe", "Website", "Actions", (c, _, stroke) =>
        {
            stroke.StrokeWidth = 1.7f;
            c.DrawCircle(12f, 12f, 9f, stroke);
            c.DrawLine(3f, 12f, 21f, 12f, stroke);
            c.DrawOval(new SKRect(7.5f, 3f, 16.5f, 21f), stroke);
        });

        Add("terminal", "Terminal", "Actions", (c, _, stroke) =>
        {
            stroke.StrokeWidth = 1.8f;
            RoundRect(c, stroke, 2.5f, 4.5f, 19f, 15f, 2f);
            using var path = new SKPath();
            path.MoveTo(6.5f, 9f);
            path.LineTo(10f, 12f);
            path.LineTo(6.5f, 15f);
            c.DrawPath(path, stroke);
            c.DrawLine(12.5f, 15.5f, 17.5f, 15.5f, stroke);
        });

        Add("gear", "Settings", "Actions", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 3.4f;
            c.DrawCircle(12f, 12f, 5.5f, stroke);
            for (var i = 0; i < 8; i++)
            {
                var count = c.Save();
                c.Translate(12f, 12f);
                c.RotateDegrees(i * 45f);
                RoundRect(c, fill, -1.5f, -11f, 3f, 4f, 1f);
                c.RestoreToCount(count);
            }

            c.DrawCircle(12f, 12f, 2.6f, fill);
        });

        Add("power", "Power", "Actions", (c, _, stroke) =>
        {
            stroke.StrokeWidth = 2.2f;
            c.DrawArc(new SKRect(4f, 4.5f, 20f, 20.5f), -60f, 300f, false, stroke);
            c.DrawLine(12f, 2.5f, 12f, 10.5f, stroke);
        });

        Add("lock", "Lock", "Actions", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 2f;
            c.DrawArc(new SKRect(7f, 4f, 17f, 14f), 180f, 180f, false, stroke);
            RoundRect(c, fill, 5f, 10.5f, 14f, 10f, 2f);
        });

        Add("restart", "Restart", "Actions", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 2.2f;
            c.DrawArc(new SKRect(4f, 4f, 20f, 20f), -40f, 300f, false, stroke);
            // Arrow head sitting on the open end of the arc (top right, pointing clockwise).
            Triangle(c, fill, new SKPoint(18.2f, 1.5f), new SKPoint(18.2f, 10.5f), new SKPoint(23f, 6f));
        });

        Add("sleep", "Sleep", "Actions", (c, fill, _) =>
        {
            using var disc = new SKPath();
            disc.AddCircle(11f, 12f, 9.2f);
            using var bite = new SKPath();
            bite.AddCircle(17.5f, 8f, 8.6f);
            using var moon = disc.Op(bite, SKPathOp.Difference);
            c.DrawPath(moon, fill);
        });

        Add("brightness", "Brightness", "Actions", (c, fill, stroke) =>
        {
            c.DrawCircle(12f, 12f, 4.5f, fill);
            stroke.StrokeWidth = 1.9f;
            for (var i = 0; i < 8; i++)
            {
                var count = c.Save();
                c.Translate(12f, 12f);
                c.RotateDegrees(i * 45f);
                c.DrawLine(0f, -7.5f, 0f, -10f, stroke);
                c.RestoreToCount(count);
            }
        });

        Add("plus", "Plus", "Actions", (c, _, stroke) =>
        {
            stroke.StrokeWidth = 2.6f;
            c.DrawLine(12f, 5f, 12f, 19f, stroke);
            c.DrawLine(5f, 12f, 19f, 12f, stroke);
        });

        Add("minus", "Minus", "Actions", (c, _, stroke) =>
        {
            stroke.StrokeWidth = 2.6f;
            c.DrawLine(5f, 12f, 19f, 12f, stroke);
        });

        Add("cross", "Close", "Actions", (c, _, stroke) =>
        {
            stroke.StrokeWidth = 2.6f;
            c.DrawLine(6f, 6f, 18f, 18f, stroke);
            c.DrawLine(18f, 6f, 6f, 18f, stroke);
        });

        Add("check", "Check", "Actions", (c, _, stroke) =>
        {
            stroke.StrokeWidth = 2.8f;
            using var path = new SKPath();
            path.MoveTo(4.5f, 12.5f);
            path.LineTo(10f, 18f);
            path.LineTo(19.5f, 6.5f);
            c.DrawPath(path, stroke);
        });

        Add("star", "Star", "Actions", (c, fill, _) =>
        {
            using var path = new SKPath();
            for (var i = 0; i < 10; i++)
            {
                var radius = i % 2 == 0 ? 9.5f : 4.2f;
                var angle = -Math.PI / 2 + i * Math.PI / 5;
                var x = 12f + (float)(Math.Cos(angle) * radius);
                var y = 12f + (float)(Math.Sin(angle) * radius);
                if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
            }

            path.Close();
            c.DrawPath(path, fill);
        });

        Add("bolt", "Trigger", "Actions", (c, fill, _) =>
        {
            using var path = new SKPath();
            path.MoveTo(13.5f, 2f);
            path.LineTo(5f, 13.5f);
            path.LineTo(11f, 13.5f);
            path.LineTo(10f, 22f);
            path.LineTo(19f, 10f);
            path.LineTo(13f, 10f);
            path.Close();
            c.DrawPath(path, fill);
        });

        Add("layers", "Layers", "Actions", (c, fill, stroke) =>
        {
            stroke.StrokeWidth = 1.7f;
            using var top = new SKPath();
            top.MoveTo(12f, 3f);
            top.LineTo(21.5f, 8f);
            top.LineTo(12f, 13f);
            top.LineTo(2.5f, 8f);
            top.Close();
            c.DrawPath(top, fill);
            using var path = new SKPath();
            path.MoveTo(2.5f, 12.5f);
            path.LineTo(12f, 17.5f);
            path.LineTo(21.5f, 12.5f);
            c.DrawPath(path, stroke);
            using var path2 = new SKPath();
            path2.MoveTo(2.5f, 16.5f);
            path2.LineTo(12f, 21.5f);
            path2.LineTo(21.5f, 16.5f);
            c.DrawPath(path2, stroke);
        });

        Add("blank", "Blank", "Actions", (_, _, _) => { });
    }

    private static void SpeakerBody(SKCanvas c, SKPaint fill)
    {
        using var path = new SKPath();
        path.MoveTo(3f, 9.5f);
        path.LineTo(6.5f, 9.5f);
        path.LineTo(11f, 5.5f);
        path.LineTo(11f, 18.5f);
        path.LineTo(6.5f, 14.5f);
        path.LineTo(3f, 14.5f);
        path.Close();
        c.DrawPath(path, fill);
    }
}
