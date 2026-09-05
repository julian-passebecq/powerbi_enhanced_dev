using System.Globalization;
using System.Text.Json.Serialization;

namespace PbiBench.Core.Quality;

public enum SemanticTestKind { Scalar, RowCount, Table, Snapshot, CompareQueries }
public enum SemanticComparison { Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual }
public enum SemanticValueKind { Blank, Number, Text, Boolean, DateTime }
public enum SemanticTestOutcome { Passed, Failed, Error }

/// <summary>Portable, invariant values; BLANK is distinct from zero, false and empty text.</summary>
public sealed record SemanticValue([property: JsonRequired] SemanticValueKind Kind, [property: JsonRequired] string? Value)
{
    public static SemanticValue Blank { get; } = new(SemanticValueKind.Blank, null);
    public static SemanticValue From(object? value)
    {
        if (value == null || value == DBNull.Value) return Blank;
        if (value is string text) return new(SemanticValueKind.Text, text);
        if (value is bool flag) return new(SemanticValueKind.Boolean, flag ? "true" : "false");
        if (value is DateTime time) return new(SemanticValueKind.DateTime, time.ToString("O", CultureInfo.InvariantCulture));
        if (value is DateTimeOffset offset) return new(SemanticValueKind.DateTime, offset.ToString("O", CultureInfo.InvariantCulture));
        if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong || value is decimal)
            return new(SemanticValueKind.Number, Convert.ToString(value, CultureInfo.InvariantCulture));
        if (value is double number && !double.IsNaN(number) && !double.IsInfinity(number)) return new(SemanticValueKind.Number, number.ToString("R", CultureInfo.InvariantCulture));
        if (value is float single && !float.IsNaN(single) && !float.IsInfinity(single)) return new(SemanticValueKind.Number, single.ToString("R", CultureInfo.InvariantCulture));
        throw new InvalidDataException("The query returned a value type that semantic tests cannot compare safely.");
    }
    public void Validate()
    {
        if (!Enum.IsDefined(typeof(SemanticValueKind), Kind)) throw new InvalidDataException("Unknown semantic value type.");
        if (Kind == SemanticValueKind.Blank) { if (Value != null) throw new InvalidDataException("BLANK must have a null value."); return; }
        if (Value == null) throw new InvalidDataException("A typed semantic value requires a value.");
        if (Kind == SemanticValueKind.Number) SemanticTestValueComparison.Number(Value);
        if (Kind == SemanticValueKind.Boolean && Value != "true" && Value != "false") throw new InvalidDataException("Boolean values must be true or false.");
        if (Kind == SemanticValueKind.DateTime) SemanticTestValueComparison.Date(Value);
    }
    public override string ToString() => Kind == SemanticValueKind.Blank ? "BLANK" : Value ?? "";
}

public sealed record SemanticSnapshotColumn(string Name, string DataType);
public sealed record SemanticSnapshot(string QueryHash, IReadOnlyList<SemanticSnapshotColumn> Columns, IReadOnlyList<IReadOnlyList<SemanticValue>> Rows);
public sealed record SemanticTestDefinition
{
    [JsonRequired] public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonRequired] public string Name { get; init; } = "New semantic test";
    [JsonRequired] public string Query { get; init; } = "EVALUATE ROW(\"Value\", 1)";
    [JsonRequired] public SemanticTestKind Kind { get; init; }
    public SemanticComparison Comparison { get; init; }
    [JsonRequired] public SemanticValue Expected { get; init; } = SemanticValue.From(1);
    public int ColumnIndex { get; init; }
    public long ExpectedRowCount { get; init; } = 1;
    public double AbsoluteTolerance { get; init; }
    public double RelativeTolerance { get; init; }
    public bool OrderIsDeterministic { get; init; }
    public string? ComparisonQuery { get; init; }
    public SemanticSnapshot? Snapshot { get; init; }
    public int RowLimit { get; init; } = 10000;
    public int TimeoutSeconds { get; init; } = 60;
}

/// <summary>Versioned project artifact. No endpoint, connection string, credential or model handler is stored.</summary>
public sealed record SemanticTestArtifact(int FormatVersion, IReadOnlyList<SemanticTestDefinition> Tests)
{
    public const int CurrentVersion = 1;
}
public sealed record SemanticTestResult(string TestId, string Name, SemanticTestOutcome Outcome, string Evidence,
    string QueryHash, DateTimeOffset StartedAt, double ElapsedMilliseconds, Guid? ExecutionId, Guid? ComparisonExecutionId = null, string? ComparisonQueryHash = null);
public sealed record SemanticTestReport(int FormatVersion, IReadOnlyList<SemanticTestResult> Results)
{
    [JsonIgnore] public bool Passed => Results.Count > 0 && Results.All(r => r.Outcome == SemanticTestOutcome.Passed);
}
