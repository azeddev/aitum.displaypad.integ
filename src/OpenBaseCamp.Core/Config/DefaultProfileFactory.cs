using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Model;

/// <summary>Builds the layout a fresh install starts with, so the pad is never blank.</summary>
public static class DefaultProfileFactory
{
    public static Profile CreateStarterProfile()
    {
        var profile = Profile.Create("Default");
        var root = profile.Root;

        Set(root, 0, ActionKind.Multimedia, "previous", "Previous",
            s => s.MultimediaCommand = MultimediaCommand.PreviousTrack);
        Set(root, 1, ActionKind.Multimedia, "play-pause", "Play",
            s => s.MultimediaCommand = MultimediaCommand.PlayPause);
        Set(root, 2, ActionKind.Multimedia, "next", "Next",
            s => s.MultimediaCommand = MultimediaCommand.NextTrack);
        Set(root, 3, ActionKind.Volume, "volume-mute", "Mute",
            s => s.VolumeCommand = VolumeCommand.ToggleMute);

        Set(root, 4, ActionKind.Volume, "volume-down", "Vol -",
            s => s.VolumeCommand = VolumeCommand.Down);
        Set(root, 5, ActionKind.Volume, "volume-up", "Vol +",
            s => s.VolumeCommand = VolumeCommand.Up);
        Set(root, 6, ActionKind.Monitor, "cpu", "CPU",
            s => s.Metric = MonitorMetric.CpuUsage);
        Set(root, 7, ActionKind.Monitor, "ram", "RAM",
            s => s.Metric = MonitorMetric.RamUsage);

        Set(root, 8, ActionKind.OpenWebsite, "globe", "Web",
            s => s.Url = "https://mountain.gg");
        Set(root, 9, ActionKind.Hotkey, "keyboard", "Copy", s =>
        {
            s.Modifiers = HotkeyModifiers.Control;
            s.VirtualKey = 'C';
        });
        Set(root, 10, ActionKind.Monitor, "clock", "Clock",
            s => s.Metric = MonitorMetric.Time);
        Set(root, 11, ActionKind.Brightness, "brightness", "Bright",
            s => s.BrightnessCommand = BrightnessCommand.Cycle);

        return profile;
    }

    private static void Set(Page page, int index, ActionKind kind, string icon, string title, Action<ActionSettings>? configure = null)
    {
        var slot = page[index];
        slot.Action = new KeyAction { Kind = kind };
        configure?.Invoke(slot.Action.Settings);
        slot.Appearance.IconId = icon;
        slot.Appearance.Title = title;
    }
}
