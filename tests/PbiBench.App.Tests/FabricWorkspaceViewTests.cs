using System.Windows.Threading;
using PbiBench.App;
using PbiBench.Core.Fabric;
using PbiBench.Core.Queries;
using PbiBench.Core.Tasks;
using PbiBench.Fabric;
using TabularEditor.TOMWrapper;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class FabricWorkspaceViewTests
{
    [Fact]
    public Task SourceFixtureAndImportPreviewMakeNoNetworkCallsAndNativeApplyIsUndoable() => Sta(() =>
    {
        using var handler = new TabularModelHandler(1600); handler.Model.AddTable("Existing");
        using var queue = new BackgroundTaskQueue(); var io = new NoIo();
        using var view = new FabricWorkspaceView(queue, io, io, io); view.Configure(() => handler, () => { });
        view.ShowSchema(Schema("Source")); view.SelectImportOptions(FabricStorageMode.Import, "Imported", new[] { "Id" });
        var plan = view.PrepareImportPreview(); Assert.Same(plan, view.LastPreview); Assert.True(plan.CanApply);
        Assert.DoesNotContain(handler.Model.Tables, table => table.Name == "Imported"); Assert.Equal(1, view.SourceColumnCount); Assert.Equal(0, io.Calls);
        plan.Apply(handler); Assert.Contains(handler.Model.Tables, table => table.Name == "Imported");
        handler.UndoManager.Undo(); Assert.DoesNotContain(handler.Model.Tables, table => table.Name == "Imported"); Assert.Equal(0, io.Calls);
        return Task.CompletedTask;
    });
    [Fact]
    public Task ReplacingSourceDuringPreviewDiscardsStaleDataEvenIfProviderReturns() => Sta(async () =>
    {
        using var queue = new BackgroundTaskQueue(); var io = new NoIo(); var pending = new PendingPreview();
        using var view = new FabricWorkspaceView(queue, io, io, pending); view.ShowSchema(Schema("Original"));
        var run = view.PreviewDataAsync(); await pending.Started.Task; view.ShowSchema(Schema("Replacement")); pending.Complete(); await run;
        Assert.Null(view.LastDataPreview); Assert.Equal("Replacement", view.SelectedSchema!.Source.Table); Assert.Equal(0, io.Calls);
    });
    [Fact]
    public Task SourcePreviewRunsOnWorkerAndRejectsDifferentSourceIdentity() => Sta(async () =>
    {
        using var queue = new BackgroundTaskQueue(); var io = new NoIo(); var pending = new PendingPreview(); var caller = Environment.CurrentManagedThreadId;
        using var view = new FabricWorkspaceView(queue, io, io, pending); view.ShowSchema(Schema("Original"));
        var run = view.PreviewDataAsync(); await pending.Started.Task; Assert.NotEqual(caller, pending.Worker); pending.Complete(Schema("Other").Source); await run;
        Assert.Null(view.LastDataPreview); Assert.Contains("different source context", view.Status);
    });
    private static FabricTableSchema Schema(string table)
    {
        var source = new FabricSourceRef("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "Lakehouse", "dbo", table, "DELTA", new("fixture.datawarehouse.fabric.microsoft.com", "33333333-3333-3333-3333-333333333333"));
        var columns = new[] { new FabricColumnSchema("Id", "long", false) };
        return new(source, columns, FabricSchemaRules.Fingerprint(source, columns), DateTimeOffset.UtcNow, Array.Empty<string>());
    }
    private sealed class PendingPreview : IFabricDataPreviewService
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<FabricDataPreview> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private FabricDataPreviewRequest? request;
        public int Worker { get; private set; }
        public Task<FabricDataPreview> PreviewAsync(FabricDataPreviewRequest request, CancellationToken cancellationToken)
        { this.request = request; Worker = Environment.CurrentManagedThreadId; Started.TrySetResult(true); return completion.Task; }
        public void Complete(FabricSourceRef? source = null) => completion.TrySetResult(new(source ?? request!.Schema.Source,
            new QueryResultSet(0, "Fixture", new[] { new QueryColumn("C0", "Id", "Int64") }, new[] { new object?[] { 1L } }, false), DateTimeOffset.UtcNow, "SELECT TOP (101) [Id] FROM [dbo].[Original]", Array.Empty<string>()));
    }
    private sealed class NoIo : IFabricAuthenticator, IFabricCatalogService, IFabricDataPreviewService
    {
        public int Calls { get; private set; } public string? AccountLabel => null;
        private Task<T> Unexpected<T>() { Calls++; return Task.FromException<T>(new InvalidOperationException("Unexpected network/authentication request.")); }
        public Task SignInAsync(FabricSignInOptions options, FabricAudience audience, CancellationToken cancellationToken) => Unexpected<bool>();
        public Task SignOutAsync(CancellationToken cancellationToken) => Unexpected<bool>();
        public Task<string> GetAccessTokenAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default) => Unexpected<string>();
        public Task<IReadOnlyList<FabricWorkspace>> ListWorkspacesAsync(CancellationToken cancellationToken) => Unexpected<IReadOnlyList<FabricWorkspace>>();
        public Task<IReadOnlyList<FabricItem>> ListItemsAsync(string workspaceId, CancellationToken cancellationToken) => Unexpected<IReadOnlyList<FabricItem>>();
        public Task<FabricItem> ResolveItemAsync(FabricItem item, CancellationToken cancellationToken) => Unexpected<FabricItem>();
        public Task<IReadOnlyList<string>> ListSchemasAsync(FabricItem item, CancellationToken cancellationToken) => Unexpected<IReadOnlyList<string>>();
        public Task<IReadOnlyList<FabricSourceRef>> ListTablesAsync(FabricItem item, string schema, CancellationToken cancellationToken) => Unexpected<IReadOnlyList<FabricSourceRef>>();
        public Task<FabricTableSchema> GetSchemaAsync(FabricSourceRef source, CancellationToken cancellationToken) => Unexpected<FabricTableSchema>();
        public Task<FabricDataPreview> PreviewAsync(FabricDataPreviewRequest request, CancellationToken cancellationToken) => Unexpected<FabricDataPreview>();
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
