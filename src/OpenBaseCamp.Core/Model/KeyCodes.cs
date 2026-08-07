namespace OpenBaseCamp.Core.Model;

public sealed record KeyCodeEntry(int VirtualKey, string Name, string Group);

/// <summary>
/// Windows virtual-key codes with display names, grouped for the hotkey picker.
/// These are what the input simulator sends; the pad itself is driven in software mode
/// so the firmware key table is not involved.
/// </summary>
public static class KeyCodes
{
    public const int VkLButton = 0x01;
    public const int VkRButton = 0x02;
    public const int VkMButton = 0x04;
    public const int VkXButton1 = 0x05;
    public const int VkXButton2 = 0x06;

    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12;
    public const int VkLWin = 0x5B;
    public const int VkLShift = 0xA0;
    public const int VkRShift = 0xA1;
    public const int VkLControl = 0xA2;
    public const int VkRControl = 0xA3;
    public const int VkLMenu = 0xA4;
    public const int VkRMenu = 0xA5;

    public const int VkBrowserBack = 0xA6;
    public const int VkBrowserForward = 0xA7;
    public const int VkBrowserRefresh = 0xA8;
    public const int VkBrowserStop = 0xA9;
    public const int VkBrowserSearch = 0xAA;
    public const int VkBrowserFavorites = 0xAB;
    public const int VkBrowserHome = 0xAC;
    public const int VkVolumeMute = 0xAD;
    public const int VkVolumeDown = 0xAE;
    public const int VkVolumeUp = 0xAF;
    public const int VkMediaNextTrack = 0xB0;
    public const int VkMediaPrevTrack = 0xB1;
    public const int VkMediaStop = 0xB2;
    public const int VkMediaPlayPause = 0xB3;
    public const int VkLaunchMail = 0xB4;
    public const int VkLaunchMediaSelect = 0xB5;
    public const int VkLaunchApp1 = 0xB6;
    public const int VkLaunchApp2 = 0xB7;

    private static readonly List<KeyCodeEntry> Entries = Build();

    public static IReadOnlyList<KeyCodeEntry> All => Entries;

    public static IEnumerable<string> Groups => Entries.Select(e => e.Group).Distinct();

    public static string NameOf(int virtualKey) =>
        Entries.FirstOrDefault(e => e.VirtualKey == virtualKey)?.Name ?? $"0x{virtualKey:X2}";

    public static int? FromName(string name) =>
        Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.VirtualKey;

    /// <summary>True for keys that are sent with KEYEVENTF_EXTENDEDKEY.</summary>
    public static bool IsExtended(int virtualKey) => virtualKey is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 // page/home/end/arrows
        or 0x2D or 0x2E // insert / delete
        or 0x90 // numlock
        or 0x6F // numpad divide
        or 0x0D // enter (numpad enter is extended; plain enter is harmless)
        or VkRControl or VkRMenu
        or VkLWin or 0x5C or 0x5D
        or >= VkBrowserBack and <= VkLaunchApp2;

    public static string Describe(HotkeyModifiers modifiers, int virtualKey)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        if (virtualKey != 0) parts.Add(NameOf(virtualKey));
        return parts.Count == 0 ? "(none)" : string.Join(" + ", parts);
    }

    private static List<KeyCodeEntry> Build()
    {
        var list = new List<KeyCodeEntry>();

        void Add(int vk, string name, string group) => list.Add(new KeyCodeEntry(vk, name, group));

        for (var c = 'A'; c <= 'Z'; c++)
        {
            Add(c, c.ToString(), "Letters");
        }

        for (var d = 0; d <= 9; d++)
        {
            Add(0x30 + d, d.ToString(), "Numbers");
        }

        for (var f = 1; f <= 24; f++)
        {
            Add(0x6F + f, $"F{f}", "Function");
        }

        Add(0x1B, "Escape", "Control");
        Add(0x09, "Tab", "Control");
        Add(0x14, "Caps Lock", "Control");
        Add(0x20, "Space", "Control");
        Add(0x0D, "Enter", "Control");
        Add(0x08, "Backspace", "Control");
        Add(0x2E, "Delete", "Control");
        Add(0x2D, "Insert", "Control");
        Add(0x24, "Home", "Control");
        Add(0x23, "End", "Control");
        Add(0x21, "Page Up", "Control");
        Add(0x22, "Page Down", "Control");
        Add(0x25, "Left Arrow", "Control");
        Add(0x26, "Up Arrow", "Control");
        Add(0x27, "Right Arrow", "Control");
        Add(0x28, "Down Arrow", "Control");
        Add(0x2C, "Print Screen", "Control");
        Add(0x91, "Scroll Lock", "Control");
        Add(0x13, "Pause", "Control");
        Add(0x5D, "Menu (App)", "Control");
        Add(0x90, "Num Lock", "Control");

        Add(0xBA, ";", "Punctuation");
        Add(0xBB, "=", "Punctuation");
        Add(0xBC, ",", "Punctuation");
        Add(0xBD, "-", "Punctuation");
        Add(0xBE, ".", "Punctuation");
        Add(0xBF, "/", "Punctuation");
        Add(0xC0, "`", "Punctuation");
        Add(0xDB, "[", "Punctuation");
        Add(0xDC, "\\", "Punctuation");
        Add(0xDD, "]", "Punctuation");
        Add(0xDE, "'", "Punctuation");

        for (var d = 0; d <= 9; d++)
        {
            Add(0x60 + d, $"Numpad {d}", "Numpad");
        }

        Add(0x6A, "Numpad *", "Numpad");
        Add(0x6B, "Numpad +", "Numpad");
        Add(0x6D, "Numpad -", "Numpad");
        Add(0x6E, "Numpad .", "Numpad");
        Add(0x6F, "Numpad /", "Numpad");

        Add(VkLShift, "Left Shift", "Modifiers");
        Add(VkRShift, "Right Shift", "Modifiers");
        Add(VkLControl, "Left Ctrl", "Modifiers");
        Add(VkRControl, "Right Ctrl", "Modifiers");
        Add(VkLMenu, "Left Alt", "Modifiers");
        Add(VkRMenu, "Right Alt", "Modifiers");
        Add(VkLWin, "Left Win", "Modifiers");
        Add(0x5C, "Right Win", "Modifiers");

        Add(VkMediaPlayPause, "Play / Pause", "Media");
        Add(VkMediaStop, "Stop", "Media");
        Add(VkMediaNextTrack, "Next Track", "Media");
        Add(VkMediaPrevTrack, "Previous Track", "Media");
        Add(VkVolumeMute, "Mute", "Media");
        Add(VkVolumeUp, "Volume Up", "Media");
        Add(VkVolumeDown, "Volume Down", "Media");
        Add(VkLaunchMediaSelect, "Media Player", "Media");
        Add(VkLaunchMail, "Mail", "Media");
        Add(VkLaunchApp2, "Calculator", "Media");
        Add(VkLaunchApp1, "My Computer", "Media");
        Add(VkBrowserHome, "Browser Home", "Media");
        Add(VkBrowserSearch, "Browser Search", "Media");
        Add(VkBrowserBack, "Browser Back", "Media");
        Add(VkBrowserForward, "Browser Forward", "Media");
        Add(VkBrowserRefresh, "Browser Refresh", "Media");
        Add(VkBrowserStop, "Browser Stop", "Media");
        Add(VkBrowserFavorites, "Browser Favourites", "Media");

        return list;
    }

    /// <summary>Virtual key used to send a <see cref="MultimediaCommand"/>.</summary>
    public static int ForMultimedia(MultimediaCommand command) => command switch
    {
        MultimediaCommand.PlayPause => VkMediaPlayPause,
        MultimediaCommand.Stop => VkMediaStop,
        MultimediaCommand.NextTrack => VkMediaNextTrack,
        MultimediaCommand.PreviousTrack => VkMediaPrevTrack,
        MultimediaCommand.Mute => VkVolumeMute,
        MultimediaCommand.VolumeUp => VkVolumeUp,
        MultimediaCommand.VolumeDown => VkVolumeDown,
        MultimediaCommand.MediaSelect => VkLaunchMediaSelect,
        MultimediaCommand.Mail => VkLaunchMail,
        MultimediaCommand.Calculator => VkLaunchApp2,
        MultimediaCommand.MyComputer => VkLaunchApp1,
        MultimediaCommand.BrowserHome => VkBrowserHome,
        MultimediaCommand.BrowserSearch => VkBrowserSearch,
        MultimediaCommand.BrowserBack => VkBrowserBack,
        MultimediaCommand.BrowserForward => VkBrowserForward,
        MultimediaCommand.BrowserRefresh => VkBrowserRefresh,
        MultimediaCommand.BrowserStop => VkBrowserStop,
        MultimediaCommand.BrowserFavorites => VkBrowserFavorites,
        _ => 0,
    };
}
