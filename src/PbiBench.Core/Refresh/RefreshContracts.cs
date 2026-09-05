using System.Text.Json.Serialization;
using PbiBench.Core.Domain;

namespace PbiBench.Core.Refresh;

public enum RefreshKind { Full, ClearValues, Calculate, DataOnly, Automatic, Add, Defragment }
public enum RefreshSourceKind { Unknown, M, Query, Calculated, Entity, None }
public enum RefreshIssueSeverity { Information, Warning, Error }
public enum RefreshOutcome { Succeeded, SucceededWithWarnings, Failed, CanceledBeforeExecution, OutcomeUnknown }
public sealed record RefreshObject(string? Table = null, string? Partition = null)
{
    public override string ToString() => Table == null ? "Entire model" : Partition == null ? Table : Table + " / " + Partition;
}
public sealed record RefreshPartitionMetadata(string Name, string Mode, RefreshSourceKind SourceKind, string? DataSource = null);
public sealed record RefreshTableMetadata(string Name, bool HasRefreshPolicy, IReadOnlyList<RefreshPartitionMetadata> Partitions);
public sealed record RefreshMetadataSnapshot(string Server, string DatabaseId, string DatabaseName, int CompatibilityLevel,
    string Fingerprint, bool IsConnected, bool HasUnsavedChanges, bool IsPowerBi, IReadOnlyList<RefreshTableMetadata> Tables);
public sealed record RefreshSourceOverride(string Table, string Partition, RefreshSourceKind SourceKind, string Expression);
public sealed record RefreshRequest
{
    [JsonRequired] public RefreshKind Kind { get; init; } = RefreshKind.Full;
    [JsonRequired] public IReadOnlyList<RefreshObject> Objects { get; init; } = new[] { new RefreshObject() };
    public int MaxParallelism { get; init; } = 2;
    public int TimeoutSeconds { get; init; } = 3600;
    public bool? ApplyRefreshPolicy { get; init; }
    public DateTime? EffectiveDate { get; init; }
    public IReadOnlyList<RefreshSourceOverride> SourceOverrides { get; init; } = Array.Empty<RefreshSourceOverride>();
}
public sealed record RefreshIssue(string Code, string Message, RefreshIssueSeverity Severity);
public sealed record RefreshConnection(string Server, string DatabaseId)
{
    [JsonIgnore] public string? ConnectionString { get; init; }
    public override string ToString() => $"Refresh target {Server}/{DatabaseId}";
}
public sealed record RefreshProgress(string Stage, string Message);
public sealed record RefreshEngineResponse(bool HasErrors, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
public sealed record RefreshRunResult(Guid RunId, Guid PlanId, RefreshOutcome Outcome, DateTimeOffset StartedAt,
    double ElapsedMilliseconds, string Message, IReadOnlyList<string> Details, bool CommandSubmitted = false);

/// <summary>A frozen original command and its approval identity; caller-editable TMSL is never executed.</summary>
public sealed class RefreshPlan
{
    private int claimed;
    internal RefreshPlan(RefreshMetadataSnapshot metadata, RefreshRequest request, string tmsl, IReadOnlyList<RefreshIssue> issues, ChangePlan changePlan)
    { Metadata = metadata; Request = request; Tmsl = tmsl; Issues = issues; ChangePlan = changePlan; }
    public RefreshMetadataSnapshot Metadata { get; }
    public RefreshRequest Request { get; }
    public string Tmsl { get; }
    public IReadOnlyList<RefreshIssue> Issues { get; }
    public ChangePlan ChangePlan { get; }
    public bool CanExecute => Issues.All(i => i.Severity != RefreshIssueSeverity.Error) && Volatile.Read(ref claimed) == 0;
    public void ValidateApproval(ApprovedChangePlan approval, RefreshConnection target)
    {
        if (!CanExecute) throw new InvalidOperationException("This refresh plan has validation errors or was already submitted. Preview a new plan.");
        if (approval == null || !ReferenceEquals(approval.Plan, ChangePlan) || string.IsNullOrWhiteSpace(approval.ApprovalActor) || approval.ApprovedAt < ChangePlan.CreatedAt || approval.ApprovedAt > DateTimeOffset.UtcNow.AddMinutes(1))
            throw new InvalidOperationException("Execution requires approval of this exact refresh plan.");
        if (target == null || target.Server != Metadata.Server || target.DatabaseId != Metadata.DatabaseId) throw new InvalidOperationException("The connection does not match the approved refresh target.");
    }
    public void ClaimExecution(ApprovedChangePlan approval, RefreshConnection target)
    {
        ValidateApproval(approval, target);
        if (Interlocked.CompareExchange(ref claimed, 1, 0) != 0) throw new InvalidOperationException("This plan was already submitted. Preview a new plan before retrying.");
    }
}

public interface IRefreshSession : IDisposable
{
    void Open(RefreshConnection connection, int timeoutSeconds);
    RefreshMetadataSnapshot CaptureMetadata();
    RefreshEngineResponse Execute(string approvedTmsl);
    void Cancel();
}
public interface IRefreshSessionFactory { IRefreshSession Create(); }
