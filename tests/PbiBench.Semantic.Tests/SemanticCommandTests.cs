using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Automation.Commands;
using PbiBench.Core.Automation;
using PbiBench.Core.Commands;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class SemanticCommandTests
{
    private static TabularModelHandler Fixture()
    { var handler = new TabularModelHandler(1702); var sales = handler.Model.AddTable("Sales"); sales.AddMeasure("Revenue", "1"); sales.AddMeasure("Other", "[Revenue] * 2"); handler.UndoManager.Clear(); return handler; }
    private static CommandRequest Edit => new() { Kind = CommandKind.Set, Selection = new[] { new CommandObject("Measure", "Revenue", "Sales") }, Property = "Description", Value = "Reviewed ü \"quoted\"" };
    [TestMethod]
    public void AsyncRecipeReturnsToOwnerAndAppliesOneUndoableExplicitEdit() => Owner(async () =>
    {
        using var handler = Fixture(); var owner = Environment.CurrentManagedThreadId; var service = new SemanticCommandService(() => handler); var before = new SemanticModelService(handler).Fingerprint();
        var prepared = await service.PrepareAsync(Edit); Assert.AreEqual(owner, Environment.CurrentManagedThreadId); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint()); Assert.AreEqual(1, prepared.Review.Changes.Count);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyAsync(prepared, "forged", "reviewer"));
        var result = await service.ApplyAsync(prepared, prepared.Review.Hash, "reviewer"); Assert.AreEqual(CommandStatus.Succeeded, result.Status);
        Assert.AreEqual(Edit.Value, handler.Model.Tables["Sales"].Measures["Revenue"].Description); Assert.AreEqual("", handler.Model.Tables["Sales"].Measures["Other"].Description); Assert.AreEqual(1, handler.UndoManager.UndoSteps);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyAsync(prepared, prepared.Review.Hash, "reviewer"));
        handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        Assert.AreSame(handler.Model, handler.Model.Tables["Sales"].AddMeasure("Still owned", "3").Model);
    });
    [TestMethod]
    public void StaleNativePlanAndWrongThreadNeverMutateModel() => Owner(async () =>
    {
        using var handler = Fixture(); var service = new SemanticCommandService(() => handler); var prepared = await service.PrepareAsync(Edit);
        handler.Model.Tables["Sales"].Description = "Intervening user edit";
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyAsync(prepared, prepared.Review.Hash, "reviewer"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Task.Run(() => service.ExecuteReadAsync(new() { Kind = CommandKind.Inspect })));
        Assert.AreEqual("", handler.Model.Tables["Sales"].Measures["Revenue"].Description); Assert.AreEqual("Intervening user edit", handler.Model.Tables["Sales"].Description);
    });
    [TestMethod]
    public void GetUsesExplicitObjectAndRejectsCredentialProperties() => Owner(async () =>
    {
        using var handler = Fixture(); var source = handler.Model.AddDataSource("Private"); source.ConnectionString = "Provider=MSOLEDBSQL;Password=private-secret;";
        var service = new SemanticCommandService(() => handler); var inspected = await service.ExecuteReadAsync(new() { Kind = CommandKind.Inspect }); Assert.IsFalse(CommandJson.Serialize(inspected).Contains("private-secret"));
        var result = await service.ExecuteReadAsync(Edit with { Kind = CommandKind.Get, Property = "Expression", Value = null }); Assert.AreEqual("1", result.Data!.Value[0].GetProperty("value").GetString());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ExecuteReadAsync(Edit with { Kind = CommandKind.Get, Property = "ConnectionString", Value = null }));
    });
    [TestMethod]
    public void SourceClaimMustMatchTheSingleLoadedModel() => Owner(async () =>
    {
        using var handler = Fixture(); using var folder = new Temp(); var path = Path.Combine(folder.Root, "foreign.bim"); File.WriteAllText(path, Microsoft.AnalysisServices.Tabular.JsonSerializer.SerializeDatabase(handler.Database));
        var service = new SemanticCommandService(() => handler);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.PrepareAsync(Edit with { Target = new(path) }));
        Assert.AreEqual("", handler.Model.Tables["Sales"].Measures["Revenue"].Description);
    });
    [TestMethod]
    public void SourceBoundReviewCannotOmitUnrelatedUnsavedEdits() => Owner(async () =>
    {
        using var folder = new Temp(); var path = Path.Combine(folder.Root, "source.bim");
        File.WriteAllText(path, "{\"name\":\"Fixture\",\"id\":\"fixture-id\",\"compatibilityLevel\":1600,\"model\":{\"culture\":\"en-US\",\"tables\":[{\"name\":\"Sales\",\"measures\":[{\"name\":\"Revenue\",\"expression\":\"1\"}]}]}}");
        using var handler = new TabularModelHandler(path); var service = new SemanticCommandService(() => handler); Assert.IsFalse(handler.HasUnsavedChanges);
        var clean = await service.PrepareAsync(Edit with { Target = new(path) }); Assert.IsTrue(clean.Review.CanApply);
        handler.Model.Tables["Sales"].Description = "Unrelated draft must not enter a disk-hash-only review"; Assert.IsTrue(handler.HasUnsavedChanges);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.PrepareAsync(Edit with { Target = new(path) }));
        // The GUI's owner-bound in-memory path remains available for an existing draft.
        var inMemory = await service.PrepareAsync(Edit); Assert.IsTrue(inMemory.Review.CanApply); Assert.AreEqual("", handler.Model.Tables["Sales"].Measures["Revenue"].Description);
    });
    [TestMethod]
    public void ReviewedOutputRejectsLockAndExternalChangesWithoutOverwriting()
    {
        using var folder = new Temp(); var path = Path.Combine(folder.Root, "output.bim"); File.WriteAllText(path, "original"); var output = CommandModelFiles.PrepareOutput(path)!;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Assert.ThrowsExactly<IOException>(() => CommandModelFiles.WriteOutput(output, "proposed", CancellationToken.None));
        Assert.AreEqual("original", File.ReadAllText(path)); File.WriteAllText(path, "external edit");
        Assert.ThrowsExactly<InvalidOperationException>(() => CommandModelFiles.WriteOutput(output, "proposed", CancellationToken.None)); Assert.AreEqual("external edit", File.ReadAllText(path));
        var missing = Path.Combine(folder.Root, "new.bim"); var newOutput = CommandModelFiles.PrepareOutput(missing)!; File.WriteAllText(missing, "another writer");
        Assert.ThrowsExactly<InvalidOperationException>(() => CommandModelFiles.WriteOutput(newOutput, "proposed", CancellationToken.None)); Assert.AreEqual("another writer", File.ReadAllText(missing));
    }
    [TestMethod]
    public void ExclusiveWriteRetainsRecoveryAndRevalidatesExpectedContent()
    {
        using var folder = new Temp(); var path = Path.Combine(folder.Root, "output.bim"); File.WriteAllText(path, "before ü"); var output = CommandModelFiles.PrepareOutput(path)!;
        var backup = CommandModelFiles.WriteOutput(output, "after", CancellationToken.None); Assert.AreEqual("before ü", File.ReadAllText(backup!)); Assert.AreEqual("after", File.ReadAllText(path));
        Assert.ThrowsExactly<InvalidOperationException>(() => PbiBench.Workspace.WorkspaceDiskStore.WriteReviewedFile(path, "before ü", "stale")); Assert.AreEqual("after", File.ReadAllText(path));
    }
    private static void Owner(Func<Task> action)
    {
        Exception? failure = null; var thread = new Thread(() =>
        {
            using var context = new Pump(); SynchronizationContext.SetSynchronizationContext(context);
            try { var task = action(); var deadline = DateTime.UtcNow.AddSeconds(30); while (!task.IsCompleted && DateTime.UtcNow < deadline) context.Tick(); if (!task.IsCompleted) throw new TimeoutException("Owner-thread test did not complete."); task.GetAwaiter().GetResult(); }
            catch (Exception error) { failure = error; }
            finally { TabularModelHandler.Cleanup(); SynchronizationContext.SetSynchronizationContext(null); }
        }) { IsBackground = true }; thread.SetApartmentState(ApartmentState.STA); thread.Start(); if (!thread.Join(TimeSpan.FromSeconds(35))) Assert.Fail("Owner-thread test hung."); if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class Pump : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();
        public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));
        internal void Tick() { if (queue.TryTake(out var work, 100)) work.Callback(work.State); }
        public void Dispose() => queue.Dispose();
    }
    private sealed class Temp : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "PbiBench-command-native-" + Guid.NewGuid().ToString("N"));
        internal Temp() => Directory.CreateDirectory(Root);
        public void Dispose() { var full = Path.GetFullPath(Root); if (Path.GetDirectoryName(full) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(full).StartsWith("PbiBench-command-native-", StringComparison.Ordinal)) throw new InvalidOperationException(); PbiBench.Workspace.WorkspaceDiskStore.RejectLinks(full); Directory.Delete(full, true); }
    }
}
