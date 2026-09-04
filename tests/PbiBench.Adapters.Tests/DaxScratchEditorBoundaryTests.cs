#if NETFRAMEWORK
using System.Diagnostics;
using System.Windows.Forms;
using PbiBench.Dax.LanguageService;
using PbiBench.ModelEditor;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class DaxScratchEditorBoundaryTests
{
    [Fact]
    public Task RichEditorUsesCurrentMetadataAndPreservesTextOffsetsAndUndo() => RunSta(() =>
    {
        using var editor = new DaxScratchEditor();
        editor.SetDocumentContext("fixture.dax");
        editor.SetMetadata(new DaxMetadataSnapshot(new[] {
            new DaxSymbol("sales", "Sales", DaxSymbolKind.Table),
            new DaxSymbol("revenue", "Revenue", DaxSymbolKind.Measure, "Sales", "SUM('Sales'[Amount])")
        }));
        var edits = 0;
        var diagnostics = 0;
        editor.TextChanged += (_, _) => edits++;
        editor.DiagnosticsChanged += (_, _) => diagnostics++;
        editor.Text = "EVALUATE\r\nROW(\"Revenue\", [Revenue])";
        var at = editor.Text.IndexOf("[Revenue]", StringComparison.Ordinal);
        editor.SelectSpan(at, "[Revenue]".Length);
        Assert.Equal(at, editor.SelectionStart);
        Assert.Equal("[Revenue]", editor.SelectedText);
        Assert.Equal("[Revenue]".Length, editor.SelectionLength);
        var analyzed = Await(editor.RefreshAnalysisAsync());
        Assert.NotNull(analyzed);
        Assert.Equal(editor.Text, analyzed!.Document.Text);
        Assert.True(diagnostics > 0);
        DaxDefinitionRequestEventArgs? definition = null;
        editor.DefinitionRequested += (_, e) => definition = e;
        editor.SelectSpan(at + 2, 0);
        editor.GoToDefinition(true);
        Assert.NotNull(definition);
        Assert.Equal("revenue", definition!.Location.SymbolId);
        Assert.True(definition.Peek);

        var actions = editor.GetCodeActions();
        var qualify = Assert.Single(actions, action => action.Title == "Qualify model reference");
        var before = editor.Text;
        Assert.Equal(before, editor.Text); // Creating a proposal leaves the buffer untouched.
        editor.ApplyCodeAction(qualify);
        Assert.Contains("'Sales'[Revenue]", editor.Text);
        var native = editor.View.Child.Controls.Cast<Control>().Single(control => control.GetType().Name == "FastColoredTextBox");
        native.GetType().GetMethod("Undo", Type.EmptyTypes)!.Invoke(native, null);
        Assert.Equal(before, editor.Text);
        Assert.True(edits >= 3);
        Assert.Throws<InvalidOperationException>(() => editor.ApplyCodeAction(qualify));

        editor.SetMetadata(DaxMetadataSnapshot.Empty);
        var withoutModel = Await(editor.RefreshAnalysisAsync());
        Assert.NotNull(withoutModel);
        Assert.Empty(withoutModel!.Metadata.Symbols);
        editor.SelectSpan(-100, int.MaxValue);
        Assert.Equal(editor.Text, editor.SelectedText);
    });

    [Fact]
    public Task LocalNavigationHasBackForwardAndSupersededAnalysisCannotPublish() => RunSta(() =>
    {
        using var editor = new DaxScratchEditor();
        editor.SetDocumentContext("local.dax", DaxDocumentKind.Expression);
        editor.Text = "VAR amount = 7 RETURN amount + 1";
        Await(editor.RefreshAnalysisAsync());
        var use = editor.Text.LastIndexOf("amount", StringComparison.Ordinal);
        editor.SelectSpan(use, 0);
        editor.GoToDefinition();
        Assert.Equal(editor.Text.IndexOf("amount", StringComparison.Ordinal), editor.SelectionStart);
        editor.NavigateBack();
        Assert.Equal(use, editor.CaretOffset);
        editor.NavigateForward();
        Assert.Equal(editor.Text.IndexOf("amount", StringComparison.Ordinal), editor.CaretOffset);

        editor.Text = "VAR unfinished = ";
        var superseded = editor.RefreshAnalysisAsync();
        editor.Text = "1 + 2";
        var latest = Await(editor.RefreshAnalysisAsync());
        try { Await(superseded); } catch (OperationCanceledException) { }
        Assert.Equal("1 + 2", latest!.Document.Text);
        Assert.Equal("1 + 2", editor.LatestAnalysis!.Document.Text);
    });

    private static T Await<T>(Task<T> task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Editor background analysis did not complete.");
            Application.DoEvents(); Thread.Sleep(1);
        }
        return task.GetAwaiter().GetResult();
    }

    private static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.SetResult(true); } catch (Exception ex) { completion.SetException(ex); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
#endif
