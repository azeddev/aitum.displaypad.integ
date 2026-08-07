using Avalonia;

namespace OpenBaseCamp.App;

internal static class Program
{
    /// <summary>True when launched by the Windows "Run" entry, which starts us minimised.</summary>
    public static bool StartMinimizedRequested { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartMinimizedRequested = args.Any(a =>
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/minimized", StringComparison.OrdinalIgnoreCase));

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
