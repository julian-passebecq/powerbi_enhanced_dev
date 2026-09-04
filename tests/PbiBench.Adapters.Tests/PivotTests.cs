using System.Globalization;
using PbiBench.Core.DataExploration;
using PbiBench.Core.Queries;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class PivotTests
{
    private static readonly DataModelSchema Schema = new("Sales model", new[]
    {
        new DataTableSchema("Sales", DataStorageMode.Import, new[]
        {
            new DataColumnSchema("Amount", "Decimal"), new DataColumnSchema("Category", "String"),
            new DataColumnSchema("OrderDate", "DateTime"), new DataColumnSchema("Quantity", "Int64"), new DataColumnSchema("Active", "Boolean")
        }, new[] { new DataMeasureSchema("Revenue", "SUM(Sales[Amount])"), new DataMeasureSchema("Margin %", "DIVIDE([Revenue],1)") }, Array.Empty<string>()),
        new DataTableSchema("Date", DataStorageMode.Import, new[] { new DataColumnSchema("Date", "DateTime", IsKey: true), new DataColumnSchema("Year", "Int64") }, Array.Empty<DataMeasureSchema>(), new[] { "Date" }),
        new DataTableSchema("Owner's \"Table\"", DataStorageMode.DirectLake, new[] { new DataColumnSchema("A] \"B\"", "String") },
            new[] { new DataMeasureSchema("A] Measure", "1") }, Array.Empty<string>())
    }, new[] { new DataRelationshipSchema("Sales_Date", "Sales", "OrderDate", "Date", "Date", true) });
    private static PivotLayout Layout => new()
    {
        Name = "Revenue by category and year", Rows = new[] { new PivotAxisField("Sales", "Category") },
        Columns = new[] { new PivotAxisField("Date", "Year") }, Values = new[] { new PivotValue("Sales", "Revenue") }
    };

    [Fact]
    public void BuilderUsesEngineTotalsAndUnambiguousProjectionInsteadOfSummingCells()
    {
        var plan = PivotQueryBuilder.Build(Layout, Schema);
        Assert.Contains("SUMMARIZECOLUMNS", plan.Dax);
        Assert.Contains("ROLLUPADDISSUBTOTAL(ROLLUPGROUP('Sales'[Category]), \"__pbp_row_total\")", plan.Dax);
        Assert.Contains("ROLLUPADDISSUBTOTAL(ROLLUPGROUP('Date'[Year]), \"__pbp_column_total\")", plan.Dax);
        Assert.Contains("\"__pbp_value_0\", 'Sales'[Revenue]", plan.Dax);
        Assert.Contains("SELECTCOLUMNS(__pbp_base", plan.Dax); Assert.Contains("TOPN(1001", plan.Dax);
        Assert.EndsWith("[__pbp_row_total] DESC, [__pbp_column_total] DESC, [__pbp_row_0] ASC, [__pbp_column_0] ASC", plan.Dax);
        Assert.Equal(new[] { PivotResultRole.Row, PivotResultRole.Column, PivotResultRole.Value, PivotResultRole.RowTotalFlag, PivotResultRole.ColumnTotalFlag }, plan.ResultColumns.Select(column => column.Role));
        Assert.Equal(Enumerable.Range(0, 5), plan.ResultColumns.Select(column => column.Ordinal));
    }

    [Fact]
    public void MultipleAxisFieldsRollTogetherAndNoAxisRequiresNoRollup()
    {
        var plan = PivotQueryBuilder.Build(Layout with { Rows = new[] { new PivotAxisField("Sales", "Category"), new PivotAxisField("Sales", "Active", true) } }, Schema);
        Assert.Contains("ROLLUPGROUP('Sales'[Category], 'Sales'[Active])", plan.Dax);
        Assert.Contains("[__pbp_row_1] DESC", plan.Dax);
        var scalar = PivotQueryBuilder.Build(Layout with { Rows = Array.Empty<PivotAxisField>(), Columns = Array.Empty<PivotAxisField>() }, Schema);
        Assert.DoesNotContain("ROLLUPADDISSUBTOTAL", scalar.Dax);
        Assert.Contains("\"__pbp_row_total\", FALSE()", scalar.Dax);
        var withoutTotals = PivotQueryBuilder.Build(Layout with { IncludeRowTotals = false, IncludeColumnTotals = false }, Schema);
        Assert.DoesNotContain("ROLLUPADDISSUBTOTAL", withoutTotals.Dax);
    }

    [Fact]
    public void FiltersAreTypedEscapedAndIntersectedConsistently()
    {
        var plan = PivotQueryBuilder.Build(Layout with { Filters = new[]
        {
            new DataFilter("Sales", "Category", DataFilterOperator.Equals, "A\"),EVALUATE Sales//"),
            new DataFilter("Date", "Year", DataFilterOperator.In) { Values = new[] { "2025", "2026" } },
            new DataFilter("Sales", "Amount", DataFilterOperator.GreaterThan, "1.25"),
            new DataFilter("Sales", "Active", DataFilterOperator.Equals, "true"),
            new DataFilter("Sales", "OrderDate", DataFilterOperator.Equals, "2026-09-04")
        } }, Schema);
        Assert.Contains("KEEPFILTERS(TREATAS({ \"A\"\"),EVALUATE Sales//\" }, 'Sales'[Category]))", plan.Dax);
        Assert.Contains("KEEPFILTERS(TREATAS({ 2025, 2026 }, 'Date'[Year]))", plan.Dax);
        Assert.Contains("KEEPFILTERS(FILTER(ALL('Sales'[Amount]), 'Sales'[Amount] > 1.25))", plan.Dax);
        Assert.Contains("TREATAS({ TRUE() }, 'Sales'[Active])", plan.Dax);
        Assert.Contains("DATE(2026, 9, 4)", plan.Dax);
        Assert.Throws<ArgumentException>(() => PivotQueryBuilder.Build(Layout with { Filters = new[] { new DataFilter("Date", "Year", DataFilterOperator.Equals, "1); EVALUATE Sales") } }, Schema));
    }

    [Fact]
    public void IdentifiersWithQuotesAndBracketsRemainDataAndModesAreVisible()
    {
        var plan = PivotQueryBuilder.Build(new PivotLayout
        {
            Rows = new[] { new PivotAxisField("Owner's \"Table\"", "A] \"B\"") },
            Values = new[] { new PivotValue("Owner's \"Table\"", "A] Measure") }
        }, Schema);
        Assert.Contains("'Owner''s \"Table\"'[A]] \"B\"]", plan.Dax);
        Assert.Contains("'Owner''s \"Table\"'[A]] Measure]", plan.Dax);
        Assert.Contains(plan.Warnings, warning => warning.Contains("capacity memory"));
    }

    [Theory]
    [InlineData(PivotAggregation.Sum, "Amount", "SUM")]
    [InlineData(PivotAggregation.Average, "Quantity", "AVERAGE")]
    [InlineData(PivotAggregation.Min, "OrderDate", "MIN")]
    [InlineData(PivotAggregation.Max, "Amount", "MAX")]
    [InlineData(PivotAggregation.Count, "Active", "COUNTA")]
    [InlineData(PivotAggregation.DistinctCount, "Category", "DISTINCTCOUNT")]
    public void ExplicitAggregationsUseTheExpectedEngineExpression(PivotAggregation aggregation, string field, string function)
    {
        var plan = PivotQueryBuilder.Build(Layout with { Values = new[] { new PivotValue("Sales", field, aggregation) } }, Schema);
        Assert.Contains(function + "('Sales'[" + field + "])", plan.Dax);
    }

    [Fact]
    public void InvalidOrStaleFieldsAndUnsafeShapesDoNotGenerateQueries()
    {
        Assert.Throws<ArgumentException>(() => PivotQueryBuilder.Build(Layout with { Rows = new[] { new PivotAxisField("Missing", "Category") } }, Schema));
        Assert.Throws<ArgumentException>(() => PivotQueryBuilder.Build(Layout with { Values = new[] { new PivotValue("Sales", "Missing") } }, Schema));
        Assert.Throws<ArgumentException>(() => PivotQueryBuilder.Build(Layout with { Values = new[] { new PivotValue("Sales", "Category", PivotAggregation.Sum) } }, Schema));
        Assert.Throws<ArgumentException>(() => PivotQueryBuilder.Build(Layout with { Columns = new[] { new PivotAxisField("sales", "category") } }, Schema));
        Assert.Throws<ArgumentException>(() => PivotQueryBuilder.Build(Layout with { Values = Array.Empty<PivotValue>() }, Schema));
        Assert.Throws<ArgumentOutOfRangeException>(() => PivotQueryBuilder.Build(Layout with { RowLimit = 0 }, Schema));
        Assert.Throws<InvalidDataException>(() => PivotQueryBuilder.Build(Layout with { Version = 999 }, Schema));
    }

    [Fact]
    public void CapturedPlanIsDetachedAndGeneratedAliasesAvoidModelNames()
    {
        var rows = new List<PivotAxisField>(Layout.Rows);
        var plan = PivotQueryBuilder.Build(Layout with { Rows = rows }, Schema); rows.Clear(); Assert.Single(plan.Layout.Rows);
        var table = Schema.Tables[0] with { Columns = Schema.Tables[0].Columns.Concat(new[] { new DataColumnSchema("__pbp_value_0", "Int64") }).ToArray() };
        var schema = Schema with { Tables = new[] { table }.Concat(Schema.Tables.Skip(1)).ToArray() };
        var collision = PivotQueryBuilder.Build(Layout, schema);
        Assert.Contains("__pbp__value_0", collision.Dax);
    }

    [Fact]
    public async Task LayoutJsonRoundTripPreservesAxesFiltersTotalsAndRefreshWithoutSecrets()
    {
        using var temp = new TemporaryDirectory(); var path = Path.Combine(temp.Root, "layout.pivot.json");
        var original = Layout with
        {
            AutoRefresh = true, IncludeColumnTotals = false, RowLimit = 42,
            Filters = new[] { new DataFilter("Date", "Year", DataFilterOperator.In) { Values = new[] { "2025", "2026" } } }
        };
        await PivotLayoutStore.SaveAsync(path, original, CancellationToken.None);
        var loaded = await PivotLayoutStore.LoadAsync(path, CancellationToken.None);
        Assert.Equal(PivotQueryBuilder.Build(original, Schema).Dax, PivotQueryBuilder.Build(loaded, Schema).Dax);
        Assert.True(loaded.AutoRefresh); Assert.False(loaded.IncludeColumnTotals); Assert.Equal(42, loaded.RowLimit);
        Assert.Contains("\"In\"", File.ReadAllText(path)); Assert.DoesNotContain("ConnectionString", File.ReadAllText(path));
        await PivotLayoutStore.SaveAsync(path, loaded with { RowLimit = 45 }, CancellationToken.None);
        Assert.Equal(45, (await PivotLayoutStore.LoadAsync(path, CancellationToken.None)).RowLimit);
        Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
    }

    [Fact]
    public async Task MalformedAndCanceledLayoutIoDoesNotSilentlyReplaceUserData()
    {
        using var temp = new TemporaryDirectory(); var path = temp.Write("layout.json", "invalid-json");
        await Assert.ThrowsAsync<InvalidDataException>(() => PivotLayoutStore.LoadAsync(path, CancellationToken.None));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PivotLayoutStore.SaveAsync(path, Layout, cancel.Token));
        Assert.Equal("invalid-json", File.ReadAllText(path));
        File.WriteAllText(path, "{\"Version\":999}");
        await Assert.ThrowsAsync<InvalidDataException>(() => PivotLayoutStore.LoadAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task SnapshotRoundTripsAndDetectsValueOrderTypeOrRowChanges()
    {
        using var temp = new TemporaryDirectory(); var plan = PivotQueryBuilder.Build(Layout, Schema);
        var result = Result(plan, new object?[] { "A", 2026L, 100m, false, false }, new object?[] { null, 2026L, 100m, true, false });
        var test = PivotTestArtifact.Create("Revenue regression", plan, result);
        var path = Path.Combine(temp.Root, "revenue.pbitest.json");
        await PivotTestArtifact.SaveAsync(path, test, CancellationToken.None);
        var loaded = await PivotTestArtifact.LoadAsync(path, CancellationToken.None);
        Assert.Empty(PivotTestArtifact.Verify(loaded, result)); Assert.Equal(64, loaded.ExpectedSha256.Length);
        Assert.NotEmpty(PivotTestArtifact.Verify(loaded, Result(plan, new object?[] { "A", 2026L, 101m, false, false }, new object?[] { null, 2026L, 100m, true, false })));
        Assert.NotEmpty(PivotTestArtifact.Verify(loaded, result with { Results = new[] { result.Results[0] with { Rows = result.Results[0].Rows.Reverse().ToArray() } } }));
        Assert.DoesNotContain("powerbi://", File.ReadAllText(path)); Assert.DoesNotContain("ConnectionString", File.ReadAllText(path));
    }

    [Fact]
    public void PartialStaleAndMismatchedResultsCannotBecomePassingSnapshotTests()
    {
        var plan = PivotQueryBuilder.Build(Layout, Schema); var result = Result(plan, new object?[] { "A", 2026L, 100m, false, false });
        Assert.Throws<InvalidOperationException>(() => PivotTestArtifact.Create("x", plan, result with { Query = "EVALUATE Sales" }));
        Assert.Throws<InvalidOperationException>(() => PivotTestArtifact.Create("x", plan, result with { Results = new[] { result.Results[0] with { IsTruncated = true } } }));
        Assert.Throws<InvalidOperationException>(() => PivotTestArtifact.Create("x", plan, result with { Results = new[] { result.Results[0], result.Results[0] } }));
        var wrongColumns = result.Results[0].Columns.Select(column => column with { Name = "wrong" }).ToArray();
        Assert.Throws<InvalidOperationException>(() => PivotTestArtifact.Create("x", plan, result with { Results = new[] { result.Results[0] with { Columns = wrongColumns } } }));
    }

    [Fact]
    public void SnapshotHashIsCultureInvariantAndDistinguishesBlankMembersFromTotals()
    {
        var plan = PivotQueryBuilder.Build(Layout, Schema);
        var result = Result(plan, new object?[] { null, 2026L, 1234.50m, false, false });
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-CH"); var first = PivotTestArtifact.Create("x", plan, result);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); var second = PivotTestArtifact.Create("x", plan, result);
            Assert.Equal(first.ExpectedSha256, second.ExpectedSha256);
            Assert.Empty(PivotTestArtifact.Verify(first, Result(plan, new object?[] { DBNull.Value, 2026L, 1234.5m, false, false })));
            Assert.NotEmpty(PivotTestArtifact.Verify(first, Result(plan, new object?[] { null, 2026L, 1234.50m, true, false })));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    private static QueryResult Result(PivotQueryPlan plan, params object?[][] rows) => new(Guid.NewGuid(), plan.Dax,
        "powerbi://private/endpoint", "Database", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(10), new[]
        {
            new QueryResultSet(0, "Pivot", plan.ResultColumns.Select(column => new QueryColumn("c" + column.Ordinal,
                "[" + column.Key + "]", column.Role is PivotResultRole.RowTotalFlag or PivotResultRole.ColumnTotalFlag ? "Boolean" : "Variant")).ToArray(), rows, false)
        }, 1, Array.Empty<string>());
}
