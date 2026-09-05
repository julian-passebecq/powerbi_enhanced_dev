using PbiBench.Core.Tasks;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class BackgroundTaskQueueTests
{
    [Fact]
    public async Task AlreadyCanceledCallerTokenPreventsTheOperationFromStarting()
    {
        using var queue = new BackgroundTaskQueue(); using var caller = new CancellationTokenSource(); caller.Cancel(); var ran = false;
        var job = queue.Enqueue("Canceled before enqueue", _ => { ran = true; return Task.FromResult(1); }, caller.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.Completion); Assert.False(ran);
        Assert.Equal(BackgroundTaskState.Canceled, Assert.Single(queue.Snapshot()).State);
    }
    [Fact]
    public async Task QueueBoundsConcurrencyAndCapacityWhileKeepingWorkOffTheCallerThread()
    {
        using var queue = new BackgroundTaskQueue(1, 2); var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = 0; var maximum = 0; var caller = Environment.CurrentManagedThreadId; var worker = caller;
        var first = queue.Enqueue("first", async context => { worker = Environment.CurrentManagedThreadId; maximum = Math.Max(maximum, Interlocked.Increment(ref current)); context.Report(25, "Reading"); started.SetResult(true); await release.Task; Interlocked.Decrement(ref current); return 10; });
        await started.Task;
        var second = queue.Enqueue("second", context => { maximum = Math.Max(maximum, Interlocked.Increment(ref current)); Interlocked.Decrement(ref current); return Task.FromResult(20); });
        Assert.Throws<InvalidOperationException>(() => queue.Enqueue("overflow", _ => Task.FromResult(0))); Assert.True(worker > 0);
        Assert.Equal(25, queue.Snapshot().Single(item => item.Id == first.Id).Progress); Assert.Equal(BackgroundTaskState.Queued, queue.Snapshot().Single(item => item.Id == second.Id).State);
        release.SetResult(true); Assert.Equal(10, await first.Completion); Assert.Equal(20, await second.Completion); Assert.Equal(1, maximum); Assert.All(queue.Snapshot(), item => Assert.Equal(BackgroundTaskState.Succeeded, item.State));
    }
    [Fact]
    public async Task QueuedAndRunningCancellationDoNotCancelOtherWork()
    {
        using var queue = new BackgroundTaskQueue(1, 3); var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var ranCanceled = false;
        var first = queue.Enqueue("running", async context => { started.SetResult(true); await Task.Delay(Timeout.Infinite, context.CancellationToken); return 1; }); await started.Task;
        var second = queue.Enqueue("queued cancellation", _ => { ranCanceled = true; return Task.FromResult(2); }); var third = queue.Enqueue("survivor", _ => Task.FromResult(3));
        second.Cancel(); first.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.Completion); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.Completion);
        Assert.Equal(3, await third.Completion); Assert.False(ranCanceled); Assert.Equal(2, queue.Snapshot().Count(item => item.State == BackgroundTaskState.Canceled));
        queue.ClearCompleted(); Assert.Empty(queue.Snapshot());
    }
    [Fact]
    public async Task FailuresAreVisibleWithoutExceptionMessageLeaksAndObserversCannotCorruptOutcomes()
    {
        using var queue = new BackgroundTaskQueue(); queue.Changed += (_, _) => throw new InvalidOperationException("observer");
        var failed = queue.Enqueue<int>("Fail safely", _ => throw new InvalidOperationException("Password=DO_NOT_LOG;"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.Completion); var item = Assert.Single(queue.Snapshot()); Assert.Equal(BackgroundTaskState.Failed, item.State);
        Assert.Equal("InvalidOperationException", item.Error); Assert.DoesNotContain("DO_NOT_LOG", item.ToString());
        Assert.Equal(5, await queue.Enqueue("next", _ => Task.FromResult(5)).Completion);
    }
    [Fact]
    public async Task LateCancellationDoesNotMislabelAnOperationThatActuallyCompleted()
    {
        using var queue = new BackgroundTaskQueue(); var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = queue.Enqueue("noncancelable completion", async _ => { started.SetResult(true); await finish.Task; return 7; }); await started.Task; job.Cancel(); finish.SetResult(true);
        Assert.Equal(7, await job.Completion); var item = Assert.Single(queue.Snapshot()); Assert.Equal(BackgroundTaskState.Succeeded, item.State); Assert.True(item.CancellationRequested); Assert.False(queue.Cancel(job.Id));
    }
    [Fact]
    public async Task DisposeCancelsCooperativeWorkWithoutBlockingAndRejectsNewTasks()
    {
        var queue = new BackgroundTaskQueue(); var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = queue.Enqueue("long read", async context => { started.SetResult(true); await Task.Delay(Timeout.Infinite, context.CancellationToken); return 1; }); await started.Task;
        queue.Dispose(); Assert.Throws<ObjectDisposedException>(() => queue.Enqueue("late", _ => Task.FromResult(1))); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.Completion);
    }
}
