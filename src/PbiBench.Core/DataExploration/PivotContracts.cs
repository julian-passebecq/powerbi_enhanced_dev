namespace PbiBench.Core.DataExploration;

public enum PivotAggregation { Measure, Sum, Average, Min, Max, Count, DistinctCount }
public sealed record PivotAxisField(string Table, string Column, bool Descending = false);
public sealed record PivotValue(string Table, string Name, PivotAggregation Aggregation = PivotAggregation.Measure, string? Caption = null);

public sealed record PivotLayout
{
    public int Version { get; init; } = 1;
    public string Name { get; init; } = "Pivot";
    public IReadOnlyList<PivotAxisField> Rows { get; init; } = Array.Empty<PivotAxisField>();
    public IReadOnlyList<PivotAxisField> Columns { get; init; } = Array.Empty<PivotAxisField>();
    public IReadOnlyList<PivotValue> Values { get; init; } = Array.Empty<PivotValue>();
    public IReadOnlyList<DataFilter> Filters { get; init; } = Array.Empty<DataFilter>();
    public bool IncludeRowTotals { get; init; } = true;
    public bool IncludeColumnTotals { get; init; } = true;
    public bool AutoRefresh { get; init; }
    public int RowLimit { get; init; } = 1000;
}

public enum PivotResultRole { Row, Column, Value, RowTotalFlag, ColumnTotalFlag }
public sealed record PivotResultColumn(string Key, string Caption, PivotResultRole Role, int Ordinal);
public sealed record PivotQueryPlan(string Dax, IReadOnlyList<PivotResultColumn> ResultColumns, int RowLimit, IReadOnlyList<string> Warnings)
{
    public PivotLayout Layout { get; init; } = new();
}

public sealed record PivotRegressionColumn(string Name, string DataType);
public sealed record PivotRegressionTest
{
    public int Version { get; init; } = 1;
    public string Kind { get; init; } = "pbibench.pivot.snapshot";
    public string Name { get; init; } = "Pivot regression";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public PivotLayout Layout { get; init; } = new();
    public string Query { get; init; } = "";
    public IReadOnlyList<PivotRegressionColumn> ExpectedColumns { get; init; } = Array.Empty<PivotRegressionColumn>();
    public int ExpectedRowCount { get; init; }
    public string ExpectedSha256 { get; init; } = "";
}
