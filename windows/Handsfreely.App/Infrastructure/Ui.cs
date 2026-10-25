using Microsoft.UI.Dispatching;

namespace Handsfreely.App.Infrastructure;

public static class Ui
{
    private static DispatcherQueue? _queue;

    public static void Init(DispatcherQueue queue) => _queue = queue;

    public static void Run(Action action)
    {
        if (_queue is null || _queue.HasThreadAccess) action();
        else _queue.TryEnqueue(() => action());
    }

    public static Task RunAsync(Func<Task> action)
    {
        if (_queue is null || _queue.HasThreadAccess) return action();
        var tcs = new TaskCompletionSource();
        _queue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        return tcs.Task;
    }
}
