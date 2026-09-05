using System.IO;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Core.Domain;
using PbiBench.Core.Workspaces;
using PbiBench.Semantic.Workspaces;
using PbiBench.Workspace;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class WorkspaceNativeTests
{
    private static TabularModelHandler Fixture()
    { var handler = new TabularModelHandler(1702); var sales = handler.Model.AddTable("Sales"); sales.AddDataColumn("Quantity", "Quantity", dataType: DataType.Int64); sales.AddMeasure("Quantity Total", "SUM('Sales'[Quantity])"); var function = handler.Model.AddFunction(); function.Name = "My.Double"; function.Expression = "(value: SCALAR INT64) => value * 2"; handler.UndoManager.Clear(); return handler; }
    [TestMethod]
    public void PinnedTmdlRoundTripIncludesUdfAndNeverReplacesNativeHandler()
    {
        using var handler = Fixture(); var codec = new TmdlWorkspaceCodec(); var original = codec.CaptureLoaded(handler); var files = codec.Serialize(original, false); Assert.IsTrue(files.Any(file => file.Path == "functions.tmdl"), string.Join(",", files.Select(file => file.Path)));
        using var temp = new NativeTemp(); var roundTrip = codec.Parse(new WorkspaceDiskSnapshot(temp.Root, files)); Assert.AreEqual(original.Hash, roundTrip.Hash); var newMeasure = handler.Model.Tables["Sales"].AddMeasure("Still native", "1"); Assert.AreSame(handler.Model, newMeasure.Model);
    }
    [TestMethod]
    public void BimAndTmdlParseCapturedFilesRatherThanChangingDiskContents()
    {
        using var handler = Fixture(); var codec = new TmdlWorkspaceCodec(); var original = codec.CaptureLoaded(handler); using var temp = new NativeTemp(); var files = codec.Serialize(original, false); foreach (var file in files) { var path = WorkspaceDiskStore.SafePath(temp.Root, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, file.Content); }
        var capture = new WorkspaceDiskStore().Capture(temp.Root); File.WriteAllText(Path.Combine(temp.Root, "model.tmdl"), "invalid external edit"); Assert.AreEqual(original.Hash, codec.Parse(capture).Hash); Assert.AreEqual(original.Hash, codec.Parse(new WorkspaceDiskSnapshot(temp.Root, codec.Serialize(original, true), true)).Hash);
    }
    [TestMethod]
    public void GeneratedRemoteCommandBindsDatabaseNameAndIdWithoutMutatingSource()
    {
        using var handler = Fixture(); var source = new TmdlWorkspaceCodec().CaptureLoaded(handler); var command = JsonNode.Parse(TomWorkspaceSyncService.CreateOrReplace(source, "Production \"model\"", "target-id"))!;
        Assert.AreEqual("Production \"model\"", command["createOrReplace"]!["object"]!["database"]!.GetValue<string>()); Assert.AreEqual("target-id", command["createOrReplace"]!["database"]!["id"]!.GetValue<string>()); Assert.IsFalse(source.DatabaseJson.Contains("target-id")); Assert.AreEqual(1, command.AsObject().Count);
    }
    [TestMethod]
    public void PublicTomCaptureAcceptsRawReviewedTmslWithoutConnecting()
    {
        using var handler = Fixture(); var source = new TmdlWorkspaceCodec().CaptureLoaded(handler); var command = TomWorkspaceSyncService.CreateOrReplace(source, "Target", "target-id");
        using var server = new Microsoft.AnalysisServices.Tabular.Server { CaptureXml = true }; server.Execute(command);
        Assert.IsFalse(server.Connected); Assert.AreEqual(1, server.CaptureLog.Count); Assert.IsTrue(server.CaptureLog[0].Contains("createOrReplace")); Assert.IsTrue(server.CaptureLog[0].Contains("target-id"));
    }
    [TestMethod]
    public async Task ReviewedPrivatePushRechecksBothStatesSnapshotsAndCommitsOnce()
    {
        using var handler = Fixture(); var codec = new TmdlWorkspaceCodec(); var before = codec.CaptureLoaded(handler); handler.Model.Tables["Sales"].Description = "disk edit"; var disk = codec.CaptureLoaded(handler); var factory = new FakeFactory(before, disk); var service = new TomWorkspaceSyncService(factory); var live = new WorkspaceLiveCapture("target-id", "Target", before); var plan = service.PreparePush(WorkspaceSemanticDiff.Compare(before, disk, before), live, new WorkspaceConnection("endpoint", "Target", "Password=private"), "disk-v1"); using var temp = new NativeTemp(); var dispatched = 0;
        var result = await service.ApplyPushAsync(plan, Approve(plan), _ => "disk-v1", temp.Root, CancellationToken.None, () => dispatched++); Assert.AreEqual(1, dispatched); Assert.AreEqual(disk.Hash, result.Live.Snapshot.Hash); Assert.IsTrue(File.Exists(result.BackupPath)); CollectionAssert.AreEqual(new[] { "Open", "Begin", "Capture", "Execute", "Commit", "Capture", "Dispose" }, factory.Calls); Assert.IsFalse(plan.Connection.ToString().Contains("private"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyPushAsync(plan, Approve(plan), _ => "disk-v1", temp.Root, CancellationToken.None));
    }
    [TestMethod]
    public async Task StaleLivePushRollsBackWithoutExecutingAndStaleDiskNeverBeginsTransaction()
    {
        using var handler = Fixture(); var codec = new TmdlWorkspaceCodec(); var original = codec.CaptureLoaded(handler); handler.Model.Tables["Sales"].Description = "disk"; var disk = codec.CaptureLoaded(handler); var factory = new FakeFactory(disk, disk); var service = new TomWorkspaceSyncService(factory); var plan = service.PreparePush(WorkspaceSemanticDiff.Compare(original, disk, original), new("id", "Target", original), new("endpoint", "Target"), "hash"); using var temp = new NativeTemp();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyPushAsync(plan, Approve(plan), _ => "hash", temp.Root, CancellationToken.None)); Assert.IsFalse(factory.Calls.Contains("Execute")); Assert.IsTrue(factory.Calls.Contains("Rollback")); factory.Calls.Clear();
        plan = service.PreparePush(WorkspaceSemanticDiff.Compare(original, disk, original), new("id", "Target", original), new("endpoint", "Target"), "hash"); await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyPushAsync(plan, Approve(plan), _ => "changed", temp.Root, CancellationToken.None)); Assert.IsFalse(factory.Calls.Contains("Begin"));
    }
    [TestMethod]
    public async Task RejectedRemoteExecutionRollsBackAndRetainsRecoverySnapshot()
    {
        using var handler = Fixture(); var codec = new TmdlWorkspaceCodec(); var original = codec.CaptureLoaded(handler); handler.Model.Tables["Sales"].Description = "disk"; var disk = codec.CaptureLoaded(handler); var factory = new FakeFactory(original, disk) { FailExecute = true }; var service = new TomWorkspaceSyncService(factory); var plan = service.PreparePush(WorkspaceSemanticDiff.Compare(original, disk, original), new("target-id", "Target", original), new("endpoint", "Target"), "hash"); using var temp = new NativeTemp();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyPushAsync(plan, Approve(plan), _ => "hash", temp.Root, CancellationToken.None)); Assert.IsTrue(factory.Calls.Contains("Rollback")); Assert.IsFalse(factory.Calls.Contains("Commit")); Assert.AreEqual(1, Directory.EnumerateFiles(temp.Root, "*.bim").Count());
    }
    [TestMethod]
    public void ConflictsUnsavedDraftsAndCompatibilityChangesRequireResolution()
    {
        using var handler = Fixture(); var codec = new TmdlWorkspaceCodec(); var baseline = codec.CaptureLoaded(handler); handler.Model.Tables["Sales"].Description = "disk"; var disk = codec.CaptureLoaded(handler); handler.Model.Tables["Sales"].Description = "live"; var live = codec.CaptureLoaded(handler); var service = new TomWorkspaceSyncService(new FakeFactory(live, disk)); var capture = new WorkspaceLiveCapture("id", "Target", live); var connection = new WorkspaceConnection("endpoint", "Target");
        Assert.ThrowsExactly<InvalidOperationException>(() => service.PreparePush(WorkspaceSemanticDiff.Compare(baseline, disk, live), capture, connection, "h")); Assert.IsNotNull(service.PreparePush(WorkspaceSemanticDiff.Compare(baseline, disk, live), capture, connection, "h", true));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.PreparePush(WorkspaceSemanticDiff.Compare(baseline, disk, live, hasUnsavedModelEdits: true), capture, connection, "h", true)); var upgraded = WorkspaceSemanticSnapshot.Parse(disk.DatabaseJson.Replace("1702", "1703")); Assert.ThrowsExactly<InvalidOperationException>(() => service.PreparePush(WorkspaceSemanticDiff.Compare(baseline, upgraded, live), capture, connection, "h", true));
    }
    private static ApprovedChangePlan Approve(WorkspacePushPlan plan) => new(plan.Plan, DateTimeOffset.UtcNow, "test");
    private sealed class FakeFactory(WorkspaceSemanticSnapshot before, WorkspaceSemanticSnapshot after) : IWorkspaceLiveSessionFactory
    {
        public List<string> Calls { get; } = new(); public bool FailExecute;
        public IWorkspaceLiveSession Create() => new Fake(this, before, after);
        private sealed class Fake(FakeFactory owner, WorkspaceSemanticSnapshot before, WorkspaceSemanticSnapshot after) : IWorkspaceLiveSession
        {
            private bool executed;
            public void Open(WorkspaceConnection connection) => owner.Calls.Add("Open");
            public WorkspaceLiveCapture Capture() { owner.Calls.Add("Capture"); return new("target-id", "Target", executed ? after : before); }
            public void BeginTransaction() => owner.Calls.Add("Begin"); public void CommitTransaction() => owner.Calls.Add("Commit"); public void RollbackTransaction() => owner.Calls.Add("Rollback");
            public void Execute(string tmsl) { owner.Calls.Add("Execute"); if (owner.FailExecute) throw new InvalidOperationException("rejected"); executed = true; }
            public void Cancel() => owner.Calls.Add("Cancel"); public void Dispose() => owner.Calls.Add("Dispose");
        }
    }
    private sealed class NativeTemp : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pbibench-workspace-native-" + Guid.NewGuid().ToString("N")); public NativeTemp() => Directory.CreateDirectory(Root);
        public void Dispose() { var path = Path.GetFullPath(Root); if (!string.Equals(Path.GetDirectoryName(path)?.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("pbibench-workspace-native-", StringComparison.Ordinal)) throw new InvalidOperationException(); if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
