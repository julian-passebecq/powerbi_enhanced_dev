using System.Data;
using PbiBench.Core.Quality;
using PbiBench.Core.Queries;

namespace PbiBench.Semantic;

/// <summary>Only a closed set of public metric rowsets can run. Each capture owns one independent TOM session.</summary>
public sealed class TomVertiPaqSnapshotService : IVertiPaqSnapshotService
{
    private readonly IQuerySessionFactory sessions;
    public TomVertiPaqSnapshotService() : this(new TomQuerySessionFactory()) { }
    public TomVertiPaqSnapshotService(IQuerySessionFactory sessions) => this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    public static string Statement(VertiPaqRowset rowset) => "SELECT * FROM $SYSTEM." + (rowset switch
    {
        VertiPaqRowset.Model => "TMSCHEMA_MODEL", VertiPaqRowset.Tables => "TMSCHEMA_TABLES", VertiPaqRowset.Columns => "TMSCHEMA_COLUMNS", VertiPaqRowset.Partitions => "TMSCHEMA_PARTITIONS",
        VertiPaqRowset.Relationships => "TMSCHEMA_RELATIONSHIPS", VertiPaqRowset.StorageTables => "DISCOVER_STORAGE_TABLES",
        VertiPaqRowset.StorageColumns => "DISCOVER_STORAGE_TABLE_COLUMNS", VertiPaqRowset.StorageSegments => "DISCOVER_STORAGE_TABLE_COLUMN_SEGMENTS",
        _ => throw new ArgumentOutOfRangeException(nameof(rowset))
    });
    public async Task<VertiPaqSnapshot> CaptureAsync(VertiPaqCaptureRequest request, CancellationToken cancellationToken)
    {
        if (request == null) throw new ArgumentNullException(nameof(request)); request.Validate(); cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try { return await Task.Run(() => Capture(request, linked.Token), CancellationToken.None).ConfigureAwait(false); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { throw new TimeoutException("The metrics capture timed out; cancellation was requested on its private session."); }
    }
    private VertiPaqSnapshot Capture(VertiPaqCaptureRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var captured = DateTimeOffset.UtcNow;
        using var session = sessions.Create();
        // QueryRequest is used only as the existing adapter's transport context. This service has its own validated,
        // typed metric request; caller-supplied query text cannot enter this path. DAX execution validation stays unchanged.
        var context = new QueryRequest(request.Server, request.Database, Statement(VertiPaqRowset.Tables), request.MaximumRowsPerRowset, request.TimeoutSeconds) { ConnectionString = request.ConnectionString };
        try { session.Open(context); }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch { throw new QueryExecutionException("Could not open the metrics connection. Verify endpoint, database and authentication."); }
        token.ThrowIfCancellationRequested();
        var results = new List<VertiPaqRowsetResult>(); var cells = 2000000;
        foreach (VertiPaqRowset rowset in Enum.GetValues(typeof(VertiPaqRowset)))
        {
            token.ThrowIfCancellationRequested();
            if (cells <= 0) { results.Add(new(rowset, null, "The capture cell limit was reached.")); continue; }
            var gate = new object(); Task? cancel = null; var complete = false; IDataReader? reader = null;
            var registration = token.Register(() => { lock (gate) { if (!complete) cancel = Task.Run(() => { try { session.Cancel(); } catch { } }); } });
            try
            {
                reader = session.Execute(Statement(rowset));
                var columns = Enumerable.Range(0, reader.FieldCount).Select(index => new QueryColumn("C" + index, reader.GetName(index), reader.GetFieldType(index)?.FullName ?? "System.Object")).ToArray();
                var rows = new List<object?[]>(); var truncated = false;
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (rows.Count >= request.MaximumRowsPerRowset || cells < columns.Length) { truncated = true; break; }
                    var values = new object?[columns.Length];
                    for (var i = 0; i < values.Length; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(values); cells -= values.Length;
                }
                results.Add(new(rowset, new QueryResultSet(0, rowset.ToString(), columns, rows, truncated)));
            }
            catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
            catch { results.Add(new(rowset, null, "Unavailable: the endpoint may not expose this rowset or the current identity lacks permission.")); }
            finally
            {
                // Join this command's cancellation before disposing its reader or starting the next rowset.
                registration.Dispose(); lock (gate) complete = true; cancel?.GetAwaiter().GetResult();
                try { reader?.Dispose(); } catch when (token.IsCancellationRequested) { }
            }
        }
        token.ThrowIfCancellationRequested(); return VertiPaqDmvProjection.Build(request.Server, request.Database, captured, results);
    }
}
