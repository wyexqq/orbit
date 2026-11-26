using System.Runtime.InteropServices;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Orbit.Interop;

[ComImport]
[Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IShellWindows
{
    int Count { get; }

    [return: MarshalAs(UnmanagedType.IDispatch)]
    object Item(object index);

    [return: MarshalAs(UnmanagedType.IUnknown)]
    object NewEnum();

    void Register();

    void RegisterPending();

    void Revoke();

    void OnNavigate();

    void OnActivated();

    [return: MarshalAs(UnmanagedType.IDispatch)]
    object FindWindowSW(ref object location, ref object locationRoot, int windowClass, out int window, int options);
}

[ComImport]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IComServiceProvider
{
    [PreserveSig]
    int QueryService(ref Guid service, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object result);
}

[ComImport]
[Guid("000214E2-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowser
{
    void GetWindow();

    void ContextSensitiveHelp();

    void InsertMenusSB();

    void SetMenuSB();

    void RemoveMenusSB();

    void SetStatusTextSB();

    void EnableModelessSB();

    void TranslateAcceleratorSB();

    void BrowseObject();

    void GetViewStateStream();

    void GetControlWindow();

    void SendControlMsg();

    [PreserveSig]
    int QueryActiveShellView(out IShellView shellView);
}

[ComImport]
[Guid("000214E3-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellView
{
    void GetWindow();

    void ContextSensitiveHelp();

    void TranslateAccelerator();

    void EnableModeless();

    void UIActivate();

    void Refresh();

    void CreateViewWindow();

    void DestroyViewWindow();

    void GetCurrentInfo();

    void AddPropertySheetPages();

    void SaveViewState();

    void SelectItem();

    [PreserveSig]
    int GetItemObject(uint item, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object result);
}

[ComImport]
[Guid("4657278B-411B-11D2-839A-00C04FD918D0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTargetHelper
{
    [PreserveSig]
    int DragEnter(IntPtr target, ComDataObject data, ref NativePoint point, int effect);

    [PreserveSig]
    int DragLeave();

    [PreserveSig]
    int DragOver(ref NativePoint point, int effect);

    [PreserveSig]
    int Drop(ComDataObject data, ref NativePoint point, int effect);

    [PreserveSig]
    int Show([MarshalAs(UnmanagedType.Bool)] bool show);
}

internal static class ShellGuids
{
    public static readonly Guid ShellWindowsClass = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    public static readonly Guid TopLevelBrowserService = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    public static readonly Guid ShellBrowserInterface = new("000214E2-0000-0000-C000-000000000046");
    public static readonly Guid DispatchInterface = new("00020400-0000-0000-C000-000000000046");
    public static readonly Guid DragDropHelperClass = new("4657278A-411B-11D2-839A-00C04FD918D0");
}
