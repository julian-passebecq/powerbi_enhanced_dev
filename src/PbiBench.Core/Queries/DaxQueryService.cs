using System.Data;
using System.Diagnostics;
using System.Data.Common;

namespace PbiBench.Core.Queries;

/// <summary>Owns bounded result materialization and run lifetime; adapters own their private transport.</summary>
public class DaxQueryService : IDaxQueryService
{
    private readonly IQuerySessionFactory sessions;
    public DaxQueryService(IQuerySessionFactory sessions) => this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));

    public async Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        request.Validate(); cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try { return await Task.Run(() => Execute(request, linked.Token), CancellationToken.None).ConfigureAwait(false); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        { throw new TimeoutException($"The query exceeded its {request.TimeoutSeconds}-second timeout and cancellation was requested."); }
    }

    private QueryResult Execute(QueryRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var started = DateTimeOffset.UtcNow; var elapsed = Stopwatch.StartNew();
        using var session = sessions.Create();
        try { session.Open(request); }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception ex) { throw new QueryExecutionException(Redact(ex.Message, request)); }
        token.ThrowIfCancellationRequested();
        var cancelGate = new object(); Task? cancelTask = null; var complete = false;
        var registration = token.Register(() =>
        {
            lock (cancelGate)
            {
                if (complete) return;
                // CancelCommand performs network I/O. Keep cancellation-button handlers responsive.
                cancelTask = Task.Run(() => { try { session.Cancel(); } catch { /* The execution path reports cancellation. */ } });
            }
        });
        IDataReader? reader = null;
        try
        {
            token.ThrowIfCancellationRequested();
            reader = session.Execute(request.Query);
            if (reader == null) throw new QueryExecutionException("The server did not return a query reader.");
            var results = new List<QueryResultSet>(); var warnings = new List<string>(); var remainingCells = request.MaximumCells;
            do
            {
                token.ThrowIfCancellationRequested();
                if (results.Count == request.MaximumResultSets)
                {
                    warnings.Add($"Only the first {request.MaximumResultSets} result sets were retained.");
                    break;
                }
                var columns = Enumerable.Range(0, reader.FieldCount).Select(i => new QueryColumn("C" + i, reader.GetName(i), reader.GetFieldType(i)?.FullName ?? "System.Object")).ToArray();
                var rows = new List<object?[]>(); var truncated = false;
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (rows.Count >= request.RowLimit || remainingCells < columns.Length)
                    {
                        truncated = true;
                        break;
                    }
                    var row = new object?[columns.Length];
                    for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row); remainingCells -= row.Length;
                }
                results.Add(new QueryResultSet(results.Count, "Result " + (results.Count + 1), columns, rows, truncated));
                if (truncated) warnings.Add($"Result {results.Count} was truncated. Limits bound retained data, not server query work.");
                token.ThrowIfCancellationRequested();
                if (remainingCells == 0)
                {
                    warnings.Add($"The {request.MaximumCells:N0}-cell limit was reached; later results were not retrieved.");
                    break;
                }
            } while (reader.NextResult());
            token.ThrowIfCancellationRequested(); elapsed.Stop();
            return new QueryResult(Guid.NewGuid(), request.Query, request.Server, request.Database, started, elapsed.Elapsed, results, request.DocumentRevision, warnings);
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception ex) { throw new QueryExecutionException(Redact(ex.Message, request)); }
        finally
        {
            // Registration disposal joins an in-flight callback; then join its cancellation worker
            // before session disposal to prevent Cancel racing a disconnected/reused server.
            registration.Dispose();
            lock (cancelGate) complete = true;
            cancelTask?.GetAwaiter().GetResult();
            try { reader?.Dispose(); } catch when (token.IsCancellationRequested) { }
        }
    }

    private static string Redact(string message, QueryRequest request)
    {
        if (string.IsNullOrEmpty(request.ConnectionString)) return message;
        message = message.Replace(request.ConnectionString!, "[connection]");
        try
        {
            var values = new DbConnectionStringBuilder { ConnectionString = request.ConnectionString };
            foreach (string key in values.Keys)
                if (key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || key.Equals("pwd", StringComparison.OrdinalIgnoreCase) || key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var secret = Convert.ToString(values[key]);
                    if (!string.IsNullOrEmpty(secret)) message = message.Replace(secret, "[redacted]");
                }
        }
        catch (ArgumentException) { return "The query connection string is invalid. Verify its settings."; }
        return message;
    }
}
