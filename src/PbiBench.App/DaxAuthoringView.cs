using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PbiBench.Dax.LanguageService;
using PbiBench.ModelEditor;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

/// <summary>Original DAX authoring workflows; every model mutation opens the common exact-change review.</summary>
public sealed class DaxAuthoringView : UserControl, IDisposable
{
    private readonly Func<TabularModelHandler?> currentHandler;
    private readonly Action changed;
    private readonly TabControl tabs = new();
    private readonly DaxScratchEditor functionEditor = new(), scriptEditor = new(), explainEditor = new();
    private readonly ComboBox functions = new() { MinWidth = 220, MaxWidth = 480 }, objects = new() { MinWidth = 250, MaxWidth = 560 };
    private readonly TextBox functionName = new() { MinWidth = 200 }, description = new() { AcceptsReturn = true, MinHeight = 44, MaxHeight = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly CheckBox hidden = new() { Content = "Hidden", Margin = new Thickness(8) };
    private readonly TextBox arguments = new() { MinWidth = 160, ToolTip = "DAX argument expressions separated by commas" };
    private readonly ComboBox valueKind = new() { ItemsSource = new[] { "Scalar", "Table" }, SelectedIndex = 0, MinWidth = 90 };
    private readonly ComboBox functionValueKind = new() { ItemsSource = new[] { "Scalar", "Table" }, SelectedIndex = 0, MinWidth = 90 };
    private readonly TextBox filterContext = new() { MinWidth = 230, ToolTip = "Optional comma-separated CALCULATE filter arguments, such as 'Date'[Year] = 2026" };
    private readonly TextBox pattern = new() { MinWidth = 180 }, replacement = new() { MinWidth = 180 };
    private readonly CheckBox regex = new() { Content = "Regex" }, matchCase = new() { Content = "Match case" }, wholeWord = new() { Content = "Whole word" }, descriptions = new() { Content = "Descriptions" };
    private readonly DataGrid scriptEntries = Grid(), matches = Grid();
    private readonly TextBox explanation = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TreeView dependencyTree = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly DataQueryView query;
    private readonly Dictionary<string, DaxFunctionEdit> drafts = new(StringComparer.OrdinalIgnoreCase);
    private TabularModelHandler? handler;
    private string? selectedFunction;
    private bool refreshing, disposed;
    private DaxScriptParseResult? parsed;

    public IReadOnlyList<DaxScratchEditor> VisibleEditors => !IsVisible ? Array.Empty<DaxScratchEditor>() : tabs.SelectedIndex switch
    { 0 => new[] { functionEditor }, 1 => new[] { scriptEditor }, 3 => new[] { explainEditor }, _ => Array.Empty<DaxScratchEditor>() };

    public void ShowTool(string tool)
    {
        var page = tabs.Items.Cast<TabItem>().FirstOrDefault(item => string.Equals(Convert.ToString(item.Header), tool, StringComparison.OrdinalIgnoreCase));
        if (page == null) throw new ArgumentException("Unknown DAX authoring tool: " + tool, nameof(tool));
        tabs.SelectedItem = page;
    }

    public DaxAuthoringView(Func<TabularModelHandler?> currentHandler, Action changed)
    {
        this.currentHandler = currentHandler; this.changed = changed;
        query = new DataQueryView(QueryTarget,
            () => handler?.IsConnected == true ? handler.Database.Server.ConnectionString : null, new TomDaxQueryService());
        var root = new DockPanel(); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(tabs); Content = root;
        tabs.Items.Add(new TabItem { Header = "UDF workbench", Content = FunctionsPage() });
        tabs.Items.Add(new TabItem { Header = "DAX scripts", Content = ScriptsPage() });
        tabs.Items.Add(new TabItem { Header = "Find / replace", Content = SearchPage() });
        tabs.Items.Add(new TabItem { Header = "DAX Explain", Content = ExplainPage() });
        functions.SelectionChanged += (_, _) => { if (!refreshing) Guard(() => { StoreDraft(); LoadFunction(functions.SelectedItem as DaxAuthoringObject); }); };
        objects.SelectionChanged += (_, _) => { if (!refreshing && objects.SelectedItem is DaxAuthoringObject item) { explainEditor.SetDocumentContext("explain:" + item.Id, item.Kind == DaxScriptObjectKind.Function ? DaxDocumentKind.Function : DaxDocumentKind.Expression, item.Table); explainEditor.Text = item.Expression; query.Invalidate(); } };
        scriptEditor.TextChanged += (_, _) => { parsed = null; scriptEntries.ItemsSource = null; };
        explainEditor.TextChanged += (_, _) => query.Invalidate();
        filterContext.TextChanged += (_, _) => query.Invalidate(); valueKind.SelectionChanged += (_, _) => query.Invalidate();
        scriptEntries.SelectionChanged += (_, _) => { if (scriptEntries.SelectedItem is DaxScriptEntry entry) scriptEditor.SelectSpan(entry.ExpressionSpan.Start, entry.ExpressionSpan.Length); };
        foreach (var editor in new[] { functionEditor, scriptEditor, explainEditor })
        {
            editor.DefinitionRequested += (_, e) => Guard(() => { var item = Service().GetObjects().FirstOrDefault(o => o.Name == e.Location.Name && o.Kind.ToString() == e.Location.Kind.ToString()); if (item != null) { tabs.SelectedIndex = 3; objects.SelectedItem = objects.Items.Cast<DaxAuthoringObject>().FirstOrDefault(o => o.Id == item.Id); Explain(); } });
            editor.ReferencesRequested += (_, e) => status.Text = string.Join(" · ", e.Locations.Select(location => location.SymbolId).Distinct());
        }
        RefreshModel();
    }

    public void RefreshModel()
    {
        if (disposed) return;
        var next = currentHandler(); var different = !ReferenceEquals(handler, next);
        if (!different) StoreDraft();
        handler = next; refreshing = true;
        try
        {
            var all = handler == null ? Array.Empty<DaxAuthoringObject>() : Service().GetObjects();
            var oldObject = (objects.SelectedItem as DaxAuthoringObject)?.Id;
            functions.ItemsSource = all.Where(item => item.Kind == DaxScriptObjectKind.Function).ToArray(); objects.ItemsSource = all;
            if (different)
            {
                drafts.Clear(); selectedFunction = null; LoadFunction(null); scriptEditor.Text = handler == null ? "" : Service().ExportScript();
                explainEditor.Text = ""; explanation.Text = ""; parsed = null; scriptEntries.ItemsSource = null; matches.ItemsSource = null;
            }
            functions.SelectedItem = functions.Items.Cast<DaxAuthoringObject>().FirstOrDefault(item => item.Id == selectedFunction);
            objects.SelectedItem = objects.Items.Cast<DaxAuthoringObject>().FirstOrDefault(item => item.Id == oldObject);
            var metadata = handler == null ? DaxMetadataSnapshot.Empty : DaxMetadataSnapshotProvider.Capture(handler);
            foreach (var editor in new[] { functionEditor, scriptEditor, explainEditor }) editor.SetMetadata(metadata);
            scriptEditor.SetDocumentContext("model-script", DaxDocumentKind.Script);
            if (different) status.Text = handler == null ? "Open a model to author DAX." : "Changes remain local until reviewed save/deploy. UDF metadata requires compatibility 1702; this tool never upgrades the model.";
            query.Invalidate();
        }
        finally { refreshing = false; }
    }

    private UIElement FunctionsPage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Note("Create or edit a DAX function using public model metadata. Rename previews all resolved callers. New-model drafts are separate from deployed engine metadata."));
        top.Children.Add(Bar(functions, Button("New", () => { StoreDraft(); refreshing = true; functions.SelectedItem = null; refreshing = false; LoadFunction(null); }),
            Button("Preview changes", () => Review(Service().PreviewFunction(CurrentDraft()))), Button("Rename with callers…", () => { if (selectedFunction == null) throw new InvalidOperationException("Select an existing function first."); Review(Service().PreviewFunctionRename(selectedFunction, functionName.Text.Trim())); })));
        top.Children.Add(Bar(Label("Name"), functionName, hidden)); top.Children.Add(Label("Description")); top.Children.Add(description);
        top.Children.Add(Bar(Label("Test arguments"), arguments, functionValueKind, Button("Prepare test query", PrepareFunctionTest)));
        panel.Children.Add(functionEditor.View); return panel;
    }
    private UIElement ScriptsPage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Note("Editable multi-object source: MEASURE, COLUMN, TABLE, CALCULATIONITEM and FUNCTION definitions ending in semicolons. FORMATSTRINGEXPRESSION updates are supported. Missing definitions never delete objects."));
        top.Children.Add(Bar(Button("Export model to draft", () => scriptEditor.Text = Service().ExportScript()), Button("Export selected objects", () => { RequireParsed(); var ids = scriptEntries.SelectedItems.Cast<DaxScriptEntry>().Select(entry => entry.ObjectKey).Distinct().ToArray(); if (ids.Length == 0) throw new InvalidOperationException("Parse and select the model objects to export first."); scriptEditor.Text = Service().ExportScript(ids); }), Button("Open…", OpenScript), Button("Save…", SaveScript),
            Button("Parse / select objects", ParseScript), Button("Preview selected", () => { RequireParsed(); var selection = scriptEntries.SelectedItems.Cast<DaxScriptEntry>().Select(entry => entry.Key).ToArray(); if (selection.Length == 0) throw new InvalidOperationException("Select one or more parsed properties. Ctrl/Shift selects multiple rows."); Review(Service().PreviewScript(scriptEditor.Text, selection)); }),
            Button("Preview all", () => Review(Service().PreviewScript(scriptEditor.Text)))));
        var grid = new System.Windows.Controls.Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        grid.Children.Add(scriptEditor.View); var splitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; System.Windows.Controls.Grid.SetRow(splitter, 1); grid.Children.Add(splitter); System.Windows.Controls.Grid.SetRow(scriptEntries, 2); grid.Children.Add(scriptEntries); panel.Children.Add(grid);
        scriptEntries.AutoGeneratingColumn += (_, e) => { if (e.PropertyName is "Expression" or "ExpressionSpan" or "Span" or "ObjectKey" or "Key") e.Cancel = true; }; return panel;
    }
    private UIElement SearchPage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Note("Search all DAX object expressions and dynamic format strings; optionally include descriptions. Preview selected applies every match in the selected objects. Literal replacement preserves dollar signs; regex replacement supports capture groups."));
        top.Children.Add(Bar(Label("Find"), pattern, Label("Replace"), replacement, regex, matchCase, wholeWord, descriptions));
        top.Children.Add(Bar(Button("Find all", () => { matches.ItemsSource = Service().Search(Search()); status.Text = matches.Items.Count + " matches. Select rows to restrict replacement to their objects."; }),
            Button("Preview selected objects", () => { var ids = matches.SelectedItems.Cast<DaxTextMatch>().Select(match => match.ObjectId).Distinct().ToArray(); if (ids.Length == 0) throw new InvalidOperationException("Select matching objects first."); Review(Service().PreviewReplace(Search(), ids)); }),
            Button("Preview all matches", () => Review(Service().PreviewReplace(Search()))), Button("Format selected objects…", () => { var ids = matches.SelectedItems.Cast<DaxTextMatch>().Select(match => match.ObjectId).Distinct().ToArray(); if (ids.Length == 0) throw new InvalidOperationException("Select matching objects first."); Review(Service().PreviewFormat(ids)); })));
        matches.AutoGeneratingColumn += (_, e) => { if (e.PropertyName is "Before" or "After" or "ObjectId") e.Cancel = true; }; panel.Children.Add(matches); return panel;
    }
    private UIElement ExplainPage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Note("Inspect dependencies, callers and VAR declarations. Evaluate a selected standalone subexpression, or the full draft, in an explicit scalar/table context. Local VAR references require their enclosing VAR/RETURN expression. This is an explain workbench, not an engine debugger."));
        top.Children.Add(Bar(objects, Button("Explain draft", Explain), Label("Result kind"), valueKind));
        top.Children.Add(Bar(Label("Filter context"), filterContext, Button("Prepare evaluation", PrepareEvaluation)));
        var grid = new System.Windows.Controls.Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        var source = new System.Windows.Controls.Grid(); source.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); source.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); source.Children.Add(explainEditor.View);
        var details = new TabControl(); details.Items.Add(new TabItem { Header = "Dependencies", Content = dependencyTree }); details.Items.Add(new TabItem { Header = "VAR / callers", Content = explanation }); System.Windows.Controls.Grid.SetColumn(details, 1); source.Children.Add(details); grid.Children.Add(source);
        var splitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; System.Windows.Controls.Grid.SetRow(splitter, 1); grid.Children.Add(splitter); System.Windows.Controls.Grid.SetRow(query, 2); grid.Children.Add(query); panel.Children.Add(grid); return panel;
    }

    private DaxAuthoringService Service() => new(handler ?? throw new InvalidOperationException("Open a semantic model first."));
    private (string? Server, string? Database) QueryTarget()
    {
        if (handler?.IsConnected != true) return (null, null);
        return (PbiBench.Core.Queries.QueryConnectionTarget.Server(handler.Database.Server.ConnectionString, handler.Database.Server.Name), handler.Database.Name);
    }
    private DaxFunctionEdit CurrentDraft() => new(selectedFunction, functionName.Text.Trim(), functionEditor.Text, description.Text, hidden.IsChecked == true);
    private void StoreDraft() { if (!refreshing) drafts[selectedFunction ?? "__new"] = CurrentDraft(); }
    private void LoadFunction(DaxAuthoringObject? item)
    {
        selectedFunction = item?.Id;
        var draft = drafts.TryGetValue(selectedFunction ?? "__new", out var saved) ? saved : new DaxFunctionEdit(selectedFunction, item?.Name ?? "MyFunction", item?.Expression ?? "(value : NUMERIC) => value", item?.Description ?? "", item != null && handler?.Model.Functions.FirstOrDefault(f => f.Name == item.Name)?.IsHidden == true);
        functionName.Text = draft.Name; description.Text = draft.Description; hidden.IsChecked = draft.IsHidden; functionEditor.SetDocumentContext("udf:" + (selectedFunction ?? "new"), DaxDocumentKind.Function); functionEditor.Text = draft.Expression;
    }
    private DaxTextSearch Search() => new(pattern.Text, replacement.Text, regex.IsChecked == true, matchCase.IsChecked == true, wholeWord.IsChecked == true, descriptions.IsChecked == true);
    private void Review(AuthoringPreview preview)
    {
        if (!AuthoringReview.Show(this, preview, currentHandler, changed)) return;
        status.Text = preview.Changes.Count + " reviewed changes applied locally. TE2 Undo restores this batch.";
        if (tabs.SelectedIndex == 0) { drafts.Remove(selectedFunction ?? "__new"); var name = functionName.Text.Trim(); selectedFunction = Service().GetFunctions().FirstOrDefault(item => item.Name == name)?.Id; refreshing = true; LoadFunction(Service().GetFunctions().FirstOrDefault(item => item.Id == selectedFunction)); refreshing = false; }
        RefreshModel();
    }
    private void ParseScript() { parsed = DaxModelScript.Parse(scriptEditor.Text); scriptEntries.ItemsSource = parsed.Entries; scriptEntries.SelectAll(); status.Text = parsed.IsValid ? parsed.Entries.Count + " parsed properties. Select rows for partial apply; preview shows the semantic diff." : string.Join(" · ", parsed.Diagnostics.Select(d => d.Message)); }
    private void RequireParsed() { if (parsed == null) throw new InvalidOperationException("Parse the current draft before selecting objects."); }
    private async void OpenScript()
    {
        try { var dialog = new OpenFileDialog { Filter = "DAX scripts|*.daxscript;*.dax|All files|*.*" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return; scriptEditor.Text = await DaxScriptFile.LoadAsync(dialog.FileName, CancellationToken.None); status.Text = "Opened " + Path.GetFileName(dialog.FileName) + " as a draft."; } catch (Exception ex) { status.Text = ex.Message; }
    }
    private async void SaveScript()
    {
        try { var dialog = new SaveFileDialog { Filter = "PbiBench DAX script|*.daxscript", FileName = "model.daxscript" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return; var text = scriptEditor.Text; await DaxScriptFile.SaveAsync(dialog.FileName, text, CancellationToken.None); status.Text = "Saved source draft to " + Path.GetFileName(dialog.FileName); } catch (Exception ex) { status.Text = ex.Message; }
    }
    private void Explain()
    {
        if (objects.SelectedItem is not DaxAuthoringObject item) throw new InvalidOperationException("Select a DAX object first.");
        var result = Service().Explain(item.Id, explainEditor.Text); explanation.Text = "Callers\n" + string.Join("\n", result.Callers) + "\n\nVAR declarations\n" + string.Join("\n", result.Variables) + "\n\nDiagnostics\n" + string.Join("\n", result.Diagnostics.Select(d => d.Message));
        dependencyTree.Items.Clear(); foreach (var node in result.DependencyTree) dependencyTree.Items.Add(Node(node));
        static TreeViewItem Node(DaxDependencyNode node) { var item = new TreeViewItem { Header = node.Name, IsExpanded = true }; foreach (var child in node.Children) item.Items.Add(Node(child)); return item; }
    }
    private void PrepareEvaluation()
    {
        var source = explainEditor.SelectionLength > 0 ? explainEditor.SelectedText : explainEditor.Text;
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("Enter an expression or select a complete subexpression first.");
        var table = valueKind.SelectedIndex == 1;
        if (!string.IsNullOrWhiteSpace(filterContext.Text)) source = (table ? "CALCULATETABLE" : "CALCULATE") + "(\n" + source + "\n, " + filterContext.Text + "\n)";
        query.SetPlan("EVALUATE\n" + (table ? source : "ROW(\"Value\",\n" + source + "\n)"), new[] { "Review Generated DAX before Run. Engine reads use deployed data/metadata; other local model edits are not deployed automatically." });
    }
    private void PrepareFunctionTest()
    {
        if (!DaxLanguageService.TryFunctionSignature(functionName.Text.Trim(), functionEditor.Text, out _)) throw new InvalidOperationException("Enter a valid function signature and body first.");
        tabs.SelectedIndex = 3;
        var call = functionName.Text.Trim() + "(" + arguments.Text + ")";
        query.SetPlan("DEFINE\n    FUNCTION " + functionName.Text.Trim() + " = " + functionEditor.Text + "\nEVALUATE\n" + (functionValueKind.SelectedIndex == 1 ? call : "ROW(\"Result\", " + call + ")"), new[] { "This query-scoped function tests the draft without changing model metadata. The connected engine must support DAX UDFs. Review Generated DAX before running." });
    }
    private void Guard(Action action) { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }
    private Button Button(string text, Action action) { var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(8, 4, 8, 4) }; button.Click += (_, _) => Guard(action); return button; }
    private static WrapPanel Bar(params UIElement[] controls) { var panel = new WrapPanel { Margin = new Thickness(3) }; foreach (var control in controls) { if (control is FrameworkElement element) element.Margin = new Thickness(3); panel.Children.Add(control); } return panel; }
    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private static DataGrid Grid() => new() { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, EnableRowVirtualization = true, EnableColumnVirtualization = true };
    public void Dispose() { if (disposed) return; disposed = true; functionEditor.Dispose(); scriptEditor.Dispose(); explainEditor.Dispose(); query.Dispose(); }
}
