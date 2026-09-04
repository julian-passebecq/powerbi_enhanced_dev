using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Core.Queries;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class QueryServiceTests
{
    private static QueryRequest Request(string query = "EVALUATE ROW(\"Value\", 1)", int limit = 10000, int timeout = 60) => new("localhost:2383", "Model", query, limit, timeout, 42);
    private static DataTable Table(params object?[] values)
    {
        var table = new DataTable(); table.Columns.Add("Value", typeof(object));
        foreach (var value in values) table.Rows.Add(value ?? DBNull.Value);
        return table;
    }

    [TestMethod]
    public async Task MultipleResultsRetainEmptySetsTypesNullsAndExecutedText()
    {
        const string query = "DEFINE VAR x = 1 EVALUATE FILTER({x}, FALSE()) EVALUATE ROW(\"Value\", x)";
        var first = Table(); var second = Table(1L, 2.5m, null, "plain");
        var session = new FakeSession { Reader = () => new DataTableReader(new[] { first, second }) };
        var result = await new DaxQueryService(new Factory(() => session)).ExecuteAsync(Request(query), CancellationToken.None);
        Assert.AreEqual(query, session.ExecutedQuery); Assert.AreEqual(query, result.Query); Assert.AreEqual(42L, result.DocumentRevision);
        Assert.AreEqual(2, result.Results.Count); Assert.AreEqual(0, result.Results[0].Rows.Count);
        Assert.AreEqual(1L, result.Results[1].Rows[0][0]); Assert.AreEqual(2.5m, result.Results[1].Rows[1][0]); Assert.IsNull(result.Results[1].Rows[2][0]);
        Assert.IsTrue(session.Disposed); Assert.IsTrue(result.Elapsed >= TimeSpan.Zero);
    }

    [TestMethod]
    public async Task RowLimitRetainsLaterResultSetsWithoutRewritingDax()
    {
        var session = new FakeSession { Reader = () => new DataTableReader(new[] { Table(1, 2, 3), Table(4) }) };
        var result = await new DaxQueryService(new Factory(() => session)).ExecuteAsync(Request(limit: 2), CancellationToken.None);
        Assert.AreEqual(2, result.Results.Count); Assert.AreEqual(2, result.Results[0].Rows.Count);
        Assert.IsTrue(result.Results[0].IsTruncated); Assert.IsFalse(result.Results[1].IsTruncated); Assert.AreEqual(4, result.Results[1].Rows[0][0]);
        StringAssert.Contains(result.Warnings.Single(), "not server query work"); Assert.AreEqual(Request().Query, session.ExecutedQuery);
    }

    [TestMethod]
    public async Task ExactRowLimitIsNotReportedAsTruncated()
    {
        var service = new DaxQueryService(new Factory(() => new FakeSession { Reader = () => Table(1, 2).CreateDataReader() }));
        var result = await service.ExecuteAsync(Request(limit: 2), CancellationToken.None);
        Assert.IsFalse(result.Results[0].IsTruncated); Assert.AreEqual(0, result.Warnings.Count);
    }

    [TestMethod]
    public async Task TotalCellsAndResultCountAreBounded()
    {
        var service = new DaxQueryService(new Factory(() => new FakeSession { Reader = () => new DataTableReader(new[] { Table(1, 2, 3), Table(4) }) }));
        var cellBound = await service.ExecuteAsync(Request() with { MaximumCells = 2 }, CancellationToken.None);
        Assert.AreEqual(1, cellBound.Results.Count); Assert.AreEqual(2, cellBound.Results[0].Rows.Count); Assert.IsTrue(cellBound.Results[0].IsTruncated);
        var setBound = await service.ExecuteAsync(Request() with { MaximumResultSets = 1 }, CancellationToken.None);
        Assert.AreEqual(1, setBound.Results.Count); Assert.IsTrue(setBound.Warnings.Any(w => w.Contains("result sets")));
    }

    [TestMethod]
    public async Task ConcurrentRunsOwnDifferentSessions()
    {
        var factory = new Factory(() => new FakeSession { Reader = () => Table(1).CreateDataReader() }); var service = new DaxQueryService(factory);
        var results = await Task.WhenAll(service.ExecuteAsync(Request(), CancellationToken.None), service.ExecuteAsync(Request(), CancellationToken.None));
        Assert.AreEqual(2, factory.Created.Count); Assert.AreNotSame(factory.Created[0], factory.Created[1]);
        Assert.IsTrue(factory.Created.All(s => s.Disposed)); Assert.AreNotEqual(results[0].Id, results[1].Id);
    }

    [TestMethod]
    public async Task PreCancelledRequestNeverCreatesAConnection()
    {
        var factory = new Factory(() => new FakeSession()); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new DaxQueryService(factory).ExecuteAsync(Request(), cancellation.Token));
        Assert.AreEqual(0, factory.Created.Count);
    }

    [TestMethod]
    public async Task CancellationTargetsOwnedSessionAndJoinsCancelBeforeDispose()
    {
        using var executing = new ManualResetEventSlim(); using var interrupted = new ManualResetEventSlim();
        var cancelComplete = false;
        var session = new FakeSession
        {
            Reader = () => { executing.Set(); if (!interrupted.Wait(5000)) throw new TimeoutException("Test cancellation was not delivered."); throw new InvalidOperationException("Transport interrupted."); },
            OnCancel = () => { cancelComplete = true; interrupted.Set(); },
            OnDispose = () => Assert.IsTrue(cancelComplete)
        };
        using var cancellation = new CancellationTokenSource();
        var execution = new DaxQueryService(new Factory(() => session)).ExecuteAsync(Request(), cancellation.Token);
        Assert.IsTrue(await Task.Run(() => executing.Wait(5000))); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => execution);
        Assert.AreEqual(1, session.CancelCount); Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task TimeoutCancelsTransportAndReportsTimeout()
    {
        using var interrupted = new ManualResetEventSlim();
        var session = new FakeSession
        {
            Reader = () => { if (!interrupted.Wait(5000)) throw new InvalidOperationException("Timeout cancellation did not arrive."); throw new InvalidOperationException("Interrupted."); },
            OnCancel = interrupted.Set
        };
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => new DaxQueryService(new Factory(() => session)).ExecuteAsync(Request(timeout: 1), CancellationToken.None));
        Assert.AreEqual(1, session.CancelCount); Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task ServerFailuresDisposeTheRunAndPreserveUsefulError()
    {
        var session = new FakeSession { Reader = () => throw new QueryExecutionException("Invalid DAX at line 2.") };
        var error = await Assert.ThrowsExactlyAsync<QueryExecutionException>(() => new DaxQueryService(new Factory(() => session)).ExecuteAsync(Request(), CancellationToken.None));
        StringAssert.Contains(error.Message, "line 2"); Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task AdapterExceptionsCannotLeakTransientPasswordInExceptionChain()
    {
        const string connection = "Data Source=localhost:2383;Password=secret-for-test";
        var session = new FakeSession { Reader = () => throw new InvalidOperationException("Failed " + connection + " and secret-for-test", new Exception(connection)) };
        var error = await Assert.ThrowsExactlyAsync<QueryExecutionException>(() => new DaxQueryService(new Factory(() => session)).ExecuteAsync(Request() with { ConnectionString = connection }, CancellationToken.None));
        Assert.IsFalse(error.ToString().Contains("secret-for-test")); Assert.IsNull(error.InnerException);
    }

    [TestMethod]
    public async Task CancelDuringConnectionPreventsQueryExecutionAfterConnectReturns()
    {
        using var connecting = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var cancellation = new CancellationTokenSource();
        var session = new FakeSession { OnOpen = () => { connecting.Set(); if (!release.Wait(5000)) throw new TimeoutException(); } };
        var execution = new DaxQueryService(new Factory(() => session)).ExecuteAsync(Request(), cancellation.Token);
        Assert.IsTrue(await Task.Run(() => connecting.Wait(5000))); cancellation.Cancel(); release.Set();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => execution);
        Assert.IsNull(session.ExecutedQuery); Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public void TomStatementEscapesXmlWithoutChangingDaxAndFactoryCreatesNewConnections()
    {
        const string query = "EVALUATE ROW(\"Text\", \"<xml>&\", \"Result\", 1 < 2 && 2 > 1)";
        var adapter = typeof(TomDaxQueryService).Assembly.GetType("PbiBench.Semantic.TomQuerySession", true)!;
        var statement = (string)adapter.GetMethod("BuildStatement", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { query })!;
        Assert.AreEqual(query, XElement.Parse(statement).Value); StringAssert.Contains(statement, "&lt;"); StringAssert.Contains(statement, "&amp;");
        var factory = (IQuerySessionFactory)Activator.CreateInstance(typeof(TomQuerySessionFactory), new object?[] { null })!;
        using var first = factory.Create(); using var second = factory.Create(); Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void TomConnectionKeepsTransientAuthButCannotReuseSessionOrRedirectCapturedTarget()
    {
        var request = Request() with { ConnectionString = "DataSource=foreign;InitialCatalog=other;Session ID=shared;User ID=test;Password=secret" };
        var adapter = typeof(TomDaxQueryService).Assembly.GetType("PbiBench.Semantic.TomQuerySession", true)!;
        var text = (string)adapter.GetMethod("BuildConnectionString", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { request })!;
        var connection = new DbConnectionStringBuilder { ConnectionString = text };
        Assert.AreEqual(request.Server, connection["Data Source"]); Assert.AreEqual(request.Database, connection["Initial Catalog"]);
        Assert.IsFalse(connection.ContainsKey("DataSource")); Assert.IsFalse(connection.ContainsKey("InitialCatalog")); Assert.IsFalse(connection.ContainsKey("Session ID"));
        Assert.AreEqual("secret", connection["Password"]); Assert.AreEqual("PbiBench DAX", connection["Application Name"]);
    }

    [TestMethod]
    public void DuplicateCaptionsHaveStableGridKeysAndCsvEscapesInvariantValues()
    {
        var set = new QueryResultSet(0, "Result 1", new[] { new QueryColumn("C0", "A,B", "System.String"), new QueryColumn("C1", "A,B", "System.Decimal") },
            new[] { new object?[] { "quoted \"line\"\r\nnext", 2.5m }, new object?[] { null, DBNull.Value } }, false);
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-CH");
            Assert.AreEqual("\"A,B\",\"A,B\"\r\n\"quoted \"\"line\"\"\r\nnext\",2.5\r\n,\r\n", QueryCsv.ToCsv(set));
            var table = set.ToDataTable(); Assert.AreEqual("C1", table.Columns[1].ColumnName); Assert.AreEqual("A,B", table.Columns[1].Caption);
            Assert.AreEqual(DBNull.Value, table.Rows[1][0]);
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }

    [TestMethod]
    public async Task HistoryIsBoundedReloadableAndNeverSerializesTransportCredentials()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PbiBench-query-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new QueryHistoryStore(directory, 2);
            var request = Request() with { ConnectionString = "Data Source=localhost:2383;Password=secret-for-test" };
            for (var i = 0; i < 3; i++) await store.AddAsync(QueryHistoryEntry.FromFailure(request with { Query = "EVALUATE {" + i + "}" }, "Failed"), CancellationToken.None);
            var loaded = await new QueryHistoryStore(directory, 2).LoadAsync(CancellationToken.None);
            Assert.AreEqual(2, loaded.Count); Assert.AreEqual("EVALUATE {2}", loaded[0].Query); Assert.AreEqual("EVALUATE {1}", loaded[1].Query);
            Assert.IsFalse(File.ReadAllText(Path.Combine(directory, "query-history.json")).Contains("secret-for-test"));
            Assert.IsFalse(JsonSerializer.Serialize(request).Contains("secret-for-test")); Assert.IsFalse(request.ToString().Contains("secret-for-test"));
            await store.ClearAsync(CancellationToken.None); Assert.AreEqual(0, (await store.LoadAsync(CancellationToken.None)).Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task CancelledCsvExportLeavesExistingFileUntouched()
    {
        var path = Path.Combine(Path.GetTempPath(), "PbiBench-query-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllText(path, "existing"); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var set = new QueryResultSet(0, "Result", new[] { new QueryColumn("C0", "Value", "System.Int32") }, new[] { new object?[] { 1 } }, false);
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => QueryCsv.ExportAsync(set, path, cancellation.Token));
            Assert.AreEqual("existing", File.ReadAllText(path));
            await QueryCsv.ExportAsync(set, path, CancellationToken.None); StringAssert.Contains(File.ReadAllText(path), "Value\r\n1\r\n");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [TestMethod]
    public void CsvFloatingPointValuesRoundTripWithoutLosingPrecision()
    {
        const double value = 0.12345678912345678;
        var set = new QueryResultSet(0, "Result", new[] { new QueryColumn("C0", "Value", "System.Double") }, new[] { new object?[] { value } }, false);
        var row = QueryCsv.ToCsv(set).Split(new[] { "\r\n" }, StringSplitOptions.None)[1];
        Assert.AreEqual(value, double.Parse(row, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void RequestRejectsConnectionStringInDisplayEndpointAndUnboundedOptions()
    {
        Assert.ThrowsExactly<ArgumentException>(() => (Request() with { Server = "localhost;Password=hidden" }).Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Request(limit: 0).Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Request(timeout: 0).Validate());
    }

    [TestMethod]
    public async Task QueryTransportRejectsXmlaTmslAndDdlBeforeOpeningAConnection()
    {
        var factory = new Factory(() => new FakeSession()); var service = new DaxQueryService(factory);
        foreach (var text in new[] { "{\"delete\":{\"object\":{\"database\":\"Model\"}}}", "<Delete xmlns=\"http://schemas.microsoft.com/analysisservices/2003/engine\"/>", "CREATE MEASURE 'Table'[M] = 1", "// EVALUATE\n{\"refresh\":{}}", "EVALUATEfoo {1}" })
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ExecuteAsync(Request(text), CancellationToken.None));
        Assert.AreEqual(0, factory.Created.Count);
        Request("\uFEFF /* query */ -- second comment\r\n// third\nDEFINE VAR x = 1 EVALUATE {x}").Validate();
    }

    private sealed class Factory : IQuerySessionFactory
    {
        private readonly Func<FakeSession> create;
        public List<FakeSession> Created { get; } = new();
        public Factory(Func<FakeSession> create) => this.create = create;
        public IQuerySession Create() { var session = create(); lock (Created) Created.Add(session); return session; }
    }
    private sealed class FakeSession : IQuerySession
    {
        public Func<IDataReader> Reader { get; set; } = () => Table(1).CreateDataReader();
        public Action? OnCancel { get; set; }
        public Action? OnOpen { get; set; }
        public Action? OnDispose { get; set; }
        public string? ExecutedQuery { get; private set; }
        public bool Disposed { get; private set; }
        public int CancelCount { get; private set; }
        public void Open(QueryRequest request) => OnOpen?.Invoke();
        public IDataReader Execute(string query) { ExecutedQuery = query; return Reader(); }
        public void Cancel() { CancelCount++; OnCancel?.Invoke(); }
        public void Dispose() { OnDispose?.Invoke(); Disposed = true; }
    }
}
