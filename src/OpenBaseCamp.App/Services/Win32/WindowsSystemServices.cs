using System.Diagnostics;
using System.Runtime.Versioning;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Services;
using static OpenBaseCamp.App.Services.Win32.NativeMethods;

namespace OpenBaseCamp.App.Services.Win32;

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessLauncher : IProcessLauncher
{
    public void Launch(string path, string? arguments, string? workingDirectory, bool asAdministrator)
    {
        var info = new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(path),
            UseShellExecute = true,
        };

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            info.Arguments = arguments;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            info.WorkingDirectory = Environment.ExpandEnvironmentVariables(workingDirectory);
        }
        else
        {
            var directory = Path.GetDirectoryName(info.FileName);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                info.WorkingDirectory = directory;
            }
        }

        if (asAdministrator)
        {
            info.Verb = "runas";
        }

        Process.Start(info);
    }

    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo
    {
        FileName = url,
        UseShellExecute = true,
    });

    public void OpenPath(string path) => Process.Start(new ProcessStartInfo
    {
        FileName = Environment.ExpandEnvironmentVariables(path),
        UseShellExecute = true,
    });
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemCommands : ISystemCommands
{
    public void Execute(SystemCommandKind command)
    {
        switch (command)
        {
            case SystemCommandKind.Shutdown:
                RunShell("shutdown", "/s /t 0");
                break;

            case SystemCommandKind.Restart:
                RunShell("shutdown", "/r /t 0");
                break;

            case SystemCommandKind.LogOff:
                RunShell("shutdown", "/l");
                break;

            case SystemCommandKind.Sleep:
                // Hibernate must be off for this to actually suspend rather than hibernate.
                SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
                break;

            case SystemCommandKind.Hibernate:
                SetSuspendState(hibernate: true, forceCritical: false, disableWakeEvent: false);
                break;

            case SystemCommandKind.LockWorkstation:
                LockWorkStation();
                break;

            case SystemCommandKind.TurnOffDisplays:
                SendMessage(HwndBroadcast, WmSysCommand, new IntPtr(ScMonitorPower), new IntPtr(2));
                break;

            case SystemCommandKind.EmptyRecycleBin:
                SHEmptyRecycleBin(IntPtr.Zero, null, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);
                break;

            case SystemCommandKind.OpenTaskManager:
                Process.Start(new ProcessStartInfo { FileName = "taskmgr.exe", UseShellExecute = true });
                break;
        }
    }

    private static void RunShell(string file, string arguments) => Process.Start(new ProcessStartInfo
    {
        FileName = file,
        Arguments = arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
    });
}

/// <summary>Watches which application is in the foreground so profiles can follow it.</summary>
[SupportedOSPlatform("windows")]
public sealed class ForegroundApplicationWatcher : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private string? _current;

    public ForegroundApplicationWatcher(int pollMs = 750)
    {
        _timer = new System.Timers.Timer(pollMs) { AutoReset = true };
        _timer.Elapsed += (_, _) => Poll();
    }

    /// <summary>Raised with the executable name (e.g. "obs64.exe") when the foreground app changes.</summary>
    public event Action<string>? ForegroundChanged;

    public string? Current => _current;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        var name = GetForegroundProcessName();
        if (name is null || string.Equals(name, _current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _current = name;
        ForegroundChanged?.Invoke(name);
    }

    public static string? GetForegroundProcessName()
    {
        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(handle, out var pid);
            if (pid == 0)
            {
                return null;
            }

            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            return string.IsNullOrEmpty(name) ? null : name + ".exe";
        }
        catch (Exception)
        {
            // The window can vanish between the two calls; that is not worth reporting.
            return null;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
