using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using PbiBench.App;
using PbiBench.Core.Automation;
using PbiBench.CSharp.LanguageService;
using PbiBench.ModelEditor;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class CSharpAutomationTests
{
    [Fact] public Task CompilerProblemsNavigateTheCorrectScriptAndRejectStaleSourceWithoutExecution() => Sta(() =>
    {
        using var handler = new TabularModelHandler(1600); var before = new SemanticModelService(handler).Fingerprint();
        using var view = new CSharpWorkspaceView("// first\r\nModel.NoSuchMethod();");
        var id = view.ActiveDocument.Id; var diagnostics = TrustedScriptRunner.Validate(view.Text);
        Assert.Contains(diagnostics, d => d.Line == 2 && d.Column > 0 && !d.IsWarning);
        view.SetDiagnostics(diagnostics); var problem = view.Problems.First(p => !p.Diagnostic.IsWarning);
        var grid = Field<DataGrid>(view, "problems"); Assert.Equal(new[] { "Script", "Severity", "Code", "Line", "Column", "Message" }, grid.Columns.Select(c => c.Header));
        view.NewDocument("// second draft"); grid.SelectedItem = problem;
        Assert.Equal(id, view.ActiveDocument.Id); Assert.InRange(view.CaretOffset, "// first\r\n".Length, view.Text.Length);
        view.Text = "// edited"; Assert.False(view.NavigateProblem(problem)); Assert.Equal("// edited", view.Text);
        Assert.Equal(before, new SemanticModelService(handler).Fingerprint()); return Task.CompletedTask;
    });
    [Fact] public Task EveryGeneratedSafeSnippetProducesARealDetachedPreviewAndAllSnippetsCompile() => Sta(() =>
    {
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("Sales"); var column = table.AddDataColumn("Amount"); column.DataType = DataType.Int64; var measure = table.AddMeasure("Revenue", "SUM('Sales'[Amount])");
        var before = new SemanticModelService(handler).Fingerprint();
        foreach (var snippet in SemanticSnippets.All)
        {
            var symbol = snippet.SelectionKind switch { "Column" => new AutomationSymbol("Column", column.Name, table.Name, true, "Int64"), "Table" => new AutomationSymbol("Table", table.Name, Selected: true), _ => new AutomationSymbol("Measure", measure.Name, table.Name, true) };
            var generated = SemanticSnippets.Generate(snippet, new[] { symbol }); Assert.True(generated.Enabled);
            Assert.DoesNotContain(TrustedScriptRunner.Validate(generated.Source), d => !d.IsWarning);
            if (!generated.TrustedOnly)
            {
                var parsed = SafeCSharpParser.Parse(generated.Source); Assert.True(parsed.IsValid, generated.Source);
                var service = new ScriptPreviewService(handler); var preview = service.PreviewScript(generated.Source, Array.Empty<TabularNamedObject>());
                Assert.True(preview.CanApply, generated.Source); Assert.NotEmpty(preview.Changes);
            }
        }
        Assert.Equal(before, new SemanticModelService(handler).Fingerprint()); return Task.CompletedTask;
    });
    [Fact] public Task MacroContextIsCheckedAtLoadAndAgainBeforePreviewAndTrustedExecution() => Sta(async () =>
    {
        var settings = Path.Combine(Path.GetTempPath(), "PbiBench-context-" + Guid.NewGuid().ToString("N"));
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("Sales"); var measure = table.AddMeasure("Revenue", "1"); var column = table.AddDataColumn("Key");
        IReadOnlyList<TabularNamedObject> selection = new[] { measure }; var before = new SemanticModelService(handler).Fingerprint();
        using var view = new ScriptAutomationView(() => handler, () => selection, () => { }, settingsDirectory: settings);
        var macro = new ScriptMacro(Guid.NewGuid().ToString(), "Measures only", MacroMode.SafeScript, "foreach (var m in Selected.Measures) { m.Description = \"Review\"; }") { Context = new(new[] { "Measure" }, 1, 2) };
        view.LoadMacroDraft(macro); selection = new[] { column };
        Field<List<ScriptMacro>>(view, "library").Add(macro); view.RefreshModel();
        var row = Assert.Single(Field<DataGrid>(view, "macros").Items.Cast<ScriptAutomationView.MacroRow>());
        Assert.False(row.Enabled); Assert.Contains("Allowed selection", row.Reason); Assert.False(Field<Button>(view, "loadMacroButton").IsEnabled);
        var safe = Field<CSharpWorkspaceView>(view, "safeSource"); Assert.Throws<InvalidOperationException>(() => view.CheckMacroContext(safe));
        await Assert.ThrowsAsync<InvalidOperationException>(() => view.PrepareSafePreviewAsync(macro.Source)); Assert.Null(view.LastPreview);
        Assert.Throws<InvalidOperationException>(() => view.LoadMacroDraft(macro));
        selection = new[] { measure }; view.LoadMacroDraft(macro with { Mode = MacroMode.TrustedLegacy }); selection = Array.Empty<TabularNamedObject>();
        var trusted = Field<CSharpWorkspaceView>(view, "trustedSource"); Assert.Throws<InvalidOperationException>(() => view.CheckMacroContext(trusted));
        Assert.False(Field<CheckBox>(view, "trust").IsChecked);
        Field<CheckBox>(view, "trust").IsChecked = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => (Task)view.GetType().GetMethod("RunTrustedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null)!);
        Assert.False(Directory.Exists(Path.Combine(settings, "TrustedScriptSnapshots"))); Assert.Equal(before, new SemanticModelService(handler).Fingerprint());
        // Detached recovery snapshots remain drafts; none are execution approvals.
    });
    [Fact] public Task AdvancedGalleryDraftsCompileButInsertionNeverGrantsTrustOrRuns() => Sta(() =>
    {
        using var handler = new TabularModelHandler(1702); var table = handler.Model.AddTable("Sales"); var measure = table.AddMeasure("Revenue", "1");
        var before = new SemanticModelService(handler).Fingerprint();
        using var view = new ScriptAutomationView(() => handler, () => new[] { measure }, () => { });
        foreach (var card in PowerBiGallery.All.Where(c => c.ExecutionMode == GalleryExecutionMode.TrustedDraft))
        {
            var source = PowerBiGallery.GenerateDraft(card, new[] { new AutomationSymbol("Measure", measure.Name, table.Name, true) }, new Dictionary<string, string>());
            var diagnostics = TrustedScriptRunner.Validate(source); Assert.DoesNotContain(diagnostics, d => !d.IsWarning);
            Field<CheckBox>(view, "trust").IsChecked = true; view.InsertGalleryDraft(source);
            Assert.Equal(source.Replace("\r\n", "\n"), Field<CSharpWorkspaceView>(view, "trustedSource").Text.Replace("\r\n", "\n"));
            Assert.False(Field<CheckBox>(view, "trust").IsChecked); Assert.Null(Field<string?>(view, "compiledTrustedSource"));
            Assert.Equal(before, new SemanticModelService(handler).Fingerprint());
        }
        return Task.CompletedTask;
    });
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher)); dispatcher.BeginInvoke(new Action(async () => { try { await action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } })); Dispatcher.Run(); });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
