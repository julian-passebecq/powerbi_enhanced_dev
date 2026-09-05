using System.Text.Json;

namespace PbiBench.AI.ContextExport;

// Deliberately has no connection, partition, credential, annotation, path or raw TOM property bag.
public sealed record ContextObject(string Id, string Kind, string Name, string? Table = null,
    string? Description = null, string? Expression = null, string? DataType = null,
    bool Hidden = false, string? DisplayFolder = null, string? FormatString = null,
    string? FormatExpression = null, string? StorageMode = null, int? Ordinal = null);
public sealed record ContextRelationship(string Id, string Name, string FromColumnId, string ToColumnId,
    bool Active, string FromCardinality, string ToCardinality, string FilterDirection);
public sealed record ContextDependency(string ObjectId, string DependencyId);
public sealed record ContextPerspective(string Name, IReadOnlyList<string> ObjectIds);
public sealed record ContextTranslation(string ObjectId, string Culture, string Property, string Value);
public sealed record ContextRole(string Name, string Table, string FilterExpression);
public sealed record ContextModel(string Name, int CompatibilityLevel, IReadOnlyList<ContextObject> Objects,
    IReadOnlyList<ContextRelationship> Relationships, IReadOnlyList<ContextDependency> Dependencies)
{
    public IReadOnlyList<ContextPerspective> Perspectives { get; init; } = Array.Empty<ContextPerspective>();
    public IReadOnlyList<ContextTranslation> Translations { get; init; } = Array.Empty<ContextTranslation>();
    public IReadOnlyList<ContextRole> Roles { get; init; } = Array.Empty<ContextRole>();
    public static string ObjectId(string kind, string? table, string name) => JsonSerializer.Serialize(new[] { kind, table, name });
}
public sealed record SampleRequest(string Table, IReadOnlyList<string> Columns, int Rows = 5,
    bool IncludeHidden = false, string? OrderColumn = null);
public sealed record SampleResult(IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows);
public interface IContextSampler
{
    Task<SampleResult> SampleAsync(SampleRequest request, CancellationToken cancellationToken);
}
public sealed record ContextEvidence(string Category, string ObjectId, string Name, string Outcome, string Detail);
public sealed record ContextExportOptions
{
    public IReadOnlyList<string> SelectedIds { get; init; } = Array.Empty<string>();
    public bool SelectedScope { get; init; }
    public IReadOnlyList<string> ExcludedIds { get; init; } = Array.Empty<string>();
    public bool IncludeRoles { get; init; }
    public bool IncludeAutomation { get; init; }
    public bool IncludeSamples { get; init; }
    public IReadOnlyList<SampleRequest> Samples { get; init; } = Array.Empty<SampleRequest>();
    public IReadOnlyList<ContextEvidence> Evidence { get; init; } = Array.Empty<ContextEvidence>();
    public int MaximumRowsPerTable { get; init; } = 250;
    public int MaximumSampleCells { get; init; } = 100000;
    public long MaximumBytes { get; init; } = 32 * 1024 * 1024;
}
public sealed record ContextFileReview(string Path, long Bytes, string Sha256);
public sealed class ContextExportPlan
{
    internal ContextExportPlan(SortedDictionary<string, byte[]> files, long maximumBytes)
    { Files = files; MaximumBytes = maximumBytes; Review = Array.AsReadOnly(files.Select(f => new ContextFileReview(f.Key, f.Value.Length, ContextExporter.Hash(f.Value))).ToArray()); }
    internal SortedDictionary<string, byte[]> Files { get; }
    internal long MaximumBytes { get; }
    public IReadOnlyList<ContextFileReview> Review { get; }
    // Conservative ZIP overhead reserve; the writer separately enforces the actual cap.
    public long EstimatedBytes => Review.Sum(f => f.Bytes) + Review.Count * 512L + 4096;
    public string ReadText(string path) => System.Text.Encoding.UTF8.GetString(Files[path]);
}
