using System.Windows;
using System.Windows.Controls;
using PbiBench.Dax.LanguageService;
using PbiBench.ModelEditor;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public partial class MainWindow
{
    private DaxWorkspaceView? daxWorkspace;
    private string? semanticWorkspaceRoot;
    private FrameworkElement ScratchSurface => scratch.View;

    private void InitializeDaxWorkspace()
    {
        daxWorkspace = new DaxWorkspaceView(initialScratch, settingsDirectory,
            () => editor.Handler == null ? DaxMetadataSnapshot.Empty : DaxMetadataSnapshotProvider.Capture(editor.Handler),
            () => (editor.Server, editor.Server == null ? null : editor.Database), () => semanticWorkspaceRoot,
            NavigateDaxSymbol, Log, queryConnectionString: () => editor.Handler?.IsConnected == true ? editor.Handler.Database.Server.ConnectionString : null);
        DaxWorkspaceSurface.Content = daxWorkspace;
    }

    private void OpenRichExpression()
    {
        RequireModel();
        if (editor.Selection.FirstOrDefault() is not IExpressionObject expression || expression is not TabularNamedObject selected)
            throw new InvalidOperationException("Select a measure, calculated column/table, calculation item or function with a DAX expression.");
        var owner = editor.Handler!;
        var before = expression.Expression;
        daxWorkspace!.OpenExpression(selected.Name, before ?? "", after =>
        {
            if (!ReferenceEquals(owner, editor.Handler) || selected.IsRemoved || expression.Expression != before)
                throw new InvalidOperationException("The model expression changed or this tab belongs to a previous model session. Open the selected expression again before applying.");
            if (after == before) { Log("Expression is unchanged."); return; }
            var rows = new[] { new PreviewRow(SemanticModelService.ObjectPath(selected), "Expression", before ?? "", after, "Replace the selected object's expression. Engine evaluation is available through Run; local diagnostics are advisory.") };
            if (!PreviewDialog.Show(this, "Apply DAX expression", "Review the exact expression change. One model undo batch will be recorded; Save remains separate.", rows, true, "Apply to model")) return;
            owner.BeginUpdate("PbiBench: edit DAX expression");
            try
            {
                expression.Expression = after;
                if (expression.Expression != after) throw new InvalidOperationException("The model did not retain the reviewed expression.");
                owner.EndUpdate(); before = after;
            }
            catch { if (owner.UndoManager.BatchDepth > 0) owner.EndUpdateAll(rollback: true); throw; }
            UpdateModelStatus(); UpdateSelection(); Log("Applied DAX expression locally; Undo restores the preceding expression.");
        }, (selected as ITabularTableObject)?.Table.Name, selected is CalculatedTable);
        GoTo("DAX");
    }

    private void NavigateDaxSymbol(DaxSymbolLocation location, bool peek)
    {
        if (peek)
        {
            var text = new TextBox { Text = location.Name + "\n\n" + location.Description + "\n\n" + location.Expression, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14), FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            var window = new Window { Title = "Peek · " + location.Name, Icon = Icon, Owner = this, Width = 680, Height = 390, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = text };
            window.ShowDialog(); return;
        }
        if (editor.Handler == null) return;
        var selected = DaxMetadataSnapshotProvider.Resolve(editor.Handler, location);
        if (selected != null) { editor.Select(selected); GoTo("Model"); }
    }
}
