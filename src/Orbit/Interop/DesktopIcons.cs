using System.Runtime.InteropServices;

namespace Orbit.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct ListViewHitTest
{
    public NativePoint Point;
    public uint Flags;
    public int Item;
    public int SubItem;
    public int Group;
}

internal static class DesktopIcons
{
    private const string ListViewClass = "SysListView32";
    private const uint HitTestMessage = 0x1012;
    private const uint ItemRectMessage = 0x100E;
    private const int IconAndLabelOfItem = 3;
    private const uint AbortIfHung = 0x0002;
    private const uint ReplyTimeoutMilliseconds = 200;
    private const uint MemoryAccess = 0x0008 | 0x0010 | 0x0020;
    private const uint ReserveAndCommit = 0x3000;
    private const uint ReadWritePages = 0x04;
    private const uint ReleaseMemory = 0x8000;
    private const int SharedBufferSize = 64;

    public static NativeRect? ItemRectAt(NativePoint point)
    {
        var listView = NativeMethods.WindowFromPoint(point);
        if (NativeMethods.ClassNameOf(listView) != ListViewClass)
        {
            return null;
        }
        NativeMethods.GetWindowThreadProcessId(listView, out var processId);
        var process = OpenProcess(MemoryAccess, false, processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }
        var sharedBuffer = IntPtr.Zero;
        try
        {
            sharedBuffer = VirtualAllocEx(process, IntPtr.Zero, SharedBufferSize, ReserveAndCommit, ReadWritePages);
            if (sharedBuffer == IntPtr.Zero)
            {
                return null;
            }
            var index = ItemIndexAt(listView, process, sharedBuffer, point);
            return index < 0 ? null : ItemRectOf(listView, process, sharedBuffer, index);
        }
        finally
        {
            if (sharedBuffer != IntPtr.Zero)
            {
                VirtualFreeEx(process, sharedBuffer, 0, ReleaseMemory);
            }
            CloseHandle(process);
        }
    }

    private static int ItemIndexAt(IntPtr listView, IntPtr process, IntPtr sharedBuffer, NativePoint point)
    {
        var pointInList = point;
        ScreenToClient(listView, ref pointInList);
        var question = new ListViewHitTest { Point = pointInList, Item = -1 };
        if (!WriteProcessMemory(process, sharedBuffer, ref question, Marshal.SizeOf<ListViewHitTest>(), out _))
        {
            return -1;
        }
        if (!Ask(listView, HitTestMessage, IntPtr.Zero, sharedBuffer, out var answer))
        {
            return -1;
        }
        return (int)answer.ToInt64();
    }

    private static NativeRect? ItemRectOf(IntPtr listView, IntPtr process, IntPtr sharedBuffer, int index)
    {
        var question = new NativeRect { Left = IconAndLabelOfItem };
        if (!WriteProcessMemory(process, sharedBuffer, ref question, Marshal.SizeOf<NativeRect>(), out _))
        {
            return null;
        }
        if (!Ask(listView, ItemRectMessage, new IntPtr(index), sharedBuffer, out var answer) || answer == IntPtr.Zero)
        {
            return null;
        }
        if (!ReadProcessMemory(process, sharedBuffer, out NativeRect itemInList, Marshal.SizeOf<NativeRect>(), out _))
        {
            return null;
        }
        var itemOnScreen = itemInList;
        MapWindowPoints(listView, IntPtr.Zero, ref itemOnScreen, 2);
        return itemOnScreen.Width > 0 && itemOnScreen.Height > 0 ? itemOnScreen : null;
    }

    private static bool Ask(IntPtr listView, uint message, IntPtr wParam, IntPtr lParam, out IntPtr answer)
    {
        var delivered = SendMessageTimeout(listView, message, wParam, lParam, AbortIfHung, ReplyTimeoutMilliseconds, out answer);
        return delivered != IntPtr.Zero;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nint size, uint allocationType, uint protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, ref ListViewHitTest value, nint size, out nint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, ref NativeRect value, nint size, out nint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, out NativeRect value, nint size, out nint read);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref NativeRect points, uint pointCount);
}
