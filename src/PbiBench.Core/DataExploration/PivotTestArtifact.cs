using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PbiBench.Core.Queries;

namespace PbiBench.Core.DataExploration;

public static class PivotTestArtifact
{
    public static PivotRegressionTest Create(string name, PivotQueryPlan plan, QueryResult result)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (result.Query != plan.Dax) throw new InvalidOperationException("The results belong to an earlier pivot query. Refresh this layout before capturing a test.");
        if (result.Results.Count != 1) throw new InvalidOperationException("A pivot snapshot requires exactly one completed result set.");
        var set = result.Results[0];
        if (set.IsTruncated) throw new InvalidOperationException("The pivot result is truncated. Raise the row limit or narrow the layout before capturing a complete regression snapshot.");
        if (set.Columns.Count != plan.ResultColumns.Count || set.Rows.Any(row => row.Length != set.Columns.Count)) throw new InvalidOperationException("The result shape does not match the pivot plan.");
        if (set.Columns.Where((column, index) => !SameResultName(column.Name, plan.ResultColumns[index].Key)).Any()) throw new InvalidOperationException("The result columns do not match the pivot plan.");
        var test = new PivotRegressionTest
        {
            Name = name, Layout = PivotQueryBuilder.Freeze(plan.Layout), Query = plan.Dax,
            ExpectedColumns = Array.AsReadOnly(set.Columns.Select(column => new PivotRegressionColumn(column.Name, column.DataType)).ToArray()),
            ExpectedRowCount = set.Rows.Count, ExpectedSha256 = Fingerprint(set)
        };
        Validate(test); return test;
    }
    public static Task SaveAsync(string path, PivotRegressionTest test, CancellationToken ct)
    { Validate(test); return PivotJsonFile.SaveAsync(path, test, ct); }
    public static async Task<PivotRegressionTest> LoadAsync(string path, CancellationToken ct)
    {
        var test = await PivotJsonFile.LoadAsync<PivotRegressionTest>(path, ct).ConfigureAwait(false); Validate(test); return test;
    }
    public static IReadOnlyList<string> Verify(PivotRegressionTest test, QueryResult result)
    {
        Validate(test);
        var failures = new List<string>();
        if (result.Query != test.Query) failures.Add("Executed DAX differs from the captured regression query.");
        if (result.Results.Count != 1) { failures.Add("Expected exactly one result set."); return failures; }
        var set = result.Results[0];
        if (set.IsTruncated) failures.Add("The result was truncated; a full snapshot comparison is not possible.");
        if (set.Rows.Count != test.ExpectedRowCount) failures.Add($"Expected {test.ExpectedRowCount} rows; received {set.Rows.Count}.");
        if (set.Columns.Count != test.ExpectedColumns.Count || set.Columns.Where((column, index) => index >= test.ExpectedColumns.Count ||
            column.Name != test.ExpectedColumns[index].Name || column.DataType != test.ExpectedColumns[index].DataType).Any()) failures.Add("Result column names or types changed.");
        if (Fingerprint(set) != test.ExpectedSha256) failures.Add("Result values or deterministic row order changed.");
        return failures.AsReadOnly();
    }
    private static void Validate(PivotRegressionTest test)
    {
        if (test == null) throw new ArgumentNullException(nameof(test));
        if (test.Version != 1 || test.Kind != "pbibench.pivot.snapshot") throw new InvalidDataException("This pivot regression format is not supported.");
        if (string.IsNullOrWhiteSpace(test.Name) || test.Name.Length > 256 || string.IsNullOrWhiteSpace(test.Query) || test.ExpectedColumns == null || test.ExpectedColumns.Count == 0 ||
            test.ExpectedColumns.Any(column => column == null) || test.ExpectedRowCount < 0 || test.ExpectedSha256 == null || test.ExpectedSha256.Length != 64 || test.ExpectedSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("The pivot regression file has incomplete assertions.");
        PivotQueryBuilder.ValidateShape(test.Layout);
    }
    private static string Fingerprint(QueryResultSet result)
    {
        using var hash = SHA256.Create();
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true))
        {
            writer.WriteLine(JsonSerializer.Serialize(result.Columns.Select(column => new[] { column.Name, column.DataType }).ToArray()));
            foreach (var row in result.Rows) writer.WriteLine(JsonSerializer.Serialize(row.Select(CanonicalCell).ToArray()));
        }
        stream.Position = 0;
        return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private static string CanonicalCell(object? value) => value switch
    {
        null => "blank:", DBNull => "blank:", string text => "text:" + text,
        DateTime date => "datetime:" + date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => "datetimeoffset:" + date.ToString("O", CultureInfo.InvariantCulture),
        byte[] bytes => "binary:" + Convert.ToBase64String(bytes),
        bool boolean => "boolean:" + (boolean ? "true" : "false"),
        decimal number => "decimal:" + number.ToString("G29", CultureInfo.InvariantCulture),
        double number => "double:" + number.ToString("R", CultureInfo.InvariantCulture),
        float number => "single:" + number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => value.GetType().Name + ":" + formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.GetType().Name + ":" + value
    };
    private static bool SameResultName(string actual, string key) => actual == key || actual == "[" + key + "]";
}
