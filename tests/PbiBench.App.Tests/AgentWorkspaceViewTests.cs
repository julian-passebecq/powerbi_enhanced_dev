using System.Windows.Threading;
using PbiBench.App;
using PbiBench.Core.Agent;
using PbiBench.Core.Automation;
using PbiBench.Core.Commands;
using PbiBench.Core.Quality;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class AgentWorkspaceViewTests
{
    [Fact]
    public Task OfflineTypedProposalUsesSharedCommandReviewAndSingleNativeUndo() => Sta(async () =>
    {
        using var handler = Model(); var calls = new DeferredProvider(); var changed = 0; var before = new SemanticModelService(handler).Fingerprint();
        using var view = View(() => handler, calls, () => changed++);
        view.CaptureContext(new()); view.LoadProposal(Action()); await view.PreparePreviewAsync();
        var prepared = view.LastPreview!; Assert.True(prepared.Review.CanApply); Assert.Equal(CommandKind.Action, prepared.Request.Kind); Assert.False(prepared.Review.IsRemote);
        Assert.Equal("", handler.Model.Tables["Sales"].Measures["Total"].DisplayFolder); Assert.Equal(0, calls.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => view.ApplyPreviewAsync("not-the-reviewed-hash", "Fixture reviewer"));
        var result = await view.ApplyPreviewAsync(prepared.Review.Hash, "Fixture reviewer"); Assert.Equal(CommandStatus.Succeeded, result.Status);
        Assert.Equal("Measures", handler.Model.Tables["Sales"].Measures["Total"].DisplayFolder); Assert.Equal(1, changed);
        handler.UndoManager.Undo(); Assert.Equal(before, new SemanticModelService(handler).Fingerprint()); Assert.Equal(0, calls.Calls);
    });
    [Fact]
    public Task ModelMutationAndSessionReplacementInvalidateExistingAgentReview() => Sta(async () =>
    {
        using var first = Model(); using var second = Model(); TabularModelHandler current = first;
        using var view = View(() => current); view.CaptureContext(new()); view.LoadProposal(Action()); await view.PreparePreviewAsync(); var hash = view.LastPreview!.Review.Hash;
        current = second;
        await Assert.ThrowsAsync<InvalidOperationException>(() => view.ApplyPreviewAsync(hash, "Fixture")); Assert.Equal("", second.Model.Tables["Sales"].Measures["Total"].DisplayFolder);
        view.RefreshModel(); Assert.Null(view.LastPreview); Assert.Equal("", view.SharedContextJson);
        view.CaptureContext(new()); view.LoadProposal(Action()); await view.PreparePreviewAsync(); second.Model.Description = "Changed while reviewing"; view.RefreshModel(); Assert.Null(view.LastPreview);
    });
    [Fact]
    public Task OnlineProviderCannotRunBeforeExplicitSharingApproval() => Sta(async () =>
    {
        using var handler = Model(); var provider = new DeferredProvider(); using var view = View(() => handler, provider);
        view.CaptureContext(new(SelectedObjects: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => view.GenerateAsync("Review the captured object.", false)); Assert.Equal(0, provider.Calls);
    });
    [Fact]
    public Task NewContextDiscardsLateProviderResponseEvenWhenCancellationIsIgnored() => Sta(async () =>
    {
        using var handler = Model(); var provider = new DeferredProvider(); using var view = View(() => handler, provider);
        view.CaptureContext(new(SelectedObjects: true)); var task = view.GenerateAsync("Review.", true); await provider.Started.Task;
        Assert.NotEqual(Environment.CurrentManagedThreadId, provider.Worker); view.CaptureContext(new()); provider.Complete();
        try { await task; } catch (OperationCanceledException) { }
        Assert.Null(view.Proposal); Assert.Null(view.LastPreview); Assert.DoesNotContain("Sales", view.SharedContextJson);
    });
    [Fact]
    public Task ModelMutationDuringProviderCallCannotAcceptItsAction() => Sta(async () =>
    {
        using var handler = Model(); var provider = new DeferredProvider(); using var view = View(() => handler, provider);
        view.CaptureContext(new()); var task = view.GenerateAsync("Review.", true); await provider.Started.Task;
        handler.Model.Description = "Changed without a UI refresh notification"; provider.Complete();
        await Assert.ThrowsAsync<InvalidOperationException>(() => task); Assert.Null(view.Proposal); Assert.Null(view.LastPreview);
    });
    [Fact]
    public Task QueryAndTestProposalsAreOnlyStagedAsDrafts() => Sta(() =>
    {
        using var handler = Model(); var provider = new DeferredProvider(); string? query = null; SemanticTestArtifact? tests = null;
        using var view = new AgentWorkspaceView(() => handler, () => Array.Empty<TabularNamedObject>(), () => { }, text => query = text, artifact => tests = artifact, provider: provider);
        view.LoadProposal(AgentProposalJson.Serialize(new(1, AgentProposalKind.Query, "Query", "Unexecuted draft.", null, "EVALUATE ROW(\"Value\",1)", null))); view.StageProposal(); Assert.NotNull(query); Assert.Null(tests);
        view.LoadProposal(AgentProposalJson.Serialize(new(1, AgentProposalKind.Test, "Test", "Unexecuted assertion.", null, null, new("Constant", query!, SemanticComparison.Equal, SemanticValue.From(1))))); view.StageProposal();
        Assert.Single(tests!.Tests); Assert.Equal(0, provider.Calls); Assert.Null(view.LastPreview);
        return Task.CompletedTask;
    });
    private static AgentWorkspaceView View(Func<TabularModelHandler?> handler, IAgentProvider? provider = null, Action? changed = null) =>
        new(handler, () => handler()!.Model.AllMeasures.Cast<TabularNamedObject>().ToArray(), changed ?? (() => { }), _ => { }, _ => { }, provider: provider);
    private static TabularModelHandler Model() { var handler = new TabularModelHandler(1600); handler.Model.AddTable("Sales").AddMeasure("Total", "1"); return handler; }
    private static string Action() => AgentProposalJson.Serialize(new(1, AgentProposalKind.Action, "Folder", "Set a literal folder.",
        new("Folder", new[] { new RecipeStep(new(RecipeScope.Measure, "Sales", "Total"), RecipeOperation.SetProperty, "DisplayFolder", RecipeValue.Literal("Measures")) }), null, null));
    private sealed class DeferredProvider : IAgentProvider
    {
        public string DisplayName => "Synthetic provider"; public bool IsOnline => true; public int Calls { get; private set; } public int Worker { get; private set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<AgentProposal> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AgentProposal> ProposeAsync(AgentRequest request, CancellationToken cancellationToken) { Calls++; Worker = Environment.CurrentManagedThreadId; Started.TrySetResult(true); return completion.Task; }
        public void Complete() => completion.TrySetResult(AgentProposalJson.Parse(Action()));
    }
    private static Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new System.Action(async () => { try { await action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } })); Dispatcher.Run();
        }) { IsBackground = true }; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
