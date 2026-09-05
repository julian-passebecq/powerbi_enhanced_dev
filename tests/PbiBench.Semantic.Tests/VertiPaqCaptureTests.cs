using System.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Core.Quality;
using PbiBench.Core.Queries;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class VertiPaqCaptureTests
{
    [TestMethod]
    public async Task MetricCaptureUsesIndependentSessionAndOnlyClosedPublicRowsets()
    {
        var factory = new Factory(); var service = new TomVertiPaqSnapshotService(factory);
        await service.CaptureAsync(new("s", "m"), CancellationToken.None);
        await service.CaptureAsync(new("s", "m"), CancellationToken.None);
        Assert.AreEqual(2, factory.Sessions.Count);
        Assert.IsFalse(ReferenceEquals(factory.Sessions[0], factory.Sessions[1]));
        foreach (var session in factory.Sessions)
        {
            Assert.IsTrue(session.Disposed); Assert.AreEqual(8, session.Statements.Count);
            Assert.IsTrue(session.Statements.All(statement => statement.StartsWith("SELECT * FROM $SYSTEM.", StringComparison.Ordinal)));
            Assert.AreEqual("s", session.Request!.Server); Assert.AreEqual("m", session.Request.Database);
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TomVertiPaqSnapshotService.Statement((VertiPaqRowset)999));
        Assert.ThrowsExactly<ArgumentException>(() => new QueryRequest("s", "m", TomVertiPaqSnapshotService.Statement(VertiPaqRowset.Tables)).Validate());
    }

    [TestMethod]
    public async Task UnavailableMetricsArePartialAndCredentialErrorsAreNotExposed()
    {
        var factory = new Factory { Execute = _ => throw new InvalidOperationException("Password=secret token=private") };
        var result = await new TomVertiPaqSnapshotService(factory).CaptureAsync(new("s", "m") { ConnectionString = "Password=secret" }, CancellationToken.None);
        Assert.AreEqual(0, result.Tables.Count);
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("identity lacks permission")));
        Assert.IsFalse(string.Join(" ", result.Warnings).Contains("secret"));
        Assert.IsFalse(string.Join(" ", result.Warnings).Contains("private"));
    }

    [TestMethod]
    public async Task TruncatedRowsetsAreDiscardedAndNeverReportedAsCompleteCounts()
    {
        var factory = new Factory { Execute = statement =>
        {
            var data = new DataTable(); data.Columns.Add("ID", typeof(long)); data.Columns.Add("Name", typeof(string)); data.Rows.Add(1L, "T"); data.Rows.Add(2L, "U"); return data.CreateDataReader();
        } };
        var result = await new TomVertiPaqSnapshotService(factory).CaptureAsync(new("s", "m", MaximumRowsPerRowset: 1), CancellationToken.None);
        Assert.AreEqual(0, result.Tables.Count);
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("incomplete metrics were discarded")));
    }

    [TestMethod]
    public async Task CancellationDuringConnectNeverExecutesAndDisposesOwnedSession()
    {
        using var canceled = new CancellationTokenSource();
        var factory = new Factory { Open = _ => canceled.Cancel() };
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new TomVertiPaqSnapshotService(factory).CaptureAsync(new("s", "m"), canceled.Token));
        Assert.AreEqual(0, factory.Sessions.Single().Statements.Count); Assert.IsTrue(factory.Sessions.Single().Disposed);
    }

    [TestMethod]
    public async Task CancellationTargetsRunningPrivateSessionAndJoinsBeforeDisposal()
    {
        using var entered = new ManualResetEventSlim(); using var released = new ManualResetEventSlim(); using var cancellation = new CancellationTokenSource();
        var cancelFinished = false;
        var factory = new Factory { Execute = _ => { entered.Set(); if (!released.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test command did not cancel."); return Empty(); },
            Cancel = () => { released.Set(); Thread.Sleep(20); cancelFinished = true; }, Dispose = () => Assert.IsTrue(cancelFinished) };
        var work = new TomVertiPaqSnapshotService(factory).CaptureAsync(new("s", "m"), cancellation.Token);
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5))); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => work);
        Assert.AreEqual(1, factory.Sessions.Single().CancelCalls); Assert.IsTrue(factory.Sessions.Single().Disposed);
    }

    [TestMethod]
    public async Task TimeoutCancelsPrivateCommandAndReportsTimeout()
    {
        using var released = new ManualResetEventSlim();
        var factory = new Factory { Execute = _ => { if (!released.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Test timeout"); return Empty(); }, Cancel = released.Set };
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => new TomVertiPaqSnapshotService(factory).CaptureAsync(new("s", "m", TimeoutSeconds: 1), CancellationToken.None));
        Assert.AreEqual(1, factory.Sessions.Single().CancelCalls);
    }

    [TestMethod]
    public async Task InvalidAndPreCanceledCaptureRequestsNeverOpenConnections()
    {
        var factory = new Factory(); var service = new TomVertiPaqSnapshotService(factory);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.CaptureAsync(new("s;Password=secret", "m"), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.CaptureAsync(new("s", "m", TimeoutSeconds: 0), CancellationToken.None));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.CaptureAsync(new("s", "m"), canceled.Token));
        Assert.AreEqual(0, factory.Sessions.Count);
    }

    private static IDataReader Empty() { var table = new DataTable(); table.Columns.Add("ID", typeof(long)); return table.CreateDataReader(); }
    private sealed class Factory : IQuerySessionFactory
    {
        public List<Session> Sessions { get; } = new();
        public Action<QueryRequest>? Open { get; init; }
        public Func<string, IDataReader>? Execute { get; init; }
        public Action? Cancel { get; init; }
        public Action? Dispose { get; init; }
        public IQuerySession Create() { var session = new Session(this); Sessions.Add(session); return session; }
    }
    private sealed class Session(Factory owner) : IQuerySession
    {
        public QueryRequest? Request { get; private set; }
        public List<string> Statements { get; } = new();
        public bool Disposed { get; private set; }
        public int CancelCalls { get; private set; }
        public void Open(QueryRequest request) { Request = request; owner.Open?.Invoke(request); }
        public IDataReader Execute(string query) { Statements.Add(query); return owner.Execute?.Invoke(query) ?? Empty(); }
        public void Cancel() { CancelCalls++; owner.Cancel?.Invoke(); }
        public void Dispose() { Disposed = true; owner.Dispose?.Invoke(); }
    }
}
