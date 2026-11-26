using System.Windows;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Core.Office;
using Orbit.Input;
using Orbit.Ring;
using Orbit.Settings;
using Orbit.Ui;

namespace Orbit;

internal sealed class OrbitApp(EventWaitHandle showPanelSignal, bool showPanelOnStart) : Application
{
    private InputHooks? hooks;
    private TrayIcon? tray;
    private RegisteredWaitHandle? showPanelWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Необработанная ошибка в задаче", args.Exception);
            args.SetObserved();
        };

        var settings = AppSettings.Load();
        var ringWindow = new RingWindow();
        var toast = new ToastWindow(settings);
        tray = new TrayIcon(settings);
        hooks = new InputHooks(Dispatcher);
        var coordinator = new ConversionCoordinator(Dispatcher, toast, tray);
        var controller = new RingController(settings, hooks, ringWindow, toast, coordinator);
        tray.SettingsChanged += controller.ApplySettings;
        tray.ExitRequested += Shutdown;
        hooks.Start();
        var panel = new ControlPanel();
        panel.TurnOffRequested += Shutdown;
        showPanelWait = ThreadPool.RegisterWaitForSingleObject(
            showPanelSignal,
            (_, _) => Dispatcher.BeginInvoke(() => panel.ShowPanel(justStarted: false)),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        if (showPanelOnStart)
        {
            panel.ShowPanel(justStarted: true);
        }
        Log.Info("Orbit запущен");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        showPanelWait?.Unregister(null);
        hooks?.Dispose();
        tray?.Dispose();
        OfficeHost.QuitAllIfIdle(TimeSpan.FromSeconds(8));
        Log.Info("Orbit закрыт");
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Необработанная ошибка", e.Exception);
        e.Handled = true;
    }
}
