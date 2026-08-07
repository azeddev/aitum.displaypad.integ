using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OpenBaseCamp.App.Services.Win32;

/// <summary>Registers the app in HKCU\...\Run so it can start with Windows.</summary>
[SupportedOSPlatform("windows")]
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenBaseCamp";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool TrySet(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                error = "The Windows startup registry key could not be opened.";
                return false;
            }

            if (enabled)
            {
                var path = GetExecutablePath();
                if (path is null)
                {
                    error = "The application path could not be determined.";
                    return false;
                }

                key.SetValue(ValueName, $"\"{path}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            return path;
        }

        return Process.GetCurrentProcess().MainModule?.FileName;
    }
}
