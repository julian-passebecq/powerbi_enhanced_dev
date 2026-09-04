namespace PbiBench.Core.Domain;

public sealed record AuditRecord(
    DateTimeOffset Timestamp,
    string Adapter,
    string Operation,
    ResourceRef? Target,
    int? StatusCode,
    string? CorrelationId,
    TimeSpan Elapsed,
    Guid? ApprovedPlanId,
    string Outcome);
