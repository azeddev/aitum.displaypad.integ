using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Config;

public sealed record PresetKey(
    string Title,
    string Icon,
    ActionKind Kind,
    Action<ActionSettings>? Configure = null,
    string? Background = null);

public sealed record PresetPack(
    string Id,
    string Name,
    string Description,
    string Icon,
    IReadOnlyList<PresetKey> Keys);

/// <summary>
/// Ready-made pages of keys for the applications Base Camp shipped "integrations" for.
/// For Photoshop, Premiere, Illustrator, Resolve and Discord those integrations were
/// curated shortcut sets rather than an API, which is exactly what these packs are.
/// OBS and streaming packs use the real integrations instead.
/// </summary>
public static class PresetPacks
{
    private const string Adobe = "#FF1B2A4A";
    private const string Resolve = "#FF2A1B33";
    private const string Stream = "#FF2A1030";

    public static IReadOnlyList<PresetPack> All { get; } = new List<PresetPack>
    {
        new("photoshop", "Adobe Photoshop", "Tools, history and layer shortcuts.", "app", new PresetKey[]
        {
            Hotkey("Undo", "back", HotkeyModifiers.Control, 'Z', Adobe),
            Hotkey("Redo", "arrow-right", HotkeyModifiers.Control | HotkeyModifiers.Shift, 'Z', Adobe),
            Hotkey("Save", "check", HotkeyModifiers.Control, 'S', Adobe),
            Hotkey("Save As", "file", HotkeyModifiers.Control | HotkeyModifiers.Shift, 'S', Adobe),
            Hotkey("Move", "arrow-right", HotkeyModifiers.None, 'V', Adobe),
            Hotkey("Brush", "text", HotkeyModifiers.None, 'B', Adobe),
            Hotkey("Eraser", "cross", HotkeyModifiers.None, 'E', Adobe),
            Hotkey("Lasso", "star", HotkeyModifiers.None, 'L', Adobe),
            Hotkey("New Layer", "plus", HotkeyModifiers.Control | HotkeyModifiers.Shift, 'N', Adobe),
            Hotkey("Deselect", "minus", HotkeyModifiers.Control, 'D', Adobe),
            Hotkey("Zoom In", "plus", HotkeyModifiers.Control, 0xBB, Adobe),
            Hotkey("Zoom Out", "minus", HotkeyModifiers.Control, 0xBD, Adobe),
        }),

        new("premiere", "Adobe Premiere Pro", "Playback, trimming and export shortcuts.", "camera", new PresetKey[]
        {
            Hotkey("Play", "play-pause", HotkeyModifiers.None, 0x20, Adobe),
            Hotkey("Mark In", "arrow-right", HotkeyModifiers.None, 'I', Adobe),
            Hotkey("Mark Out", "arrow-left", HotkeyModifiers.None, 'O', Adobe),
            Hotkey("Razor", "cross", HotkeyModifiers.None, 'C', Adobe),
            Hotkey("Select", "arrow-up", HotkeyModifiers.None, 'V', Adobe),
            Hotkey("Ripple Del", "minus", HotkeyModifiers.Shift, 0x2E, Adobe),
            Hotkey("Undo", "back", HotkeyModifiers.Control, 'Z', Adobe),
            Hotkey("Save", "check", HotkeyModifiers.Control, 'S', Adobe),
            Hotkey("Render", "bolt", HotkeyModifiers.None, 0x0D, Adobe),
            Hotkey("Export", "file", HotkeyModifiers.Control, 'M', Adobe),
            Hotkey("Zoom In", "plus", HotkeyModifiers.None, 0xBB, Adobe),
            Hotkey("Zoom Out", "minus", HotkeyModifiers.None, 0xBD, Adobe),
        }),

        new("illustrator", "Adobe Illustrator", "Tool and object shortcuts.", "star", new PresetKey[]
        {
            Hotkey("Undo", "back", HotkeyModifiers.Control, 'Z', Adobe),
            Hotkey("Redo", "arrow-right", HotkeyModifiers.Control | HotkeyModifiers.Shift, 'Z', Adobe),
            Hotkey("Save", "check", HotkeyModifiers.Control, 'S', Adobe),
            Hotkey("Select", "arrow-up", HotkeyModifiers.None, 'V', Adobe),
            Hotkey("Direct Sel", "arrow-right", HotkeyModifiers.None, 'A', Adobe),
            Hotkey("Pen", "text", HotkeyModifiers.None, 'P', Adobe),
            Hotkey("Type", "text", HotkeyModifiers.None, 'T', Adobe),
            Hotkey("Rectangle", "app", HotkeyModifiers.None, 'M', Adobe),
            Hotkey("Group", "layers", HotkeyModifiers.Control, 'G', Adobe),
            Hotkey("Ungroup", "layers", HotkeyModifiers.Control | HotkeyModifiers.Shift, 'G', Adobe),
            Hotkey("Zoom In", "plus", HotkeyModifiers.Control, 0xBB, Adobe),
            Hotkey("Zoom Out", "minus", HotkeyModifiers.Control, 0xBD, Adobe),
        }),

        new("resolve", "DaVinci Resolve", "Page switching and edit shortcuts.", "scene", new PresetKey[]
        {
            Hotkey("Media", "folder", HotkeyModifiers.Shift, '2', Resolve),
            Hotkey("Cut", "cross", HotkeyModifiers.Shift, '3', Resolve),
            Hotkey("Edit", "macro", HotkeyModifiers.Shift, '4', Resolve),
            Hotkey("Fusion", "layers", HotkeyModifiers.Shift, '5', Resolve),
            Hotkey("Color", "brightness", HotkeyModifiers.Shift, '6', Resolve),
            Hotkey("Fairlight", "volume-up", HotkeyModifiers.Shift, '7', Resolve),
            Hotkey("Deliver", "bolt", HotkeyModifiers.Shift, '8', Resolve),
            Hotkey("Play", "play-pause", HotkeyModifiers.None, 0x20, Resolve),
            Hotkey("Mark In", "arrow-right", HotkeyModifiers.None, 'I', Resolve),
            Hotkey("Mark Out", "arrow-left", HotkeyModifiers.None, 'O', Resolve),
            Hotkey("Blade", "cross", HotkeyModifiers.None, 'B', Resolve),
            Hotkey("Save", "check", HotkeyModifiers.Control, 'S', Resolve),
        }),

        new("discord", "Discord", "Discord's default push-to-talk shortcuts plus quick links.", "microphone", new PresetKey[]
        {
            Hotkey("Mute", "microphone-off", HotkeyModifiers.Control | HotkeyModifiers.Shift, 'M'),
            Hotkey("Deafen", "volume-mute", HotkeyModifiers.Control | HotkeyModifiers.Shift, 'D'),
            new("Discord", "app", ActionKind.LaunchProgram, s => s.Path = "discord.exe"),
            new("Blank", "blank", ActionKind.None),
            new("Blank", "blank", ActionKind.None),
            new("Blank", "blank", ActionKind.None),
            new("Blank", "blank", ActionKind.None),
            new("Blank", "blank", ActionKind.None),
            new("Blank", "blank", ActionKind.None),
            new("Blank", "blank", ActionKind.None),
            new("Blank", "blank", ActionKind.None),
            Back(),
        }),

        new("obs", "OBS Studio", "Streaming and recording controls over obs-websocket.", "stream", new PresetKey[]
        {
            Obs("Stream", "stream", ObsCommand.ToggleStream, Stream),
            Obs("Record", "record", ObsCommand.ToggleRecord, Stream),
            Obs("Pause Rec", "pause", ObsCommand.PauseResumeRecord, Stream),
            Obs("Replay", "layers", ObsCommand.ToggleReplayBuffer, Stream),
            Obs("Save Clip", "check", ObsCommand.SaveReplayBuffer, Stream),
            Obs("Virtual Cam", "camera", ObsCommand.ToggleVirtualCam, Stream),
            Obs("Studio", "scene", ObsCommand.ToggleStudioMode, Stream),
            Obs("Transition", "arrow-right", ObsCommand.TriggerStudioTransition, Stream),
            new("Mic Mute", "microphone-off", ActionKind.Obs, s =>
            {
                s.ObsCommand = ObsCommand.ToggleInputMute;
                s.ObsSource = "Mic/Aux";
            }, Stream),
            new("Blank", "blank", ActionKind.None),
            new("Blank", "blank", ActionKind.None),
            Back(),
        }),

        new("streaming", "Streaming deck", "OBS, Twitch and now-playing keys in one page.", "bolt", new PresetKey[]
        {
            Obs("Stream", "stream", ObsCommand.ToggleStream, Stream),
            Obs("Record", "record", ObsCommand.ToggleRecord, Stream),
            new("Clip", "camera", ActionKind.Twitch, s => s.TwitchCommand = TwitchCommand.CreateClip, Stream),
            new("Marker", "star", ActionKind.Twitch, s => s.TwitchCommand = TwitchCommand.CreateMarker, Stream),
            new("Viewers", "profile", ActionKind.Twitch, s => s.TwitchCommand = TwitchCommand.ShowViewerCount, Stream),
            new("Ad Break", "clock", ActionKind.Twitch, s =>
            {
                s.TwitchCommand = TwitchCommand.StartCommercial;
                s.TwitchCommercialSeconds = 60;
            }, Stream),
            new("Prev", "previous", ActionKind.MediaSession, s => s.MediaSessionCommand = MediaSessionCommand.Previous),
            new("Play", "play-pause", ActionKind.MediaSession, s => s.MediaSessionCommand = MediaSessionCommand.PlayPause),
            new("Next", "next", ActionKind.MediaSession, s => s.MediaSessionCommand = MediaSessionCommand.Next),
            new(string.Empty, "blank", ActionKind.MediaSession, s =>
            {
                s.MediaSessionCommand = MediaSessionCommand.ShowNowPlaying;
                s.ShowArtwork = true;
            }),
            new("Blank", "blank", ActionKind.None),
            Back(),
        }),

        new("media", "Media", "Transport controls and a now-playing key with album art.", "play-pause", new PresetKey[]
        {
            new("Prev", "previous", ActionKind.MediaSession, s => s.MediaSessionCommand = MediaSessionCommand.Previous),
            new("Play", "play-pause", ActionKind.MediaSession, s => s.MediaSessionCommand = MediaSessionCommand.PlayPause),
            new("Next", "next", ActionKind.MediaSession, s => s.MediaSessionCommand = MediaSessionCommand.Next),
            new("Stop", "stop", ActionKind.MediaSession, s => s.MediaSessionCommand = MediaSessionCommand.Stop),
            new("Vol -", "volume-down", ActionKind.Volume, s => s.VolumeCommand = VolumeCommand.Down),
            new("Vol +", "volume-up", ActionKind.Volume, s => s.VolumeCommand = VolumeCommand.Up),
            new("Mute", "volume-mute", ActionKind.Volume, s => s.VolumeCommand = VolumeCommand.ToggleMute),
            new("Shuffle", "macro", ActionKind.Spotify, s => s.SpotifyCommand = SpotifyCommand.ToggleShuffle),
            new("Repeat", "restart", ActionKind.Spotify, s => s.SpotifyCommand = SpotifyCommand.CycleRepeat),
            new("Like", "star", ActionKind.Spotify, s => s.SpotifyCommand = SpotifyCommand.SaveTrack),
            new(string.Empty, "blank", ActionKind.MediaSession, s =>
            {
                s.MediaSessionCommand = MediaSessionCommand.ShowNowPlaying;
                s.ShowArtwork = true;
            }),
            Back(),
        }),
    };

    public static PresetPack? Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds a page from a pack, ready to be added to a profile.</summary>
    public static Page CreatePage(PresetPack pack, string? parentPageId)
    {
        var page = Page.Create(pack.Name, parentPageId);

        for (var i = 0; i < page.Keys.Count && i < pack.Keys.Count; i++)
        {
            var preset = pack.Keys[i];
            var slot = page.Keys[i];

            if (preset.Kind == ActionKind.None && preset.Icon == "blank")
            {
                continue;
            }

            slot.Action = new KeyAction { Kind = preset.Kind };
            preset.Configure?.Invoke(slot.Action.Settings);
            slot.Appearance.IconId = preset.Icon;
            slot.Appearance.Title = preset.Title;

            if (preset.Background is { } background)
            {
                slot.Appearance.BackgroundColor = background;
            }
        }

        return page;
    }

    private static PresetKey Hotkey(string title, string icon, HotkeyModifiers modifiers, int key, string? background = null) =>
        new(title, icon, ActionKind.Hotkey, s =>
        {
            s.Modifiers = modifiers;
            s.VirtualKey = key;
        }, background);

    private static PresetKey Obs(string title, string icon, ObsCommand command, string? background = null) =>
        new(title, icon, ActionKind.Obs, s => s.ObsCommand = command, background);

    private static PresetKey Back() => new("Back", "back", ActionKind.NavigateBack);
}
