using System.Runtime.InteropServices;
using Orbit.Core;
using Orbit.Interop;

namespace Orbit.Explorer;

internal static class ExplorerSelection
{
    private const int DesktopFolderId = 0;
    private const int DesktopWindowClass = 8;
    private const int NeedDispatchOption = 1;
    private const uint BackgroundViewItem = 0;

    private static readonly StaWorker Worker = new("Orbit Shell");

    public static Task<IReadOnlyList<string>> ReadAsync(Surface surface)
    {
        return Worker.RunAsync(() => Read(surface));
    }

    private static IReadOnlyList<string> Read(Surface surface)
    {
        try
        {
            var shellWindows = CreateShellWindows();
            var folderView = surface.Kind == SurfaceKind.Desktop
                ? DesktopFolderView(shellWindows)
                : ExplorerFolderView(shellWindows, surface.RootWindow);
            return folderView is null ? [] : SelectedPaths(folderView);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Log.Error("Не прочитал выделение в Проводнике", error);
            return [];
        }
    }

    private static IShellWindows CreateShellWindows()
    {
        var type = Type.GetTypeFromCLSID(ShellGuids.ShellWindowsClass)!;
        return (IShellWindows)Activator.CreateInstance(type)!;
    }

    private static object? ExplorerFolderView(IShellWindows shellWindows, IntPtr rootWindow)
    {
        var count = shellWindows.Count;
        for (var index = 0; index < count; index++)
        {
            dynamic? window = shellWindows.Item(index);
            if (window is null)
            {
                continue;
            }
            long handle;
            try
            {
                handle = Convert.ToInt64(window.HWND);
            }
            catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                continue;
            }
            if (handle == rootWindow.ToInt64())
            {
                return window.Document;
            }
        }
        return null;
    }

    private static object? DesktopFolderView(IShellWindows shellWindows)
    {
        object location = DesktopFolderId;
        object locationRoot = null!;
        var desktop = shellWindows.FindWindowSW(ref location, ref locationRoot, DesktopWindowClass, out _, NeedDispatchOption);
        var serviceProvider = (IComServiceProvider)desktop;
        var service = ShellGuids.TopLevelBrowserService;
        var browserInterface = ShellGuids.ShellBrowserInterface;
        if (serviceProvider.QueryService(ref service, ref browserInterface, out var browserObject) != 0)
        {
            return null;
        }
        var browser = (IShellBrowser)browserObject;
        if (browser.QueryActiveShellView(out var shellView) != 0)
        {
            return null;
        }
        var dispatch = ShellGuids.DispatchInterface;
        return shellView.GetItemObject(BackgroundViewItem, ref dispatch, out var folderView) == 0 ? folderView : null;
    }

    private static IReadOnlyList<string> SelectedPaths(object folderView)
    {
        dynamic view = folderView;
        dynamic items = view.SelectedItems();
        var count = (int)items.Count;
        var paths = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            dynamic item = items.Item(index);
            string path = item.Path;
            if (File.Exists(path))
            {
                paths.Add(path);
            }
        }
        return paths;
    }
}
