using System.Collections.Concurrent;

namespace LocalDictation.App.Services;

/// <summary>A stalled third-party UIA provider cannot freeze the recording UI.</summary>
public sealed class AutomationWorker : IDisposable
{
    readonly BlockingCollection<Action> queue = new(2);
    readonly Thread thread;
    volatile bool unavailable;
    public AutomationWorker()
    {
        thread = new Thread(() =>
        {
            foreach (var action in queue.GetConsumingEnumerable()) action();
        }) { IsBackground = true, Name = "Local Dictation UI Automation" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }
    public async Task<T> Run<T>(Func<CancellationToken, T> action, TimeSpan? timeout = null)
    {
        if (unavailable) throw new InvalidOperationException("The target application's accessibility provider is unavailable.");
        using var cancelled = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
        var token = cancelled.Token;
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryAdd(() =>
        {
            try { token.ThrowIfCancellationRequested(); source.TrySetResult(action(token)); }
            catch (Exception ex) { source.TrySetException(ex); }
        })) throw new InvalidOperationException("The cursor safety check is busy.");
        try { return await source.Task.WaitAsync(cancelled.Token); }
        catch (OperationCanceledException)
        {
            unavailable = true;
            throw new InvalidOperationException("The target application did not answer its accessibility check. Continue in Review.");
        }
    }
    public void Dispose() => queue.CompleteAdding();
}
