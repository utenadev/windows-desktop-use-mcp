using System.Runtime.InteropServices;

namespace WindowsDesktopUse.Input;

/// <summary>
/// Mouse button types
/// </summary>
public enum MouseButton
{
    Left,
    Right,
    Middle
}

/// <summary>
/// Key actions
/// </summary>
public enum KeyAction
{
    Press,
    Release,
    Click
}

/// <summary>
/// Service for mouse and keyboard input operations
/// </summary>
public class InputService
{
    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public MOUSEKEYBDHARDWAREINPUT mkhi;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct MOUSEKEYBDHARDWAREINPUT
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;
    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// Move mouse cursor to absolute position
    /// </summary>
    public void MoveMouse(int x, int y)
    {
        SetCursorPos(x, y);
    }

    /// <summary>
    /// Get current mouse position
    /// </summary>
    public (int x, int y) GetMousePosition()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    /// <summary>
    /// Click mouse button
    /// </summary>
    public void ClickMouse(MouseButton button, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var (down, up) = button switch
            {
                MouseButton.Left => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
                MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
                MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
                _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
            };

            mouse_event(down, 0, 0, 0, 0);
            mouse_event(up, 0, 0, 0, 0);

            if (i < count - 1)
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>
    /// Drag and drop from start to end position
    /// </summary>
    public void DragMouse(int startX, int startY, int endX, int endY)
    {
        MoveMouse(startX, startY);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        Thread.Sleep(50);
        MoveMouse(endX, endY);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    /// <summary>
    /// Type text (simulates keystrokes for each character)
    /// </summary>
    public void TypeText(string text)
    {
        foreach (char c in text)
        {
            SendKey(c);
        }
    }

    /// <summary>
    /// Send a single key
    /// </summary>
    private void SendKey(char c)
    {
        var inputs = new INPUT[2];

        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].mkhi.ki = new KEYBDINPUT
        {
            wVk = 0,
            wScan = c,
            dwFlags = 0x0004, // KEYEVENTF_UNICODE
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].mkhi.ki = new KEYBDINPUT
        {
            wVk = 0,
            wScan = c,
            dwFlags = 0x0004 | KEYEVENTF_KEYUP,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    /// <summary>
    /// Press a virtual key
    /// </summary>
    public void PressKey(ushort virtualKey, KeyAction action = KeyAction.Click)
    {
        switch (action)
        {
            case KeyAction.Press:
                SendVirtualKey(virtualKey, false);
                break;
            case KeyAction.Release:
                SendVirtualKey(virtualKey, true);
                break;
            case KeyAction.Click:
                SendVirtualKey(virtualKey, false);
                SendVirtualKey(virtualKey, true);
                break;
        }
    }

    private void SendVirtualKey(ushort virtualKey, bool keyUp)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }

    /// <summary>
    /// Common virtual key codes
    /// </summary>
    public static class VirtualKeys
    {
        public const ushort Enter = 0x0D;
        public const ushort Tab = 0x09;
        public const ushort Escape = 0x1B;
        public const ushort Space = 0x20;
        public const ushort Backspace = 0x08;
        public const ushort Delete = 0x2E;
        public const ushort Left = 0x25;
        public const ushort Up = 0x26;
        public const ushort Right = 0x27;
        public const ushort Down = 0x28;
        public const ushort Home = 0x24;
        public const ushort End = 0x23;
        public const ushort PageUp = 0x21;
        public const ushort PageDown = 0x22;
        public const ushort Shift = 0x10;
        public const ushort Control = 0x11;
        public const ushort Alt = 0x12;
        public const ushort Win = 0x5B;
        public const ushort F1 = 0x70;
        public const ushort F12 = 0x7B;
        public const ushort A = 0x41;
        public const ushort C = 0x43;
        public const ushort V = 0x56;
        public const ushort X = 0x58;
        public const ushort Z = 0x5A;
    }
}
