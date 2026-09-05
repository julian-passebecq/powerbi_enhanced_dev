using PbiBench.Core.Commands;

namespace PbiBench.Automation.Commands;

/// <summary>Host-owned executable preview. It is never deserialized from CLI or Agent input.</summary>
public sealed class PreparedCommand
{
    private readonly Func<string, CancellationToken, Task<CommandResult>> apply;
    private int consumed;
    internal PreparedCommand(CommandRequest request, CommandReview review, Func<string, CancellationToken, Task<CommandResult>> apply)
    { Request = request; Review = review; this.apply = apply; }
    public CommandRequest Request { get; }
    public CommandReview Review { get; }
    internal Task<CommandResult> ApplyAsync(string hash, string actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Review.CanApply || hash != Review.Hash || string.IsNullOrWhiteSpace(actor)) throw new InvalidOperationException("Apply requires approval of this exact applicable preview.");
        if (Interlocked.CompareExchange(ref consumed, 1, 0) != 0) throw new InvalidOperationException("This preview was already consumed. Prepare a new review.");
        return apply(actor, ct);
    }
}
