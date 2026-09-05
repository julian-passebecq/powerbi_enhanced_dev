using System.Windows.Threading;
using PbiBench.App;
using PbiBench.Core.Refresh;
using PbiBench.Core.Tasks;
using TabularEditor.TOMWrapper;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class AdvancedRefreshViewTests
{
    [Fact]
    public Task OfflinePreviewNeverQueuesARefresh() => Sta(() =>
    {
        using var handler = new TabularModelHandler(1600); handler.Model.AddTable("Sales");
        using var queue = new BackgroundTaskQueue(); using var view = new AdvancedRefreshView(() => handler, queue);
        view.SetScope("Sales"); var plan = view.Preview();
        Assert.False(plan.CanExecute); Assert.Contains(plan.Issues, issue => issue.Code == "OFFLINE");
        Assert.Equal(new RefreshObject("Sales"), Assert.Single(plan.Request.Objects));
        Assert.Contains("\"sequence\"", plan.Tmsl); Assert.Empty(queue.Snapshot()); Assert.Null(view.LastResult);
    });

    [Fact]
    public Task LoadingMixedProfileScopesKeepsMissingObjectsVisibleForValidation() => Sta(() =>
    {
        using var handler = new TabularModelHandler(1600); handler.Model.AddTable("Sales");
        using var queue = new BackgroundTaskQueue(); using var view = new AdvancedRefreshView(() => handler, queue);
        var requested = new[] { new RefreshObject("Sales"), new RefreshObject("Missing table", "Missing partition") };
        view.LoadProfile(new(1, "Mixed development profile", new() { Objects = requested, MaxParallelism = 3, Kind = RefreshKind.DataOnly }));
        var plan = view.Preview(); Assert.Equal(requested, plan.Request.Objects); Assert.Equal(3, plan.Request.MaxParallelism);
        Assert.Equal(RefreshKind.DataOnly, plan.Request.Kind); Assert.Contains(plan.Issues, issue => issue.Code == "TABLE" && issue.Message.Contains("Missing table"));
        Assert.Contains("Missing partition", plan.Tmsl); Assert.Empty(queue.Snapshot());
    });

    [Fact]
    public Task ModelEditsInvalidateThePreviewWithoutDroppingTheDevelopmentDraft() => Sta(() =>
    {
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("Sales");
        using var queue = new BackgroundTaskQueue(); using var view = new AdvancedRefreshView(() => handler, queue);
        view.LoadProfile(new(1, "Retained draft", new() { Objects = new[] { new RefreshObject("Sales") }, TimeoutSeconds = 400, MaxParallelism = 4 }));
        var first = view.Preview(); view.RefreshModel(); Assert.Same(first, view.LastPlan);
        table.Name = "Renamed Sales"; view.RefreshModel(); Assert.Null(view.LastPlan);
        var next = view.Preview(); Assert.NotEqual(first.Metadata.Fingerprint, next.Metadata.Fingerprint);
        Assert.Equal("Sales", Assert.Single(next.Request.Objects).Table); Assert.Equal(400, next.Request.TimeoutSeconds); Assert.Equal(4, next.Request.MaxParallelism);
        Assert.Contains(next.Issues, issue => issue.Code == "TABLE"); Assert.Empty(queue.Snapshot());
    });

    private static Task Sta(Action action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(() => { try { action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } })); Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
