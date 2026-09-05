using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Automation.Commands;
using PbiBench.Core.Commands;
using PbiBench.Core.Refresh;
using PbiBench.Core.Workspaces;
using PbiBench.Semantic.Workspaces;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class ConnectedCommandTests
{
    private static readonly RefreshMetadataSnapshot Metadata = new("fixture", "stable-id", "Visible name", 1600, "exact-metadata", true, false, false,
        new[] { new RefreshTableMetadata("Facts", false, new[] { new RefreshPartitionMetadata("Current", "Import", RefreshSourceKind.M) }) });
    private static CommandRequest RefreshRequest() => new() { Kind = CommandKind.Refresh, Target = new(Server: "fixture", Database: "Visible name"), Refresh = new() { Kind = RefreshKind.Add, Objects = new[] { new RefreshObject("Facts", "Current") } } };
    private static Task<PreparedCommand> Prepare(CommandRequest request, RefreshFactory refresh, WorkspaceFactory workspace) =>
        ConnectedCommandOperations.PrepareAsync(request, _ => "Password=private-secret;", new TomRefreshService(refresh), new TomWorkspaceSyncService(workspace), CancellationToken.None);

    [TestMethod]
    public async Task RefreshReviewIsStableAcrossProcessesAndApplyUsesResolvedIdentityExactlyOnce()
    {
        var factory = new RefreshFactory(); var a = await Prepare(RefreshRequest(), factory, new()); var b = await Prepare(RefreshRequest(), factory, new());
        Assert.AreEqual(a.Review.Hash, b.Review.Hash); Assert.IsTrue(a.Review.TargetIdentity.Contains("stable-id")); Assert.AreEqual(0, factory.Executions);
        var result = await a.ApplyAsync(a.Review.Hash, "fixture reviewer", CancellationToken.None);
        Assert.AreEqual(CommandStatus.Succeeded, result.Status); Assert.AreEqual("stable-id", factory.LastTarget!.DatabaseId); Assert.AreEqual(1, factory.Executions);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => a.ApplyAsync(a.Review.Hash, "fixture reviewer", CancellationToken.None));
        Assert.AreEqual(1, factory.Executions); Assert.IsFalse(CommandJson.Serialize(result).Contains("private-secret"));
    }
    [TestMethod]
    public async Task MetadataOrApprovalChangesRejectRefreshBeforeDispatch()
    {
        var factory = new RefreshFactory(); var prepared = await Prepare(RefreshRequest(), factory, new());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => prepared.ApplyAsync("incorrect", "reviewer", CancellationToken.None));
        factory.Snapshot = Metadata with { Fingerprint = "changed" };
        var newer = await Prepare(RefreshRequest(), factory, new()); Assert.AreNotEqual(prepared.Review.Hash, newer.Review.Hash);
        var result = await prepared.ApplyAsync(prepared.Review.Hash, "reviewer", CancellationToken.None);
        Assert.AreEqual(CommandStatus.Failed, result.Status); Assert.AreEqual(0, factory.Executions);
    }
    [TestMethod]
    public async Task CaptureIsReadOnlyAndSanitizesConnectionFailures()
    {
        var factory = new RefreshFactory(); var service = new TomRefreshService(factory);
        var snapshot = await service.CaptureAsync(new("fixture", "Visible name"), 10, CancellationToken.None);
        Assert.AreEqual("stable-id", snapshot.DatabaseId); Assert.AreEqual(0, factory.Executions); Assert.AreEqual(1, factory.Disposed);
        factory.FailOpen = true;
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CaptureAsync(new("fixture", "Visible name"), 10, CancellationToken.None));
        Assert.IsFalse(error.ToString().Contains("private-secret")); Assert.AreEqual(2, factory.Disposed);
    }
    [TestMethod]
    public async Task DeploymentReviewsExactDiskAndTargetThenRetainsRecoveryWithoutSerializingLiveObjects()
    {
        using var fixture = new ModelFile(); var transport = new WorkspaceFactory();
        var request = new CommandRequest { Kind = CommandKind.Deploy, Target = new(fixture.Path, "fixture", "Visible name") };
        var prepared = await Prepare(request, new(), transport); var repeated = await Prepare(request, new(), transport);
        Assert.AreEqual(prepared.Review.Hash, repeated.Review.Hash); Assert.IsTrue(prepared.Review.CanApply); Assert.IsTrue(prepared.Review.Changes.Any(change => change.Property == "description"));
        Assert.AreEqual(0, transport.Executions); var result = await prepared.ApplyAsync(prepared.Review.Hash, "reviewer", CancellationToken.None);
        Assert.AreEqual(CommandStatus.Succeeded, result.Status); Assert.AreEqual(1, transport.Executions); Assert.AreEqual(1, transport.Commits);
        var backup = result.Data!.Value.GetProperty("backupPath").GetString()!; Assert.IsTrue(File.Exists(backup));
        using var json = JsonDocument.Parse(transport.LastCommand!); var replace = json.RootElement.GetProperty("createOrReplace");
        Assert.AreEqual("Visible name", replace.GetProperty("object").GetProperty("database").GetString());
        Assert.AreEqual("stable-id", replace.GetProperty("database").GetProperty("id").GetString());
        Assert.IsFalse(CommandJson.Serialize(result).Contains("databaseJson"));
    }
    [TestMethod]
    public async Task ExternalSourceEditPreventsDeployment()
    {
        using var fixture = new ModelFile(); var transport = new WorkspaceFactory();
        var request = new CommandRequest { Kind = CommandKind.Deploy, Target = new(fixture.Path, "fixture", "Visible name") };
        var prepared = await Prepare(request, new(), transport); File.AppendAllText(fixture.Path, " ");
        var changed = await Prepare(request, new(), transport); Assert.AreNotEqual(prepared.Review.Hash, changed.Review.Hash);
        var rejected = await prepared.ApplyAsync(prepared.Review.Hash, "reviewer", CancellationToken.None);
        Assert.AreEqual(CommandStatus.Failed, rejected.Status); Assert.AreEqual(0, transport.Executions);
    }
    [TestMethod]
    public async Task CredentialAssignmentsAreRedactedFromReviewButRemainBoundToItsHash()
    {
        using var fixture = new ModelFile(); var request = new CommandRequest { Kind = CommandKind.Deploy, Target = new(fixture.Path, "fixture", "Visible name") };
        File.WriteAllText(fixture.Path, ModelJson("Password=first-private-value;")); var first = await Prepare(request, new(), new());
        Assert.IsFalse(CommandJson.Serialize(first.Review).Contains("first-private-value")); Assert.IsTrue(first.Review.CommandText!.Contains("[redacted:"));
        File.WriteAllText(fixture.Path, ModelJson("Password=second-private-value;")); var second = await Prepare(request, new(), new());
        Assert.AreNotEqual(first.Review.Hash, second.Review.Hash); Assert.IsFalse(CommandJson.Serialize(second.Review).Contains("second-private-value"));
    }
    [TestMethod]
    [DataRow("Authorization=Bearer fixture-private-token;")]
    [DataRow("ApiKey=fixture-private-token;")]
    [DataRow("x-api-key=fixture-private-token;")]
    [DataRow("sig=fixture-private-token;")]
    [DataRow("Web.Contents(\"https://example.test\", [Headers=[Authorization=\"Bearer fixture-private-token\", #\"x-api-key\"=\"fixture-private-token\"]])")]
    public async Task RefreshRejectsInlineAuthenticationAndDeploymentReviewRedactsIt(string expression)
    {
        var request = RefreshRequest() with { Refresh = RefreshRequest().Refresh! with
            { Kind = RefreshKind.Full, SourceOverrides = new[] { new RefreshSourceOverride("Facts", "Current", RefreshSourceKind.M, expression) } } };
        var refreshFactory = new RefreshFactory();
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() => Prepare(request, refreshFactory, new()));
        Assert.IsFalse(error.ToString().Contains("fixture-private-token")); Assert.IsNull(refreshFactory.LastTarget);
        using var fixture = new ModelFile(); File.WriteAllText(fixture.Path, ModelJson(expression));
        var deploy = await Prepare(new() { Kind = CommandKind.Deploy, Target = new(fixture.Path, "fixture", "Visible name") }, new(), new());
        Assert.IsFalse(CommandJson.Serialize(deploy.Review).Contains("fixture-private-token"));
    }
    [TestMethod]
    public async Task DeploymentAfterSubmissionCannotBeMisreportedAsUnchangedOnTransportFailure()
    {
        using var fixture = new ModelFile(); var transport = new WorkspaceFactory { FailCommit = true };
        var prepared = await Prepare(new() { Kind = CommandKind.Deploy, Target = new(fixture.Path, "fixture", "Visible name") }, new(), transport);
        var result = await prepared.ApplyAsync(prepared.Review.Hash, "reviewer", CancellationToken.None);
        Assert.AreEqual(CommandStatus.OutcomeUnknown, result.Status); Assert.AreEqual(6, result.ExitCode); Assert.AreEqual(1, transport.Executions);
        Assert.IsFalse(CommandJson.Serialize(result).Contains("private-secret"));
    }
    [TestMethod]
    public async Task MultilineTomSourceArrayNeverSeparatesASecretFromItsRedaction()
    {
        using var fixture = new ModelFile();
        var json = JsonSerializer.Serialize(new { name = "Visible name", id = "stable-id", compatibilityLevel = 1600, model = new { culture = "en-US", tables = new[] {
            new { name = "Facts", columns = new[] { new { name = "Value", dataType = "int64", sourceColumn = "Value" } }, partitions = new[] {
                new { name = "Current", mode = "import", source = new { type = "m", expression = new[] { "let", "ApiKey =", "\"fixture-multiline-private\",", "Data = #table({\"Value\"}, {{1}})", "in Data" } } }
            } }
        } } });
        File.WriteAllText(fixture.Path, json);
        var prepared = await Prepare(new() { Kind = CommandKind.Deploy, Target = new(fixture.Path, "fixture", "Visible name") }, new(), new());
        Assert.IsFalse(CommandJson.Serialize(prepared.Review).Contains("fixture-multiline-private"));
        Assert.IsTrue(prepared.Review.CommandText!.Contains("[redacted:"));
    }
    private sealed class ModelFile : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PbiBench-command-test-" + Guid.NewGuid().ToString("N"));
        internal ModelFile() { Directory.CreateDirectory(root); Path = System.IO.Path.Combine(root, "source.bim"); File.WriteAllText(Path, ModelJson("proposed")); }
        internal string Path { get; }
        public void Dispose() { var full = System.IO.Path.GetFullPath(root); if (System.IO.Path.GetDirectoryName(full) != System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar) || !System.IO.Path.GetFileName(full).StartsWith("PbiBench-command-test-", StringComparison.Ordinal)) throw new InvalidOperationException(); PbiBench.Workspace.WorkspaceDiskStore.RejectLinks(full); Directory.Delete(full, true); }
    }
    private static string ModelJson(string description) => JsonSerializer.Serialize(new { name = "Visible name", id = "stable-id", compatibilityLevel = 1600, model = new { culture = "en-US", description } });
    private sealed class WorkspaceFactory : IWorkspaceLiveSessionFactory
    {
        internal int Executions, Commits; internal bool FailCommit; internal string? LastCommand;
        public IWorkspaceLiveSession Create() => new Session(this);
        private sealed class Session(WorkspaceFactory owner) : IWorkspaceLiveSession
        {
            public void Open(WorkspaceConnection connection) { }
            public WorkspaceLiveCapture Capture() => new("stable-id", "Visible name", new TmdlWorkspaceCodec().Normalize(ModelJson(owner.Commits > 0 ? "proposed" : "before")));
            public void BeginTransaction() { }
            public void Execute(string tmsl) { owner.Executions++; owner.LastCommand = tmsl; }
            public void CommitTransaction() { if (owner.FailCommit) throw new IOException("Password=private-secret"); owner.Commits++; }
            public void RollbackTransaction() { }
            public void Cancel() { }
            public void Dispose() { }
        }
    }
    private sealed class RefreshFactory : IRefreshSessionFactory
    {
        internal RefreshMetadataSnapshot Snapshot = Metadata; internal int Executions, Disposed; internal RefreshConnection? LastTarget; internal bool FailOpen;
        public IRefreshSession Create() => new Session(this);
        private sealed class Session(RefreshFactory owner) : IRefreshSession
        {
            public void Open(RefreshConnection connection, int timeoutSeconds) { owner.LastTarget = connection; if (owner.FailOpen) throw new IOException("Password=private-secret"); }
            public RefreshMetadataSnapshot CaptureMetadata() => owner.Snapshot;
            public RefreshEngineResponse Execute(string approvedTmsl) { owner.Executions++; return new(false, Array.Empty<string>(), Array.Empty<string>()); }
            public void Cancel() { }
            public void Dispose() => owner.Disposed++;
        }
    }
}
