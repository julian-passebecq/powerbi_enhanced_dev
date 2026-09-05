using System.Security.Cryptography;
using System.Text;
using PbiBench.Core.Queries;

namespace PbiBench.Core.Quality;

/// <summary>Read-only semantic assertions. Each call uses the injected query service's independent engine session.</summary>
public sealed class SemanticTestService
{
    private readonly IDaxQueryService queries;
    public SemanticTestService(IDaxQueryService queries) { this.queries = queries ?? throw new ArgumentNullException(nameof(queries)); }

    public async Task<SemanticTestResult> RunAsync(SemanticTestDefinition definition, QueryRequest target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(definition); var test = Freeze(definition); var request = Request(test, target, test.Query); request.Validate();
        var started = DateTimeOffset.UtcNow; var timer = System.Diagnostics.Stopwatch.StartNew(); Guid? firstId = null;
        try
        {
            var first = await Execute(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested(); var table = ValidateResult(request, first); firstId = first.Id;
            bool passed; string evidence; Guid? secondId = null;
            switch (test.Kind)
            {
                case SemanticTestKind.RowCount:
                    passed = CompareNumbers(table.Rows.Count, test.ExpectedRowCount, test.Comparison); evidence = $"Returned {table.Rows.Count:N0} rows; expected {test.Comparison} {test.ExpectedRowCount:N0}."; break;
                case SemanticTestKind.Scalar:
                    if (table.Rows.Count != 1 || table.Columns.Count != 1) throw new InvalidDataException("Scalar assertions require exactly one row and one column.");
                    passed = Compare(SemanticValue.From(table.Rows[0][0]), test.Expected, test); evidence = passed ? "Scalar assertion passed." : "The scalar value did not satisfy the assertion."; break;
                case SemanticTestKind.Table:
                    if (test.ColumnIndex >= table.Columns.Count) throw new InvalidDataException("The assertion column is outside the result schema.");
                    if (table.Rows.Count == 0) { passed = false; evidence = "The table assertion returned no rows; it cannot establish that values satisfy the assertion."; break; }
                    var failed = 0;
                    foreach (var row in table.Rows) { cancellationToken.ThrowIfCancellationRequested(); if (!Compare(SemanticValue.From(row[test.ColumnIndex]), test.Expected, test)) failed++; }
                    passed = failed == 0; evidence = $"{table.Rows.Count - failed:N0} of {table.Rows.Count:N0} rows satisfied the assertion in column {test.ColumnIndex + 1}."; break;
                case SemanticTestKind.Snapshot:
                    var actual = Capture(request.Query, table, cancellationToken);
                    (passed, evidence) = CompareTables(test.Snapshot!, actual, test, cancellationToken); break;
                case SemanticTestKind.CompareQueries:
                    var secondRequest = Request(test, target, test.ComparisonQuery!);
                    var second = await Execute(secondRequest, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested(); var secondTable = ValidateResult(secondRequest, second); secondId = second.Id;
                    if (second.Id == first.Id) throw new InvalidDataException("The A/B queries returned the same execution identity; independent runs are required.");
                    (passed, evidence) = CompareTables(Capture(request.Query, table, cancellationToken), Capture(secondRequest.Query, secondTable, cancellationToken), test, cancellationToken);
                    evidence = "A/B output comparison: " + evidence + " Queries ran separately on the selected model; concurrent data changes can affect equivalence."; break;
                default: throw new InvalidDataException("Unknown semantic assertion kind.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(test.Id, test.Name, passed ? SemanticTestOutcome.Passed : SemanticTestOutcome.Failed, evidence, Hash(test.Query), started, timer.Elapsed.TotalMilliseconds, firstId, secondId,
                test.Kind == SemanticTestKind.CompareQueries ? Hash(test.ComparisonQuery!) : null);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException error)
        { return new(test.Id, test.Name, SemanticTestOutcome.Error, error.Message, Hash(test.Query), started, timer.Elapsed.TotalMilliseconds, firstId); }
        catch (Exception)
        {
            // Provider exceptions can contain credentials. Persist only a neutral outcome; never manufacture a passing result.
            return new(test.Id, test.Name, SemanticTestOutcome.Error, "The engine run failed. Verify connection, DAX and query limits, then run again.", Hash(test.Query), started, timer.Elapsed.TotalMilliseconds, firstId);
        }
    }

    public async Task<SemanticSnapshot> CaptureSnapshotAsync(SemanticTestDefinition definition, QueryRequest target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!definition.OrderIsDeterministic) throw new InvalidDataException("Confirm deterministic row ordering before capturing an ordered snapshot.");
        Validate(definition with { Kind = SemanticTestKind.RowCount, Snapshot = null });
        var request = Request(definition, target, definition.Query); request.Validate();
        var result = await Execute(request, cancellationToken).ConfigureAwait(false); cancellationToken.ThrowIfCancellationRequested();
        return Capture(request.Query, ValidateResult(request, result), cancellationToken);
    }

    public static void Validate(SemanticTestDefinition test)
    {
        if (test == null) throw new ArgumentNullException(nameof(test));
        if (string.IsNullOrWhiteSpace(test.Id) || test.Id.Length > 200 || string.IsNullOrWhiteSpace(test.Name) || test.Name.Length > 500) throw new InvalidDataException("Tests require a bounded id and name.");
        if (!Enum.IsDefined(typeof(SemanticTestKind), test.Kind) || !Enum.IsDefined(typeof(SemanticComparison), test.Comparison)) throw new InvalidDataException("Unknown assertion kind or comparison.");
        if (test.Query == null || test.Query.Length > 1000000 || (test.ComparisonQuery?.Length ?? 0) > 1000000) throw new InvalidDataException("A test query exceeds its size limit.");
        new QueryRequest("validation", "validation", test.Query, test.RowLimit, test.TimeoutSeconds).Validate();
        if (test.RowLimit > 100000) throw new InvalidDataException("Semantic tests support at most 100,000 rows per result.");
        if (test.ColumnIndex < 0 || test.ExpectedRowCount < 0) throw new InvalidDataException("Column indexes and expected row counts cannot be negative.");
        if (!FiniteNonnegative(test.AbsoluteTolerance) || !FiniteNonnegative(test.RelativeTolerance)) throw new InvalidDataException("Tolerances must be finite and nonnegative.");
        if (test.Expected == null) throw new InvalidDataException("An expected value is required."); test.Expected.Validate();
        if ((test.Kind == SemanticTestKind.Snapshot || test.Kind == SemanticTestKind.CompareQueries) && !test.OrderIsDeterministic) throw new InvalidDataException("Ordered comparisons require confirmation that both queries return deterministic row order. Include an ORDER BY with a unique tie breaker where needed.");
        if (test.Kind == SemanticTestKind.CompareQueries) new QueryRequest("validation", "validation", test.ComparisonQuery ?? "", test.RowLimit, test.TimeoutSeconds).Validate();
        if (test.Kind == SemanticTestKind.Snapshot && test.Snapshot == null) throw new InvalidDataException("Capture or import a baseline before running a snapshot assertion.");
        if (test.Snapshot != null)
        {
            if (test.Snapshot.QueryHash != Hash(test.Query)) throw new InvalidDataException("The snapshot belongs to different DAX. Capture a new baseline or restore the original query.");
            if (test.Snapshot.Columns == null || test.Snapshot.Rows == null || test.Snapshot.Columns.Count < 1 || test.Snapshot.Columns.Count > 1000 || test.Snapshot.Rows.Count > 100000 || (long)test.Snapshot.Columns.Count * test.Snapshot.Rows.Count > 250000) throw new InvalidDataException("The snapshot has an invalid or excessive shape.");
            foreach (var column in test.Snapshot.Columns) if (column == null || column.Name == null || column.DataType == null) throw new InvalidDataException("Snapshot columns require a name and type.");
            foreach (var row in test.Snapshot.Rows)
            {
                if (row == null || row.Count != test.Snapshot.Columns.Count) throw new InvalidDataException("A snapshot row does not match its schema.");
                foreach (var value in row) { if (value == null) throw new InvalidDataException("A snapshot value is missing its type."); value.Validate(); }
            }
        }
    }

    private static SemanticTestDefinition Freeze(SemanticTestDefinition test) => test.Snapshot == null ? test : test with
    { Snapshot = test.Snapshot with { Columns = test.Snapshot.Columns.ToArray(), Rows = test.Snapshot.Rows.Select(r => (IReadOnlyList<SemanticValue>)r.ToArray()).ToArray() } };
    private async Task<QueryResult> Execute(QueryRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var started = DateTimeOffset.UtcNow; QueryResult result;
        try { result = await queries.ExecuteAsync(request, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new QueryExecutionException("The engine run failed. Verify the connection and query, then retry."); }
        if (result == null || result.StartedAt < started || result.StartedAt > DateTimeOffset.UtcNow || result.Elapsed < TimeSpan.Zero)
            throw new InvalidDataException("The query service returned a stale or invalid execution timestamp. Run the test again.");
        return result;
    }
    private static bool FiniteNonnegative(double n) => !double.IsNaN(n) && !double.IsInfinity(n) && n >= 0;
    private static QueryRequest Request(SemanticTestDefinition test, QueryRequest target, string query) => target with
    { Query = query, RowLimit = test.RowLimit, TimeoutSeconds = test.TimeoutSeconds, MaximumResultSets = 2, MaximumCells = 250000 };

    private static QueryResultSet ValidateResult(QueryRequest request, QueryResult result)
    {
        if (result == null || result.Id == Guid.Empty || result.Query != request.Query || result.Server != request.Server || result.Database != request.Database || result.DocumentRevision != request.DocumentRevision)
            throw new InvalidDataException("The returned run does not match this test's query, model and document revision.");
        if (result.Results == null || result.Results.Count != 1 || result.Results[0] == null) throw new InvalidDataException("Semantic assertions require exactly one result set; additional or missing results cannot be ignored.");
        var set = result.Results[0];
        if (set.IsTruncated || (result.Warnings != null && result.Warnings.Count != 0)) throw new InvalidDataException("The engine result was truncated or returned warnings. Raise limits or resolve the warning before asserting results.");
        if (set.Columns == null || set.Rows == null || set.Columns.Count == 0 || set.Columns.Count > 1000 || set.Rows.Count > request.RowLimit || (long)set.Columns.Count * set.Rows.Count > request.MaximumCells) throw new InvalidDataException("The engine result has an invalid or excessive shape.");
        if (set.Columns.Any(c => c == null || c.Name == null || c.DataType == null) || set.Rows.Any(r => r == null || r.Length != set.Columns.Count)) throw new InvalidDataException("The engine result rows do not match their column schema.");
        return set;
    }
    private static SemanticSnapshot Capture(string query, QueryResultSet set, CancellationToken token)
    {
        var rows = new List<IReadOnlyList<SemanticValue>>();
        foreach (var row in set.Rows) { token.ThrowIfCancellationRequested(); rows.Add(row.Select(SemanticValue.From).ToArray()); }
        return new(Hash(query), set.Columns.Select(c => new SemanticSnapshotColumn(c.Name, c.DataType)).ToArray(), rows);
    }
    private static (bool Passed, string Evidence) CompareTables(SemanticSnapshot expected, SemanticSnapshot actual, SemanticTestDefinition test, CancellationToken token)
    {
        if (!expected.Columns.SequenceEqual(actual.Columns)) return (false, "The ordered column names or data types differ from the expected schema.");
        if (expected.Rows.Count != actual.Rows.Count) return (false, $"Expected {expected.Rows.Count:N0} rows; received {actual.Rows.Count:N0}.");
        for (var row = 0; row < expected.Rows.Count; row++)
        {
            token.ThrowIfCancellationRequested();
            for (var col = 0; col < expected.Columns.Count; col++)
                if (!Compare(actual.Rows[row][col], expected.Rows[row][col], test with { Comparison = SemanticComparison.Equal })) return (false, $"Ordered values differ at row {row + 1:N0}, column {col + 1}.");
        }
        return (true, $"All {actual.Rows.Count:N0} ordered rows and {actual.Columns.Count} columns match.");
    }
    private static bool Compare(SemanticValue actual, SemanticValue expected, SemanticTestDefinition test)
    {
        actual.Validate(); expected.Validate();
        if (actual.Kind != expected.Kind) return test.Comparison == SemanticComparison.NotEqual;
        if (actual.Kind == SemanticValueKind.Number)
        {
            var number = SemanticTestValueComparison.CompareNumbers(actual.Value!, expected.Value!, test.AbsoluteTolerance, test.RelativeTolerance);
            return CompareOrder(number.Order, number.Equal, test.Comparison);
        }
        if (actual.Kind == SemanticValueKind.DateTime)
        {
            var a = SemanticTestValueComparison.Date(actual.Value!); var b = SemanticTestValueComparison.Date(expected.Value!);
            if (a.Zoned != b.Zoned) throw new InvalidDataException("Date/time comparison requires consistent timezone semantics; compare two unzoned values or two values with explicit offsets.");
            return CompareOrder(a.Ticks.CompareTo(b.Ticks), a.Ticks == b.Ticks, test.Comparison);
        }
        var same = string.Equals(actual.Value, expected.Value, StringComparison.Ordinal);
        if (test.Comparison == SemanticComparison.Equal) return same;
        if (test.Comparison == SemanticComparison.NotEqual) return !same;
        if (actual.Kind != SemanticValueKind.Text && actual.Kind != SemanticValueKind.DateTime) throw new InvalidDataException("Ordering assertions require numbers, text or date/time values.");
        return CompareOrder(string.Compare(actual.Value, expected.Value, StringComparison.Ordinal), same, test.Comparison);
    }
    private static bool CompareNumbers(long actual, long expected, SemanticComparison comparison) => CompareOrder(actual.CompareTo(expected), actual == expected, comparison);
    private static bool CompareOrder(int order, bool equal, SemanticComparison comparison) => comparison switch
    {
        SemanticComparison.Equal => equal, SemanticComparison.NotEqual => !equal,
        SemanticComparison.GreaterThan => order > 0 && !equal, SemanticComparison.GreaterThanOrEqual => order > 0 || equal,
        SemanticComparison.LessThan => order < 0 && !equal, SemanticComparison.LessThanOrEqual => order < 0 || equal,
        _ => throw new InvalidDataException("Unknown comparison.")
    };
    public static string Hash(string query)
    {
        using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(query))).Replace("-", "").ToLowerInvariant();
    }
}
