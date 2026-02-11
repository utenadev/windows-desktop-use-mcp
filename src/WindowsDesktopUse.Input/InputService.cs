using System.Diagnostics;
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
/// Service for mouse and keyboard input operations using SendInput API
/// Security-restricted: Only safe navigation keys are allowed
/// </summary>
public class InputService
{
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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
    /// Move mouse cursor to absolute position using SendInput
    /// </summary>
    public static void MoveMouse(int x, int y)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                mi = new MOUSEINPUT
                {
                    dx = x,
                    dy = y,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                    mouseData = 0
                }
            }
        };

        _ = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }

    /// <summary>
    /// Get current mouse position
    /// </summary>
    public static (int x, int y) GetMousePosition()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    /// <summary>
    /// Click mouse button using SendInput
    /// </summary>
    public static async Task ClickMouseAsync(MouseButton button, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var (downFlag, upFlag) = button switch
            {
                MouseButton.Left => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
                MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
                MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
                _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
            };

            // Send mouse down
            var downInput = new INPUT
            {
                type = INPUT_MOUSE,
                mkhi = new MOUSEKEYBDHARDWAREINPUT
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        dwFlags = downFlag,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero,
                        mouseData = 0
                    }
                }
            };

            // Send mouse up
            var upInput = new INPUT
            {
                type = INPUT_MOUSE,
                mkhi = new MOUSEKEYBDHARDWAREINPUT
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        dwFlags = upFlag,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero,
                        mouseData = 0
                    }
                }
            };

            _ = SendInput(1, new[] { downInput }, Marshal.SizeOf(typeof(INPUT)));
            _ = SendInput(1, new[] { upInput }, Marshal.SizeOf(typeof(INPUT)));

            if (i < count - 1)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Drag and drop from start to end position using SendInput
    /// </summary>
    public static async Task DragMouseAsync(int startX, int startY, int endX, int endY)
    {
        // Move to start position
        MoveMouse(startX, startY);
        await Task.Delay(50).ConfigureAwait(false);

        // Mouse down at start position
        var downInput = new INPUT
        {
            type = INPUT_MOUSE,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    dwFlags = MOUSEEVENTF_LEFTDOWN,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                    mouseData = 0
                }
            }
        };
        _ = SendInput(1, new[] { downInput }, Marshal.SizeOf(typeof(INPUT)));
        await Task.Delay(50).ConfigureAwait(false);

        // Move to end position
        MoveMouse(endX, endY);
        await Task.Delay(50).ConfigureAwait(false);

        // Mouse up at end position
        var upInput = new INPUT
        {
            type = INPUT_MOUSE,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    dwFlags = MOUSEEVENTF_LEFTUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                    mouseData = 0
                }
            }
        };
        _ = SendInput(1, new[] { upInput }, Marshal.SizeOf(typeof(INPUT)));
    }

    /// <summary>
    /// Press a virtual key using SendInput
    /// Security: Only safe navigation keys are allowed
    /// </summary>
    public static void PressKey(ushort virtualKey, KeyAction action = KeyAction.Click)
    {
        // Security check: Only allow safe navigation keys
        if (!IsAllowedKey(virtualKey))
        {
            throw new InvalidOperationException($"Key 0x{virtualKey:X2} is not allowed for security reasons. Only navigation keys (arrows, Tab, Enter, Escape, etc.) are permitted.");
        }

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

    /// <summary>
    /// Terminate the process that owns the specified window
    /// </summary>
    public static void TerminateWindowProcess(IntPtr hWnd)
    {
        _ = GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0) return;

        try
        {
            var process = Process.GetProcessById((int)processId);
            process.Kill(true);
        }
        catch (ArgumentException)
        {
            // Process already exited
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Input] Failed to terminate process {processId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if a virtual key is allowed (safe navigation keys only)
    /// </summary>
    private static bool IsAllowedKey(ushort virtualKey)
    {
        return virtualKey switch
        {
            // Navigation keys
            VirtualKeys.Enter => true,
            VirtualKeys.Tab => true,
            VirtualKeys.Escape => true,
            VirtualKeys.Space => true,
            VirtualKeys.Backspace => true,
            VirtualKeys.Delete => true,
            // Arrow keys
            VirtualKeys.Left => true,
            VirtualKeys.Up => true,
            VirtualKeys.Right => true,
            VirtualKeys.Down => true,
            // Page/Line navigation
            VirtualKeys.Home => true,
            VirtualKeys.End => true,
            VirtualKeys.PageUp => true,
            VirtualKeys.PageDown => true,
            // Everything else is forbidden
            _ => false
        };
    }

    private static void SendVirtualKey(ushort virtualKey, bool keyUp)
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

        _ = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }

    /// <summary>
    /// Common virtual key codes - Safe navigation keys only
    /// </summary>
    public static class VirtualKeys
    {
        // Navigation keys
        public const ushort Enter = 0x0D;
        public const ushort Tab = 0x09;
        public const ushort Escape = 0x1B;
        public const ushort Space = 0x20;
        public const ushort Backspace = 0x08;
        public const ushort Delete = 0x2E;

        // Arrow keys
        public const ushort Left = 0x25;
        public const ushort Up = 0x26;
        public const ushort Right = 0x27;
        public const ushort Down = 0x28;

        // Page/Line navigation
        public const ushort Home = 0x24;
        public const ushort End = 0x23;
        public const ushort PageUp = 0x21;
        public const ushort PageDown = 0x22;
    }
}
