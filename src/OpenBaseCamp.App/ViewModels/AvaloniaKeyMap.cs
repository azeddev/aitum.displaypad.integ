using Avalonia.Input;

namespace OpenBaseCamp.App.ViewModels;

/// <summary>Maps Avalonia's <see cref="Key"/> onto Windows virtual-key codes for macro recording.</summary>
public static class AvaloniaKeyMap
{
    public static int ToVirtualKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => 'A' + (key - Key.A),
        >= Key.D0 and <= Key.D9 => '0' + (key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => 0x60 + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F24 => 0x70 + (key - Key.F1),

        Key.Escape => 0x1B,
        Key.Tab => 0x09,
        Key.Space => 0x20,
        Key.Enter => 0x0D,
        Key.Back => 0x08,
        Key.Delete => 0x2E,
        Key.Insert => 0x2D,
        Key.Home => 0x24,
        Key.End => 0x23,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.Left => 0x25,
        Key.Up => 0x26,
        Key.Right => 0x27,
        Key.Down => 0x28,
        Key.CapsLock => 0x14,
        Key.NumLock => 0x90,
        Key.Scroll => 0x91,
        Key.PrintScreen => 0x2C,
        Key.Pause => 0x13,
        Key.Apps => 0x5D,

        Key.LeftShift => 0xA0,
        Key.RightShift => 0xA1,
        Key.LeftCtrl => 0xA2,
        Key.RightCtrl => 0xA3,
        Key.LeftAlt => 0xA4,
        Key.RightAlt => 0xA5,
        Key.LWin => 0x5B,
        Key.RWin => 0x5C,

        Key.Add => 0x6B,
        Key.Subtract => 0x6D,
        Key.Multiply => 0x6A,
        Key.Divide => 0x6F,
        Key.Decimal => 0x6E,

        Key.OemSemicolon => 0xBA,
        Key.OemPlus => 0xBB,
        Key.OemComma => 0xBC,
        Key.OemMinus => 0xBD,
        Key.OemPeriod => 0xBE,
        Key.OemQuestion => 0xBF,
        Key.OemTilde => 0xC0,
        Key.OemOpenBrackets => 0xDB,
        Key.OemPipe => 0xDC,
        Key.OemCloseBrackets => 0xDD,
        Key.OemQuotes => 0xDE,
        Key.OemBackslash => 0xE2,

        Key.MediaPlayPause => 0xB3,
        Key.MediaStop => 0xB2,
        Key.MediaNextTrack => 0xB0,
        Key.MediaPreviousTrack => 0xB1,
        Key.VolumeMute => 0xAD,
        Key.VolumeDown => 0xAE,
        Key.VolumeUp => 0xAF,

        _ => 0,
    };
}
