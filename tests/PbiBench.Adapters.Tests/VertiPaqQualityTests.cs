using System.Globalization;
using System.IO.Compression;
using System.Text;
using PbiBench.Core.Quality;
using PbiBench.Core.Queries;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class VertiPaqQualityTests
{
    [Fact]
    public async Task PublicSqlbiFixtureImportsActualVpaxReferencesMetricsAndPartitions()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "VertiPaq", "Contoso.vpax");
        var snapshot = await new VpaxSnapshotReader().ReadAsync(path, CancellationToken.None);
        Assert.Equal("1.2.0", snapshot.SchemaVersion);
        Assert.NotEmpty(snapshot.Tables); Assert.NotEmpty(snapshot.Relationships); Assert.NotEmpty(snapshot.Partitions);
        var currency = Assert.Single(snapshot.Tables, table => table.Name == "Currency");
        Assert.Equal(28L, currency.Rows);
        Assert.Equal("Import", currency.StorageMode);
        var key = Assert.Single(snapshot.Columns, column => column.Table == "Currency" && column.Name == "CurrencyKey");
        Assert.Equal(28L, key.Cardinality); Assert.Equal(1440L, key.DictionaryBytes); Assert.Equal(152L, key.DataBytes); Assert.Equal(240L, key.HierarchyBytes); Assert.Equal(1832L, key.TotalBytes);
        Assert.All(snapshot.Relationships, relationship => { Assert.NotEqual("(unresolved)", relationship.FromTable); Assert.NotEqual("(unresolved)", relationship.ToTable); Assert.Null(relationship.MissingKeys); });
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("Embedded Model.bim was not opened"));
    }

    [Fact]
    public void VpaxHandlesLegacyNamesWrappedArraysForwardReferencesAndNullableFields()
    {
        var snapshot = Parse(Sample);
        var column = Assert.Single(snapshot.Columns);
        Assert.Equal("ID", column.Name); Assert.Equal("T", column.Table); Assert.Equal(30L, column.DataBytes); Assert.Equal(4L, column.HierarchyBytes); Assert.Equal(39L, column.TotalBytes);
        Assert.Equal(1.5, Assert.Single(snapshot.Segments).Temperature);
        Assert.Null(column.IsResident); Assert.Null(Assert.Single(snapshot.Segments).LastAccessed);
        Assert.Equal("P", Assert.Single(snapshot.Partitions).Name);
        Assert.Equal(39L, snapshot.TotalBytes);
    }

    [Fact]
    public void MissingMetricsStayUnavailableAndUncollectedRiNeverMeansZero()
    {
        var snapshot = Parse(Sample.Replace("\"DictionarySize\":5,", "").Replace("\"StatisticsEnabled\":true", "\"StatisticsEnabled\":false"));
        Assert.Null(Assert.Single(snapshot.Columns).DictionaryBytes); Assert.Null(snapshot.TotalBytes);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("disabled or not recorded"));
        Assert.DoesNotContain(VertiPaqOptimization.Build(snapshot), signal => signal.Id.StartsWith("VPAX_SIZE:", StringComparison.Ordinal));
    }

    [Fact]
    public void DirectQueryStatisticsRequireExplicitFullExtractionBeforeRiCountsAreTrusted()
    {
        var json = Sample.Replace("\"Relationships\":[]", "\"Relationships\":[{\"FromColumn\":{\"$ref\":\"c\"},\"ToColumn\":{\"$ref\":\"c\"},\"MissingKeys\":0,\"InvalidRows\":0}]")
            .Replace("\"Mode\":0", "\"Mode\":1");
        Assert.Null(Assert.Single(Parse(json).Relationships).MissingKeys);
        var full = json.Replace("\"StatisticsEnabled\":true", "\"StatisticsEnabled\":true,\"DirectQueryMode\":1");
        Assert.Equal(0L, Assert.Single(Parse(full).Relationships).MissingKeys);
    }

    [Theory]
    [InlineData(0, "Import")]
    [InlineData(1, "DirectQuery")]
    [InlineData(2, "Import")]
    [InlineData(3, "Push")]
    [InlineData(4, "Dual")]
    [InlineData(5, "DirectLake")]
    public void VpaxStorageModesFollowPublicPartitionEnumAndResolveDefault(int mode, string expected)
        => Assert.Equal(expected, Assert.Single(Parse(Sample.Replace("\"Mode\":0", "\"Mode\":" + mode.ToString(CultureInfo.InvariantCulture))).Partitions).Mode);

    [Theory]
    [InlineData("{\"DaxModelVersion\":\"2.0.0\",\"Tables\":[]}")]
    [InlineData("{\"DaxModelVersion\":\"1.10.0\",\"Tables\":[]}")]
    [InlineData("{\"DaxModelVersion\":\"1.9.0\",\"Tables\":[{\"$ref\":\"absent\"}]}")]
    [InlineData("{\"DaxModelVersion\":\"1.9.0\",\"Tables\":[{\"$id\":\"loop\",\"$ref\":\"loop\"}]}")]
    [InlineData("{\"DaxModelVersion\":\"1.9.0\",\"Tables\":[{\"$id\":\"same\"},{\"$id\":\"same\"}]}")]
    public void UnsupportedAndInvalidReferencesAreRejected(string json) => Assert.Throws<InvalidDataException>(() => Parse(json));

    [Fact]
    public void InvalidNumericMetricsAndCanceledParsingAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Sample.Replace("\"DictionarySize\":5", "\"DictionarySize\":-1")));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => VpaxSnapshotReader.Parse(Encoding.UTF8.GetBytes(Sample), "test", canceled.Token));
    }

    [Fact]
    public async Task VpaxArchiveReadsOnlySingleStatisticsPartAndNeverExtractsEmbeddedFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PbiBenchVpax-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "sample.vpax");
            using (var file = File.Create(path)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (var write = new StreamWriter(zip.CreateEntry("DaxModel.json").Open())) write.Write(Sample);
                using (var write = new StreamWriter(zip.CreateEntry("../Model.bim").Open())) write.Write("not a model; never extract");
            }
            await new VpaxSnapshotReader().ReadAsync(path, CancellationToken.None);
            Assert.Single(Directory.GetFiles(directory));
            var duplicate = Path.Combine(directory, "duplicate.vpax");
            using (var file = File.Create(duplicate)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                foreach (var name in new[] { "DaxModel.json", "daxmodel.JSON" }) using (var write = new StreamWriter(zip.CreateEntry(name).Open())) write.Write(Sample);
            await Assert.ThrowsAsync<InvalidDataException>(() => new VpaxSnapshotReader().ReadAsync(duplicate, CancellationToken.None));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PublicDmvProjectionAggregatesSegmentsWithoutRepeatingDictionaryOrRows()
    {
        var result = Project(); var column = Assert.Single(result.Columns); var table = Assert.Single(result.Tables);
        Assert.Equal(100L, column.Cardinality); Assert.Equal(120L, column.DataBytes); Assert.Equal(100L, column.DictionaryBytes); Assert.Equal(30L, column.HierarchyBytes);
        Assert.Equal(250L, table.TotalBytes); Assert.Equal(100L, table.Rows); Assert.Equal(2, result.Segments.Count);
        Assert.Null(column.IsResident); Assert.False(result.StatisticsCollected);
    }

    [Fact]
    public void TruncatedStorageRowsetsCannotCreateApparentlyCompleteTotals()
    {
        var snapshot = Project(truncated: true);
        var column = Assert.Single(snapshot.Columns); Assert.Null(column.DataBytes); Assert.Null(column.HierarchyBytes); Assert.Null(snapshot.TotalBytes);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("incomplete metrics were discarded"));
    }

    [Fact]
    public void OptimizationSignalsRetainProvenanceAndRequireBenchmarksForMemoryProposals()
    {
        var external = new OptimizationSignal("rule", "PbiBench Pack 1", "Modeling", "REVIEW", "Title", "Evidence");
        var signals = VertiPaqOptimization.Build(Parse(Sample), new[] { external });
        Assert.Contains(external, signals);
        Assert.All(signals.Where(signal => signal.Source.StartsWith("VertiPaq", StringComparison.Ordinal)), signal => { Assert.Equal("BENCHMARK", signal.Risk); Assert.Contains("Profile", signal.Recommendation); });
    }

    [Fact]
    public async Task BenchmarkAlternatesQueriesAndRequiresFullTypedEquivalentResults()
    {
        var calls = new List<string>(); var service = new QueryBenchmarkService(new StubQueries(request =>
        {
            calls.Add(request.Query); return Result(request, 1L) with { Elapsed = TimeSpan.FromMilliseconds(request.Query.EndsWith("A", StringComparison.Ordinal) ? 20 : 10) };
        }));
        var result = await service.RunAsync(new("server", "model", "EVALUATE A", "EVALUATE B", Iterations: 3, ModelFingerprint: "fingerprint"), CancellationToken.None);
        Assert.Equal(new[] { "EVALUATE A", "EVALUATE B", "EVALUATE A", "EVALUATE B", "EVALUATE A", "EVALUATE B" }, calls);
        Assert.True(result.EquivalentResults); Assert.Equal(-50.0, result.ChangePercent); Assert.Equal("fingerprint", result.ModelFingerprint);
        Assert.Contains("cache", result.TimingSource); Assert.NotEqual(result.BaselineQueryHash, result.CandidateQueryHash);
    }

    [Fact]
    public async Task BenchmarkDoesNotAcceptTypeDifferencesTruncationOrFailureAsEquivalent()
    {
        foreach (var mode in new[] { "type", "truncated", "failure" })
        {
            var result = await new QueryBenchmarkService(new StubQueries(request =>
            {
                if (mode == "failure") throw new InvalidOperationException("provider secret error");
                var result = Result(request, request.Query.EndsWith("B", StringComparison.Ordinal) ? (mode == "type" ? (object)1.0 : 1L) : 1L);
                if (mode == "truncated") result = result with { Results = new[] { result.Results[0] with { IsTruncated = true } } };
                return result;
            })).RunAsync(new("server", "model", "EVALUATE A", "EVALUATE B", Iterations: 1), CancellationToken.None);
            Assert.False(result.EquivalentResults); Assert.Null(result.ChangePercent);
            Assert.DoesNotContain("secret", string.Join(" ", result.Samples.Select(sample => sample.Error)));
        }
    }

    [Theory]
    [InlineData("server")]
    [InlineData("database")]
    [InlineData("query")]
    [InlineData("revision")]
    [InlineData("empty")]
    [InlineData("shape")]
    [InlineData("identity")]
    [InlineData("negative")]
    [InlineData("timestamp")]
    public async Task BenchmarkRejectsStaleMalformedAndReplayedProviderResults(string mode)
    {
        var identity = Guid.NewGuid();
        var evidence = await new QueryBenchmarkService(new StubQueries(request =>
        {
            var result = Result(request, 1L);
            return mode switch
            {
                "server" => result with { Server = "foreign" }, "database" => result with { Database = "foreign" }, "query" => result with { Query = "EVALUATE stale" },
                "revision" => result with { DocumentRevision = 9 }, "empty" => result with { Results = Array.Empty<QueryResultSet>() },
                "shape" => result with { Results = new[] { result.Results[0] with { Rows = new[] { Array.Empty<object?>() } } } },
                "identity" => result with { Id = identity }, "negative" => result with { Elapsed = TimeSpan.FromMilliseconds(-1) }, "timestamp" => result with { StartedAt = DateTimeOffset.UtcNow.AddHours(-1) }, _ => result
            };
        })).RunAsync(new("server", "model", "EVALUATE A", "EVALUATE B", Iterations: 1), CancellationToken.None);
        Assert.False(evidence.EquivalentResults); Assert.Null(evidence.BaselineMedianMs); Assert.Contains(evidence.Samples, sample => sample.Error != null);
    }

    [Fact]
    public async Task BenchmarkHonorsCancellationAfterProviderReturnsAndBeforeAnyExecution()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new QueryBenchmarkService(new StubQueries(request => { cancellation.Cancel(); return Result(request, 1L); }));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RunAsync(new("s", "m", "EVALUATE A", "EVALUATE B"), cancellation.Token));
        var called = false;
        var never = new QueryBenchmarkService(new StubQueries(request => { called = true; return Result(request, 1L); }));
        await Assert.ThrowsAsync<OperationCanceledException>(() => never.RunAsync(new("s", "m", "EVALUATE A", "EVALUATE B"), cancellation.Token));
        Assert.False(called);
    }

    [Fact]
    public async Task BenchmarkPreservesDaxGuardAndNeverSerializesCredentials()
    {
        var service = new QueryBenchmarkService(new StubQueries(request => Result(request, 1L)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(new("s", "m", "SELECT * FROM $SYSTEM.TMSCHEMA_TABLES", "EVALUATE B"), CancellationToken.None));
        var evidence = await service.RunAsync(new("s", "m", "EVALUATE A", "EVALUATE B", Iterations: 1) { ConnectionString = "Password=secret" }, CancellationToken.None);
        var path = Path.Combine(Path.GetTempPath(), "PbiBenchBenchmark-" + Guid.NewGuid().ToString("N") + ".json");
        try { await QueryBenchmarkStore.SaveAsync(evidence, path, CancellationToken.None); var json = File.ReadAllText(path); Assert.DoesNotContain("secret", json); Assert.Contains("BaselineQueryHash", json); }
        finally { File.Delete(path); }
    }

    private static VertiPaqSnapshot Parse(string text) => VpaxSnapshotReader.Parse(Encoding.UTF8.GetBytes(text), "test");
    private const string Sample = """
        {"DaxModelVersion":"1.9.0","ModelName":"M","ExtractorProperties":{"StatisticsEnabled":true},"DefaultMode":0,
        "Tables":{"$id":"tables","$values":[{"$id":"t","TableName":{"Name":"T"},"RowsCount":10,"ReferentialIntegrityViolationCount":0,
        "Columns":[{"$id":"c","ColumnName":{"Name":"ID"},"DataType":"Int64","ColumnCardinality":10,"DictionarySize":5,
        "ColumnSegments":[{"Partition":{"$ref":"p"},"SegmentNumber":0,"SegmentRows":10,"UsedSize":30,"Temperature":1.5,"LastAccessed":null}],
        "ColumnHierarchies":[{"UsedSize":4}]}],"UserHierarchies":[],"Partitions":[{"$id":"p","PartitionName":"P","Mode":0}]}]},"Relationships":[]}
        """;
    private static VertiPaqSnapshot Project(bool truncated = false)
    {
        return VertiPaqDmvProjection.Build("s", "m", DateTimeOffset.UtcNow, new[]
        {
            Set(VertiPaqRowset.Tables, new[] { "ID", "Name" }, new object?[] { 1, "T" }),
            Set(VertiPaqRowset.Columns, new[] { "ID", "TableID", "ExplicitName", "ExplicitDataType" }, new object?[] { 10, 1, "Key", 6 }),
            Set(VertiPaqRowset.Partitions, new[] { "TableID", "Name", "Mode" }, new object?[] { 1, "P", 0 }),
            Set(VertiPaqRowset.Relationships, new[] { "ID" }),
            Set(VertiPaqRowset.StorageColumns, new[] { "DIMENSION_NAME", "COLUMN_ID", "ATTRIBUTE_NAME", "COLUMN_TYPE", "DICTIONARY_SIZE" }, new object?[] { "T", "Key(10)", "Key", "BASIC_DATA", 100 }, new object?[] { "T", "Key(10)", "Key", "BASIC_DATA", 100 }),
            Set(VertiPaqRowset.StorageTables, new[] { "DIMENSION_NAME", "TABLE_ID", "ROWS_COUNT", "RIVIOLATION_COUNT" }, new object?[] { "T", "T(1)", 100, 0 }, new object?[] { "T", "H$T(1)$Key(10)", 103, 0 }),
            Set(VertiPaqRowset.StorageSegments, new[] { "DIMENSION_NAME", "TABLE_ID", "COLUMN_ID", "USED_SIZE", "SEGMENT_NUMBER", "RECORDS_COUNT" },
                new object?[] { "T", "T(1)", "Key(10)", 50, 0, 40 }, new object?[] { "T", "T(1)", "Key(10)", 70, 1, 60 },
                new object?[] { "T", "H$T(1)$Key(10)", "Internal", 10, 0, 100 }, new object?[] { "T", "H$T(1)$Key(10)", "Internal", 20, 1, 100 }) with { Result = Set(VertiPaqRowset.StorageSegments, new[] { "DIMENSION_NAME", "TABLE_ID", "COLUMN_ID", "USED_SIZE", "SEGMENT_NUMBER", "RECORDS_COUNT" },
                    new object?[] { "T", "T(1)", "Key(10)", 50, 0, 40 }, new object?[] { "T", "T(1)", "Key(10)", 70, 1, 60 }, new object?[] { "T", "H$T(1)$Key(10)", "Internal", 10, 0, 100 }, new object?[] { "T", "H$T(1)$Key(10)", "Internal", 20, 1, 100 }).Result! with { IsTruncated = truncated } }
        });
    }
    private static VertiPaqRowsetResult Set(VertiPaqRowset rowset, string[] columns, params object?[][] rows) => new(rowset,
        new(0, rowset.ToString(), columns.Select((name, index) => new QueryColumn("C" + index, name, "System.Object")).ToArray(), rows, false));
    private static QueryResult Result(QueryRequest request, object value) => new(Guid.NewGuid(), request.Query, request.Server, request.Database, DateTimeOffset.UtcNow,
        TimeSpan.FromMilliseconds(1), new[] { new QueryResultSet(0, "Result", new[] { new QueryColumn("C0", "Value", value.GetType().FullName!) }, new[] { new object?[] { value } }, false) }, request.DocumentRevision, Array.Empty<string>());
    private sealed class StubQueries(Func<QueryRequest, QueryResult> execute) : IDaxQueryService
    { public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken) => Task.FromResult(execute(request)); }
}
