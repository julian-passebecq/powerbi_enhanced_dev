#if NETFRAMEWORK
using System.Reflection;
using System.Windows.Forms;
using PbiBench.Core.Commands;
using PbiBench.ModelEditor;
using Xunit;

namespace PbiBench.Adapters.Tests;

[Collection("Native TE2")]
public sealed class ModelEditorBoundaryTests
{
    [Fact]
    public Task MigratedCommandsAndCompactChromePreserveNativeEditorOperations() => RunSta(() =>
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Root, "model.bim");
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", "pass1-demo.bim"), path);
        using var editor = new Te2ModelEditor(() => true, Path.Combine(temp.Root, "profile"));
        Assert.Throws<InvalidOperationException>(() => editor.ShowLegacyCommands(false));
        Assert.True(editor.LegacyCommandsVisible);
        editor.Open(path);
        var routes = new WorkbenchCommandRegistry();
        var calls = new Dictionary<WorkbenchCommandId, int>();
        foreach (WorkbenchCommandId id in Enum.GetValues(typeof(WorkbenchCommandId)))
        {
            var command = id;
            routes.Register(command, () => calls[command] = calls.TryGetValue(command, out var count) ? count + 1 : 1);
        }
        editor.ConfigureCommands(routes);
        var form = editor.View.Child;
        var menu = NativeField<MenuStrip>(editor, "menuStrip1");
        var toolbar = NativeField<ToolStrip>(editor, "toolStrip2");
        var originalItemCount = toolbar.Items.Count;
        editor.ShowLegacyCommands(false);
        Assert.False(editor.LegacyCommandsVisible);
        Assert.False(menu.Visible);
        Assert.False(toolbar.Items["btnSave"].Available);
        Assert.True(toolbar.Items["cmbPerspective"].Available);
        Assert.True(toolbar.Items["cmbTranslation"].Available);
        Assert.True(toolbar.Items["txtFilter"].Available);
        foreach (var pair in new[] {
            ("actOpenFile", WorkbenchCommandId.Open), ("actOpenDB", WorkbenchCommandId.Connect),
            ("actSave", WorkbenchCommandId.Save), ("actOpenBPA", WorkbenchCommandId.RunBpa) })
        {
            ExecuteNativeAction(editor, pair.Item1);
            Assert.Equal(1, calls[pair.Item2]);
        }
        NativeField<ToolStripMenuItem>(editor, "bestPracticeAnalyzerToolStripMenuItem").PerformClick();
        Assert.Equal(2, calls[WorkbenchCommandId.RunBpa]);
        Assert.False(NativeField<Form>(editor, "BPAForm").Visible);

        var measure = editor.Handler!.Model.Tables["Sales"].Measures["Revenue"];
        editor.Select(measure);
        Assert.Same(measure, editor.Selection.Single());
        Assert.Equal(measure.Expression, editor.ActiveExpression);
        var context = NativeField<Control>(editor, "tvModel").ContextMenuStrip;
        typeof(ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context, new object[] { new System.ComponentModel.CancelEventArgs() });
        var analyze = context.Items["pbibenchAnalyzeDax"];
        Assert.True(analyze.Available);
        analyze.PerformClick();
        Assert.Equal(1, calls[WorkbenchCommandId.DaxStudio]);
        string? previewedTable = null;
        editor.RequestPreviewData = tableName => previewedTable = tableName;
        editor.Select(editor.Handler.Model.Tables["Sales"]);
        typeof(ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context, new object[] { new System.ComponentModel.CancelEventArgs() });
        Assert.False(analyze.Available);
        var previewData = context.Items["pbibenchPreviewData"];
        Assert.True(previewData.Available);
        previewData.PerformClick();
        Assert.Equal("Sales", previewedTable);
        editor.Select(measure);

        editor.ShowScriptEditor();
        Assert.Equal("pgCSharpScript", NativeField<TabControl>(editor, "tabCodeEditors").SelectedTab.Name);
        // Native text undo remains independent from model undo. ActiveControl represents
        // native editor focus in this unattached boundary fixture without desktop input.
        var scriptEditor = NativeField<Control>(editor, "txtAdvanced");
        scriptEditor.Text = "/* initial script */";
        scriptEditor.GetType().GetMethod("ClearUndo", Type.EmptyTypes)!.Invoke(scriptEditor, null);
        scriptEditor.GetType().GetMethod("SelectAll", Type.EmptyTypes)!.Invoke(scriptEditor, null);
        scriptEditor.GetType().GetMethod("InsertText", new[] { typeof(string) })!.Invoke(scriptEditor, new object[] { "/* revised script */" });
        var modelStepsBeforeTextUndo = editor.Handler.UndoManager.UndoSteps;
        ((Form)form).ActiveControl = scriptEditor;
        ExecuteNativeAction(editor, "actUndo");
        Assert.Equal("/* initial script */", scriptEditor.Text);
        ExecuteNativeAction(editor, "actRedo");
        Assert.Equal("/* revised script */", scriptEditor.Text);
        Assert.Equal(modelStepsBeforeTextUndo, editor.Handler.UndoManager.UndoSteps);
        editor.FocusExpressionEditor();
        Assert.Equal(0, NativeField<TabControl>(editor, "tabCodeEditors").SelectedIndex);
        editor.ShowDependencies();
        var ui = NativeField<object>(editor, "UI");
        var dependency = (Form)ui.GetType().GetField("DependencyForm", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(ui)!;
        Assert.True(dependency.Visible);
        dependency.Close();

        // A shell click is outside native focus. A stale expression ActiveControl must
        // not make the model command silently undo an unrelated text editor buffer.
        measure.Description = "first edit";
        measure.Description = "second edit";
        routes.Register(WorkbenchCommandId.Undo, editor.Undo);
        routes.Register(WorkbenchCommandId.Redo, editor.Redo);
        routes.Execute(WorkbenchCommandId.Undo);
        Assert.Equal("first edit", measure.Description);
        // The test host is unattached and cannot acquire OS focus; assign its native
        // active control to exercise the same tree context used by native shortcuts.
        ((Form)form).ActiveControl = NativeField<Control>(editor, "tvModel");
        ExecuteNativeAction(editor, "actRedo");
        Assert.Equal("second edit", measure.Description);
        ExecuteNativeAction(editor, "actUndo");
        Assert.Equal("first edit", measure.Description);
        routes.Execute(WorkbenchCommandId.Redo);
        Assert.Equal("second edit", measure.Description);
        routes.Register(WorkbenchCommandId.Save, editor.Save);
        ExecuteNativeAction(editor, "actSave");
        Assert.False(editor.Handler.HasUnsavedChanges);
        Assert.Contains("second edit", File.ReadAllText(path));

        editor.ShowLegacyCommands(true);
        Assert.True(menu.Visible);
        Assert.True(toolbar.Items["btnSave"].Available);
        Assert.Equal(originalItemCount, toolbar.Items.Count);
        Assert.False(form.IsDisposed);
        NativeField<Form>(editor, "BPAForm").Dispose();
    });

    [Fact]
    public Task NativeExitRequestsShellCloseAndCanceledReplacementKeepsDirtyModel() => RunSta(() =>
    {
        using var temp = new TemporaryDirectory();
        var first = Path.Combine(temp.Root, "first.bim");
        var second = Path.Combine(temp.Root, "second.bim");
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", "pass1-demo.bim"), first);
        File.Copy(first, second);
        var allowDiscard = false;
        var discardChecks = 0;
        var profile = Path.Combine(temp.Root, "profile");
        using var editor = new Te2ModelEditor(() => { discardChecks++; return allowDiscard; }, profile);
        Assert.True(editor.CanClose());
        editor.Open(first);
        Assert.NotNull(editor.Handler);
        Assert.True(editor.CanClose());
        Assert.True(editor.TreeRootCount > 0);
        Assert.True(File.Exists(Path.Combine(profile, "RecentFiles.json")));
        var initial = editor.Handler;
        initial.Model.Tables.First().Description = "Unsaved fixture change";
        Assert.True(initial.HasUnsavedChanges);
        Assert.False(editor.CanClose());
        editor.Open(second);
        Assert.Same(initial, editor.Handler);
        editor.Connect("must-not-attempt-network", "fixture");
        Assert.Same(initial, editor.Handler);
        editor.New();
        Assert.Same(initial, editor.Handler);
        ExecuteNativeAction(editor, "actNewModel");
        Assert.Same(initial, editor.Handler);
        Assert.Equal(5, discardChecks);

        var closeRequests = 0;
        editor.RequestClose = () => closeRequests++;
        ExecuteNativeAction(editor, "actExit");
        Assert.Equal(1, closeRequests);
        Assert.False(editor.View.Child.IsDisposed);
        Assert.Same(initial, editor.Handler);

        allowDiscard = true;
        editor.Open(second);
        Assert.NotSame(initial, editor.Handler);
        Assert.Equal(second, editor.FilePath);
        Assert.True(editor.CanClose());

        // Exercise the complete upstream BPA window, including its actual model binding and scan.
        var hostedForm = editor.View.Child;
        var bpa = (Form)hostedForm.GetType().BaseType!.GetField("BPAForm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hostedForm)!;
        try
        {
            editor.ShowNativeBpa();
            Assert.True(bpa.Visible);
            Assert.Same(editor.Handler!.Model, bpa.GetType().GetProperty("Model")!.GetValue(bpa));
            bpa.GetType().GetMethod("AnalyzeAll", Type.EmptyTypes)!.Invoke(bpa, null);
            bpa.Close();
            Assert.False(bpa.Visible);
            Assert.False(editor.View.Child.IsDisposed);
        }
        finally
        {
            // Upstream Close hides BPA for reuse; explicitly dispose this fixture's auxiliary form.
            bpa.Dispose();
        }
    });

    private static void ExecuteNativeAction(Te2ModelEditor editor, string name)
    {
        var form = editor.View.Child;
        var action = form.GetType().BaseType!.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(form)!;
        action.GetType().GetMethod("DoExecute", Type.EmptyTypes)!.Invoke(action, null);
    }

    private static T NativeField<T>(Te2ModelEditor editor, string name) where T : class =>
        (T)editor.View.Child.GetType().BaseType!.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(editor.View.Child)!;

    private static Task RunSta(Action body)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { body(); result.SetResult(true); }
            catch (Exception error) { result.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }
}
#endif
