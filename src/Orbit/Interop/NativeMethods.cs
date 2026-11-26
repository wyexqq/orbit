using System.Runtime.InteropServices;
using System.Text;

namespace Orbit.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;

    public NativePoint(int x, int y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;

    public readonly bool Contains(NativePoint point)
    {
        return point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct LowLevelMouseInfo
{
    public NativePoint Point;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LowLevelKeyboardInfo
{
    public uint VirtualKey;
    public uint ScanCode;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    public IntPtr Window;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public NativePoint Point;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo
{
    public int Size;
    public NativeRect Monitor;
    public NativeRect WorkArea;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GuiThreadInfo
{
    public int Size;
    public uint Flags;
    public IntPtr Active;
    public IntPtr Focus;
    public IntPtr Capture;
    public IntPtr MenuOwner;
    public IntPtr MoveSize;
    public IntPtr Caret;
    public NativeRect CaretRect;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardInput
{
    public ushort VirtualKey;
    public ushort ScanCode;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct InputUnion
{
    [FieldOffset(0)]
    public KeyboardInput Keyboard;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeInput
{
    public uint Type;
    public InputUnion Data;
}

internal delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

internal static class NativeMethods
{
    public const int MouseLowLevelHook = 14;
    public const int KeyboardLowLevelHook = 13;
    public const int LeftButtonDown = 0x0201;
    public const int LeftButtonUp = 0x0202;
    public const int MouseMove = 0x0200;
    public const int KeyDown = 0x0100;
    public const int KeyUp = 0x0101;
    public const int SystemKeyDown = 0x0104;
    public const int SystemKeyUp = 0x0105;
    public const uint QuitMessage = 0x0012;
    public const uint AltDownFlag = 0x20;
    public const uint InjectedKeyFlag = 0x10;
    public const int LeftMouseButton = 0x01;
    public const int RightMouseButton = 0x02;
    public const int SwappedButtonsMetric = 23;
    public const int ControlKey = 0x11;
    public const int ShiftKey = 0x10;
    public const int MenuMaskKey = 0xE8;
    public const uint KeyboardInputType = 1;
    public const uint KeyUpInputFlag = 0x0002;
    public const uint RootAncestor = 2;
    public const uint DefaultToNearestMonitor = 2;
    public const uint MoveSizeFlag = 0x0002;
    public const int ExtendedStyle = -20;
    public const long NoActivateStyle = 0x08000000;
    public const long ToolWindowStyle = 0x00000080;
    public const long TopmostStyle = 0x00000008;
    public const int MouseActivateMessage = 0x0021;
    public const int NoActivateReply = 3;
    public const uint NoSizeFlag = 0x0001;
    public const uint NoMoveFlag = 0x0002;
    public const uint NoZOrderFlag = 0x0004;
    public const uint NoActivateFlag = 0x0010;
    public const uint ShowWindowFlag = 0x0040;
    public const int AnyProcess = -1;
    public static readonly IntPtr TopmostWindow = new(-1);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int hookType, HookProcedure procedure, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, NativeInput[] inputs, int size);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    public static string ClassNameOf(IntPtr window)
    {
        var className = new StringBuilder(256);
        return GetClassName(window, className, className.Capacity) > 0 ? className.ToString() : string.Empty;
    }

    public static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    public static bool IsPrimaryButtonDown()
    {
        var buttonsSwapped = GetSystemMetrics(SwappedButtonsMetric) != 0;
        return IsKeyDown(buttonsSwapped ? RightMouseButton : LeftMouseButton);
    }

    public static NativePoint CursorPosition()
    {
        GetCursorPos(out var point);
        return point;
    }

    public static bool IsForegroundWindowMovingOrSizing()
    {
        var foreground = GetForegroundWindow();
        var threadId = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(threadId, ref info) && (info.Flags & MoveSizeFlag) != 0;
    }

    public static void TapMenuMaskKey()
    {
        var inputs = new[]
        {
            KeyInput(MenuMaskKey, 0),
            KeyInput(MenuMaskKey, KeyUpInputFlag),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
    }

    public static (NativeRect WorkArea, double Scale) MonitorAt(NativePoint point)
    {
        var monitor = MonitorFromPoint(point, DefaultToNearestMonitor);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        var scale = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
        return (info.WorkArea, scale);
    }

    public static void MakeNonActivatingToolWindow(IntPtr window)
    {
        var style = GetWindowLongPtr(window, ExtendedStyle).ToInt64();
        style |= NoActivateStyle | ToolWindowStyle | TopmostStyle;
        SetWindowLongPtr(window, ExtendedStyle, new IntPtr(style));
    }

    private static NativeInput KeyInput(int virtualKey, uint flags)
    {
        return new NativeInput
        {
            Type = KeyboardInputType,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput { VirtualKey = (ushort)virtualKey, Flags = flags },
            },
        };
    }
}
