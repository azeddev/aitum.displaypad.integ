using OpenBaseCamp.App.Device;
using OpenBaseCamp.App.Platform;
using OpenBaseCamp.Core.Actions;
using OpenBaseCamp.Core.Config;
using OpenBaseCamp.Core.Devices;
using OpenBaseCamp.Core.Integrations.Aitum;
using OpenBaseCamp.Core.Integrations.Obs;
using OpenBaseCamp.Core.Integrations.Spotify;
using OpenBaseCamp.Core.Integrations.Twitch;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Services;

namespace OpenBaseCamp.App.ViewModels;

/// <summary>Composition root: builds every service and wires them together.</summary>
public sealed class AppHost : IDisposable
{
    private readonly System.Timers.Timer _foregroundTimer;
    private string? _lastForeground;
    private bool _disposed;

    public AppHost(string? configDirectory = null)
    {
        Store = new ConfigStore(configDirectory);
        Config = Store.Load();

        Hardware = PlatformFactory.CreateHardware(Path.Combine(Path.GetTempPath(), "OpenBaseCamp"));
        Obs = new ObsWebSocketClient();
        Aitum = new AitumClient();
        Aitum.ApplySettings(Config.Settings.Aitum);

        Spotify = new SpotifyClient();
        Spotify.ApplySettings(Config.Settings.Spotify);

        Twitch = new TwitchClient();
        Twitch.ApplySettings(Config.Settings.Twitch);

        MediaSession = PlatformFactory.CreateMediaSession();

        Audio = PlatformFactory.CreateAudio();
        Metrics = PlatformFactory.CreateMetrics();

        Controller = new PadController(Hardware, Store, Config, Metrics, Audio, Obs, Aitum, Spotify, Twitch, MediaSession);

        Executor = new ActionExecutor(new ActionServices
        {
            Input = PlatformFactory.CreateInput(),
            Launcher = PlatformFactory.CreateLauncher(),
            System = PlatformFactory.CreateSystemCommands(),
            Audio = Audio,
            Device = Controller,
            Navigation = Controller,
            Obs = Obs,
            Aitum = Aitum,
            Spotify = Spotify,
            Twitch = Twitch,
            MediaSession = MediaSession,
            ReportError = message => Error?.Invoke(message),
        });

        Controller.AttachExecutor(Executor);
        Controller.Error += message => Error?.Invoke(message);
        Hardware.Log += message => Log?.Invoke(message);

        _foregroundTimer = new System.Timers.Timer(750) { AutoReset = true };
        _foregroundTimer.Elapsed += (_, _) => PollForeground();
    }

    public ConfigStore Store { get; }

    public AppConfig Config { get; }

    public IPadHardware Hardware { get; }

    public ObsWebSocketClient Obs { get; }

    public AitumClient Aitum { get; }

    public SpotifyClient Spotify { get; }

    public TwitchClient Twitch { get; }

    public IMediaSessionController MediaSession { get; }

    public IAudioController Audio { get; }

    public ISystemMetricsProvider Metrics { get; }

    public PadController Controller { get; }

    public ActionExecutor Executor { get; }

    public event Action<string>? Error;

    public event Action<string>? Log;

    public MainViewModel CreateMainViewModel() => new(this);

    public async Task StartAsync()
    {
        try
        {
            await Hardware.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"The DisplayPad SDK could not be started: {ex.Message}");
        }

        if (Config.Settings.Obs.Enabled)
        {
            _ = Obs.ApplySettingsAsync(Config.Settings.Obs);
        }

        await MediaSession.StartAsync().ConfigureAwait(false);

        Controller.Start();
        Controller.InvalidateAll();
        _foregroundTimer.Start();
    }

    private void PollForeground()
    {
        var name = PlatformFactory.GetForegroundProcessName();
        if (name is null || string.Equals(name, _lastForeground, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastForeground = name;
        Controller.OnForegroundApplicationChanged(name);
    }

    public void SaveConfig() => Store.Save(Config);

    /// <summary>Saves, releases the device and terminates the process.</summary>
    public void Shutdown()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            SaveConfig();
        }
        catch (Exception)
        {
            // A failed save must not prevent shutdown.
        }

        Dispose();
        Hardware.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _foregroundTimer.Stop();
        _foregroundTimer.Dispose();
        Executor.StopAll();
        Controller.Dispose();
        Aitum.Dispose();
        Spotify.Dispose();
        Twitch.Dispose();
        (MediaSession as IDisposable)?.Dispose();
        _ = Obs.DisposeAsync().AsTask();
        (Audio as IDisposable)?.Dispose();
        (Metrics as IDisposable)?.Dispose();
    }
}
