using System.IO;
using System.Windows;
using System.Windows.Threading;
using PbiBench.App;
using PbiBench.CSharp.LanguageService;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;
using Xunit;

namespace PbiBench.App.Tests;
public sealed class V11WorkspaceTests
{
    [Fact] public Task UnopenedWorkspaceCannotOverwriteExistingRecovery() => Sta(async () =>
    {
        var path = Path.GetTempFileName();
        try
        {
            var original = new ScriptDocument(Guid.NewGuid().ToString(), "original.csx", "precious unsaved text");
            await ScriptWorkspaceFiles.SaveRecoveryAsync(path, new(new[] { original }, original.Id), default);
            using (var view = new CSharpWorkspaceView("default example")) { view.Configure(path, () => Array.Empty<AutomationSymbol>()); await view.SaveRecoveryAsync(); }
            Assert.Equal("precious unsaved text", (await ScriptWorkspaceFiles.LoadRecoveryAsync(path, default)).Documents[0].Text);
        }
        finally { File.Delete(path); }
    });
    [Fact] public Task ScriptTabsRecoverDirtyTextWithoutExecution() => Sta(async () =>
    {
        var path = Path.GetTempFileName(); File.Delete(path);
        try
        {
            using (var view = new CSharpWorkspaceView("original")) { view.Configure(path, () => Array.Empty<AutomationSymbol>()); view.NewDocument("unsaved second"); Assert.Equal(2, view.DocumentCount); Assert.True(view.ActiveDirty); await view.SaveRecoveryAsync(); }
            var recovery = await ScriptWorkspaceFiles.LoadRecoveryAsync(path, default); Assert.Equal(2, recovery.Documents.Count); Assert.Equal("unsaved second", recovery.Documents.Single(d => d.Id == recovery.ActiveId).Text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    });
    [Fact] public Task ExportReviewStartsWithoutSamplesAndDoesNotWriteOrMutateModel() => Sta(async () =>
    {
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("Sales"); table.AddDataColumn("Amount"); table.AddMeasure("Revenue", "SUM('Sales'[Amount])");
        var before = new SemanticModelService(handler).Fingerprint(); var view = new AIContextExportWindow(AIContextCapture.Capture(handler), Array.Empty<string>(), null);
        await view.PrepareAsync(); Assert.NotNull(view.CurrentPlan); Assert.DoesNotContain(view.CurrentPlan!.Review, f => f.Path.StartsWith("samples/")); Assert.Equal(before, new SemanticModelService(handler).Fingerprint()); view.Close();
    });
    private static Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher)); dispatcher.BeginInvoke(new Action(async () => { try { await action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } })); Dispatcher.Run(); });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
