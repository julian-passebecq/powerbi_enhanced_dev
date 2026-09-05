using PbiBench.Core.Quality;
using PbiBench.Core.Queries;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class SemanticTestTests
{
    private static QueryRequest Target() => new("server", "model", "EVALUATE ROW(\"Value\", 1)", DocumentRevision: 42) { ConnectionString = "Password=DO_NOT_PERSIST;" };
    private static SemanticTestDefinition Definition() => new() { Id = "assertion", Name = "Revenue", Query = "EVALUATE ROW(\"Value\", 1)", Expected = SemanticValue.From(1) };
    private sealed class FixtureService : IDaxQueryService
    {
        private readonly Func<QueryRequest, QueryResult> execute;
        public FixtureService(Func<QueryRequest, QueryResult> execute) { this.execute = execute; }
        public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); request.Validate(); return Task.FromResult(execute(request)); }
    }
    private static QueryResult Result(QueryRequest request, params object?[][] rows) => new(Guid.NewGuid(), request.Query, request.Server, request.Database,
        DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(3), new[] { new QueryResultSet(0, "Result", new[] { new QueryColumn("c0", "Value", "System.Object") }, rows, false) }, request.DocumentRevision, Array.Empty<string>());
    private static SemanticTestService Service(params object?[][] rows) => new(new FixtureService(request => Result(request, rows)));

    [Fact]
    public async Task ScalarUsesExactlyOneCompleteResultAndRetainsActualExecutionId()
    {
        var result = await Service(new object?[] { 1 }).RunAsync(Definition(), Target(), CancellationToken.None);
        Assert.Equal(SemanticTestOutcome.Passed, result.Outcome); Assert.NotNull(result.ExecutionId); Assert.Equal(SemanticTestService.Hash(Definition().Query), result.QueryHash);
        Assert.Equal(SemanticTestOutcome.Error, (await Service(new object?[] { 1 }, new object?[] { 1 }).RunAsync(Definition(), Target(), CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData(null, SemanticTestOutcome.Failed)]
    [InlineData("", SemanticTestOutcome.Failed)]
    [InlineData(false, SemanticTestOutcome.Failed)]
    [InlineData(0, SemanticTestOutcome.Passed)]
    public async Task BlankZeroFalseAndEmptyTextRemainDistinct(object? actual, SemanticTestOutcome outcome)
    { Assert.Equal(outcome, (await Service(new[] { actual }).RunAsync(Definition() with { Expected = SemanticValue.From(0) }, Target(), CancellationToken.None)).Outcome); }

    [Fact]
    public async Task NumericComparisonPreservesInt64AndSubdecimalValues()
    {
        Assert.Equal(SemanticTestOutcome.Failed, (await Service(new object?[] { long.MaxValue }).RunAsync(Definition() with { Expected = SemanticValue.From(long.MaxValue - 1) }, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Failed, (await Service(new object?[] { 1e-30 }).RunAsync(Definition() with { Expected = SemanticValue.From(0) }, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Passed, (await Service(new object?[] { 1e-30 }).RunAsync(Definition() with { Expected = SemanticValue.From(0), AbsoluteTolerance = 1e-30 }, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Failed, (await Service(new object?[] { 1m }).RunAsync(Definition() with { Expected = new(SemanticValueKind.Number, "1.00000000000000000000000000001") }, Target(), CancellationToken.None)).Outcome);
        Assert.Throws<InvalidDataException>(() => new SemanticValue(SemanticValueKind.Number, "1e-400").Validate());
        Assert.Throws<InvalidDataException>(() => new SemanticValue(SemanticValueKind.Number, "NaN").Validate());
    }

    [Fact]
    public async Task NumericToleranceUsesAbsolutePlusRelativeWithoutRoundingAwayDifference()
    {
        var test = Definition() with { Expected = SemanticValue.From(100), AbsoluteTolerance = 0.01, RelativeTolerance = 0.001 };
        Assert.Equal(SemanticTestOutcome.Passed, (await Service(new object?[] { 100.1m }).RunAsync(test, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Failed, (await Service(new object?[] { 100.2m }).RunAsync(test, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Failed, (await Service(new object?[] { long.MaxValue }).RunAsync(test with { Expected = SemanticValue.From(long.MaxValue - 1), AbsoluteTolerance = 0.5, RelativeTolerance = 0 }, Target(), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task DatesCompareChronologicallyAndRejectAmbiguousMixedZones()
    {
        var instant = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(2));
        var test = Definition() with { Expected = new(SemanticValueKind.DateTime, "2026-09-05T08:00:00Z") };
        Assert.Equal(SemanticTestOutcome.Passed, (await Service(new object?[] { instant }).RunAsync(test, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Passed, (await Service(new object?[] { instant }).RunAsync(test with { Expected = new(SemanticValueKind.DateTime, "2026-09-05T09:00:00Z"), Comparison = SemanticComparison.LessThan }, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Error, (await Service(new object?[] { instant }).RunAsync(test with { Expected = new(SemanticValueKind.DateTime, "2026-09-05T08:00:00") }, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Passed, (await Service(new object?[] { new DateTime(2026, 9, 5, 0, 0, 0) }).RunAsync(test with { Expected = new(SemanticValueKind.DateTime, "2026-09-05") }, Target(), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task TableAssertionsCheckEveryRowAndDoNotPassEmptyResults()
    {
        var test = Definition() with { Kind = SemanticTestKind.Table, Comparison = SemanticComparison.GreaterThan, Expected = SemanticValue.From(0) };
        Assert.Equal(SemanticTestOutcome.Passed, (await Service(new object?[] { 2 }, new object?[] { 4 }).RunAsync(test, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Failed, (await Service(new object?[] { 2 }, new object?[] { 0 }).RunAsync(test, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Failed, (await Service().RunAsync(test, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Error, (await Service(new object?[] { 1 }).RunAsync(test with { ColumnIndex = 1 }, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Passed, (await Service().RunAsync(test with { Kind = SemanticTestKind.RowCount, ExpectedRowCount = 0, Comparison = SemanticComparison.Equal }, Target(), CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("server")]
    [InlineData("database")]
    [InlineData("revision")]
    [InlineData("truncated")]
    [InlineData("extra-result")]
    [InlineData("warning")]
    [InlineData("row-shape")]
    [InlineData("empty-id")]
    [InlineData("stale-run")]
    public async Task IncompleteOrMismatchedEngineResultsNeverPass(string fault)
    {
        var service = new SemanticTestService(new FixtureService(request =>
        {
            var result = Result(request, new object?[] { 1 }); return fault switch
            {
                "query" => result with { Query = "different" }, "server" => result with { Server = "different" }, "database" => result with { Database = "different" },
                "revision" => result with { DocumentRevision = 100 }, "truncated" => result with { Results = new[] { result.Results[0] with { IsTruncated = true } } },
                "extra-result" => result with { Results = new[] { result.Results[0], result.Results[0] } }, "warning" => result with { Warnings = new[] { "Some results were omitted" } },
                "row-shape" => result with { Results = new[] { result.Results[0] with { Rows = new[] { new object?[] { 1, 2 } } } } }, "empty-id" => result with { Id = Guid.Empty },
                "stale-run" => result with { StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1) }, _ => result
            };
        }));
        Assert.Equal(SemanticTestOutcome.Error, (await service.RunAsync(Definition(), Target(), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task OrderedSnapshotBindsQueryAndDetectsOrderAndSchemaChanges()
    {
        var test = Definition() with { OrderIsDeterministic = true }; var service = Service(new object?[] { 1 }, new object?[] { 2 });
        var snapshot = await service.CaptureSnapshotAsync(test, Target(), CancellationToken.None); test = test with { Kind = SemanticTestKind.Snapshot, Snapshot = snapshot };
        Assert.Equal(SemanticTestOutcome.Passed, (await service.RunAsync(test, Target(), CancellationToken.None)).Outcome);
        Assert.Equal(SemanticTestOutcome.Failed, (await Service(new object?[] { 2 }, new object?[] { 1 }).RunAsync(test, Target(), CancellationToken.None)).Outcome);
        var changedSchema = new SemanticTestService(new FixtureService(request => { var result = Result(request, new object?[] { 1 }, new object?[] { 2 }); return result with { Results = new[] { result.Results[0] with { Columns = new[] { new QueryColumn("c0", "Renamed", "System.Object") } } } }; }));
        Assert.Equal(SemanticTestOutcome.Failed, (await changedSchema.RunAsync(test, Target(), CancellationToken.None)).Outcome);
        Assert.Throws<InvalidDataException>(() => SemanticTestService.Validate(test with { Query = test.Query + " " }));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CaptureSnapshotAsync(test with { OrderIsDeterministic = false }, Target(), CancellationToken.None));
        var recaptured = await service.CaptureSnapshotAsync(test with { Query = test.Query + " " }, Target(), CancellationToken.None); Assert.NotEqual(snapshot.QueryHash, recaptured.QueryHash);
    }

    [Fact]
    public async Task AbComparisonChecksBothRunsAndSchemaWithoutRewritingDax()
    {
        var test = Definition() with { Kind = SemanticTestKind.CompareQueries, ComparisonQuery = "EVALUATE ROW(\"Value\", 2)", OrderIsDeterministic = true };
        var seen = new List<QueryRequest>();
        var service = new SemanticTestService(new FixtureService(request => { seen.Add(request); return Result(request, new object?[] { 1 }); }));
        var result = await service.RunAsync(test, Target(), CancellationToken.None);
        Assert.Equal(SemanticTestOutcome.Passed, result.Outcome); Assert.NotNull(result.ComparisonExecutionId); Assert.NotEqual(result.ExecutionId, result.ComparisonExecutionId);
        Assert.Equal(test.Query, seen[0].Query); Assert.Equal(test.ComparisonQuery, seen[1].Query); Assert.All(seen, request => Assert.Equal(Target().ConnectionString, request.ConnectionString));
        var different = new SemanticTestService(new FixtureService(request => Result(request, new object?[] { request.Query == test.Query ? 1 : 2 })));
        Assert.Equal(SemanticTestOutcome.Failed, (await different.RunAsync(test, Target(), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task EngineFailuresCannotLeakCredentialsOrBecomePassingReports()
    {
        var service = new SemanticTestService(new FixtureService(_ => throw new InvalidDataException("Password=DO_NOT_PERSIST;")));
        var result = await service.RunAsync(Definition(), Target(), CancellationToken.None); Assert.Equal(SemanticTestOutcome.Error, result.Outcome); Assert.Null(result.ExecutionId);
        Assert.DoesNotContain("DO_NOT_PERSIST", result.Evidence); Assert.False(new SemanticTestReport(1, new[] { result }).Passed); Assert.False(new SemanticTestReport(1, Array.Empty<SemanticTestResult>()).Passed);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(new object?[] { 1 }).RunAsync(Definition(), Target(), cancellation.Token));
    }

    [Fact]
    public async Task VersionedArtifactsRoundTripTypedSnapshotsWithoutConnectionFields()
    {
        var test = Definition() with { OrderIsDeterministic = true };
        var snapshot = await Service(new object?[] { null }, new object?[] { "" }, new object?[] { long.MaxValue }, new object?[] { false }).CaptureSnapshotAsync(test, Target(), CancellationToken.None);
        var artifact = new SemanticTestArtifact(1, new[] { test with { Kind = SemanticTestKind.Snapshot, Snapshot = snapshot } });
        var json = SemanticTestArtifactStore.Serialize(artifact); var loaded = SemanticTestArtifactStore.Deserialize(json);
        Assert.Equal(SemanticValueKind.Blank, loaded.Tests[0].Snapshot!.Rows[0][0].Kind); Assert.Equal(long.MaxValue.ToString(), loaded.Tests[0].Snapshot!.Rows[2][0].Value);
        Assert.DoesNotContain("ConnectionString", json); Assert.DoesNotContain("DO_NOT_PERSIST", json); Assert.DoesNotContain("Server", json);
        Assert.Throws<InvalidDataException>(() => SemanticTestArtifactStore.Deserialize(json.Replace("\"FormatVersion\": 1", "\"FormatVersion\": 9")));
        Assert.Throws<InvalidDataException>(() => SemanticTestArtifactStore.Deserialize(json.Insert(json.IndexOf('{') + 1, "\"ConnectionString\":\"Password=secret\",")));
        Assert.Throws<InvalidDataException>(() => SemanticTestArtifactStore.Serialize(new(1, new[] { test, test })));
        Assert.Throws<InvalidDataException>(() => SemanticTestArtifactStore.Deserialize("{\"FormatVersion\":1,\"Tests\":[{}]}"));
        var directory = Path.Combine(Path.GetTempPath(), "PbiBench-tests-" + Guid.NewGuid().ToString("N")); var path = Path.Combine(directory, "model.pbibench-tests.json");
        try
        {
            await SemanticTestArtifactStore.SaveAsync(path, artifact, CancellationToken.None); await SemanticTestArtifactStore.SaveAsync(path, artifact, CancellationToken.None);
            Assert.Equal(artifact.Tests[0].Id, (await SemanticTestArtifactStore.LoadAsync(path, CancellationToken.None)).Tests[0].Id);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
