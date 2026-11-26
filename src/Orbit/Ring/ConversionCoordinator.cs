using System.Diagnostics;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Core.Conversion;
using Orbit.Ui;

namespace Orbit.Ring;

internal sealed class ConversionCoordinator(Dispatcher dispatcher, ToastWindow toast, TrayIcon tray)
{
    private static readonly TimeSpan InRingBudget = TimeSpan.FromMilliseconds(700);
    private const string NeutralAccent = "#2F6FE0";

    private readonly SemaphoreSlim queue = new(1, 1);
    private object? toastOwner;
    private int runningInBackground;

    public async Task RunAsync(
        ConversionRequest request,
        string accent,
        Action<double> showRingProgress,
        Func<Task> finishInRing,
        Action releaseRing)
    {
        var job = new object();
        var showsInRing = true;
        var cancellation = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();
        var progress = new SyncProgress<RunProgress>(update => dispatcher.BeginInvoke(() =>
        {
            if (showsInRing)
            {
                showRingProgress(update.Overall);
            }
            else if (toastOwner == job)
            {
                toast.UpdateProgress(update.Overall, Texts.ProgressLine(update));
            }
        }));
        var running = RunQueuedAsync(request, progress, cancellation.Token);
        var finishedQuickly = await Task.WhenAny(running, Task.Delay(InRingBudget)) == running;
        if (finishedQuickly)
        {
            var quickResult = await running;
            if (quickResult.SucceededCount > 0)
            {
                await finishInRing();
            }
            else
            {
                releaseRing();
            }
            Report(quickResult, stopwatch.Elapsed, job);
            return;
        }
        showsInRing = false;
        releaseRing();
        toastOwner = job;
        toast.ShowContent(new ToastContent(
            Texts.ProgressTitle(request),
            "0% · идёт в фоне",
            "Отменить",
            cancellation.Cancel,
            0,
            accent));
        SetBackgroundCount(runningInBackground + 1);
        var result = await running;
        SetBackgroundCount(runningInBackground - 1);
        Report(result, stopwatch.Elapsed, job);
    }

    private async Task<ConversionResult> RunQueuedAsync(
        ConversionRequest request,
        IProgress<RunProgress> progress,
        CancellationToken cancellation)
    {
        try
        {
            await queue.WaitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            return new ConversionResult(request, [], [], WasCancelled: true);
        }
        try
        {
            return await Task.Run(() => ConversionRunner.RunAsync(request, progress, cancellation));
        }
        finally
        {
            queue.Release();
        }
    }

    private void Report(ConversionResult result, TimeSpan elapsed, object job)
    {
        if (result.WasCancelled)
        {
            UndoQuietly(result, job);
            return;
        }
        toastOwner = job;
        if (result.SucceededCount == 0)
        {
            var reason = result.Failures.FirstOrDefault()?.Error ?? "Неизвестная ошибка";
            toast.ShowContent(new ToastContent(Texts.FailureTitle(result), reason, AutoHideMilliseconds: 7000));
            return;
        }
        toast.ShowContent(new ToastContent(
            Texts.DoneTitle(result),
            Texts.DoneSubtitle(result, elapsed),
            "Отменить",
            () => UndoWithToast(result, job),
            AutoHideMilliseconds: 6000));
    }

    private async void UndoQuietly(ConversionResult result, object job)
    {
        var problems = await Task.Run(() => ConversionRunner.Undo(result));
        toastOwner = job;
        var sub = problems.Count == 0 ? "Файлы не тронуты" : $"Не вернул: {string.Join(", ", problems)}";
        toast.ShowContent(new ToastContent("Отменено", sub, AutoHideMilliseconds: 2500));
    }

    private async void UndoWithToast(ConversionResult result, object job)
    {
        toast.ShowContent(new ToastContent("Возвращаю как было…", string.Empty, Accent: NeutralAccent));
        try
        {
            var problems = await Task.Run(() => ConversionRunner.Undo(result));
            toastOwner = job;
            var sub = problems.Count == 0 ? string.Empty : $"Не вернул: {string.Join(", ", problems)}";
            toast.ShowContent(new ToastContent("Вернул как было", sub, AutoHideMilliseconds: 2500));
        }
        catch (Exception error)
        {
            Log.Error("Отмена не удалась", error);
            toast.ShowContent(new ToastContent("Не получилось отменить", error.Message, AutoHideMilliseconds: 5000));
        }
    }

    private void SetBackgroundCount(int count)
    {
        runningInBackground = Math.Max(0, count);
        tray.ShowBusy(runningInBackground > 0);
    }
}
