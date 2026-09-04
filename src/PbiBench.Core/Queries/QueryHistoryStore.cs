using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PbiBench.Core.Queries;

public sealed record QueryHistoryEntry
{
    public Guid Id { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public string Server { get; init; } = "";
    public string Database { get; init; } = "";
    public string Query { get; init; } = "";
    public string Status { get; init; } = "";
    public double ElapsedMilliseconds { get; init; }
    public int ResultCount { get; init; }
    public int RowCount { get; init; }
    public bool Truncated { get; init; }
    [JsonIgnore]
    public string Summary
    {
        get
        {
            var preview = string.Join(" ", (Query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (preview.Length > 80) preview = preview.Substring(0, 80) + "…";
            return $"{StartedAt.ToLocalTime():g} · {Status} · {RowCount:N0} rows · {preview}";
        }
    }

    public static QueryHistoryEntry FromResult(QueryResult result) => new()
    {
        Id = result.Id, StartedAt = result.StartedAt, Server = result.Server, Database = result.Database, Query = result.Query,
        Status = "Completed", ElapsedMilliseconds = result.Elapsed.TotalMilliseconds, ResultCount = result.Results.Count,
        RowCount = result.Results.Sum(r => r.Rows.Count), Truncated = result.Results.Any(r => r.IsTruncated)
    };
    public static QueryHistoryEntry FromFailure(QueryRequest request, string status) => new()
    {
        Id = Guid.NewGuid(), StartedAt = DateTimeOffset.UtcNow, Server = request.Server, Database = request.Database,
        Query = request.Query, Status = status == "Canceled" || status == "Cancelled" ? "Canceled" : status == "Timed out" ? status : "Failed"
    };
}

/// <summary>Bounded local history. Contains executed DAX and display context, never transport credentials.</summary>
public sealed class QueryHistoryStore
{
    private readonly string path;
    private readonly int capacity;
    private readonly SemaphoreSlim gate = new(1, 1);
    private const int MaximumFileBytes = 16 * 1024 * 1024;
    public QueryHistoryStore(string settingsDirectory, int capacity = 100)
    {
        if (string.IsNullOrWhiteSpace(settingsDirectory)) throw new ArgumentException("A settings directory is required.", nameof(settingsDirectory));
        if (capacity < 1 || capacity > 1000) throw new ArgumentOutOfRangeException(nameof(capacity));
        path = Path.Combine(Path.GetFullPath(settingsDirectory), "query-history.json"); this.capacity = capacity;
    }

    public async Task<IReadOnlyList<QueryHistoryEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await Task.Run(() => Read(cancellationToken), cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public async Task AddAsync(QueryHistoryEntry entry, CancellationToken cancellationToken)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                var entries = new[] { entry }.Concat(Read(cancellationToken).Where(e => e.Id != entry.Id)).Take(capacity).ToList();
                var json = JsonSerializer.Serialize(entries);
                while (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes && entries.Count > 1)
                {
                    entries.RemoveAt(entries.Count - 1); json = JsonSerializer.Serialize(entries);
                }
                if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes) throw new InvalidDataException("This query is too large to retain in local history.");
                Save(json, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await Task.Run(() => Save("[]", cancellationToken), cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private IReadOnlyList<QueryHistoryEntry> Read(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return Array.Empty<QueryHistoryEntry>();
        if (new FileInfo(path).Length > MaximumFileBytes) throw new InvalidDataException("The query history file exceeds its size limit.");
        try
        {
            var entries = JsonSerializer.Deserialize<List<QueryHistoryEntry>>(File.ReadAllText(path)) ?? new List<QueryHistoryEntry>();
            token.ThrowIfCancellationRequested(); return entries.Where(e => e != null).Take(capacity).ToArray();
        }
        catch (JsonException) { throw new InvalidDataException("The local query history is unreadable. Clear history to start a new file."); }
    }

    private void Save(string json, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false)); token.ThrowIfCancellationRequested();
            AtomicQueryFile.Commit(temporary, path, token);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
