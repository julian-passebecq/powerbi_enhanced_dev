using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Reflection;
using PbiBench.AI.ContextExport;
using PbiBench.App;
using PbiBench.CSharp.LanguageService;
using PbiBench.Semantic;
using PbiBench.ModelEditor;
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
    [Fact] public Task RestoredWorkspaceShowsDetachedDraftAndSignalsTrustInvalidation() => Sta(async () =>
    {
        var path = Path.GetTempFileName(); var source = Path.GetTempFileName();
        try
        {
            File.WriteAllText(source, "original"); var original = (await ScriptWorkspaceFiles.OpenAsync(source, default)) with { Text = "precious draft" };
            await ScriptWorkspaceFiles.SaveRecoveryAsync(path, new(new[] { original }, original.Id), default); File.WriteAllText(source, "external");
            using var view = new CSharpWorkspaceView("example"); var changed = 0; view.TextChanged += (_, _) => changed++;
            view.Configure(path, () => Array.Empty<AutomationSymbol>()); await view.RestoreAsync(default);
            Assert.Equal("precious draft", view.Text); Assert.True(view.ActiveDirty); Assert.True(view.ActiveDocument.IsRecovered); Assert.Null(view.ActiveDocument.FilePath); Assert.True(changed > 0);
            var tabs = Field<TabControl>(view, "tabs"); Assert.Contains("Recovered · detached", ((TabItem)tabs.SelectedItem).Header.ToString());
            await Assert.ThrowsAsync<ScriptFileConflictException>(() => ScriptWorkspaceFiles.SaveAsync(view.ActiveDocument, source, default)); Assert.Equal("external", File.ReadAllText(source));
        }
        finally { File.Delete(path); File.Delete(source); }
    });
    [Fact] public Task TreeSelectionInvalidatesAlreadySelectedReviewedPlan() => Sta(async () =>
    {
        var model = ExportModel(); var view = new AIContextExportWindow(model, Array.Empty<string>(), null);
        try
        {
            view.UseTreeSelection(new[] { model.Objects[0].Id }); await Review(view);
            // Selected scope is already checked, which used to bypass invalidation.
            view.UseTreeSelection(new[] { model.Objects[1].Id }); AssertInvalidated(view);
            await Review(view); view.UseTreeSelection(new[] { model.Objects[1].Id }); AssertInvalidated(view);
        }
        finally { view.Close(); }
    });
    [Theory] [InlineData("sample")] [InlineData("sampleReview")] [InlineData("roles")] [InlineData("automation")] [InlineData("selected")]
    [InlineData("bpa")] [InlineData("metrics")] [InlineData("tests")] [InlineData("workspace")]
    public Task ProgrammaticAndRoutedCheckboxChangesClearExactReview(string field) => Sta(async () =>
    {
        var view = new AIContextExportWindow(ExportModel(), Array.Empty<string>(), null);
        try
        {
            if (field == "sample") Field<CheckBox>(view, "sampleReview").IsChecked = true;
            await Review(view); var check = Field<CheckBox>(view, field); check.IsChecked = true; AssertInvalidated(view);
            await Review(view); check.IsChecked = false; AssertInvalidated(view);
            await Review(view); check.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent)); AssertInvalidated(view);
        }
        finally { view.Close(); }
    });
    [Theory] [InlineData("Include")] [InlineData("Exclude")] [InlineData("Sample")] [InlineData("Rows")]
    [InlineData("IncludeHidden")] [InlineData("OrderColumn")] [InlineData("maximumBytes")] [InlineData("maximumRows")] [InlineData("maximumCells")]
    public Task BoundScopeSampleAndLimitChangesClearExactReview(string setting) => Sta(async () =>
    {
        var view = new AIContextExportWindow(ExportModel(), Array.Empty<string>(), null);
        try
        {
            await Review(view);
            var obj = Field<List<AIContextExportWindow.ObjectChoice>>(view, "objects")[1]; var table = Field<List<AIContextExportWindow.TableChoice>>(view, "tables")[0];
            switch (setting)
            {
                case "Include": obj.Include = false; break; case "Exclude": obj.Exclude = true; break; case "Sample": obj.Sample = false; break;
                case "Rows": table.Rows = 5; break; case "IncludeHidden": table.IncludeHidden = true; break; case "OrderColumn": table.OrderColumn = "Amount"; break;
                default: Field<TextBox>(view, setting).Text = "10"; break;
            }
            AssertInvalidated(view);
        }
        finally { view.Close(); }
    });
    [Fact] public Task SettingsChangeDuringSamplingCannotPublishAStalePlan() => Sta(async () =>
    {
        var sampler = new DelayedSampler(); var view = new AIContextExportWindow(ExportModel(), Array.Empty<string>(), sampler);
        try
        {
            Field<CheckBox>(view, "sample").IsChecked = true; Field<CheckBox>(view, "sampleReview").IsChecked = true;
            Field<List<AIContextExportWindow.TableChoice>>(view, "tables")[0].Rows = 1;
            var prepare = view.PrepareAsync();
            Assert.Same(sampler.Started.Task, await Task.WhenAny(sampler.Started.Task, Task.Delay(10000)));
            Field<CheckBox>(view, "roles").IsChecked = true;
            sampler.Result.SetResult(new SampleResult(new[] { "Amount" }, new[] { new object?[] { 1 } }));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepare); AssertInvalidated(view);
        }
        finally { sampler.Result.TrySetResult(new SampleResult(new[] { "Amount" }, Array.Empty<object?[]>())); view.Close(); }
    });
    [Fact] public Task EscapedCompletionCompilesWithExistingTe2CompilerWithoutExecuting() => Sta(() =>
    {
        var language = new CSharpLanguageService(); const string name = "Sales \"quoted\" \\ [日本] Zürich 🧮\n";
        var source = "Model.Tables[\""; var table = Assert.Single(language.Complete(source, source.Length, new[] { new AutomationSymbol("Table", name) }));
        var script = "var t = " + source + table.Text + "\"];\n";
        foreach (var kind in new[] { "Column", "Measure" })
        {
            var prefix = "Model.Tables[\"" + table.Text + "\"]." + kind + "s[\"";
            var completion = Assert.Single(language.Complete(prefix, prefix.Length, new[] { new AutomationSymbol(kind, name, name) }));
            script += "var " + kind.ToLowerInvariant() + " = " + prefix + completion.Text + "\"];\n";
        }
        Assert.DoesNotContain(TrustedScriptRunner.Validate(script), d => !d.IsWarning); return Task.CompletedTask;
    });
    private static ContextModel ExportModel() => new("Fixture", 1600, new[] {
        new ContextObject(ContextModel.ObjectId("Table", null, "Sales"), "Table", "Sales"),
        new ContextObject(ContextModel.ObjectId("Column", "Sales", "Amount"), "Column", "Amount", "Sales") }, Array.Empty<ContextRelationship>(), Array.Empty<ContextDependency>());
    private static async Task Review(AIContextExportWindow view)
    {
        await view.PrepareAsync(); Assert.NotNull(view.CurrentPlan); Assert.NotNull(Field<DataGrid>(view, "files").ItemsSource);
        Assert.NotEmpty(Field<TextBox>(view, "content").Text); Field<CheckBox>(view, "reviewed").IsChecked = true;
    }
    private static void AssertInvalidated(AIContextExportWindow view)
    { Assert.Null(view.CurrentPlan); Assert.False(Field<CheckBox>(view, "reviewed").IsChecked); Assert.Null(Field<DataGrid>(view, "files").ItemsSource); Assert.Empty(Field<TextBox>(view, "content").Text); }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private sealed class DelayedSampler : IContextSampler
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SampleResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SampleResult> SampleAsync(SampleRequest request, CancellationToken ct) { Started.TrySetResult(true); return Result.Task; }
    }
    private static Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher)); dispatcher.BeginInvoke(new Action(async () => { try { await action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } })); Dispatcher.Run(); });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
