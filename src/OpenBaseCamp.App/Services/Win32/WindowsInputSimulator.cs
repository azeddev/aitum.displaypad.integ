using System.Runtime.Versioning;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Services;
using static OpenBaseCamp.App.Services.Win32.NativeMethods;

namespace OpenBaseCamp.App.Services.Win32;

[SupportedOSPlatform("windows")]
public sealed class WindowsInputSimulator : IInputSimulator
{
    private static readonly int InputSize = System.Runtime.InteropServices.Marshal.SizeOf<Input>();

    public void KeyDown(int virtualKey) => Send(KeyInput(virtualKey, down: true));

    public void KeyUp(int virtualKey) => Send(KeyInput(virtualKey, down: false));

    public void TapKey(int virtualKey) =>
        Send(KeyInput(virtualKey, true), KeyInput(virtualKey, false));

    public void SendHotkey(HotkeyModifiers modifiers, int virtualKey)
    {
        var inputs = new List<Input>(10);
        var held = new List<int>(4);

        if (modifiers.HasFlag(HotkeyModifiers.Control)) held.Add(KeyCodes.VkControl);
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) held.Add(KeyCodes.VkShift);
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) held.Add(KeyCodes.VkMenu);
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) held.Add(KeyCodes.VkLWin);

        foreach (var modifier in held)
        {
            inputs.Add(KeyInput(modifier, down: true));
        }

        if (virtualKey != 0)
        {
            inputs.Add(KeyInput(virtualKey, down: true));
            inputs.Add(KeyInput(virtualKey, down: false));
        }

        for (var i = held.Count - 1; i >= 0; i--)
        {
            inputs.Add(KeyInput(held[i], down: false));
        }

        Send(inputs.ToArray());
    }

    public void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var inputs = new List<Input>(text.Length * 2);
        foreach (var ch in text)
        {
            // Newlines must be sent as a real Return press; KEYEVENTF_UNICODE '\n' is ignored.
            if (ch is '\n')
            {
                inputs.Add(KeyInput(0x0D, true));
                inputs.Add(KeyInput(0x0D, false));
                continue;
            }

            if (ch is '\r')
            {
                continue;
            }

            inputs.Add(UnicodeInput(ch, down: true));
            inputs.Add(UnicodeInput(ch, down: false));
        }

        // SendInput takes a bounded array; chunk long text so a single call stays reasonable.
        const int chunk = 200;
        for (var offset = 0; offset < inputs.Count; offset += chunk)
        {
            Send(inputs.Skip(offset).Take(chunk).ToArray());
        }
    }

    public void MouseButton(int button, bool down)
    {
        var input = new Input { type = InputMouse };
        switch (button)
        {
            case 1:
                input.u.mi.dwFlags = down ? MouseEventLeftDown : MouseEventLeftUp;
                break;
            case 2:
                input.u.mi.dwFlags = down ? MouseEventRightDown : MouseEventRightUp;
                break;
            case 3:
                input.u.mi.dwFlags = down ? MouseEventMiddleDown : MouseEventMiddleUp;
                break;
            case 4:
                input.u.mi.dwFlags = down ? MouseEventXDown : MouseEventXUp;
                input.u.mi.mouseData = XButton1;
                break;
            case 5:
                input.u.mi.dwFlags = down ? MouseEventXDown : MouseEventXUp;
                input.u.mi.mouseData = XButton2;
                break;
            default:
                return;
        }

        Send(input);
    }

    public void MouseClick(int button)
    {
        MouseButton(button, down: true);
        MouseButton(button, down: false);
    }

    public void MouseWheel(int delta)
    {
        var input = new Input { type = InputMouse };
        input.u.mi.dwFlags = MouseEventWheel;
        input.u.mi.mouseData = unchecked((uint)delta);
        Send(input);
    }

    public void MouseMove(int deltaX, int deltaY)
    {
        var input = new Input { type = InputMouse };
        input.u.mi.dwFlags = MouseEventMove;
        input.u.mi.dx = deltaX;
        input.u.mi.dy = deltaY;
        Send(input);
    }

    private static Input KeyInput(int virtualKey, bool down)
    {
        var input = new Input { type = InputKeyboard };
        input.u.ki.wVk = (ushort)virtualKey;
        input.u.ki.dwFlags = down ? 0 : KeyEventKeyUp;
        if (KeyCodes.IsExtended(virtualKey))
        {
            input.u.ki.dwFlags |= KeyEventExtendedKey;
        }

        return input;
    }

    private static Input UnicodeInput(char ch, bool down)
    {
        var input = new Input { type = InputKeyboard };
        input.u.ki.wScan = ch;
        input.u.ki.dwFlags = KeyEventUnicode | (down ? 0 : KeyEventKeyUp);
        return input;
    }

    private static void Send(params Input[] inputs)
    {
        if (inputs.Length > 0)
        {
            SendInput((uint)inputs.Length, inputs, InputSize);
        }
    }
}
