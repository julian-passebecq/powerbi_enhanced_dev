using System.Text.Json.Serialization;

namespace PbiBench.Core.Quality;

public sealed record OptimizationSignal(string Id, string Source, string Category, string Risk, string Title,
    string Evidence, string? Table = null, string? Column = null, string? Recommendation = null);
public sealed record VertiPaqTable(string Name, long? Rows, long? DataBytes, long? DictionaryBytes, long? HierarchyBytes,
    long? RelationshipBytes, long? UserHierarchyBytes, string StorageMode, long? RiViolations)
{
    public long? TotalBytes => VertiPaqNumbers.Sum(DataBytes, DictionaryBytes, HierarchyBytes, RelationshipBytes, UserHierarchyBytes);
}
public sealed record VertiPaqColumn(string Table, string Name, string DataType, long? Cardinality, long? DataBytes,
    long? DictionaryBytes, long? HierarchyBytes, string? Encoding, bool? IsResident)
{
    public long? TotalBytes => VertiPaqNumbers.Sum(DataBytes, DictionaryBytes, HierarchyBytes);
}
public sealed record VertiPaqPartition(string Table, string Name, string Mode, string? State, DateTimeOffset? RefreshedAt);
public sealed record VertiPaqSegment(string Table, string Column, string? Partition, long Number, long? Rows, long? DataBytes,
    bool? IsResident, bool? IsPageable, double? Temperature, DateTimeOffset? LastAccessed);
public sealed record VertiPaqRelationship(string Name, string FromTable, string FromColumn, string ToTable, string ToColumn,
    long? MissingKeys, long? InvalidRows, long? FromBytes, long? ToBytes);
public sealed record VertiPaqSnapshot(string Source, string ModelName, string? Server, DateTimeOffset? CapturedAt,
    string SchemaVersion, bool? StatisticsCollected, IReadOnlyList<VertiPaqTable> Tables, IReadOnlyList<VertiPaqColumn> Columns,
    IReadOnlyList<VertiPaqPartition> Partitions, IReadOnlyList<VertiPaqSegment> Segments,
    IReadOnlyList<VertiPaqRelationship> Relationships, IReadOnlyList<string> Warnings)
{
    public long? TotalBytes => VertiPaqNumbers.Sum(Tables.Select(table => table.TotalBytes));
}
public sealed record VertiPaqCaptureRequest(string Server, string Database, int TimeoutSeconds = 60, int MaximumRowsPerRowset = 200000)
{
    [JsonIgnore] public string? ConnectionString { get; init; }
    public override string ToString() => "VertiPaq metrics on " + Server + " / " + Database;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Server) || Server.IndexOfAny(new[] { ';', '\r', '\n', '\0' }) >= 0) throw new ArgumentException("A server endpoint without connection-string options is required.");
        if (string.IsNullOrWhiteSpace(Database) || Database.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new ArgumentException("A database is required.");
        if (TimeoutSeconds < 1 || TimeoutSeconds > 3600) throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds));
        if (MaximumRowsPerRowset < 1 || MaximumRowsPerRowset > 1000000) throw new ArgumentOutOfRangeException(nameof(MaximumRowsPerRowset));
    }
}
public interface IVertiPaqSnapshotService { Task<VertiPaqSnapshot> CaptureAsync(VertiPaqCaptureRequest request, CancellationToken cancellationToken); }
public interface IVpaxSnapshotReader { Task<VertiPaqSnapshot> ReadAsync(string path, CancellationToken cancellationToken); }
public static class VertiPaqNumbers
{
    public static long? Sum(params long?[] values) => Sum((IEnumerable<long?>)values);
    public static long? Sum(IEnumerable<long?> values)
    {
        long total = 0;
        foreach (var value in values) { if (!value.HasValue) return null; total = checked(total + value.Value); }
        return total;
    }
}
