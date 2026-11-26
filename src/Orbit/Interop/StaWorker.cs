using System.Collections.Concurrent;

namespace Orbit.Interop;

internal sealed class StaWorker
{
    private readonly BlockingCollection<Action> queue = new();

    public StaWorker(string name)
    {
        var thread = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = name,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Add(() =>
        {
            try
            {
                completion.TrySetResult(work());
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        });
        return completion.Task;
    }

    private void ProcessQueue()
    {
        foreach (var work in queue.GetConsumingEnumerable())
        {
            work();
        }
    }
}
