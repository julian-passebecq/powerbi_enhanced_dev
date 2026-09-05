using PbiBench.Automation.Commands;
using PbiBench.Core.Agent;
using PbiBench.Core.Commands;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Automation.Agent;

/// <summary>Agent provenance checks wrap the common GUI/CLI command facade; no separate model-edit engine exists here.</summary>
public sealed class AgentProposalService
{
    private readonly Func<TabularModelHandler?> currentHandler;
    private readonly SemanticCommandService commands;
    private TabularModelHandler? capturedHandler;
    private AgentContextDocument? context;
    public AgentProposalService(Func<TabularModelHandler?> currentHandler)
    { this.currentHandler = currentHandler; commands = new(currentHandler); }
    public AgentContextDocument Capture(IReadOnlyList<TabularNamedObject> selection, AgentContextOptions options, AgentContextExtras? extras = null)
    {
        var handler = currentHandler(); var captured = AgentContextCapture.Capture(handler, selection, options, extras);
        capturedHandler = handler; return context = captured;
    }
    public void Invalidate() { context = null; capturedHandler = null; }
    public void ValidateContext(AgentContextDocument supplied)
    {
        var handler = currentHandler();
        if (!ReferenceEquals(context, supplied) || !ReferenceEquals(capturedHandler, handler) ||
            supplied.ModelFingerprint != (handler == null ? "" : new SemanticModelService(handler).Fingerprint()))
            throw new InvalidOperationException("The model or captured context changed. Capture and review context again.");
    }
    public async Task<PreparedCommand> PrepareAsync(AgentProposal proposal, AgentContextDocument supplied, CancellationToken ct)
    {
        AgentProposalJson.Validate(proposal); ValidateContext(supplied);
        if (proposal.Kind != AgentProposalKind.Action || proposal.Recipe == null) throw new InvalidOperationException("Only typed action proposals have a model-change preview.");
        if (currentHandler() == null) throw new InvalidOperationException("Open a semantic model before previewing an action.");
        // The facade captures on the owner context, computes detached metadata off-thread, then materializes on the owner context.
        var prepared = await commands.PrepareAsync(new CommandRequest { Kind = CommandKind.Action, Recipe = proposal.Recipe }, ct);
        ct.ThrowIfCancellationRequested(); ValidateContext(supplied); return prepared;
    }
    public Task<CommandResult> ApplyAsync(PreparedCommand prepared, AgentContextDocument supplied, string reviewHash, string actor, CancellationToken ct)
    {
        ValidateContext(supplied);
        if (prepared.Review.IsRemote || prepared.Request.Kind != CommandKind.Action) throw new InvalidOperationException("The Agent page applies only reviewed local model actions.");
        return commands.ApplyAsync(prepared, reviewHash, actor, ct);
    }
}
