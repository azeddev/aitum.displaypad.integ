namespace OpenBaseCamp.Core.Model;

/// <summary>
/// Controls what the app does to the firmware's own key handling when it takes over.
/// The firmware can execute the bindings stored in its flash by itself; while OpenBaseCamp
/// drives the pad it normally wants those disabled so a press produces exactly one action.
/// </summary>
public enum FirmwareKeyMode
{
    /// <summary>Do not touch the setting (use whatever the device was left with).</summary>
    LeaveUnchanged,

    /// <summary>Call EnableKeyFunc(false) - only OpenBaseCamp reacts to presses.</summary>
    Disable,

    /// <summary>Call EnableKeyFunc(true) - the firmware also runs its stored bindings.</summary>
    Enable,
}

public sealed class ObsSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 4455;
    public string Password { get; set; } = string.Empty;
    public bool AutoReconnect { get; set; } = true;
}

public sealed class AitumSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 7777;
}

public sealed class DeviceSettings
{
    /// <summary>0/25/50/75/100 - the only values the firmware accepts.</summary>
    public int Brightness { get; set; } = 100;

    public bool SleepEnabled { get; set; }
    public int SleepHours { get; set; }
    public int SleepMinutes { get; set; } = 10;
    public int SleepSeconds { get; set; }

    public int Columns { get; set; } = DisplayPadLayout.DefaultColumns;

    public FirmwareKeyMode FirmwareKeyMode { get; set; } = FirmwareKeyMode.Disable;

    /// <summary>
    /// Maps the raw <c>wMatrix</c> value reported by the SDK to a 0..11 key index.
    /// Populated by the key-mapping wizard; empty means "use the built-in heuristic".
    /// </summary>
    public Dictionary<string, int> KeyMatrixMap { get; set; } = new();
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool AutoSwitchProfileByApp { get; set; } = true;

    /// <summary>How often live monitoring keys are repainted.</summary>
    public int MonitorRefreshMs { get; set; } = 1000;

    public DeviceSettings Device { get; set; } = new();
    public ObsSettings Obs { get; set; } = new();
    public AitumSettings Aitum { get; set; } = new();
}

/// <summary>Everything persisted to disk.</summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    public List<Profile> Profiles { get; set; } = new();

    public string ActiveProfileId { get; set; } = string.Empty;

    public AppSettings Settings { get; set; } = new();

    public Profile ActiveProfile =>
        Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles[0];

    public void Normalize()
    {
        if (Profiles.Count == 0)
        {
            Profiles.Add(DefaultProfileFactory.CreateStarterProfile());
        }

        foreach (var profile in Profiles)
        {
            profile.Normalize();
        }

        if (Profiles.All(p => p.Id != ActiveProfileId))
        {
            ActiveProfileId = Profiles[0].Id;
        }

        var device = Settings.Device;
        device.Brightness = BrightnessLevels.Snap(device.Brightness);
        if (device.Columns is not (1 or 2 or 3 or 4 or 6 or 12))
        {
            device.Columns = DisplayPadLayout.DefaultColumns;
        }

        Settings.MonitorRefreshMs = Math.Clamp(Settings.MonitorRefreshMs, 250, 60_000);
    }
}

public static class BrightnessLevels
{
    public static readonly int[] All = { 0, 25, 50, 75, 100 };

    public static int Snap(int value)
    {
        var best = All[0];
        foreach (var level in All)
        {
            if (Math.Abs(level - value) < Math.Abs(best - value))
            {
                best = level;
            }
        }

        return best;
    }

    public static int Step(int current, int direction)
    {
        var index = Array.IndexOf(All, Snap(current));
        return All[Math.Clamp(index + Math.Sign(direction), 0, All.Length - 1)];
    }

    public static int Cycle(int current)
    {
        var index = Array.IndexOf(All, Snap(current));
        return All[(index + 1) % All.Length];
    }
}
