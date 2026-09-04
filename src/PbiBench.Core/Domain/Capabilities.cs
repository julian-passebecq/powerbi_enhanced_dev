namespace PbiBench.Core.Domain;

[Flags]
public enum ToolCapability
{
    None = 0,
    ReadMetadata = 1 << 0,
    WriteMetadata = 1 << 1,
    QueryDax = 1 << 2,
    Refresh = 1 << 3,
    GetDefinition = 1 << 4,
    UpdateDefinition = 1 << 5,
    AdminInventory = 1 << 6,
    ReportAuthoring = 1 << 7,
    GitDiff = 1 << 8
}

public sealed record AdapterCapability(
    string AdapterId,
    ToolCapability Capabilities,
    bool IsConnected,
    bool IsPreview,
    string? Detail = null);
