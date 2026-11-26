using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Orbit.Core.Office;

public sealed class OfficeHost
{
    private static readonly TimeSpan IdleBeforeQuit = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan QuitGrace = TimeSpan.FromSeconds(5);

    private readonly string progId;
    private readonly string productName;
    private readonly string processName;
    private readonly Action<dynamic> prepare;
    private readonly Func<dynamic, int> countOpenDocuments;
    private readonly BlockingCollection<Action> queue = new();
    private readonly object threadGate = new();
    private Thread? thread;
    private object? application;
    private int? ownedProcessId;
    private volatile bool stoppedByWatchdog;

    public OfficeHost(
        string progId,
        string productName,
        string processName,
        Action<dynamic> prepare,
        Func<dynamic, int> countOpenDocuments)
    {
        this.progId = progId;
        this.productName = productName;
        this.processName = processName;
        this.prepare = prepare;
        this.countOpenDocuments = countOpenDocuments;
    }

    public static OfficeHost Word { get; } = new(
        "Word.Application",
        "Word",
        "WINWORD",
        word =>
        {
            word.Visible = false;
            word.DisplayAlerts = 0;
        },
        word => (int)word.Documents.Count);

    public static OfficeHost PowerPoint { get; } = new(
        "PowerPoint.Application",
        "PowerPoint",
        "POWERPNT",
        powerPoint => powerPoint.DisplayAlerts = 1,
        powerPoint => (int)powerPoint.Presentations.Count);

    public static OfficeHost Excel { get; } = new(
        "Excel.Application",
        "Excel",
        "EXCEL",
        excel =>
        {
            excel.Visible = false;
            excel.DisplayAlerts = false;
            excel.ScreenUpdating = false;
        },
        excel => (int)excel.Workbooks.Count);

    public bool IsInstalled => Type.GetTypeFromProgID(progId) is not null;

    public Task<T> RunAsync<T>(Func<object, T> work, CancellationToken cancellation, TimeSpan? timeout = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = timeout ?? DefaultTimeout;
        EnsureThread();
        queue.Add(() =>
        {
            if (cancellation.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellation);
                return;
            }
            using var finished = new CancellationTokenSource();
            _ = StopIfStuckAsync(deadline, finished.Token);
            try
            {
                completion.TrySetResult(work(Application()));
            }
            catch (Exception error)
            {
                completion.TrySetException(Describe(error, deadline));
            }
            finally
            {
                finished.Cancel();
            }
        });
        return completion.Task;
    }

    public Task RunAsync(Action<object> work, CancellationToken cancellation, TimeSpan? timeout = null)
    {
        return RunAsync<bool>(app =>
        {
            work(app);
            return true;
        }, cancellation, timeout);
    }

    public static void QuitAllIfIdle(TimeSpan timeout)
    {
        Task[] quits = [Word.QuitAsync(), PowerPoint.QuitAsync(), Excel.QuitAsync()];
        Task.WaitAll(quits, timeout);
    }

    public Task QuitAsync()
    {
        lock (threadGate)
        {
            if (thread is not { IsAlive: true })
            {
                return Task.CompletedTask;
            }
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Add(() =>
        {
            QuitIfIdle();
            completion.TrySetResult();
        });
        return completion.Task;
    }

    private void EnsureThread()
    {
        lock (threadGate)
        {
            if (thread is { IsAlive: true })
            {
                return;
            }
            thread = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = $"Orbit {productName}",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    private void ProcessQueue()
    {
        while (true)
        {
            if (queue.TryTake(out var work, IdleBeforeQuit))
            {
                work();
                continue;
            }
            QuitIfIdle();
        }
    }

    private object Application()
    {
        if (application is not null && !stoppedByWatchdog && IsAlive(application))
        {
            return application;
        }
        ForgetApplication();
        var type = Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException($"Для этого нужен Microsoft {productName}, а он не установлен");
        var runningBefore = RunningProcessIds();
        application = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Не смог запустить {productName}");
        var startedByUs = RunningProcessIds().Except(runningBefore).ToList();
        ownedProcessId = startedByUs.Count == 1 ? startedByUs[0] : null;
        prepare(application);
        return application;
    }

    private HashSet<int> RunningProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            ids.Add(process.Id);
            process.Dispose();
        }
        return ids;
    }

    private async Task StopIfStuckAsync(TimeSpan deadline, CancellationToken finished)
    {
        try
        {
            await Task.Delay(deadline, finished);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (ownedProcessId is not int processId)
        {
            Log.Info($"{productName} не отвечает {deadline.TotalMinutes} мин, но он не наш — не трогаю");
            return;
        }
        try
        {
            using var stuck = Process.GetProcessById(processId);
            stoppedByWatchdog = true;
            stuck.Kill();
            Log.Info($"{productName} не отвечал {deadline.TotalMinutes} мин — остановил процесс {processId}");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error($"Не смог остановить зависший {productName}", error);
        }
    }

    private bool IsAlive(object candidate)
    {
        try
        {
            _ = countOpenDocuments(candidate);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidComObjectException)
        {
            return false;
        }
    }

    private void QuitIfIdle()
    {
        if (application is null || stoppedByWatchdog)
        {
            ForgetApplication();
            return;
        }
        ReleaseLeftoverReferences();
        var processToWaitFor = ownedProcessId;
        var quitRequested = false;
        try
        {
            if (countOpenDocuments(application) == 0)
            {
                dynamic runningApplication = application;
                runningApplication.Quit();
                quitRequested = true;
            }
        }
        catch (COMException error)
        {
            Log.Error($"{productName} не закрылся", error);
        }
        finally
        {
            ForgetApplication();
        }
        if (quitRequested && processToWaitFor is int processId)
        {
            EnsureExited(processId);
        }
    }

    private void EnsureExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(QuitGrace))
            {
                process.Kill();
                Log.Info($"{productName} после выхода не закрылся сам — завершил процесс {processId}");
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Info($"{productName} уже закрыт");
        }
    }

    private void ForgetApplication()
    {
        if (application is not null)
        {
            try
            {
                Marshal.FinalReleaseComObject(application);
            }
            catch (InvalidComObjectException)
            {
                Log.Info($"{productName} уже отпущен");
            }
        }
        application = null;
        ownedProcessId = null;
        stoppedByWatchdog = false;
        ReleaseLeftoverReferences();
    }

    private static void ReleaseLeftoverReferences()
    {
        for (var pass = 0; pass < 2; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private Exception Describe(Exception error, TimeSpan deadline)
    {
        if (stoppedByWatchdog)
        {
            return new TimeoutException($"{productName} не отвечал {deadline.TotalMinutes:0} мин — остановил его", error);
        }
        if (error is COMException comError)
        {
            Log.Error($"{productName} вернул ошибку", comError);
            return new InvalidOperationException($"{productName} не смог сохранить файл: {comError.Message}", comError);
        }
        return error;
    }
}
