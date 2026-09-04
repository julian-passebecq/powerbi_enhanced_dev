namespace PbiBench.Core.Domain;

public enum ApprovalLevel { Inspect = 0, Query = 1, Propose = 2, LocalWrite = 3, RemoteModelWrite = 4, WorkspaceWrite = 5, TenantWrite = 6 }

public sealed record PlannedChange(string Target, string Operation, string BeforeSummary, string AfterSummary, IReadOnlyList<string> Validation);

public sealed record ChangePlan(
    Guid Id,
    DateTimeOffset CreatedAt,
    ApprovalLevel RequiredApproval,
    ResourceRef Target,
    IReadOnlyList<PlannedChange> Changes,
    string SnapshotStrategy,
    string RollbackStrategy);

public sealed record ApprovedChangePlan(ChangePlan Plan, DateTimeOffset ApprovedAt, string ApprovalActor);
