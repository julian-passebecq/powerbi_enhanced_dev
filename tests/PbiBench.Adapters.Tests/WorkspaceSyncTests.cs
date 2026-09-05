using PbiBench.Core.Domain;
using PbiBench.Core.Workspaces;
using PbiBench.Git;
using PbiBench.Workspace;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class WorkspaceSyncTests
{
    private static WorkspaceSemanticSnapshot Snapshot(string value, string? second = null) => WorkspaceSemanticSnapshot.Parse("{\"name\":\"fixture\",\"compatibilityLevel\":1702,\"model\":{\"tables\":[{\"name\":\"Sales\",\"lineageTag\":\"table-id\",\"measures\":[{\"name\":\"Revenue\",\"lineageTag\":\"measure-id\",\"expression\":\"" + value + "\"}" + (second == null ? "" : ",{\"name\":\"Other\",\"expression\":\"" + second + "\"}") + "]}]}}");
    [Theory]
    [InlineData("1", "1", 0, WorkspaceChangeKind.DiskOnly)]
    [InlineData("2", "1", 1, WorkspaceChangeKind.DiskOnly)]
    [InlineData("1", "2", 1, WorkspaceChangeKind.LiveOnly)]
    [InlineData("2", "2", 1, WorkspaceChangeKind.SameChange)]
    [InlineData("2", "3", 1, WorkspaceChangeKind.Conflict)]
    public void ThreeWayDiffDistinguishesIndependentAndDivergentEdits(string disk, string live, int count, WorkspaceChangeKind kind)
    { var result = WorkspaceSemanticDiff.Compare(Snapshot("1"), Snapshot(disk), Snapshot(live), 12, 9, true); Assert.Equal(count, result.Changes.Count); if (count > 0) Assert.Equal(kind, result.Changes[0].Kind); Assert.Equal(12, result.DiskSequence); Assert.True(result.HasUnsavedModelEdits); }
    [Fact]
    public void PropertyOrderingAndDatabaseIdentityDoNotChangeSemanticHash()
    { var before = Snapshot("1"); var text = before.DatabaseJson.Replace("\"name\":\"fixture\"", "\"id\":\"another-id\",\"name\":\"Another target\"").Replace("\"name\":\"Revenue\",\"lineageTag\":\"measure-id\"", "\"lineageTag\":\"measure-id\",\"name\":\"Revenue\""); Assert.Equal(before.Hash, WorkspaceSemanticSnapshot.Parse(text).Hash); }
    [Fact]
    public void StableLineageMakesRenameOnePropertyAndDeletionConflictsWithLiveChange()
    { var before = Snapshot("1"); var rename = WorkspaceSemanticSnapshot.Parse(before.DatabaseJson.Replace("Revenue", "Gross")); var diff = WorkspaceSemanticDiff.Between(before, rename); Assert.Single(diff); Assert.Equal("name", diff[0].Property); var deleted = WorkspaceSemanticSnapshot.Parse("{\"compatibilityLevel\":1702,\"model\":{\"tables\":[]}}"); Assert.True(WorkspaceSemanticDiff.Compare(before, deleted, Snapshot("3")).HasConflicts); }
    [Fact]
    public void NamedArraysIgnoreObjectOrderButOrderedPropertiesRetainOrder()
    { var first = Snapshot("1", "2"); var reordered = first.DatabaseJson.Replace("{\"name\":\"Revenue\",\"lineageTag\":\"measure-id\",\"expression\":\"1\"},{\"name\":\"Other\",\"expression\":\"2\"}", "{\"name\":\"Other\",\"expression\":\"2\"},{\"name\":\"Revenue\",\"lineageTag\":\"measure-id\",\"expression\":\"1\"}"); Assert.Equal(first.Hash, WorkspaceSemanticSnapshot.Parse(reordered).Hash); }
    [Fact]
    public void DuplicateObjectIdentitiesAndMalformedMetadataAreRejected()
    { Assert.Throws<ArgumentException>(() => WorkspaceSemanticSnapshot.Parse("{\"model\":{\"tables\":[{\"name\":\"A\",\"lineageTag\":\"duplicate\"},{\"name\":\"B\",\"lineageTag\":\"duplicate\"}]}}")); Assert.Throws<ArgumentException>(() => WorkspaceSemanticSnapshot.Parse("{}")); }
    [Fact]
    public async Task DiskApplyPreservesUnknownArtifactsAndBacksUpExactSemanticContent()
    {
        using var temp = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(temp.Root, "database.tmdl"), "old database"); File.WriteAllText(Path.Combine(temp.Root, "obsolete.tmdl"), "old object"); File.WriteAllText(Path.Combine(temp.Root, "notes.txt"), "keep me"); Directory.CreateDirectory(Path.Combine(temp.Root, "DAXQueries")); File.WriteAllText(Path.Combine(temp.Root, "DAXQueries", "one.dax"), "EVALUATE {1}");
        var store = new WorkspaceDiskStore(); var plan = store.Prepare(store.Capture(temp.Root), new[] { new WorkspaceFile("database.tmdl", "new database"), new WorkspaceFile("tables/New.tmdl", "new table") }); var result = await store.ApplyAsync(plan, Approve(plan), CancellationToken.None);
        Assert.Equal(3, result.ChangedFiles); Assert.Equal("new database", File.ReadAllText(Path.Combine(temp.Root, "database.tmdl"))); Assert.False(File.Exists(Path.Combine(temp.Root, "obsolete.tmdl"))); Assert.Equal("keep me", File.ReadAllText(Path.Combine(temp.Root, "notes.txt"))); Assert.Equal("EVALUATE {1}", File.ReadAllText(Path.Combine(temp.Root, "DAXQueries", "one.dax"))); Assert.Equal("old database", File.ReadAllText(Path.Combine(result.BackupDirectory, "database.tmdl"))); Assert.Equal(plan.After.Hash, store.Capture(temp.Root).Hash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(plan, Approve(plan), CancellationToken.None));
    }
    [Fact]
    public async Task StaleDiskAndTamperedApprovalCannotWriteOrDelete()
    {
        using var temp = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(temp.Root, "model.bim"), "old"); var store = new WorkspaceDiskStore(); var plan = store.Prepare(store.Capture(temp.Root), new[] { new WorkspaceFile("model.bim", "new") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(plan, new ApprovedChangePlan(plan.Plan with { Id = Guid.NewGuid() }, DateTimeOffset.UtcNow, "test"), CancellationToken.None)); File.WriteAllText(Path.Combine(temp.Root, "model.bim"), "external");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(plan, Approve(plan), CancellationToken.None)); Assert.Equal("external", File.ReadAllText(Path.Combine(temp.Root, "model.bim")));
    }
    [Fact]
    public async Task ConcurrentReplayClaimsOnePlanAndCreatesOneRecoveryBackup()
    {
        using var temp = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(temp.Root, "model.bim"), new string('a', 100000)); var store = new WorkspaceDiskStore(); var plan = store.Prepare(store.Capture(temp.Root), new[] { new WorkspaceFile("model.bim", "reviewed") }); var approval = Approve(plan);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ => { try { await store.ApplyAsync(plan, approval, CancellationToken.None); return true; } catch (InvalidOperationException) { return false; } }));
        Assert.Single(results, success => success); Assert.Equal("reviewed", File.ReadAllText(Path.Combine(temp.Root, "model.bim"))); Assert.Single(Directory.EnumerateDirectories(Path.Combine(temp.Root, ".pbibench", "workspace-backups")));
    }
    [Fact]
    public void ApprovalTimeAndTransientConnectionFieldsAreValidated()
    {
        var now = DateTimeOffset.UtcNow; var plan = new ChangePlan(Guid.NewGuid(), now, ApprovalLevel.WorkspaceWrite, new ResourceRef("pbip", null, null, "folder", "definition", "fixture"), Array.Empty<PlannedChange>(), "backup", "restore");
        Assert.Throws<InvalidOperationException>(() => WorkspaceApproval.Validate(plan, new(plan, now.AddMinutes(-1), "test"))); Assert.Throws<InvalidOperationException>(() => WorkspaceApproval.Validate(plan, new(plan, now.AddHours(1), "test"))); var expired = plan with { CreatedAt = now.AddMinutes(-31) }; Assert.Throws<InvalidOperationException>(() => WorkspaceApproval.Validate(expired, new(expired, now, "test")));
        var connection = new WorkspaceConnection("endpoint", "database", "Password=secret"); Assert.DoesNotContain("secret", connection.ToString()); Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(connection)); Assert.Throws<ArgumentException>(() => new WorkspaceConnection("endpoint;Password=secret", "database"));
    }
    [Fact]
    public async Task CancellationAfterFirstWriteRestoresPriorFiles()
    {
        using var temp = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(temp.Root, "a.tmdl"), "a"); File.WriteAllText(Path.Combine(temp.Root, "b.tmdl"), "b"); using var cancel = new CancellationTokenSource(); var store = new WorkspaceDiskStore(); var before = store.Capture(temp.Root); var plan = store.Prepare(before, new[] { new WorkspaceFile("a.tmdl", "new-a"), new WorkspaceFile("b.tmdl", "new-b") }); store.Progress += (done, _) => { if (done == 1) cancel.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ApplyAsync(plan, Approve(plan), cancel.Token)); Assert.Equal(before.Hash, store.Capture(temp.Root).Hash);
    }
    [Fact]
    public async Task RollbackNeverOverwritesAConcurrentNewerFile()
    {
        using var temp = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(temp.Root, "a.tmdl"), "a"); File.WriteAllText(Path.Combine(temp.Root, "b.tmdl"), "b"); using var cancel = new CancellationTokenSource(); var store = new WorkspaceDiskStore(); var plan = store.Prepare(store.Capture(temp.Root), new[] { new WorkspaceFile("a.tmdl", "new-a"), new WorkspaceFile("b.tmdl", "new-b") }); store.Progress += (done, _) => { if (done == 1) { File.WriteAllText(Path.Combine(temp.Root, "a.tmdl"), "new external edit"); cancel.Cancel(); } };
        var error = await Assert.ThrowsAsync<IOException>(() => store.ApplyAsync(plan, Approve(plan), cancel.Token)); Assert.Contains("Backup:", error.Message); Assert.Equal("new external edit", File.ReadAllText(Path.Combine(temp.Root, "a.tmdl"))); Assert.Equal("b", File.ReadAllText(Path.Combine(temp.Root, "b.tmdl")));
    }
    [Theory]
    [InlineData("../escape.tmdl")]
    [InlineData("C:/escape.tmdl")]
    [InlineData("tables/CON.tmdl")]
    [InlineData(".pbi/private.tmdl")]
    [InlineData("DAXQueries/keep.tmdl")]
    public void DiskPlanRejectsUnsafeOrPreservedPaths(string path)
    { using var temp = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(temp.Root, "model.tmdl"), "original"); var store = new WorkspaceDiskStore(); Assert.Throws<InvalidDataException>(() => store.Prepare(store.Capture(temp.Root), new[] { new WorkspaceFile(path, "bad") })); }
    [Fact]
    public async Task BaselinesAreBoundToProfileRootAndLiveTarget()
    { using var temp = new TemporaryWorkspace(); var first = new WorkspaceBaselineStore(temp.Root, temp.Root, "endpoint", "model"); await first.SaveAsync(Snapshot("1"), CancellationToken.None); Assert.Equal(Snapshot("1").Hash, (await first.LoadAsync(CancellationToken.None))!.Hash); Assert.Null(await new WorkspaceBaselineStore(temp.Root, temp.Root, "endpoint", "other").LoadAsync(CancellationToken.None)); }
    [Fact]
    public async Task WatcherInvalidatesWithoutWritingAndStopsAfterDisposal()
    { using var temp = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(temp.Root, "model.tmdl"), "old"); using var watcher = new WorkspaceWatcher(temp.Root, 50); var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); watcher.Changed += (_, _) => changed.TrySetResult(true); File.WriteAllText(Path.Combine(temp.Root, "model.tmdl"), "external"); Assert.Same(changed.Task, await Task.WhenAny(changed.Task, Task.Delay(5000))); Assert.True(watcher.Sequence > 0); watcher.Dispose(); Assert.Equal("external", File.ReadAllText(Path.Combine(temp.Root, "model.tmdl"))); }
    [Fact]
    public async Task WatcherIgnoresDirectoryTimestampsAndProtectedArtifacts()
    {
        using var temp = new TemporaryWorkspace(); var tables = Path.Combine(temp.Root, "tables"); Directory.CreateDirectory(tables); File.WriteAllText(Path.Combine(tables, "Sales.tmdl"), "table Sales");
        using var watcher = new WorkspaceWatcher(temp.Root, 50);
        Directory.SetLastWriteTimeUtc(tables, DateTime.UtcNow.AddMinutes(-1)); File.WriteAllText(Path.Combine(tables, "README"), "notes");
        foreach (var name in new[] { ".PbiBench", ".PBI", ".GIT", "daxqueries" }) { var directory = Path.Combine(temp.Root, name); Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "ignored.tmdl"), "metadata artifact"); }
        await Task.Delay(350); Assert.Equal(0, watcher.Sequence); Assert.Null(watcher.LastChange);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WatcherInvalidatesForDirectoryRenameOrDeletionIncludingDottedNames(bool delete)
    {
        using var temp = new TemporaryWorkspace(); var tables = Path.Combine(temp.Root, "tables.v1"); Directory.CreateDirectory(tables);
        using var watcher = new WorkspaceWatcher(temp.Root, 50); var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); watcher.Changed += (_, _) => changed.TrySetResult(true);
        if (delete) Directory.Delete(tables); else { var destination = Path.Combine(temp.Root, "tables.v2"); Assert.StartsWith(Path.GetFullPath(temp.Root) + Path.DirectorySeparatorChar, Path.GetFullPath(tables)); Assert.StartsWith(Path.GetFullPath(temp.Root) + Path.DirectorySeparatorChar, Path.GetFullPath(destination)); Directory.Move(tables, destination); }
        Assert.Same(changed.Task, await Task.WhenAny(changed.Task, Task.Delay(5000))); Assert.True(watcher.Sequence > 0);
    }
    [Fact]
    public async Task GitBaselineReadsPinnedBlobsWithoutCheckoutAndRejectsLinks()
    { using var temp = new TemporaryWorkspace(); var fake = new FakeGit(); var baseline = await new GitSemanticBaselineReader(fake).ReadAsync(temp.Root, temp.Root, false, CancellationToken.None); Assert.Single(baseline.Files); Assert.Equal("model text", baseline.Files[0].Content); Assert.DoesNotContain(fake.Commands, args => args.Contains("checkout") || args.Contains("reset")); fake.Link = true; await Assert.ThrowsAsync<InvalidDataException>(() => new GitSemanticBaselineReader(fake).ReadAsync(temp.Root, temp.Root, false, CancellationToken.None)); }
    private static ApprovedChangePlan Approve(WorkspaceDiskPlan plan) => new(plan.Plan, DateTimeOffset.UtcNow, "test");
    private sealed class FakeGit : IGitProcessRunner
    {
        public List<IReadOnlyList<string>> Commands { get; } = new(); public bool Link;
        public Task<GitResult> RunAsync(string root, IReadOnlyList<string> arguments, CancellationToken ct = default) { Commands.Add(arguments); return Task.FromResult(new GitResult(0, arguments[0] switch { "rev-parse" => new string('a', 40), "ls-tree" => (Link ? "120000" : "100644") + " blob " + new string('b', 40) + "\tmodel.tmdl\0", "cat-file" when arguments[1] == "-s" => "10", "cat-file" => "model text", _ => throw new InvalidOperationException() }, "")); }
    }
    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pbibench-workspace-test-" + Guid.NewGuid().ToString("N"));
        public TemporaryWorkspace() => Directory.CreateDirectory(Root);
        public void Dispose() { var full = Path.GetFullPath(Root); if (!string.Equals(Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("pbibench-workspace-test-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected cleanup path."); if (Directory.Exists(full)) Directory.Delete(full, true); }
    }
}
