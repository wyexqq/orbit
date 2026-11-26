using Orbit.Interop;

namespace Orbit.Explorer;

internal enum SurfaceKind
{
    Other,
    ExplorerWindow,
    Desktop,
}

internal readonly record struct Surface(SurfaceKind Kind, IntPtr RootWindow)
{
    public bool IsShell => Kind != SurfaceKind.Other;
}

internal static class ExplorerSurface
{
    private const string ShellViewClass = "SHELLDLL_DefView";
    private static readonly string[] ExplorerRootClasses = ["CabinetWClass", "ExploreWClass"];
    private static readonly string[] DesktopRootClasses = ["Progman", "WorkerW"];
    private static readonly string[] FileViewClasses = ["DirectUIHWND", "SysListView32"];

    public static Surface FileViewAt(NativePoint point)
    {
        var window = NativeMethods.WindowFromPoint(point);
        if (window == IntPtr.Zero)
        {
            return new Surface(SurfaceKind.Other, IntPtr.Zero);
        }
        var isFileView = FileViewClasses.Contains(NativeMethods.ClassNameOf(window))
            && NativeMethods.ClassNameOf(NativeMethods.GetParent(window)) == ShellViewClass;
        if (!isFileView)
        {
            return new Surface(SurfaceKind.Other, IntPtr.Zero);
        }
        return Classify(NativeMethods.GetAncestor(window, NativeMethods.RootAncestor));
    }

    public static Surface Foreground()
    {
        return Classify(NativeMethods.GetForegroundWindow());
    }

    public static bool IsShellForeground()
    {
        return Foreground().IsShell;
    }

    private static Surface Classify(IntPtr root)
    {
        var className = NativeMethods.ClassNameOf(root);
        if (ExplorerRootClasses.Contains(className))
        {
            return new Surface(SurfaceKind.ExplorerWindow, root);
        }
        if (DesktopRootClasses.Contains(className))
        {
            return new Surface(SurfaceKind.Desktop, root);
        }
        return new Surface(SurfaceKind.Other, IntPtr.Zero);
    }
}
