using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PbiBench.Core.Queries;

namespace PbiBench.Core.Quality;

public sealed record QueryBenchmarkRequest(string Server, string Database, string BaselineQuery, string CandidateQuery,
    int Iterations = 3, int TimeoutSeconds = 60, int RowLimit = 10000, string? ModelFingerprint = null, long DocumentRevision = 0)
{
    [JsonIgnore] public string? ConnectionString { get; init; }
    public override string ToString() => "Query A/B benchmark on " + Server + " / " + Database;
}
public sealed record QueryBenchmarkSample(string Variant, int Iteration, double ElapsedMilliseconds, long RowCount,
    string? ResultHash, bool Complete, string? Error);
public sealed record QueryBenchmarkEvidence(Guid Id, DateTimeOffset StartedAt, string Server, string Database,
    string BaselineQuery, string CandidateQuery, string BaselineQueryHash, string CandidateQueryHash, string? ModelFingerprint,
    IReadOnlyList<QueryBenchmarkSample> Samples, bool EquivalentResults, double? BaselineMedianMs, double? CandidateMedianMs,
    double? ChangePercent, IReadOnlyList<string> Warnings)
{
    public string TimingSource => "Client elapsed including connection and result transfer; cache not controlled";
    public string Summary => !EquivalentResults ? "Results are incomplete, failed, or differ; no performance comparison is accepted." :
        $"Exact ordered results match. Baseline median {BaselineMedianMs:N1} ms; candidate median {CandidateMedianMs:N1} ms; change {ChangePercent:N1}%.";
}

/// <summary>Alternating bounded read-only DAX runs; never clears caches or starts server traces.</summary>
public sealed class QueryBenchmarkService
{
    private readonly IDaxQueryService queries;
    public QueryBenchmarkService(IDaxQueryService queries) => this.queries = queries ?? throw new ArgumentNullException(nameof(queries));
    public async Task<QueryBenchmarkEvidence> RunAsync(QueryBenchmarkRequest request, CancellationToken cancellationToken)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Iterations < 1 || request.Iterations > 10) throw new ArgumentOutOfRangeException(nameof(request.Iterations));
        if (request.RowLimit > 100000) throw new ArgumentOutOfRangeException(nameof(request.RowLimit));
        QueryRequest Query(string text) => new(request.Server, request.Database, text, request.RowLimit, request.TimeoutSeconds, request.DocumentRevision)
            { ConnectionString = request.ConnectionString, MaximumCells = 250000, MaximumResultSets = 16 };
        var baseline = Query(request.BaselineQuery); var candidate = Query(request.CandidateQuery);
        baseline.Validate(); candidate.Validate(); cancellationToken.ThrowIfCancellationRequested();
        var started = DateTimeOffset.UtcNow; var samples = new List<QueryBenchmarkSample>(); var resultIds = new HashSet<Guid>();
        var warnings = new List<string> { "Client timings include a fresh connection and result transfer. Engine cache and concurrent load are uncontrolled; FE/SE timings are unavailable.",
            "Runs alternate baseline/candidate. Result equivalence is exact, typed and row-order-sensitive; matching results do not prove every report or security context is equivalent." };
        var failed = false;
        for (var iteration = 1; iteration <= request.Iterations && !failed; iteration++)
        {
            foreach (var variant in new[] { (Name: "Baseline", Query: baseline), (Name: "Candidate", Query: candidate) })
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var executionStarted = DateTimeOffset.UtcNow;
                    var result = await queries.ExecuteAsync(variant.Query, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    var invalid = ValidateResult(variant.Query, result, resultIds, executionStarted);
                    if (invalid != null)
                    {
                        samples.Add(new(variant.Name, iteration, 0, 0, null, false, invalid)); failed = true; break;
                    }
                    var complete = !result.Results.Any(set => set.IsTruncated) && result.Warnings.Count == 0;
                    samples.Add(new(variant.Name, iteration, result.Elapsed.TotalMilliseconds, result.Results.Sum(set => (long)set.Rows.Count), complete ? HashResult(result, cancellationToken) : null, complete, null));
                    if (!complete) warnings.Add(variant.Name + " result retention was incomplete; increase explicit limits or reduce the benchmark query's output.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    samples.Add(new(variant.Name, iteration, 0, 0, null, false, ex is TimeoutException ? "Query timeout." : "Query failed. Run it in DAX to inspect the error."));
                    failed = true; break;
                }
            }
        }
        var equivalent = samples.Count == request.Iterations * 2 && samples.All(sample => sample.Complete && sample.Error == null)
            && samples.Select(sample => sample.ResultHash).Distinct(StringComparer.Ordinal).Count() == 1;
        var baselineMedian = equivalent ? Median(samples.Where(sample => sample.Variant == "Baseline").Select(sample => sample.ElapsedMilliseconds)) : (double?)null;
        var candidateMedian = equivalent ? Median(samples.Where(sample => sample.Variant == "Candidate").Select(sample => sample.ElapsedMilliseconds)) : (double?)null;
        var change = baselineMedian > 0 ? (candidateMedian - baselineMedian) / baselineMedian * 100 : null;
        return new(Guid.NewGuid(), started, request.Server, request.Database, request.BaselineQuery, request.CandidateQuery, Hash(request.BaselineQuery), Hash(request.CandidateQuery),
            request.ModelFingerprint, samples.ToArray(), equivalent, baselineMedian, candidateMedian, change, warnings.Distinct().ToArray());
    }
    private static string? ValidateResult(QueryRequest request, QueryResult result, HashSet<Guid> ids, DateTimeOffset executionStarted)
    {
        if (result == null || result.Server != request.Server || result.Database != request.Database || result.Query != request.Query || result.DocumentRevision != request.DocumentRevision)
            return "The provider returned a result for another query, target, or document revision.";
        if (result.Id == Guid.Empty || !ids.Add(result.Id)) return "The provider returned an empty or reused execution identity.";
        if (result.Elapsed < TimeSpan.Zero || result.Results == null || result.Results.Count == 0 || result.Results.Count > request.MaximumResultSets || result.Warnings == null ||
            result.StartedAt < executionStarted.AddSeconds(-1) || result.StartedAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return "The provider returned missing results or invalid timing metadata.";
        long cells = 0;
        for (var index = 0; index < result.Results.Count; index++)
        {
            var set = result.Results[index];
            if (set == null || set.Index != index || set.Columns == null || set.Columns.Count == 0 || set.Rows == null || set.Rows.Count > request.RowLimit ||
                set.Columns.Any(column => column == null || string.IsNullOrWhiteSpace(column.Key) || string.IsNullOrWhiteSpace(column.DataType)) ||
                set.Columns.Select(column => column.Key).Distinct(StringComparer.Ordinal).Count() != set.Columns.Count ||
                set.Rows.Any(row => row == null || row.Length != set.Columns.Count)) return "The provider returned a malformed result grid.";
            cells += (long)set.Columns.Count * set.Rows.Count;
            if (cells > request.MaximumCells) return "The provider exceeded the retained-cell limit.";
        }
        return null;
    }
    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray(); var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }
    public static string Hash(string text)
    {
        using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
    }
    public static string HashResult(QueryResult result, CancellationToken cancellationToken = default)
    {
        // Length-prefix each field and include CLR types to distinguish null/empty, numeric types, delimiters and grids.
        var text = new StringBuilder();
        void Field(string? value) { if (value == null) text.Append("-1:"); else text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value); }
        foreach (var set in result.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Field("Result"); Field(set.Columns.Count.ToString(CultureInfo.InvariantCulture)); Field(set.Rows.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var column in set.Columns) { Field(column.Name); Field(column.DataType); }
            foreach (var row in set.Rows) foreach (var value in row)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Field(value?.GetType().FullName);
                Field(value switch { null => null, DateTime date => date.ToString("O", CultureInfo.InvariantCulture), DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
                    double number => number.ToString("R", CultureInfo.InvariantCulture), float number => number.ToString("R", CultureInfo.InvariantCulture), byte[] bytes => Convert.ToBase64String(bytes),
                    IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() });
            }
        }
        return Hash(text.ToString());
    }
}

public static class QueryBenchmarkStore
{
    public static async Task SaveAsync(QueryBenchmarkEvidence evidence, string path, CancellationToken cancellationToken)
    {
        if (evidence == null) throw new ArgumentNullException(nameof(evidence));
        var destination = Path.GetFullPath(path); var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory); var temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(evidence, new JsonSerializerOptions { WriteIndented = true });
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)) await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            AtomicQueryFile.Commit(temporary, destination, cancellationToken);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
