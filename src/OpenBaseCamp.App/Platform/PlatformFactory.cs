using OpenBaseCamp.App.Device;
using OpenBaseCamp.Core.Devices;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Services;

#if WINDOWS
using OpenBaseCamp.App.Services.Win32;
#endif

namespace OpenBaseCamp.App.Platform;

/// <summary>
/// Chooses the real Windows implementations or inert stand-ins. The shipping target is
/// <c>net8.0-windows</c>; the plain <c>net8.0</c> build exists so the UI can be compiled and
/// previewed on a machine without the DisplayPad SDK.
/// </summary>
public static class PlatformFactory
{
    public static bool IsWindows =>
#if WINDOWS
        OperatingSystem.IsWindows();
#else
        false;
#endif

    public static IPadHardware CreateHardware(string scratchDirectory)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            return DisplayPadService.Create(scratchDirectory);
        }
#endif
        _ = scratchDirectory;
        return new NullPadHardware();
    }

    public static IInputSimulator CreateInput()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            return new WindowsInputSimulator();
        }
#endif
        return new NullInputSimulator();
    }

    public static IProcessLauncher CreateLauncher()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            return new WindowsProcessLauncher();
        }
#endif
        return new NullProcessLauncher();
    }

    public static ISystemCommands CreateSystemCommands()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSystemCommands();
        }
#endif
        return new NullSystemCommands();
    }

    public static IAudioController CreateAudio()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            return new WindowsAudioController();
        }
#endif
        return new NullAudioController();
    }

    public static IMediaSessionController CreateMediaSession()
    {
#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return new WindowsMediaSessionController();
        }
#endif
        return new NullMediaSessionController();
    }

    public static ISystemMetricsProvider CreateMetrics()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            return new WindowsMetricsProvider();
        }
#endif
        return new NullMetricsProvider();
    }

    public static bool AutoStartEnabled
    {
        get
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                return AutoStart.IsEnabled();
            }
#endif
            return false;
        }
    }

    public static bool TrySetAutoStart(bool enabled, out string? error)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            return AutoStart.TrySet(enabled, out error);
        }
#endif
        _ = enabled;
        error = "Starting with Windows is only available on Windows.";
        return false;
    }

    public static string? GetForegroundProcessName()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            return ForegroundApplicationWatcher.GetForegroundProcessName();
        }
#endif
        return null;
    }
}

internal sealed class NullInputSimulator : IInputSimulator
{
    public void KeyDown(int virtualKey) { }

    public void KeyUp(int virtualKey) { }

    public void TapKey(int virtualKey) { }

    public void SendHotkey(HotkeyModifiers modifiers, int virtualKey) { }

    public void TypeText(string text) { }

    public void MouseButton(int button, bool down) { }

    public void MouseClick(int button) { }

    public void MouseWheel(int delta) { }

    public void MouseMove(int deltaX, int deltaY) { }
}

internal sealed class NullProcessLauncher : IProcessLauncher
{
    public void Launch(string path, string? arguments, string? workingDirectory, bool asAdministrator) { }

    public void OpenUrl(string url) { }

    public void OpenPath(string path) { }
}

internal sealed class NullSystemCommands : ISystemCommands
{
    public void Execute(SystemCommandKind command) { }
}

internal sealed class NullAudioController : IAudioController
{
    private float _level = 0.42f;
    private bool _muted;

    public float GetVolume() => _level;

    public void SetVolume(float level) => _level = Math.Clamp(level, 0f, 1f);

    public void StepVolume(int percentPoints) => SetVolume(_level + percentPoints / 100f);

    public bool GetMute() => _muted;

    public void SetMute(bool muted) => _muted = muted;

    public void ToggleMute() => _muted = !_muted;
}

internal sealed class NullMetricsProvider : ISystemMetricsProvider
{
    public SystemMetrics Sample() => new(23, 47, 15.1, 32, 61, 68, 412, 8400, 1250);
}
