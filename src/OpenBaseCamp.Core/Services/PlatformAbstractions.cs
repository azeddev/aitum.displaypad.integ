using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Services;

/// <summary>Synthesises keyboard and mouse input.</summary>
public interface IInputSimulator
{
    void KeyDown(int virtualKey);

    void KeyUp(int virtualKey);

    void TapKey(int virtualKey);

    void SendHotkey(HotkeyModifiers modifiers, int virtualKey);

    void TypeText(string text);

    /// <summary>1 = left, 2 = right, 3 = middle, 4 = X1, 5 = X2.</summary>
    void MouseButton(int button, bool down);

    void MouseClick(int button);

    /// <summary>Positive scrolls up.</summary>
    void MouseWheel(int delta);

    void MouseMove(int deltaX, int deltaY);
}

public interface IProcessLauncher
{
    void Launch(string path, string? arguments, string? workingDirectory, bool asAdministrator);

    void OpenUrl(string url);

    void OpenPath(string path);
}

public interface ISystemCommands
{
    void Execute(SystemCommandKind command);
}

public interface IAudioController
{
    /// <summary>Master output volume, 0..1. Returns -1 when unavailable.</summary>
    float GetVolume();

    void SetVolume(float level);

    void StepVolume(int percentPoints);

    bool GetMute();

    void SetMute(bool muted);

    void ToggleMute();
}

public readonly record struct SystemMetrics(
    double CpuPercent,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double GpuPercent,
    double DiskUsedPercent,
    double DiskFreeGb,
    double NetworkDownKbps,
    double NetworkUpKbps)
{
    public static SystemMetrics Empty => new(0, 0, 0, 0, -1, 0, 0, 0, 0);
}

public interface ISystemMetricsProvider
{
    SystemMetrics Sample();
}

/// <summary>Device-level controls an action can reach.</summary>
public interface IPadDeviceControl
{
    int Brightness { get; }

    Task SetBrightnessAsync(int percent);

    Task SetSleepTimerAsync(bool enabled, int hours, int minutes, int seconds);
}

/// <summary>Page and profile navigation driven from a key press.</summary>
public interface IPadNavigation
{
    Task OpenPageAsync(string pageId);

    Task GoBackAsync();

    Task GoHomeAsync();

    Task SwitchProfileAsync(string profileId);

    Task NextProfileAsync();

    Task PreviousProfileAsync();
}

/// <summary>Everything <see cref="Actions.ActionExecutor"/> needs, in one place.</summary>
public sealed class ActionServices
{
    public required IInputSimulator Input { get; init; }

    public required IProcessLauncher Launcher { get; init; }

    public required ISystemCommands System { get; init; }

    public required IAudioController Audio { get; init; }

    public required IPadDeviceControl Device { get; init; }

    public required IPadNavigation Navigation { get; init; }

    public required Integrations.Obs.ObsWebSocketClient Obs { get; init; }

    public required Integrations.Aitum.AitumClient Aitum { get; init; }

    public required Integrations.Spotify.SpotifyClient Spotify { get; init; }

    public required Integrations.Twitch.TwitchClient Twitch { get; init; }

    public required IMediaSessionController MediaSession { get; init; }

    /// <summary>Surfaces a problem to the user without throwing out of a key press.</summary>
    public Action<string>? ReportError { get; init; }
}
