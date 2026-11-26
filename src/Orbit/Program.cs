using Orbit.Interop;
using Orbit.Settings;

namespace Orbit;

internal static class Program
{
    private const string SingleInstanceName = @"Local\Orbit.SingleInstance";
    private const string ShowPanelSignalName = @"Local\Orbit.ShowPanel";

    [STAThread]
    private static void Main(string[] args)
    {
        var startedWithWindows = args.Contains(AppSettings.AutostartArgument, StringComparer.OrdinalIgnoreCase);
        using var showPanelSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowPanelSignalName);
        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (!startedWithWindows)
            {
                NativeMethods.AllowSetForegroundWindow(NativeMethods.AnyProcess);
                showPanelSignal.Set();
            }
            return;
        }
        var app = new OrbitApp(showPanelSignal, showPanelOnStart: !startedWithWindows);
        app.Run();
    }
}
