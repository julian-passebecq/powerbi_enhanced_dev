using PbiBench.Core.DataExploration;
using PbiBench.Core.Queries;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class DataPreviewTests
{
    private static DataTableSchema Table(DataStorageMode mode = DataStorageMode.Import) => new("Sales' ledger", mode,
        new[] { new DataColumnSchema("ID]", "Int64", IsKey: true), new DataColumnSchema("Label", "String"), new DataColumnSchema("Amount", "Double") },
        Array.Empty<DataMeasureSchema>(), new[] { "ID]" });
    private static DataModelSchema Model(DataTableSchema table) => new("Fixture", new[] { table }, Array.Empty<DataRelationshipSchema>());
    private static DataPreviewCapabilities Verified(DataTableSchema table) => new(WindowSupport.Supported, table.CandidateKeyColumns) { TableName = table.Name };

    [Theory]
    [InlineData(DataStorageMode.Unknown)]
    [InlineData(DataStorageMode.DirectQuery)]
    [InlineData(DataStorageMode.DirectLake)]
    [InlineData(DataStorageMode.Dual)]
    [InlineData(DataStorageMode.Mixed)]
    public void NonImportModesNeverPretendStablePaging(DataStorageMode mode)
    {
        var table = Table(mode);
        var plan = DataPreviewBuilder.Build(Model(table), new(table.Name, 200, 200), Verified(table));
        Assert.False(plan.CanPage); Assert.Equal(DataPreviewMode.FirstN, plan.Mode); Assert.Equal(0, plan.Offset);
        Assert.Contains("TOPN(200", plan.Query);
        Assert.DoesNotContain("WINDOW(", plan.Query);
        Assert.Contains(plan.Warnings, warning => warning.Contains("offset"));
        if (mode == DataStorageMode.DirectLake) Assert.Contains(plan.Warnings, warning => warning.Contains("capacity memory"));
    }

    [Fact]
    public void CandidateKeyMetadataAndFunctionSupportAloneDoNotEnablePaging()
    {
        var table = Table(); var request = new DataPreviewRequest(table.Name);
        Assert.False(DataPreviewBuilder.Build(Model(table), request).CanPage);
        Assert.False(DataPreviewBuilder.Build(Model(table), request, new(WindowSupport.Supported, Array.Empty<string>())).CanPage);
        Assert.False(DataPreviewBuilder.Build(Model(table), request, new(WindowSupport.Unknown, table.CandidateKeyColumns)).CanPage);
        Assert.False(DataPreviewBuilder.Build(Model(table), request, Verified(table) with { TableName = "Different table" }).CanPage);
    }

    [Fact]
    public void VerifiedPagingUsesInclusiveAbsoluteBoundsAndUniqueTieBreaker()
    {
        var table = Table(); var model = Model(table);
        var request = new DataPreviewRequest(table.Name, 0, 25)
        {
            Sort = new[] { new DataSort("Label", true) },
            Filters = new[] { new DataFilter(table.Name, "Label", DataFilterOperator.Contains, "x\"*?~") }
        };
        var first = DataPreviewBuilder.Build(model, request, Verified(table));
        var second = DataPreviewBuilder.Build(model, request with { Offset = 25 }, Verified(table));
        Assert.True(first.CanPage); Assert.Equal(25, second.Offset);
        Assert.Contains("WINDOW(1, ABS, 25, ABS", first.Query);
        Assert.Contains("WINDOW(26, ABS, 50, ABS", second.Query);
        Assert.Contains("ORDERBY('Sales'' ledger'[Label], DESC, 'Sales'' ledger'[ID]]], ASC)", first.Query);
        Assert.Contains("ORDER BY 'Sales'' ledger'[Label] DESC, 'Sales'' ledger'[ID]]] ASC", first.Query);
        Assert.Contains("\"x\"\"~*~?~~\"", first.Query);
    }

    [Theory]
    [InlineData(-1, 100)] [InlineData(int.MaxValue, 100)] [InlineData(0, 0)] [InlineData(0, 10001)]
    public void InvalidPageBoundsFailBeforeGeneratingAQuery(int offset, int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DataPreviewBuilder.Build(Model(Table()), new(Table().Name, offset, size)));

    [Fact]
    public void TypedFiltersRejectUnknownMetadataAndExpressionInjection()
    {
        var table = Table(); var model = Model(table);
        Assert.Throws<ArgumentException>(() => DataPreviewBuilder.Build(model, new(table.Name) { Sort = new[] { new DataSort("Not a column") } }));
        Assert.Throws<ArgumentException>(() => DataPreviewBuilder.Build(model, new(table.Name) { Filters = new[] { new DataFilter("Other", "Amount", DataFilterOperator.Equals, "1") } }));
        Assert.Throws<ArgumentException>(() => DataPreviewBuilder.Build(model, new(table.Name) { Filters = new[] { new DataFilter(table.Name, "Amount", DataFilterOperator.Equals, "0); EVALUATE ROW(\"injected\", 1)") } }));
        Assert.Equal("BLANK()", DaxDataSyntax.Literal(null, "Int64"));
        Assert.Equal("\"\"", DaxDataSyntax.Literal("", "String"));
        Assert.Equal("9223372036854775807", DaxDataSyntax.Literal(long.MaxValue.ToString(), "Int64"));
        Assert.Throws<ArgumentException>(() => DaxDataSyntax.Literal("NaN", "Double"));
        Assert.Throws<ArgumentException>(() => DaxDataSyntax.Literal("05/09/2026", "DateTime"));
        Assert.Equal("(DATE(2026, 9, 5) + TIME(12, 30, 0))", DaxDataSyntax.Literal("2026-09-05T12:30:00", "DateTime"));
        var predicate = DaxDataSyntax.Predicate(new(table.Name, "Amount", DataFilterOperator.Equals, "0"), table.Columns[2]);
        Assert.Contains(" == 0", predicate); // Strict equality must not turn blanks into zero matches.
        var set = DaxDataSyntax.Predicate(new(table.Name, "Label", DataFilterOperator.In) { Values = new string?[] { "a\"b", null } }, table.Columns[1]);
        Assert.Contains("IN { \"a\"\"b\", BLANK() }", set);
    }

    [Fact]
    public async Task ExplicitVerificationUsesZeroRowProbeThenFullKeyCheck()
    {
        var table = Table();
        var fake = new ProbeQueries(2, 2);
        var connection = new QueryRequest("localhost:123", "Fixture", "EVALUATE ROW(\"x\", 1)") { ConnectionString = "Password=transient-secret" };
        var caps = await new DataPreviewCapabilityService(fake).VerifyPagingAsync(connection, table, CancellationToken.None);
        Assert.Equal(WindowSupport.Supported, caps.WindowSupport);
        Assert.Equal(table.CandidateKeyColumns, caps.VerifiedKeyColumns);
        Assert.Equal("localhost:123", caps.Server);
        Assert.Equal(2, fake.Requests.Count);
        Assert.Contains("FILTER('Sales'' ledger', FALSE())", fake.Requests[0].Query);
        Assert.Contains("SUMMARIZE('Sales'' ledger', 'Sales'' ledger'[ID]]])", fake.Requests[1].Query);
        Assert.All(fake.Requests, request => { Assert.Equal(1, request.RowLimit); Assert.Equal(connection.ConnectionString, request.ConnectionString); });
        Assert.DoesNotContain("transient-secret", caps.ToString());
    }

    [Fact]
    public async Task NonUniqueOrFailedChecksRetainHonestFallbackAndCancelPropagates()
    {
        var connection = new QueryRequest("localhost:123", "Fixture", "EVALUATE ROW(\"x\", 1)");
        var duplicate = await new DataPreviewCapabilityService(new ProbeQueries(3, 2)).VerifyPagingAsync(connection, Table(), CancellationToken.None);
        Assert.Empty(duplicate.VerifiedKeyColumns); Assert.Contains("not unique", duplicate.VerificationMessage);
        var unavailable = await new DataPreviewCapabilityService(new ProbeQueries(0, 0) { Fail = true }).VerifyPagingAsync(connection, Table(), CancellationToken.None);
        Assert.Equal(WindowSupport.Unknown, unavailable.WindowSupport); Assert.Empty(unavailable.VerifiedKeyColumns);
        var fake = new ProbeQueries(0, 0);
        var noKeys = await new DataPreviewCapabilityService(fake).VerifyPagingAsync(connection, Table() with { CandidateKeyColumns = Array.Empty<string>() }, CancellationToken.None);
        Assert.Empty(fake.Requests); Assert.Empty(noKeys.VerifiedKeyColumns);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DataPreviewCapabilityService(fake).VerifyPagingAsync(connection, Table(), cancelled.Token));
        Assert.Empty(fake.Requests);
    }

    private sealed class ProbeQueries(long rows, long distinct) : IDaxQueryService
    {
        public List<QueryRequest> Requests { get; } = new();
        public bool Fail { get; init; }
        public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request);
            if (Fail) throw new QueryExecutionException("The engine is unavailable.");
            var result = Requests.Count == 1
                ? new QueryResultSet(0, "Probe", Array.Empty<QueryColumn>(), Array.Empty<object?[]>(), false)
                : new QueryResultSet(0, "Key counts", new[] { new QueryColumn("C0", "Rows", "Int64"), new QueryColumn("C1", "DistinctKeys", "Int64") }, new[] { new object?[] { rows, distinct } }, false);
            return Task.FromResult(new QueryResult(Guid.NewGuid(), request.Query, request.Server, request.Database, DateTimeOffset.UtcNow, TimeSpan.Zero, new[] { result }, 0, Array.Empty<string>()));
        }
    }
}
