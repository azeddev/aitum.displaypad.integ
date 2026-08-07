using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Actions;

public sealed record ActionDescriptor(
    ActionKind Kind,
    string Category,
    string Name,
    string Description,
    string DefaultIcon);

/// <summary>
/// The action picker's contents. Categories mirror Base Camp's key binding tabs so the
/// app is familiar to anyone coming from the official software.
/// </summary>
public static class ActionCatalog
{
    public static readonly IReadOnlyList<ActionDescriptor> All = new List<ActionDescriptor>
    {
        new(ActionKind.None, "General", "Nothing", "Leave the key unassigned.", "blank"),
        new(ActionKind.MultiAction, "General", "Multi Action", "Run several actions in order.", "layers"),
        new(ActionKind.Delay, "General", "Delay", "Pause inside a multi action.", "clock"),

        new(ActionKind.Hotkey, "Input", "Hotkey", "Send a key combination such as Ctrl + Shift + M.", "keyboard"),
        new(ActionKind.Text, "Input", "Text", "Type a block of text.", "text"),
        new(ActionKind.Macro, "Input", "Macro", "Play a recorded sequence of keys and clicks.", "macro"),
        new(ActionKind.Mouse, "Input", "Mouse", "Click or scroll.", "mouse"),
        new(ActionKind.Multimedia, "Input", "Multimedia", "Play/pause, track skip, browser and app keys.", "play-pause"),
        new(ActionKind.Volume, "Input", "Volume", "Change or mute the Windows master volume.", "volume-up"),

        new(ActionKind.LaunchProgram, "Launch", "Program", "Start an application.", "app"),
        new(ActionKind.OpenFile, "Launch", "File", "Open a document with its default program.", "file"),
        new(ActionKind.OpenFolder, "Launch", "Folder", "Open a folder in Explorer.", "folder"),
        new(ActionKind.OpenWebsite, "Launch", "Website", "Open a URL in the default browser.", "globe"),

        new(ActionKind.SystemCommand, "System", "System Command", "Shut down, restart, sleep, lock and more.", "power"),

        new(ActionKind.Folder, "Navigation", "Folder", "Open another page of keys.", "folder"),
        new(ActionKind.NavigateBack, "Navigation", "Back", "Return to the parent page.", "back"),
        new(ActionKind.NavigateHome, "Navigation", "Home", "Jump to the profile's first page.", "home"),
        new(ActionKind.SwitchProfile, "Navigation", "Switch Profile", "Activate a specific profile.", "profile"),
        new(ActionKind.NextProfile, "Navigation", "Next Profile", "Move to the next profile.", "arrow-right"),
        new(ActionKind.PreviousProfile, "Navigation", "Previous Profile", "Move to the previous profile.", "arrow-left"),

        new(ActionKind.Brightness, "Device", "Brightness", "Set, step or cycle the display brightness.", "brightness"),
        new(ActionKind.DisplaySleep, "Device", "Sleep Displays", "Put the key displays to sleep now.", "sleep"),

        new(ActionKind.Monitor, "Monitoring", "PC Monitor", "Show a live value such as CPU load on the key.", "chart"),

        new(ActionKind.Obs, "OBS Studio", "OBS", "Scenes, streaming, recording, sources and filters.", "stream"),
        new(ActionKind.AitumRule, "Aitum", "Trigger Rule", "Run an Aitum Desktop rule.", "bolt"),
        new(ActionKind.AitumState, "Aitum", "Show State", "Display an Aitum state variable on the key.", "chart"),
    };

    public static IEnumerable<string> Categories => All.Select(a => a.Category).Distinct();

    public static ActionDescriptor Describe(ActionKind kind) =>
        All.FirstOrDefault(a => a.Kind == kind) ?? All[0];

    /// <summary>A short, human readable summary of a configured action, shown under each key.</summary>
    public static string Summarize(KeyAction action, Func<string?, string?>? pageName = null, Func<string?, string?>? profileName = null)
    {
        var s = action.Settings;
        return action.Kind switch
        {
            ActionKind.None => "Not assigned",
            ActionKind.Hotkey => KeyCodes.Describe(s.Modifiers, s.VirtualKey),
            ActionKind.Text => Truncate(s.Text, 28),
            ActionKind.Macro => $"{s.MacroEvents.Count} events · {s.MacroPlayMode}",
            ActionKind.Mouse => s.MouseCommand.ToString(),
            ActionKind.Multimedia => s.MultimediaCommand.ToString(),
            ActionKind.Volume => s.VolumeCommand == VolumeCommand.SetLevel
                ? $"Set volume {s.VolumeLevel}%"
                : s.VolumeCommand.ToString(),
            ActionKind.LaunchProgram or ActionKind.OpenFile or ActionKind.OpenFolder => Truncate(s.Path, 32),
            ActionKind.OpenWebsite => Truncate(s.Url, 32),
            ActionKind.SystemCommand => s.SystemCommand.ToString(),
            ActionKind.Folder => pageName?.Invoke(s.TargetPageId) ?? "Folder",
            ActionKind.NavigateBack => "Back",
            ActionKind.NavigateHome => "Home",
            ActionKind.SwitchProfile => profileName?.Invoke(s.TargetProfileId) ?? "Switch profile",
            ActionKind.NextProfile => "Next profile",
            ActionKind.PreviousProfile => "Previous profile",
            ActionKind.Brightness => s.BrightnessCommand == BrightnessCommand.Set
                ? $"Brightness {s.BrightnessLevel}%"
                : $"Brightness {s.BrightnessCommand}",
            ActionKind.DisplaySleep => "Sleep displays",
            ActionKind.Monitor => s.Metric.ToString(),
            ActionKind.MultiAction => $"{s.Steps.Count} steps",
            ActionKind.Delay => $"{s.DelayMs} ms",
            ActionKind.Obs => DescribeObs(s),
            ActionKind.AitumRule => s.AitumRuleName ?? "Aitum rule",
            ActionKind.AitumState => s.AitumStateName ?? "Aitum state",
            _ => action.Kind.ToString(),
        };
    }

    private static string DescribeObs(ActionSettings s) => s.ObsCommand switch
    {
        ObsCommand.SetScene => $"Scene: {s.ObsScene}",
        ObsCommand.ToggleInputMute or ObsCommand.MuteInput or ObsCommand.UnmuteInput => $"{s.ObsCommand}: {s.ObsSource}",
        ObsCommand.ToggleSourceVisibility => $"Source: {s.ObsSource}",
        ObsCommand.ToggleFilter => $"Filter: {s.ObsFilter}",
        ObsCommand.SetTransition => $"Transition: {s.ObsTransition}",
        _ => s.ObsCommand.ToString(),
    };

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Not configured";
        }

        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }
}
