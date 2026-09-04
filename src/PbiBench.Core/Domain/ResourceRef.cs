namespace PbiBench.Core.Domain;

public sealed record ResourceRef(
    string Provider,
    string? TenantId,
    string? WorkspaceId,
    string? ItemId,
    string? ItemType,
    string DisplayName);
