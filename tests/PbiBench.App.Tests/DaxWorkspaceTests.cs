using System.IO;
using System.Windows.Threading;
using PbiBench.App;
using PbiBench.Core.Queries;
using PbiBench.Dax.LanguageService;
using PbiBench.ModelEditor;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace PbiBench.App.Tests;

public sealed class DaxWorkspaceTests
{
    [Fact]
    public Task SuccessfulResultsSurviveUnavailableLocalHistory() => Sta(async () =>
    {
        using var folder = new TestFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "query-history.json"));
        var service = new DeferredQueries();
        using var workspace = Create(folder.Path, service);
        var run = workspace.RunAsync(DaxRunScope.All);
        Assert.Equal("EVALUATE ROW ( \"Result\", 1 )", service.Request!.Query.Trim());
        service.Complete(); await run;
        Assert.Equal(2, workspace.ResultCount);
        Assert.Contains("history could not be saved", workspace.StatusText);
    });

    [Fact]
    public Task EditingDocumentDuringQueryPreventsStaleResultsReplacingCurrentView() => Sta(async () =>
    {
        using var folder = new TestFolder(); var service = new DeferredQueries();
        using var workspace = Create(folder.Path, service);
        var run = workspace.RunAsync(DaxRunScope.All);
        workspace.ActiveEditor.Text = "EVALUATE ROW ( \"Revised\", 2 )";
        service.Complete(); await run;
        Assert.Equal(0, workspace.ResultCount);
        Assert.Contains("document or connection changed", workspace.StatusText);
        var history = await new QueryHistoryStore(folder.Path).LoadAsync(CancellationToken.None);
        Assert.Single(history); Assert.Contains("Result", history[0].Query);
    });

    [Fact]
    public Task CancellationReachesOnlyTheRunningRequestAndAllowsAnotherRun() => Sta(async () =>
    {
        using var folder = new TestFolder(); var service = new DeferredQueries();
        using var workspace = Create(folder.Path, service);
        var run = workspace.RunAsync(DaxRunScope.All);
        workspace.CancelActiveQuery();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var next = workspace.RunAsync(DaxRunScope.All); service.Complete(); await next;
        Assert.Equal(2, service.Count); Assert.Equal(2, workspace.ResultCount);
    });

    [Fact]
    public Task AllDocumentBuffersRecoverWithoutDroppingTheFirstOrDuplicatingTheActiveTab() => Sta(() =>
    {
        using var folder = new TestFolder();
        using (var workspace = Create(folder.Path, new DeferredQueries()))
        {
            workspace.ActiveEditor.Text = "EVALUATE ROW ( \"First unsaved\", 8 )";
            workspace.OpenQuery("EVALUATE ROW ( \"Second\", 9 )");
            workspace.ActiveEditor.Text = "EVALUATE ROW ( \"Second unsaved\", 10 )";
        }
        using var recovered = Create(folder.Path, new DeferredQueries());
        Assert.Equal(2, recovered.DocumentCount);
        Assert.Contains("Second unsaved", recovered.ActiveEditor.Text);
        var serialized = File.ReadAllText(Path.Combine(folder.Path, "dax-documents-v9.json"));
        Assert.Contains("First unsaved", serialized); Assert.Contains("Second unsaved", serialized);
        return Task.CompletedTask;
    });

    private static DaxWorkspaceView Create(string folder, IDaxQueryService service) => new(
        new DaxScratchEditor { Text = "EVALUATE ROW ( \"Result\", 1 )" }, folder,
        () => DaxMetadataSnapshot.Empty, () => ("fixture-server", "fixture-model"), () => null, (_, _) => { }, _ => { }, service);

    private sealed class DeferredQueries : IDaxQueryService
    {
        private TaskCompletionSource<QueryResult> completion = null!;
        public QueryRequest? Request { get; private set; }
        public int Count { get; private set; }
        public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken)
        {
            Request = request; Count++; completion = new TaskCompletionSource<QueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var current = completion;
            cancellationToken.Register(() => current.TrySetCanceled());
            return current.Task;
        }
        public void Complete()
        {
            var request = Request!;
            completion.SetResult(new QueryResult(Guid.NewGuid(), request.Query, request.Server, request.Database, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(4),
                new[] { new QueryResultSet(0, "First", new[] { new QueryColumn("C0", "Value", "Int64") }, new[] { new object?[] { 1L } }, false),
                    new QueryResultSet(1, "Second", new[] { new QueryColumn("C0", "Value", "Int64") }, new[] { new object?[] { 2L } }, false) }, request.DocumentRevision, Array.Empty<string>()));
        }
    }
    private static Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.TrySetResult(true); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PbiBench-DaxUi-" + Guid.NewGuid().ToString("N"));
        public TestFolder() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
