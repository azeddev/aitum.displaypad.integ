namespace OpenBaseCamp.Core.Model;

/// <summary>A single recorded step of a macro.</summary>
public sealed class MacroEvent
{
    public MacroEventType Type { get; set; }

    /// <summary>Windows virtual key code for key events, mouse button index for mouse events.</summary>
    public int Code { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>Wheel delta (positive = up).</summary>
    public int Delta { get; set; }

    public string? Text { get; set; }

    /// <summary>Delay applied *after* this event, in milliseconds.</summary>
    public int DelayMs { get; set; }

    public MacroEvent Clone() => (MacroEvent)MemberwiseClone();
}

/// <summary>
/// Flat settings bag for every action kind. Only the fields relevant to
/// <see cref="KeyAction.Kind"/> are used; the rest stay null/default and are omitted
/// from the serialized config.
/// </summary>
public sealed class ActionSettings
{
    // Hotkey / macro-free key press
    public HotkeyModifiers Modifiers { get; set; }
    public int VirtualKey { get; set; }

    // Text
    public string? Text { get; set; }
    public bool PressEnterAfterText { get; set; }

    // Macro
    public List<MacroEvent> MacroEvents { get; set; } = new();
    public MacroPlayMode MacroPlayMode { get; set; } = MacroPlayMode.Once;
    public int MacroRepeatCount { get; set; } = 1;

    // Mouse / multimedia / volume / system / brightness
    public MouseCommand MouseCommand { get; set; }
    public MultimediaCommand MultimediaCommand { get; set; }
    public VolumeCommand VolumeCommand { get; set; }
    public int VolumeLevel { get; set; } = 50;
    public int VolumeStep { get; set; } = 2;
    public SystemCommandKind SystemCommand { get; set; }
    public BrightnessCommand BrightnessCommand { get; set; }

    /// <summary>Brightness percentage. The firmware only accepts 0/25/50/75/100.</summary>
    public int BrightnessLevel { get; set; } = 100;

    // Launching
    public string? Path { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public bool RunAsAdministrator { get; set; }
    public string? Url { get; set; }

    // Navigation
    public string? TargetPageId { get; set; }
    public string? TargetProfileId { get; set; }

    // Monitoring
    public MonitorMetric Metric { get; set; }
    public string? MetricArgument { get; set; }

    // Composition
    public List<KeyAction> Steps { get; set; } = new();
    public int DelayMs { get; set; } = 250;

    // OBS
    public ObsCommand ObsCommand { get; set; }
    public string? ObsScene { get; set; }
    public string? ObsSource { get; set; }
    public string? ObsFilter { get; set; }
    public string? ObsTransition { get; set; }

    // Aitum
    public string? AitumRuleId { get; set; }
    public string? AitumRuleName { get; set; }
    public string? AitumStateName { get; set; }

    // Windows media session
    public MediaSessionCommand MediaSessionCommand { get; set; }

    /// <summary>Show the album/thumbnail art on the key face instead of the icon.</summary>
    public bool ShowArtwork { get; set; } = true;

    // Spotify
    public SpotifyCommand SpotifyCommand { get; set; }

    /// <summary>Playlist, album or track URI for SpotifyCommand.PlayContext.</summary>
    public string? SpotifyUri { get; set; }

    // Twitch
    public TwitchCommand TwitchCommand { get; set; }

    /// <summary>Title, category, chat message, announcement or channel name, per command.</summary>
    public string? TwitchText { get; set; }

    public int TwitchCommercialSeconds { get; set; } = 60;

    public int TwitchSlowModeSeconds { get; set; } = 5;

    public ActionSettings Clone()
    {
        var copy = (ActionSettings)MemberwiseClone();
        copy.MacroEvents = MacroEvents.Select(e => e.Clone()).ToList();
        copy.Steps = Steps.Select(s => s.Clone()).ToList();
        return copy;
    }
}

/// <summary>What a key does when pressed.</summary>
public sealed class KeyAction
{
    public ActionKind Kind { get; set; } = ActionKind.None;

    public ActionSettings Settings { get; set; } = new();

    public static KeyAction None() => new();

    public KeyAction Clone() => new() { Kind = Kind, Settings = Settings.Clone() };

    public bool IsEmpty => Kind == ActionKind.None;
}
