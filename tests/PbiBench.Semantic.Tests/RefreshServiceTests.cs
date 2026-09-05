using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Core.Domain;
using PbiBench.Core.Refresh;
using System.Text.Json;
using System.Xml.Linq;
using TOM = Microsoft.AnalysisServices.Tabular;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class RefreshServiceTests
{
    private static RefreshMetadataSnapshot Metadata() => new("fixture-server", "fixture-id", "Fixture model", 1702, "fingerprint", true, false, true,
        new[] { new RefreshTableMetadata("Facts", false, new[] { new RefreshPartitionMetadata("Current", "Import", RefreshSourceKind.M) }) });
    private static RefreshPlan Plan() => RefreshPlanner.Build(Metadata(), new() { Objects = new[] { new RefreshObject("Facts", "Current") } });
    private static ApprovedChangePlan Approve(RefreshPlan plan) => new(plan.ChangePlan, DateTimeOffset.UtcNow, "fixture-reviewer");
    private static RefreshConnection Connection() => new("fixture-server", "fixture-id") { ConnectionString = "Password=do-not-leak;" };

    [TestMethod]
    public async Task OnlyTheFrozenApprovedCommandExecutesOnAnOwnedSession()
    {
        var factory = new Factory(); var plan = Plan(); var service = new TomRefreshService(factory);
        var result = await service.ExecuteAsync(plan, Approve(plan), Connection(), null, CancellationToken.None);
        Assert.AreEqual(RefreshOutcome.Succeeded, result.Outcome); Assert.IsTrue(result.CommandSubmitted); Assert.AreEqual(plan.Tmsl, factory.Last!.Command); Assert.IsTrue(factory.Last.Disposed); Assert.AreEqual(1, factory.Created);
        Assert.AreEqual("fixture-id", factory.Last.Connection!.DatabaseId); Assert.AreEqual(1, factory.Last.Captures);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ExecuteAsync(plan, Approve(plan), Connection(), null, CancellationToken.None)); Assert.AreEqual(1, factory.Created);
    }
    [TestMethod]
    public async Task ForeignApprovalAndTargetAreRejectedBeforeOpeningAnyConnection()
    {
        var factory = new Factory(); var service = new TomRefreshService(factory); var plan = Plan(); var other = Plan();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ExecuteAsync(plan, Approve(other), Connection(), null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ExecuteAsync(plan, Approve(plan), Connection() with { DatabaseId = "other" }, null, CancellationToken.None)); Assert.AreEqual(0, factory.Created);
    }
    [TestMethod]
    [DataRow("fingerprint")]
    [DataRow("name")]
    [DataRow("id")]
    [DataRow("server")]
    public async Task StaleMetadataIsDetectedBeforeTheRemoteCommand(string field)
    {
        var snapshot = field switch { "name" => Metadata() with { DatabaseName = "renamed" }, "id" => Metadata() with { DatabaseId = "replacement" }, "server" => Metadata() with { Server = "different" }, _ => Metadata() with { Fingerprint = "changed" } };
        var factory = new Factory { Snapshot = snapshot }; var plan = Plan();
        var result = await new TomRefreshService(factory).ExecuteAsync(plan, Approve(plan), Connection(), null, CancellationToken.None);
        Assert.AreEqual(RefreshOutcome.Failed, result.Outcome); Assert.IsFalse(result.CommandSubmitted); Assert.IsNull(factory.Last!.Command); Assert.IsTrue(result.Message.Contains("No refresh command")); Assert.IsTrue(factory.Last.Disposed);
    }
    [TestMethod]
    public async Task CancellationBeforeSubmissionDoesNotRunTmsl()
    {
        using var cancellation = new CancellationTokenSource(); var factory = new Factory { OnOpen = () => cancellation.Cancel() }; var plan = Plan();
        var result = await new TomRefreshService(factory).ExecuteAsync(plan, Approve(plan), Connection(), null, cancellation.Token);
        Assert.AreEqual(RefreshOutcome.CanceledBeforeExecution, result.Outcome); Assert.IsFalse(result.CommandSubmitted); Assert.IsNull(factory.Last!.Command); Assert.IsTrue(factory.Last.Disposed);
    }
    [TestMethod]
    public async Task CancellationAfterSubmissionIsUncertainAndTargetsOnlyThePrivateSession()
    {
        using var cancellation = new CancellationTokenSource(); using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Factory { OnExecute = _ => { entered.SetResult(true); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); throw new OperationCanceledException(); }, OnCancel = () => release.Set() }; var plan = Plan();
        var run = new TomRefreshService(factory).ExecuteAsync(plan, Approve(plan), Connection(), null, cancellation.Token);
        await entered.Task; cancellation.Cancel(); var result = await run;
        Assert.AreEqual(RefreshOutcome.OutcomeUnknown, result.Outcome); Assert.IsTrue(result.CommandSubmitted); Assert.IsTrue(result.Message.Contains("unconfirmed")); Assert.AreEqual(1, factory.Last!.Cancels); Assert.IsTrue(factory.Last.Disposed); Assert.AreEqual(1, factory.Created);
    }
    [TestMethod]
    public async Task ServerSuccessAfterLateCancellationIsNotReportedAsRolledBack()
    {
        using var cancellation = new CancellationTokenSource(); var factory = new Factory { OnExecute = _ => { cancellation.Cancel(); return new(false, Array.Empty<string>(), Array.Empty<string>()); } }; var plan = Plan();
        var result = await new TomRefreshService(factory).ExecuteAsync(plan, Approve(plan), Connection(), null, cancellation.Token);
        Assert.AreEqual(RefreshOutcome.SucceededWithWarnings, result.Outcome); Assert.IsTrue(result.Details.Any(message => message.Contains("not undone")));
    }
    [TestMethod]
    public async Task ServerErrorsAndConnectionLossAreDistinguishedWithoutCredentialLeakage()
    {
        var factory = new Factory { OnExecute = _ => new(true, new[] { "Source failed. Password=do-not-leak; access_token=another-secret" }, Array.Empty<string>()) }; var plan = Plan();
        var failed = await new TomRefreshService(factory).ExecuteAsync(plan, Approve(plan), Connection(), null, CancellationToken.None);
        Assert.AreEqual(RefreshOutcome.Failed, failed.Outcome); Assert.IsFalse(string.Join(" ", failed.Details).Contains("do-not-leak")); Assert.IsFalse(string.Join(" ", failed.Details).Contains("another-secret"));
        factory = new Factory { OnExecute = _ => throw new IOException("Password=do-not-leak") }; plan = Plan();
        var unknown = await new TomRefreshService(factory).ExecuteAsync(plan, Approve(plan), Connection(), null, CancellationToken.None);
        Assert.AreEqual(RefreshOutcome.OutcomeUnknown, unknown.Outcome); Assert.IsFalse(unknown.Message.Contains("do-not-leak"));
    }
    [TestMethod]
    public void PublicTomCapturePreservesTheReviewedJsonWithoutConnecting()
    {
        using var server = new TOM.Server { CaptureXml = true }; var plan = Plan();
        server.Execute(new XElement("Statement", plan.Tmsl).ToString(SaveOptions.DisableFormatting));
        Assert.IsFalse(server.Connected); Assert.AreEqual(1, server.CaptureLog.Count);
        // XML readers normalize literal CRLF whitespace, while the reviewed JSON's values remain unchanged.
        Assert.AreEqual(plan.Tmsl.Replace("\r\n", "\n"), XElement.Parse(server.CaptureLog[0]).Value);
    }
    [TestMethod]
    public void TmslDatabaseReferenceMatchesThePublicTomScripterForDistinctNameAndId()
    {
        var database = new TOM.Database { ID = "stable-id", Name = "A \"quoted\" model", CompatibilityLevel = 1500, Model = new TOM.Model() };
        using var native = JsonDocument.Parse(TOM.JsonScripter.ScriptRefresh(database, TOM.RefreshType.Full));
        var plan = RefreshPlanner.Build(RefreshMetadataProvider.Capture(database, "fixture", false), new());
        using var own = JsonDocument.Parse(plan.Tmsl);
        Assert.AreEqual(database.ID, plan.Metadata.DatabaseId);
        Assert.AreEqual(native.RootElement.GetProperty("refresh").GetProperty("objects")[0].GetProperty("database").GetString(),
            own.RootElement.GetProperty("sequence").GetProperty("operations")[0].GetProperty("refresh").GetProperty("objects")[0].GetProperty("database").GetString());
    }
    [TestMethod]
    public void MetadataCaptureIsDetachedAndFingerprintDetectsSourceChanges()
    {
        var database = new TOM.Database { ID = "id", Name = "Fixture", CompatibilityLevel = 1500, Model = new TOM.Model { DefaultMode = TOM.ModeType.Import } };
        var table = new TOM.Table { Name = "Facts" }; database.Model.Tables.Add(table); var partition = new TOM.Partition { Name = "P", Source = new TOM.MPartitionSource { Expression = "#table({\"ID\"}, {{1}})" } }; table.Partitions.Add(partition);
        var first = RefreshMetadataProvider.Capture(database, "fixture", false); Assert.AreEqual("Import", first.Tables[0].Partitions[0].Mode); Assert.AreEqual(RefreshSourceKind.M, first.Tables[0].Partitions[0].SourceKind); Assert.IsFalse(first.IsConnected);
        ((TOM.MPartitionSource)partition.Source).Expression = "#table({\"ID\"}, {{2}})"; table.Name = "Renamed"; var second = RefreshMetadataProvider.Capture(database, "fixture", false);
        Assert.AreEqual("Facts", first.Tables[0].Name); Assert.AreNotEqual(first.Fingerprint, second.Fingerprint); Assert.IsFalse(first.ToString().Contains("#table"));
    }
    private sealed class Factory : IRefreshSessionFactory
    {
        public int Created; public Session? Last; public RefreshMetadataSnapshot Snapshot = Metadata(); public Action? OnOpen, OnCancel; public Func<string, RefreshEngineResponse>? OnExecute;
        public IRefreshSession Create() { Created++; return Last = new Session(this); }
    }
    private sealed class Session : IRefreshSession
    {
        private readonly Factory factory;
        public Session(Factory factory) { this.factory = factory; }
        public bool Disposed; public string? Command; public RefreshConnection? Connection; public int Captures, Cancels;
        public void Open(RefreshConnection connection, int timeoutSeconds) { Connection = connection; factory.OnOpen?.Invoke(); }
        public RefreshMetadataSnapshot CaptureMetadata() { Captures++; return factory.Snapshot; }
        public RefreshEngineResponse Execute(string approvedTmsl) { Command = approvedTmsl; return factory.OnExecute?.Invoke(approvedTmsl) ?? new(false, Array.Empty<string>(), Array.Empty<string>()); }
        public void Cancel() { Interlocked.Increment(ref Cancels); factory.OnCancel?.Invoke(); }
        public void Dispose() { Disposed = true; }
    }
}
