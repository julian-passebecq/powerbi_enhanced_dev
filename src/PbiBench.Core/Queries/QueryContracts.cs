using System.Data;
using System.Text.Json.Serialization;

namespace PbiBench.Core.Queries;

public sealed record QueryRequest(string Server, string Database, string Query, int RowLimit = 10000,
    int TimeoutSeconds = 60, long DocumentRevision = 0)
{
    /// <summary>Optional transient credentials. Never included in persistence, result context, or ToString.</summary>
    [JsonIgnore] public string? ConnectionString { get; init; }
    public int MaximumResultSets { get; init; } = 32;
    public int MaximumCells { get; init; } = 1000000;
    public override string ToString() => $"DAX query on {Server}/{Database}; row limit {RowLimit}; timeout {TimeoutSeconds}s";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Server) || Server.IndexOf(';') >= 0 || Server.IndexOf('\r') >= 0 || Server.IndexOf('\n') >= 0)
            throw new ArgumentException("A server endpoint is required. Connection-string options belong in the transient connection string.", nameof(Server));
        if (string.IsNullOrWhiteSpace(Database)) throw new ArgumentException("A database is required.", nameof(Database));
        if (string.IsNullOrWhiteSpace(Query)) throw new ArgumentException("The DAX query is empty.", nameof(Query));
        var leadingKeyword = LeadingKeyword(Query);
        if (!leadingKeyword.Equals("EVALUATE", StringComparison.OrdinalIgnoreCase) && !leadingKeyword.Equals("DEFINE", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only DAX queries beginning with EVALUATE or DEFINE can run here. Model changes require a reviewed change plan.", nameof(Query));
        if (RowLimit < 1 || RowLimit > 1000000) throw new ArgumentOutOfRangeException(nameof(RowLimit), "Choose a row limit from 1 to 1,000,000.");
        if (TimeoutSeconds < 1 || TimeoutSeconds > 3600) throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds));
        if (MaximumResultSets < 1 || MaximumResultSets > 256) throw new ArgumentOutOfRangeException(nameof(MaximumResultSets));
        if (MaximumCells < 1 || MaximumCells > 10000000) throw new ArgumentOutOfRangeException(nameof(MaximumCells));
    }

    private static string LeadingKeyword(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]) || text[index] == '\uFEFF') { index++; continue; }
            if (index + 1 < text.Length && ((text[index] == '/' && text[index + 1] == '/') || (text[index] == '-' && text[index + 1] == '-')))
            {
                while (index < text.Length && text[index] != '\r' && text[index] != '\n') index++;
                continue;
            }
            if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '*')
            {
                var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (end < 0) return string.Empty;
                index = end + 2; continue;
            }
            break;
        }
        var start = index;
        while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_')) index++;
        return text.Substring(start, index - start);
    }
}

public sealed record QueryColumn(string Key, string Name, string DataType);

public sealed record QueryResultSet(int Index, string Name, IReadOnlyList<QueryColumn> Columns,
    IReadOnlyList<object?[]> Rows, bool IsTruncated)
{
    /// <summary>Stable ordinal keys keep duplicate or punctuation-heavy captions usable in WPF.</summary>
    public DataTable ToDataTable()
    {
        var table = new DataTable(Name) { Locale = System.Globalization.CultureInfo.InvariantCulture };
        foreach (var column in Columns) table.Columns.Add(new DataColumn(column.Key, typeof(object)) { Caption = column.Name });
        foreach (var row in Rows) table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
        return table;
    }
}

public sealed record QueryResult(Guid Id, string Query, string Server, string Database, DateTimeOffset StartedAt,
    TimeSpan Elapsed, IReadOnlyList<QueryResultSet> Results, long DocumentRevision, IReadOnlyList<string> Warnings);

public interface IDaxQueryService
{
    Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken);
}

/// <summary>A run owns its session and reader. Cancel must target only that session and be safe during execution.</summary>
public interface IQuerySession : IDisposable
{
    void Open(QueryRequest request);
    IDataReader Execute(string query);
    void Cancel();
}

public interface IQuerySessionFactory { IQuerySession Create(); }

public sealed class QueryExecutionException : Exception
{
    public QueryExecutionException(string message) : base(message) { }
}
