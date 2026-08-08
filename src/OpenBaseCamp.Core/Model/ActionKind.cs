namespace OpenBaseCamp.Core.Model;

/// <summary>
/// Everything a DisplayPad key can be bound to. Mirrors the binding categories of the
/// official Base Camp software plus the integrations this app adds.
/// </summary>
public enum ActionKind
{
    None = 0,

    // Input
    Hotkey,
    Text,
    Macro,
    Mouse,
    Multimedia,
    Volume,

    // Launching
    LaunchProgram,
    OpenFile,
    OpenFolder,
    OpenWebsite,

    // Operating system
    SystemCommand,

    // Navigation inside the pad
    Folder,
    NavigateBack,
    NavigateHome,
    SwitchProfile,
    NextProfile,
    PreviousProfile,

    // Device
    Brightness,
    DisplaySleep,

    // Live data on the key face
    Monitor,

    // Composition
    MultiAction,
    Delay,

    // Integrations
    Obs,
    AitumRule,
    AitumState,
    MediaSession,
    Spotify,
    Twitch,
}

/// <summary>
/// Transport controls that work with whatever is playing through Windows: Spotify,
/// a browser tab, VLC, anything that registers a media session. Needs no setup.
/// </summary>
public enum MediaSessionCommand
{
    PlayPause,
    Play,
    Pause,
    Next,
    Previous,
    Stop,
    ShowNowPlaying,
}

public enum SpotifyCommand
{
    PlayPause,
    Play,
    Pause,
    Next,
    Previous,
    ToggleShuffle,
    CycleRepeat,
    VolumeUp,
    VolumeDown,
    SetVolume,
    SaveTrack,
    PlayContext,
    ShowNowPlaying,
}

public enum TwitchCommand
{
    CreateClip,
    CreateMarker,
    StartCommercial,
    SetTitle,
    SetCategory,
    SendChatMessage,
    SendAnnouncement,
    Shoutout,
    ToggleEmoteOnly,
    ToggleSubscriberOnly,
    ToggleFollowerOnly,
    ToggleSlowMode,
    ShowViewerCount,
    ShowLiveStatus,
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8,
}

public enum MultimediaCommand
{
    PlayPause,
    Stop,
    NextTrack,
    PreviousTrack,
    Mute,
    VolumeUp,
    VolumeDown,
    MediaSelect,
    Mail,
    Calculator,
    MyComputer,
    BrowserHome,
    BrowserSearch,
    BrowserBack,
    BrowserForward,
    BrowserRefresh,
    BrowserStop,
    BrowserFavorites,
}

public enum MouseCommand
{
    LeftClick,
    RightClick,
    MiddleClick,
    DoubleClick,
    Button4,
    Button5,
    ScrollUp,
    ScrollDown,
}

public enum VolumeCommand
{
    Up,
    Down,
    ToggleMute,
    SetLevel,
}

public enum SystemCommandKind
{
    Shutdown,
    Restart,
    Sleep,
    Hibernate,
    LockWorkstation,
    LogOff,
    TurnOffDisplays,
    EmptyRecycleBin,
    OpenTaskManager,
}

public enum BrightnessCommand
{
    Increase,
    Decrease,
    Cycle,
    Set,
}

/// <summary>Live values that can be painted onto a key face.</summary>
public enum MonitorMetric
{
    CpuUsage,
    RamUsage,
    RamUsedGb,
    GpuUsage,
    DiskUsage,
    DiskFreeGb,
    NetworkDown,
    NetworkUp,
    Time,
    Date,
    MasterVolume,
    AitumState,
}

public enum ObsCommand
{
    SetScene,
    ToggleStream,
    StartStream,
    StopStream,
    ToggleRecord,
    StartRecord,
    StopRecord,
    PauseResumeRecord,
    ToggleReplayBuffer,
    SaveReplayBuffer,
    ToggleSourceVisibility,
    ToggleInputMute,
    MuteInput,
    UnmuteInput,
    ToggleFilter,
    SetTransition,
    ToggleVirtualCam,
    ToggleStudioMode,
    TriggerStudioTransition,
    RefreshBrowserSource,
    SetSceneCollection,
}

public enum MacroPlayMode
{
    Once,
    RepeatCount,
    Toggle,
    HoldToRepeat,
}

public enum MacroEventType
{
    KeyDown,
    KeyUp,
    MouseDown,
    MouseUp,
    Wheel,
    MouseMove,
    Delay,
    Text,
}
