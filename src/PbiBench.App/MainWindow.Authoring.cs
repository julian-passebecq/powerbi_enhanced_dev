using System.Windows.Controls;
using PbiBench.Semantic.ModelAuthoring;

namespace PbiBench.App;

public partial class MainWindow
{
    private MetadataEditorsView? metadataEditors;
    private DaxAuthoringView? daxAuthoring;
    private void InitializeModelAuthoring()
    {
        var tabs = new TabControl();
        metadataEditors = new MetadataEditorsView(() => editor.Handler, () => Run(UpdateSessionAsync));
        metadataEditors.DaxQueryRequested += query => { daxWorkspace!.OpenQuery(query, "Calendar validation"); GoTo("DAX"); };
        daxAuthoring = new DaxAuthoringView(() => editor.Handler, () => Run(UpdateSessionAsync));
        tabs.Items.Add(new TabItem { Header = "DAX authoring", Content = daxAuthoring });
        tabs.Items.Add(new TabItem { Header = "Metadata editors", Content = metadataEditors });
        AuthoringPage.Content = tabs;
    }
    private void OpenAuthoring(object sender, System.Windows.RoutedEventArgs e) => GoTo("Model tools");
    private void OpenAuthoringTool(string tool, bool metadata)
    {
        GoTo("Model tools"); ((TabControl)AuthoringPage.Content).SelectedIndex = metadata ? 1 : 0;
        if (metadata) metadataEditors!.ShowTool(tool); else daxAuthoring!.ShowTool(tool);
    }
    private void AddAuthoringCommands(IDictionary<string, Action> entries)
    {
        foreach (var tool in new[] { "UDF workbench", "DAX scripts", "Find / replace", "DAX Explain" }) entries["Model tools · " + tool] = () => OpenAuthoringTool(tool, false);
        foreach (var tool in new[] { "Calendar", "Perspectives", "Translations" }) entries["Model tools · " + tool] = () => OpenAuthoringTool(tool, true);
    }
    private async Task RunAuthoringSmokeAsync(string outputRoot, List<string> checks)
    {
        var handler = editor.Handler!;
        var dax = new DaxAuthoringService(handler); var measure = handler.Model.Tables["Sales"].Measures["Revenue"];
        var selected = dax.GetObjects().Single(obj => obj.Name == "Revenue"); var before = measure.Expression;
        GoTo("Model"); editor.Select(measure);
        var nativeExpression = editor.View.Child.Controls.Find("txtExpression", true).Single();
        nativeExpression.Text = before + " + 0";
        Check(measure.Expression == before, "Native expression remains a draft before entering Model tools", checks);
        OpenAuthoringTool("DAX scripts", false);
        Check(measure.Expression == before + " + 0", "Entering Model tools accepts the pending native expression", checks);
        handler.UndoManager.Undo();
        var script = dax.ExportScript(new[] { selected.Id });
        var preview = dax.PreviewScript(script.Replace(before, before + " + 0"));
        Check(preview.CanApply && preview.Changes.Count == 1 && measure.Expression == before, "DAX script preview isolates one real model expression", checks);
        preview.Apply(handler); Check(measure.Expression == before + " + 0", "DAX script applies its reviewed expression", checks);
        handler.UndoManager.Undo(); Check(measure.Expression == before, "DAX script change is restored by one model undo", checks);
        var function = dax.PreviewFunction(new DaxFunctionEdit(null, "PbiBench.Smoke", "(value : NUMERIC) => value * 2"));
        Check(!function.CanApply && handler.CompatibilityLevel == 1600, "UDF authoring reports compatibility requirements without upgrading the model", checks);
        OpenAuthoringTool("UDF workbench", false); await PaintAsync(); Capture(outputRoot, "udf-workbench");
        OpenAuthoringTool("Calendar", true); await PaintAsync(); Capture(outputRoot, "calendar-editor");
        OpenAuthoringTool("DAX scripts", false); await PaintAsync(); Capture(outputRoot, "dax-scripts");
        var perspectives = new PerspectiveEditorService(handler); perspectives.PreviewCreate("Smoke perspective").Apply(handler);
        var perspective = perspectives.Capture(); var member = perspective.Members.Single(item => item.Kind == "Measure" && item.Name == "Revenue");
        perspectives.PreviewMembership(new[] { new PerspectiveMembershipChange(member.Id, "Smoke perspective", true) }).Apply(handler);
        Check(measure.InPerspective["Smoke perspective"], "Perspective authoring changes actual model membership", checks);
        OpenAuthoringTool("Perspectives", true); await PaintAsync(); Capture(outputRoot, "perspectives");
        Check(AuthoringVisuals(metadataEditors!).OfType<DataGrid>().Any(grid => !grid.IsReadOnly && grid.Columns.Any(column => Convert.ToString(column.Header) == "Smoke perspective")), "Entering Model tools refreshes the editable perspective matrix from native metadata", checks);
        handler.UndoManager.Undo(); handler.UndoManager.Undo();
        var translations = new TranslationEditorService(handler); translations.PreviewCreateCulture("fr-FR").Apply(handler);
        var translated = translations.Capture().Members.Single(item => item.Name == "Revenue" && item.Kind == "Measure");
        translations.PreviewCells(new[] { new TranslationCell(translated.Id, "fr-FR", TranslationProperty.Name, "Chiffre d’affaires") }).Apply(handler);
        Check(translations.Capture().Cells.Any(cell => cell.ObjectId == translated.Id && cell.Value == "Chiffre d’affaires"), "Translation editor changes a reviewed metadata cell", checks);
        OpenAuthoringTool("Translations", true); await PaintAsync(); Capture(outputRoot, "translations");
        Check(AuthoringVisuals(metadataEditors!).OfType<DataGrid>().Any(grid => !grid.IsReadOnly && grid.Columns.Any(column => Convert.ToString(column.Header) == "fr-FR")), "Entering Model tools refreshes the editable translation matrix", checks);
        handler.UndoManager.Undo(); handler.UndoManager.Undo();
        var table = handler.Model.Tables["Sales"]; var groups = new TableGroupService(handler);
        groups.PreviewAssign(new[] { table }, "Business facts").Apply(handler);
        Check(TableGroupService.Read(table).Group == "Business facts", "Table group assignment uses model annotations", checks);
        editor.Select(table); GoTo("Model diagram"); await PaintAsync(); Capture(outputRoot, "diagram-authoring"); handler.UndoManager.Undo();
        await UpdateSessionAsync(); GoTo("Model");
    }
    private static IEnumerable<System.Windows.DependencyObject> AuthoringVisuals(System.Windows.DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in AuthoringVisuals(child)) yield return nested;
        }
    }
}
