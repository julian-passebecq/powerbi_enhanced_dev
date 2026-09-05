using System.IO;
using System.Windows.Threading;
using PbiBench.App;
using PbiBench.Core.Tasks;
using PbiBench.Semantic.Workspaces;
using PbiBench.Workspace;
using TabularEditor.TOMWrapper;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class WorkspaceSyncViewTests
{
    [Fact]
    public Task FirstTmdlComparisonIsNotInvalidatedByItsOwnReadOnlyCapture() => Sta(async () =>
    {
        using var temp = new TemporaryWorkspace(); using var handler = new TabularModelHandler(1702); handler.Model.AddTable("Original"); var codec = new TmdlWorkspaceCodec();
        foreach (var file in codec.Serialize(codec.CaptureLoaded(handler), false)) { var path = WorkspaceDiskStore.SafePath(temp.Root, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, file.Content); }
        using var view = new WorkspaceSyncView(() => handler, () => { }, settingsDirectory: Path.Combine(temp.Root, "profile")); view.Configure(temp.Root, null, null);
        await view.CompareAsync(); Assert.True(view.LastComparison != null, view.Status); Assert.Empty(view.LastComparison!.Changes);
    });
    [Fact]
    public Task QueuedOldWatcherNotificationCannotInvalidateTheNewFolder() => Sta(async () =>
    {
        using var temp = new TemporaryWorkspace(); using var handler = new TabularModelHandler(1702); var snapshot = new TmdlWorkspaceCodec().CaptureLoaded(handler);
        var first = Path.Combine(temp.Root, "first"); var second = Path.Combine(temp.Root, "second"); Directory.CreateDirectory(first); Directory.CreateDirectory(second); File.WriteAllText(Path.Combine(first, "model.bim"), snapshot.DatabaseJson); File.WriteAllText(Path.Combine(second, "model.bim"), snapshot.DatabaseJson);
        using var view = new WorkspaceSyncView(() => null, () => { }, settingsDirectory: Path.Combine(temp.Root, "profile")); view.Configure(first, null, null);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var oldWatcher = typeof(WorkspaceSyncView).GetField("watcher", flags)!.GetValue(view);
        var notify = typeof(WorkspaceSyncView).GetMethod("OnDiskChanged", flags)!;
        notify.Invoke(view, new[] { oldWatcher, EventArgs.Empty }); // Queue before rebinding, execute afterward.
        view.Configure(second, null, null); var configuredStatus = view.Status;
        await Dispatcher.Yield(DispatcherPriority.Background); Assert.Equal(configuredStatus, view.Status);
        await view.CompareAsync(); Assert.NotNull(view.LastComparison); var comparison = view.LastComparison; var comparedStatus = view.Status;
        notify.Invoke(view, new[] { oldWatcher, EventArgs.Empty }); // A late callback after the new comparison.
        await Dispatcher.Yield(DispatcherPriority.Background); Assert.Same(comparison, view.LastComparison); Assert.Equal(comparedStatus, view.Status);
    });
    [Fact]
    public Task DelayedNotificationDoesNotInvalidateAnAlreadyCapturedDiskSequence() => Sta(async () =>
    {
        using var temp = new TemporaryWorkspace(); using var handler = new TabularModelHandler(1702); var codec = new TmdlWorkspaceCodec(); var original = codec.CaptureLoaded(handler); var path = Path.Combine(temp.Root, "model.bim"); File.WriteAllText(path, original.DatabaseJson);
        using var view = new WorkspaceSyncView(() => null, () => { }, settingsDirectory: Path.Combine(temp.Root, "profile")); view.Configure(temp.Root, null, null); await view.CompareAsync(); Assert.NotNull(view.LastComparison);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var watcher = (WorkspaceWatcher)typeof(WorkspaceSyncView).GetField("watcher", flags)!.GetValue(view)!;
        var notification = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); watcher.Changed += (_, _) => notification.TrySetResult(true);
        handler.Model.Description = "External disk update"; var updated = codec.CaptureLoaded(handler); File.WriteAllText(path, updated.DatabaseJson);
        Assert.Same(notification.Task, await Task.WhenAny(notification.Task, Task.Delay(5000))); await Dispatcher.Yield(DispatcherPriority.Background); Assert.Null(view.LastComparison);
        await view.CompareAsync(); Assert.NotNull(view.LastComparison); var comparison = view.LastComparison; Assert.Equal(updated.Hash, comparison!.Disk.Hash);
        typeof(WorkspaceSyncView).GetMethod("OnDiskChanged", flags)!.Invoke(view, new object[] { watcher, EventArgs.Empty });
        await Dispatcher.Yield(DispatcherPriority.Background); Assert.Same(comparison, view.LastComparison);
    });
    [Fact]
    public Task ActualDiskEditDuringQueuedComparisonStillRejectsTheCapture() => Sta(async () =>
    {
        using var temp = new TemporaryWorkspace(); using var handler = new TabularModelHandler(1702); var codec = new TmdlWorkspaceCodec(); var path = Path.Combine(temp.Root, "model.bim"); File.WriteAllText(path, codec.CaptureLoaded(handler).DatabaseJson);
        using var queue = new BackgroundTaskQueue(1); var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupying = queue.Enqueue("Controlled earlier operation", async _ => { entered.TrySetResult(true); return await release.Task; }); await entered.Task;
        using var view = new WorkspaceSyncView(() => null, () => { }, queue, Path.Combine(temp.Root, "profile")); view.Configure(temp.Root, null, null);
        var watcher = (WorkspaceWatcher)typeof(WorkspaceSyncView).GetField("watcher", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
        var comparison = view.CompareAsync();
        try
        {
            handler.Model.Description = "Written while comparison was queued"; File.WriteAllText(path, codec.CaptureLoaded(handler).DatabaseJson);
            var deadline = DateTime.UtcNow.AddSeconds(5); while (watcher.Sequence == 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
            Assert.True(watcher.Sequence > 0);
        }
        finally { release.TrySetResult(true); }
        await occupying.Completion; await comparison; Assert.Null(view.LastComparison); Assert.Contains("changed during capture", view.Status);
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CompletedOldSynchronizationOnlyPersistsToItsOriginalStore(bool switchHandler) => Sta(async () =>
    {
        using var temp = new TemporaryWorkspace(); using var handler = new TabularModelHandler(1702); handler.Model.AddTable("Original"); var snapshot = new TmdlWorkspaceCodec().CaptureLoaded(handler);
        var first = Path.Combine(temp.Root, "first"); var second = Path.Combine(temp.Root, "second"); Directory.CreateDirectory(first); Directory.CreateDirectory(second); File.WriteAllText(Path.Combine(first, "model.bim"), snapshot.DatabaseJson); File.WriteAllText(Path.Combine(second, "model.bim"), snapshot.DatabaseJson);
        TabularModelHandler? current = handler; using var queue = new BackgroundTaskQueue(); var notifications = 0; using var view = new WorkspaceSyncView(() => current, () => notifications++, queue, temp.Root);
        view.Configure(first, null, null); var completeOriginal = view.CaptureSynchronizationCompletion();
        if (switchHandler) { current = null; view.Configure(first, null, null); } else view.Configure(second, null, null);
        Assert.False(await completeOriginal(snapshot)); Assert.Equal(0, notifications); Assert.Null(view.LastComparison);
        Assert.Equal(snapshot.Hash, (await new WorkspaceBaselineStore(temp.Root, first, null, null).LoadAsync(CancellationToken.None))!.Hash);
        Assert.Null(await new WorkspaceBaselineStore(temp.Root, second, null, null).LoadAsync(CancellationToken.None));
    });
    [Fact]
    public Task CompletedCurrentSynchronizationUpdatesOnlyWhileTheBindingStillMatches() => Sta(async () =>
    {
        using var temp = new TemporaryWorkspace(); using var handler = new TabularModelHandler(1702); handler.Model.AddTable("Original"); var snapshot = new TmdlWorkspaceCodec().CaptureLoaded(handler); var definition = Path.Combine(temp.Root, "definition"); Directory.CreateDirectory(definition); File.WriteAllText(Path.Combine(definition, "model.bim"), snapshot.DatabaseJson);
        using var queue = new BackgroundTaskQueue(); var notifications = 0; using var view = new WorkspaceSyncView(() => null, () => notifications++, queue, Path.Combine(temp.Root, "profile")); view.Configure(definition, null, null);
        Assert.True(await view.CaptureSynchronizationCompletion()(snapshot)); Assert.Equal(1, notifications); Assert.Equal(snapshot.Hash, view.LastComparison!.Baseline.Hash);
    });
    [Fact]
    public Task CompletionDoesNotRefreshANewBindingSelectedByTheChangeCallback() => Sta(async () =>
    {
        using var temp = new TemporaryWorkspace(); using var handler = new TabularModelHandler(1702); handler.Model.AddTable("Original"); var snapshot = new TmdlWorkspaceCodec().CaptureLoaded(handler); File.WriteAllText(Path.Combine(temp.Root, "model.bim"), snapshot.DatabaseJson);
        using var queue = new BackgroundTaskQueue(); WorkspaceSyncView? view = null;
        using (view = new WorkspaceSyncView(() => null, () => view!.Configure(null, null, null), queue, Path.Combine(temp.Root, "profile")))
        { view.Configure(temp.Root, null, null); Assert.False(await view.CaptureSynchronizationCompletion()(snapshot)); Assert.Null(view.LastComparison); }
    });
    private static async Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher)); dispatcher.BeginInvoke(new Action(async () => { try { await action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } })); Dispatcher.Run(); }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); if (await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(30))) != completion.Task) throw new TimeoutException("Workspace completion STA test timed out."); await completion.Task;
    }
    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pbibench-workspace-view-" + Guid.NewGuid().ToString("N")); public TemporaryWorkspace() => Directory.CreateDirectory(Root);
        public void Dispose() { var path = Path.GetFullPath(Root); if (!string.Equals(Path.GetDirectoryName(path)?.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("pbibench-workspace-view-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected cleanup path."); if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
