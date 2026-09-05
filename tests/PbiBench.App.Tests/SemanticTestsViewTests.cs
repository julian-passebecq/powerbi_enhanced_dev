using System.Windows.Threading;
using PbiBench.App;
using PbiBench.Core.Quality;
using PbiBench.Core.Queries;
using PbiBench.Core.Tasks;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class SemanticTestsViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task QueueCancellationCallbacksRunAwayFromTheUiThread(bool externalCancellation) => Sta(async () =>
    {
        using var queue = new BackgroundTaskQueue(); var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously); var caller = Environment.CurrentManagedThreadId;
        using var external = new CancellationTokenSource();
        CancellationTokenRegistration registration = default;
        var job = queue.Enqueue("Cancel private operation", async context =>
        {
            registration = context.CancellationToken.Register(() => callback.TrySetResult(Environment.CurrentManagedThreadId)); started.SetResult(true);
            await Task.Delay(Timeout.Infinite, context.CancellationToken); return true;
        }, external.Token);
        await started.Task; if (externalCancellation) external.Cancel(); else job.Cancel();
        Assert.Same(callback.Task, await Task.WhenAny(callback.Task, Task.Delay(TimeSpan.FromSeconds(10))));
        Assert.NotEqual(caller, await callback.Task); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.Completion); registration.Dispose();
    });
    [Fact]
    public Task LoadingTestsDoesNotExecuteAndModelChangeInvalidatesCompletedEvidence() => Sta(async () =>
    {
        var target = (Server: (string?)"fixture", Database: (string?)"fixture");
        using var queue = new BackgroundTaskQueue(); var queries = new PendingQueries();
        using var view = new SemanticTestsView(() => target, () => "Password=transient", queries, queue);
        view.LoadArtifact(Artifact()); Assert.Null(queries.Request);
        var caller = Environment.CurrentManagedThreadId; var run = view.RunAllAsync(); await queries.Started.Task;
        Assert.NotEqual(caller, queries.WorkerThread); queries.Complete(); await run;
        Assert.Equal(SemanticTestOutcome.Passed, Assert.Single(view.LastResults).Outcome);
        Assert.Equal("Password=transient", queries.Request!.ConnectionString);
        target = ("other", "fixture"); Assert.Empty(view.LastResults);
    });
    [Fact]
    public Task ReplacingAnArtifactDuringExecutionDiscardsStaleEvidence() => Sta(async () =>
    {
        using var queue = new BackgroundTaskQueue(); var queries = new PendingQueries(); using var view = new SemanticTestsView(() => ("fixture", "fixture"), () => null, queries, queue);
        view.LoadArtifact(Artifact()); var run = view.RunAllAsync(); await queries.Started.Task;
        view.LoadArtifact(Artifact("replacement")); queries.Complete(); await run; Assert.Empty(view.LastResults);
    });
    [Fact]
    public Task ModelRefreshCancelsReadOnlyWorkAndPreservesDraftForTheNextRun() => Sta(async () =>
    {
        using var queue = new BackgroundTaskQueue(); var queries = new PendingQueries(); using var view = new SemanticTestsView(() => ("fixture", "fixture"), () => null, queries, queue);
        view.LoadArtifact(Artifact()); var run = view.RunAllAsync(); await queries.Started.Task; view.RefreshModel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run); Assert.Empty(view.LastResults);
        queries.Reset(); var next = view.RunAllAsync(); await queries.Started.Task; queries.Complete(); await next;
        Assert.Equal("fixture-test", Assert.Single(view.LastResults).TestId);
    });
    [Fact]
    public Task DisconnectedTestsRequireAnEngineAndNeverProduceAPass() => Sta(async () =>
    {
        using var queue = new BackgroundTaskQueue(); var queries = new PendingQueries(); using var view = new SemanticTestsView(() => (null, null), () => null, queries, queue);
        view.LoadArtifact(Artifact()); await Assert.ThrowsAsync<InvalidOperationException>(() => view.RunAllAsync()); Assert.Empty(view.LastResults); Assert.Null(queries.Request); Assert.Empty(queue.Snapshot());
    });
    private static SemanticTestArtifact Artifact(string id = "fixture-test") => new(1, new[] { new SemanticTestDefinition { Id = id, Expected = SemanticValue.From(1) } });
    [Fact]
    public Task StagingAnAgentArtifactPreservesExistingSuiteAndUnsavedEditorDraft() => Sta(() =>
    {
        using var queue = new BackgroundTaskQueue(); var queries = new PendingQueries();
        using var view = new SemanticTestsView(() => (null, null), () => null, queries, queue);
        view.LoadArtifact(new(1, new[] { new SemanticTestDefinition { Id = "first", Name = "First existing" }, new SemanticTestDefinition { Id = "second", Name = "Second existing" } }));
        var nameEditor = (System.Windows.Controls.TextBox)typeof(SemanticTestsView).GetField("name", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(view)!;
        nameEditor.Text = "Unsaved edited name";
        view.AppendArtifact(new(1, new[] { new SemanticTestDefinition { Id = "first", Name = "Agent proposal" } }));
        var captured = view.CaptureArtifact(); Assert.Equal(3, captured.Tests.Count);
        Assert.Equal("Unsaved edited name", captured.Tests.Single(test => test.Id == "first").Name);
        Assert.Equal("Second existing", captured.Tests.Single(test => test.Id == "second").Name);
        Assert.Contains(captured.Tests, test => test.Name == "Agent proposal" && test.Id != "first" && test.Id != "second");
        Assert.Equal("Agent proposal", nameEditor.Text); Assert.Null(queries.Request); Assert.Empty(view.LastResults);
        return Task.CompletedTask;
    });
    private sealed class PendingQueries : IDaxQueryService
    {
        public TaskCompletionSource<bool> Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QueryRequest? Request { get; private set; }
        public int WorkerThread { get; private set; }
        private TaskCompletionSource<QueryResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken token)
        {
            WorkerThread = Environment.CurrentManagedThreadId; Request = request; var pending = completion;
            token.Register(() => pending.TrySetCanceled()); Started.TrySetResult(true); return pending.Task;
        }
        public void Complete() => completion.TrySetResult(new QueryResult(Guid.NewGuid(), Request!.Query, Request.Server, Request.Database, DateTimeOffset.UtcNow, TimeSpan.Zero,
            new[] { new QueryResultSet(0, "Fixture", new[] { new QueryColumn("C0", "Value", "Int64") }, new[] { new object?[] { 1L } }, false) }, Request.DocumentRevision, Array.Empty<string>()));
        public void Reset() { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); Started = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    }
    private static Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () => { try { await action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } })); Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
