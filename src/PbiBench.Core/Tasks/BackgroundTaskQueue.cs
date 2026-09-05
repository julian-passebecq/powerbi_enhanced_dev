namespace PbiBench.Core.Tasks;

public enum BackgroundTaskState { Queued, Running, Succeeded, Failed, Canceled }
public sealed record BackgroundTaskInfo(Guid Id, string Title, BackgroundTaskState State, double? Progress,
    string Message, DateTimeOffset QueuedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
    bool CancellationRequested, string? Error);

public sealed class BackgroundTaskContext
{
    private readonly Action<double?, string> report;
    internal BackgroundTaskContext(CancellationToken token, Action<double?, string> report) { CancellationToken = token; this.report = report; }
    public CancellationToken CancellationToken { get; }
    public void Report(double? percent, string message)
    {
        if (percent.HasValue && (double.IsNaN(percent.Value) || percent < 0 || percent > 100)) throw new ArgumentOutOfRangeException(nameof(percent));
        report(percent, message ?? string.Empty);
    }
}

public sealed class BackgroundTaskHandle<T>
{
    private readonly Action cancel;
    internal BackgroundTaskHandle(Guid id, Task<T> completion, Action cancel) { Id = id; Completion = completion; this.cancel = cancel; }
    public Guid Id { get; }
    public Task<T> Completion { get; }
    public void Cancel() => cancel();
}

/// <summary>Bounded worker queue. Callers capture detached input before enqueueing; work must own any engine connection.
/// Only display status is retained, never operation results, transport credentials or exception messages.</summary>
public sealed class BackgroundTaskQueue : IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, Entry> entries = new();
    private readonly SemaphoreSlim concurrency;
    private readonly int capacity;
    private bool disposed;
    private int outstanding;
    private sealed class Entry
    {
        internal Entry(BackgroundTaskInfo info, CancellationTokenSource cancellation) { Info = info; Cancellation = cancellation; }
        internal BackgroundTaskInfo Info;
        internal readonly CancellationTokenSource Cancellation;
        internal Task? CancellationDispatch;
        internal CancellationTokenRegistration CallerCancellation;
    }

    public BackgroundTaskQueue(int maxConcurrency = 2, int capacity = 32)
    {
        if (maxConcurrency < 1 || maxConcurrency > 16) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        if (capacity < maxConcurrency || capacity > 1000) throw new ArgumentOutOfRangeException(nameof(capacity));
        concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency); this.capacity = capacity;
    }
    public event EventHandler? Changed;
    public IReadOnlyList<BackgroundTaskInfo> Snapshot() { lock (sync) return entries.Values.Select(e => e.Info).OrderByDescending(e => e.QueuedAt).ToArray(); }

    public BackgroundTaskHandle<T> Enqueue<T>(string title, Func<BackgroundTaskContext, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A task title is required.", nameof(title));
        if (work == null) throw new ArgumentNullException(nameof(work));
        Entry entry;
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BackgroundTaskQueue));
            if (outstanding >= capacity) throw new InvalidOperationException("The background task queue is full. Cancel or finish an existing task first.");
            var id = Guid.NewGuid();
            entry = new Entry(new(id, title, BackgroundTaskState.Queued, null, "Queued", DateTimeOffset.UtcNow, null, null, false, null),
                new CancellationTokenSource());
            entries.Add(id, entry); outstanding++;
        }
        try { entry.CallerCancellation = cancellationToken.Register(() => Cancel(entry.Info.Id)); }
        catch
        {
            lock (sync) { entries.Remove(entry.Info.Id); outstanding--; }
            entry.Cancellation.Dispose(); throw;
        }
        Notify();
        _ = Task.Run(async () =>
        {
            var acquired = false;
            try
            {
                await concurrency.WaitAsync(entry.Cancellation.Token).ConfigureAwait(false); acquired = true;
                entry.Cancellation.Token.ThrowIfCancellationRequested();
                if (IsCancellationRequested(entry)) throw new OperationCanceledException(entry.Cancellation.Token);
                Update(entry, i => i with { State = BackgroundTaskState.Running, StartedAt = DateTimeOffset.UtcNow, Message = "Running" });
                var context = new BackgroundTaskContext(entry.Cancellation.Token, (percent, message) => Update(entry, i =>
                    i.FinishedAt.HasValue ? i : i with { Progress = percent, Message = message }));
                var result = await work(context).ConfigureAwait(false);
                Finish(entry, BackgroundTaskState.Succeeded, "Completed", null);
                completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (IsCancellationRequested(entry))
            { Finish(entry, BackgroundTaskState.Canceled, "Canceled", null); completion.TrySetCanceled(); }
            catch (Exception error)
            {
                // Exception messages can contain connection strings or query values. Consumers receive the exception through Completion.
                Finish(entry, BackgroundTaskState.Failed, "Failed", error.GetType().Name); completion.TrySetException(error);
            }
            finally
            {
                if (acquired) concurrency.Release();
                entry.CallerCancellation.Dispose();
                Task? cancellationDispatch; lock (sync) cancellationDispatch = entry.CancellationDispatch;
                if (cancellationDispatch != null) await cancellationDispatch.ConfigureAwait(false);
                entry.Cancellation.Dispose();
            }
        });
        return new BackgroundTaskHandle<T>(entry.Info.Id, completion.Task, () => Cancel(entry.Info.Id));
    }

    public bool Cancel(Guid id)
    {
        Entry? entry;
        lock (sync)
        {
            if (!entries.TryGetValue(id, out entry) || entry.Info.FinishedAt.HasValue) return false;
            if (entry.Info.CancellationRequested) return true;
            entry.Info = entry.Info with { CancellationRequested = true, Message = "Cancellation requested" };
            entry.CancellationDispatch = Task.Run(() =>
            {
                // User cancellation must not synchronously run an adapter's potentially blocking callbacks on the UI thread.
                try { entry.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { /* The operation finished between lookup and cancellation. */ }
                catch (AggregateException) { /* An operation's cancellation callback cannot break queue control. */ }
            });
        }
        Notify(); return true;
    }
    public void ClearCompleted()
    {
        lock (sync) foreach (var id in entries.Where(e => e.Value.Info.FinishedAt.HasValue).Select(e => e.Key).ToArray()) entries.Remove(id);
        Notify();
    }
    private void Update(Entry entry, Func<BackgroundTaskInfo, BackgroundTaskInfo> update) { lock (sync) entry.Info = update(entry.Info); Notify(); }
    private bool IsCancellationRequested(Entry entry) { lock (sync) return entry.Info.CancellationRequested || entry.Cancellation.IsCancellationRequested; }
    private void Finish(Entry entry, BackgroundTaskState state, string message, string? error)
    {
        lock (sync)
        {
            entry.Info = entry.Info with { State = state, Message = message, Progress = state == BackgroundTaskState.Succeeded ? 100 : entry.Info.Progress,
                CancellationRequested = entry.Info.CancellationRequested || entry.Cancellation.IsCancellationRequested, FinishedAt = DateTimeOffset.UtcNow, Error = error };
            outstanding--;
            foreach (var id in entries.Values.Where(e => e.Info.FinishedAt.HasValue).OrderByDescending(e => e.Info.FinishedAt).Skip(100).Select(e => e.Info.Id).ToArray()) entries.Remove(id);
        }
        Notify();
    }
    private void Notify()
    {
        var handlers = Changed;
        if (handlers == null) return;
        foreach (EventHandler handler in handlers.GetInvocationList()) try { handler(this, EventArgs.Empty); } catch { /* UI observers do not own task outcomes. */ }
    }
    public void Dispose()
    {
        Guid[] pending;
        lock (sync) { if (disposed) return; disposed = true; pending = entries.Values.Where(e => !e.Info.FinishedAt.HasValue).Select(e => e.Info.Id).ToArray(); }
        foreach (var id in pending) Cancel(id);
        // Cancellation remains cooperative; disposing the view or app must never block the UI thread.
    }
}
