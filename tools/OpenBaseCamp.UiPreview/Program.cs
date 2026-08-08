using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using OpenBaseCamp.App.ViewModels;
using OpenBaseCamp.App.Views;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.UiPreview;

/// <summary>
/// Boots the real view models against a headless Avalonia platform and writes screenshots
/// of every window, so the interface can be checked without a DisplayPad or a display.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var output = args.FirstOrDefault() ?? "preview";
        Directory.CreateDirectory(output);

        var configDirectory = Path.Combine(Path.GetTempPath(), "OpenBaseCampPreview", Guid.NewGuid().ToString("N"));

        AppBuilder
            .Configure<App.App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var host = new AppHost(configDirectory);
        var main = host.CreateMainViewModel();

        var mainWindow = new MainWindow { DataContext = main };
        main.AttachWindow(mainWindow);
        Capture(mainWindow, Path.Combine(output, "01-main.png"));

        // Select a key so the inspector shows a fully populated action.
        main.SelectedKey = main.Keys[9];
        Capture(mainWindow, Path.Combine(output, "02-main-hotkey-selected.png"));

        main.SelectedKey = main.Keys[6];
        Capture(mainWindow, Path.Combine(output, "03-main-monitor-selected.png"));

        var settings = new SettingsWindow { DataContext = new SettingsViewModel(main) };
        Capture(settings, Path.Combine(output, "04-settings.png"));

        if (settings.FindControl<TabControl>("Tabs") is { } tabs)
        {
            tabs.SelectedIndex = 3; // Spotify
            Capture(settings, Path.Combine(output, "04b-settings-spotify.png"));
        }

        // Bind a key to Spotify so the new inspector section is exercised.
        var spotifySlot = host.Controller.CurrentPage.Keys[3];
        spotifySlot.Action = new KeyAction
        {
            Kind = ActionKind.Spotify,
            Settings = { SpotifyCommand = SpotifyCommand.PlayContext, SpotifyUri = "spotify:playlist:37i9dQZF1DXcBWIGoYBM5M" },
        };
        spotifySlot.Appearance.IconId = "play";
        spotifySlot.Appearance.Title = "Focus";
        main.RefreshKeys();
        main.SelectedKey = main.Keys[3];
        Capture(mainWindow, Path.Combine(output, "03b-main-spotify-selected.png"));

        var editor = new KeyEditorViewModel(main, main.Keys[0].Slot!);
        var macro = new MacroEditorWindow { DataContext = new MacroEditorViewModel(main, editor) };
        Capture(macro, Path.Combine(output, "05-macro.png"));

        var multi = new MultiActionWindow { DataContext = new MultiActionViewModel(main, editor) };
        Capture(multi, Path.Combine(output, "06-multiaction.png"));

        // A key face at the exact size the pad receives, for pixel level review.
        File.WriteAllBytes(Path.Combine(output, "07-key-102px.png"),
            Core.Rendering.KeyImageRenderer.RenderPng(host.Controller.BuildRequest(
                host.Controller.CurrentPage.Keys[6], DisplayPadLayout.KeyPixels)));

        // A now-playing key with stand-in album art, at device size.
        File.WriteAllBytes(Path.Combine(output, "08-nowplaying-102px.png"),
            Core.Rendering.KeyImageRenderer.RenderPng(new Core.Rendering.KeyRenderRequest
            {
                Appearance = new KeyAppearance
                {
                    BackgroundColor = "#FF101014",
                    Title = "Chvrches — The Mother We Share",
                    TitleSize = 13,
                    IconFit = IconFit.Cover,
                },
                ImageOverride = FakeAlbumArt(),
                Size = DisplayPadLayout.KeyPixels,
                IsActive = true,
            }));

        host.Dispose();
        Console.WriteLine($"Wrote screenshots to {Path.GetFullPath(output)}");
        return 0;
    }

    /// <summary>A stand-in cover so artwork rendering can be reviewed without a Spotify account.</summary>
    private static byte[] FakeAlbumArt()
    {
        using var bitmap = new SkiaSharp.SKBitmap(300, 300);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            using var shader = SkiaSharp.SKShader.CreateLinearGradient(
                new SkiaSharp.SKPoint(0, 0),
                new SkiaSharp.SKPoint(300, 300),
                new[] { new SkiaSharp.SKColor(0x1D, 0xB9, 0x54), new SkiaSharp.SKColor(0x10, 0x3A, 0x8A) },
                null,
                SkiaSharp.SKShaderTileMode.Clamp);
            using var paint = new SkiaSharp.SKPaint { Shader = shader };
            canvas.DrawRect(new SkiaSharp.SKRect(0, 0, 300, 300), paint);

            using var disc = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0, 0, 0, 90), IsAntialias = true };
            canvas.DrawCircle(150, 150, 92, disc);
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void Capture(Window window, string path)
    {
        window.Show();

        // Let bindings, layout and the first render pass settle.
        for (var i = 0; i < 12; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine($"No frame captured for {path}");
            return;
        }

        frame.Save(path);
        Console.WriteLine($"  {Path.GetFileName(path)}  {frame.PixelSize.Width}x{frame.PixelSize.Height}");
        window.Hide();
    }
}
