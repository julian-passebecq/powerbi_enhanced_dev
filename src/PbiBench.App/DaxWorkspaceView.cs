using System.Data;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PbiBench.Core.Queries;
using PbiBench.Dax.LanguageService;
using PbiBench.ModelEditor;
using PbiBench.Semantic;

namespace PbiBench.App;

/// <summary>Original document workspace over the existing editor and public TOM query transport.</summary>
public sealed class DaxWorkspaceView : UserControl, IDisposable
{
    private readonly TabControl documents = new();
    private readonly TabControl resultTabs = new();
    private readonly ListBox history = new() { DisplayMemberPath = "Summary" };
    private readonly DataGrid diagnostics = new() { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly TextBox rowLimit = new() { Text = "10000", Width = 72, Padding = new Thickness(5), Margin = new Thickness(0, 0, 8, 6) };
    private readonly TextBox executedText = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly List<DocumentTab> tabs = new();
    private readonly IDaxQueryService queries;
    private readonly QueryHistoryStore historyStore;
    private readonly string statePath;
    private readonly Func<DaxMetadataSnapshot> metadata;
    private readonly Func<(string? Server, string? Database)> connection;
    private readonly Func<string?> projectDirectory;
    private readonly Func<string?>? queryConnectionString;
    private readonly Action<string> log;
    private readonly Action<DaxSymbolLocation, bool> navigate;
    private readonly Button runButton;
    private readonly Button cancelButton;
    private CancellationTokenSource? running;
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    public DaxScratchEditor ActiveEditor => Active.Editor;
    public int DocumentCount => tabs.Count;
    public int ResultCount => resultTabs.Items.Count;
    public string StatusText => status.Text;
    public void CancelActiveQuery() => CancelRun();
    private DocumentTab Active => (DocumentTab)((TabItem)documents.SelectedItem).Tag;

    public DaxWorkspaceView(DaxScratchEditor initialEditor, string settingsDirectory,
        Func<DaxMetadataSnapshot> metadata, Func<(string? Server, string? Database)> connection,
        Func<string?> projectDirectory, Action<DaxSymbolLocation, bool> navigate, Action<string> log,
        IDaxQueryService? queries = null, Func<string?>? queryConnectionString = null)
    {
        this.metadata = metadata; this.connection = connection; this.projectDirectory = projectDirectory;
        this.navigate = navigate; this.log = log; this.queries = queries ?? new TomDaxQueryService();
        this.queryConnectionString = queryConnectionString;
        historyStore = new QueryHistoryStore(settingsDirectory);
        statePath = Path.Combine(settingsDirectory, "dax-documents-v9.json");
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 100 });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 100 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var commands = new WrapPanel();
        commands.Children.Add(Button("New", () => OpenQuery("EVALUATE\n    ROW ( \"Result\", 1 )")));
        commands.Children.Add(Button("Open .dax…", OpenFile));
        commands.Children.Add(Button("Save…", SaveActive));
        runButton = Button("Run all · F5", () => StartRun(DaxRunScope.All)); commands.Children.Add(runButton);
        commands.Children.Add(Button("Run selection", () => StartRun(DaxRunScope.Selection)));
        commands.Children.Add(Button("Run statement", () => StartRun(DaxRunScope.CurrentStatement)));
        cancelButton = Button("Cancel", CancelRun); cancelButton.IsEnabled = false; commands.Children.Add(cancelButton);
        commands.Children.Add(new TextBlock { Text = "Max displayed rows", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 6) });
        commands.Children.Add(rowLimit);
        commands.Children.Add(Button("Apply expression…", ApplyExpression));
        grid.Children.Add(commands);
        Grid.SetRow(documents, 1); grid.Children.Add(documents);
        var splitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetRow(splitter, 2); grid.Children.Add(splitter);
        var output = new TabControl(); Grid.SetRow(output, 3); grid.Children.Add(output);
        output.Items.Add(new TabItem { Header = "Results", Content = resultTabs });
        output.Items.Add(new TabItem { Header = "Diagnostics", Content = diagnostics });
        output.Items.Add(new TabItem { Header = "Executed DAX", Content = executedText });
        output.Items.Add(new TabItem { Header = "History", Content = history });
        Grid.SetRow(status, 4); grid.Children.Add(status);
        Content = grid;
        AddDocument(initialEditor, "Scratch query", null);
        documents.SelectionChanged += (_, _) => { if (documents.SelectedItem != null) { RefreshDiagnostics(); Active.Editor.Focus(); } };
        diagnostics.MouseDoubleClick += (_, _) => NavigateDiagnostic();
        history.MouseDoubleClick += (_, _) => { if (history.SelectedItem is QueryHistoryEntry entry) OpenQuery(entry.Query, "History query"); };
        Loaded += async (_, _) => { try { history.ItemsSource = await historyStore.LoadAsync(lifetime.Token); } catch (OperationCanceledException) { } catch (Exception ex) { ShowError(ex); } };
        RestoreDocuments();
        status.Text = "Ctrl+Space · Complete   F12 · Definition   Alt+F12 · Peek   F5 · Run\nQueries use the connected engine. Offline model files have no data engine.";
    }

    private Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { ShowError(ex); } };
        return button;
    }
    private DocumentTab AddDocument(DaxScratchEditor editor, string title, string? path)
    {
        var doc = new DocumentTab { Editor = editor, Title = title, Path = path, SavedText = editor.Text };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        doc.Label = new TextBlock { Text = title, Margin = new Thickness(2, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(doc.Label);
        var close = new Button { Content = "×", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0), ToolTip = "Close document" };
        close.Click += (_, _) => CloseDocument(doc); header.Children.Add(close);
        doc.Tab = new TabItem { Header = header, Content = editor.View, Tag = doc, ToolTip = path ?? title };
        tabs.Add(doc); documents.Items.Add(doc.Tab); documents.SelectedItem = doc.Tab;
        editor.TextChanged += (_, _) => { doc.Revision++; doc.Label.Text = doc.Title + (editor.Text == doc.SavedText ? "" : " •"); };
        editor.SetMetadata(metadata());
        editor.DefinitionRequested += (_, e) => navigate(e.Location, e.Peek);
        editor.DiagnosticsChanged += (_, _) => { if (!disposed && ReferenceEquals(doc, Active)) RefreshDiagnostics(); };
        editor.RunRequested += (_, e) => StartRun(e.Scope);
        return doc;
    }
    public void OpenQuery(string text, string title = "Query")
    {
        var editor = new DaxScratchEditor { Text = text }; AddDocument(editor, title + " " + (tabs.Count + 1), null);
    }
    public void OpenExpression(string title, string text, Action<string> apply, string? table = null, bool tableExpression = false)
    {
        var editor = new DaxScratchEditor { Text = text };
        var doc = AddDocument(editor, title, null); doc.ApplyExpression = apply; doc.IsExpression = true; doc.TableExpression = tableExpression;
        editor.SetDocumentContext(DaxDocumentKind.Expression, table);
        status.Text = "Model expression document. Apply expression shows a diff before updating the model; model undo remains available.";
    }
    public void RefreshMetadata()
    {
        var snapshot = metadata(); foreach (var doc in tabs) doc.Editor.SetMetadata(snapshot);
    }
    private void RefreshDiagnostics() => diagnostics.ItemsSource = Active.Editor.Diagnostics;
    private void NavigateDiagnostic()
    {
        if (diagnostics.SelectedItem is DaxDiagnostic diagnostic) { Active.Editor.SelectSpan(diagnostic.Span.Start, diagnostic.Span.Length); Active.Editor.Focus(); }
    }
    private void OpenFile()
    {
        var dialog = new OpenFileDialog { Filter = "DAX documents|*.dax;*.msdax|All files|*.*", Multiselect = true };
        var directory = QueryDirectory(); if (Directory.Exists(directory)) dialog.InitialDirectory = directory;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        foreach (var path in dialog.FileNames)
        {
            var existing = tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { documents.SelectedItem = existing.Tab; continue; }
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("DAX documents larger than 4 MB are not supported by this editor.");
            AddDocument(new DaxScratchEditor { Text = File.ReadAllText(path) }, Path.GetFileName(path), path);
        }
    }
    private string QueryDirectory()
    {
        var root = projectDirectory(); return string.IsNullOrWhiteSpace(root) ? Path.GetDirectoryName(statePath)! : Path.Combine(root!, "DAXQueries");
    }
    public void SaveActive()
    {
        var doc = Active;
        var dialog = new SaveFileDialog { Filter = "DAX query|*.dax|DAX document|*.msdax", FileName = doc.Path == null ? "query.dax" : Path.GetFileName(doc.Path) };
        var directory = doc.Path == null ? QueryDirectory() : Path.GetDirectoryName(doc.Path)!;
        Directory.CreateDirectory(directory); dialog.InitialDirectory = directory;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        File.WriteAllText(dialog.FileName, doc.Editor.Text);
        doc.Path = dialog.FileName; doc.Title = Path.GetFileName(doc.Path); doc.SavedText = doc.Editor.Text;
        doc.Label.Text = doc.Title; doc.Tab.ToolTip = doc.Path; log("Saved DAX document: " + doc.Path);
    }
    private void CloseDocument(DocumentTab doc)
    {
        if (doc.Editor.Text != doc.SavedText && MessageBox.Show(Window.GetWindow(this), "Close this document and discard its unsaved text?", doc.Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (tabs.Count == 1) { OpenQuery("EVALUATE ROW ( \"Result\", 1 )"); }
        doc.Tab.Content = null; documents.Items.Remove(doc.Tab); tabs.Remove(doc); doc.Editor.Dispose();
        if (documents.SelectedIndex < 0) documents.SelectedIndex = 0;
    }
    private void ApplyExpression()
    {
        var doc = Active;
        if (doc.ApplyExpression == null) throw new InvalidOperationException("Open a selected model expression with Edit in DAX IDE before applying it to the model.");
        doc.ApplyExpression(doc.Editor.Text); RefreshMetadata();
    }
    private async void StartRun(DaxRunScope scope)
    {
        try { await RunAsync(scope); } catch (OperationCanceledException) { if (!disposed) status.Text = "Query canceled."; } catch (Exception ex) { if (!disposed) ShowError(ex); }
    }
    public async Task RunAsync(DaxRunScope scope)
    {
        if (running != null) throw new InvalidOperationException("Wait for the current query to finish or cancel it.");
        var target = connection();
        if (string.IsNullOrWhiteSpace(target.Server) || string.IsNullOrWhiteSpace(target.Database)) throw new InvalidOperationException("Connect to Desktop or an XMLA model to execute DAX. A local BIM/TMDL file contains metadata only.");
        if (!int.TryParse(rowLimit.Text, out var limit) || limit < 1 || limit > 100000) throw new InvalidOperationException("Displayed row limit must be between 1 and 100,000.");
        var doc = Active; var revision = doc.Revision;
        var query = SelectQuery(doc, scope);
        var request = new QueryRequest(target.Server!, target.Database!, query, limit, 60, revision) { ConnectionString = queryConnectionString?.Invoke() };
        running = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var run = running;
        runButton.IsEnabled = false; cancelButton.IsEnabled = true; executedText.Text = query;
        status.Text = $"Running on {target.Server} / {target.Database}. At most {limit:N0} rows are retained per result; this does not limit server work.";
        try
        {
            var result = await queries.ExecuteAsync(request, run.Token);
            if (disposed) return;
            var historySaved = await TryRecordHistoryAsync(QueryHistoryEntry.FromResult(result));
            if (disposed) return;
            if (doc.Revision != revision || connection() != target || !tabs.Contains(doc) || !ReferenceEquals(doc, Active)) { status.Text = "Query finished; document or connection changed. Results were kept out of the current view. The exact query is in history."; return; }
            DisplayResults(result);
            if (!historySaved) status.Text += "\nResults are available, but local history could not be saved.";
        }
        catch (Exception error)
        {
            if (!disposed)
            {
                await TryRecordHistoryAsync(QueryHistoryEntry.FromFailure(request, error is OperationCanceledException ? "Cancelled" : error is TimeoutException ? "Timed out" : "Failed"));
            }
            throw;
        }
        finally
        {
            run.Dispose(); running = null;
            if (!disposed) { runButton.IsEnabled = true; cancelButton.IsEnabled = false; }
        }
    }
    private async Task<bool> TryRecordHistoryAsync(QueryHistoryEntry entry)
    {
        if (disposed) return false;
        var token = lifetime.Token;
        try
        {
            await historyStore.AddAsync(entry, token);
            var entries = await historyStore.LoadAsync(token);
            if (!disposed) history.ItemsSource = entries;
            return true;
        }
        catch (Exception error) when (error is IOException || error is OperationCanceledException || error is UnauthorizedAccessException || error is JsonException) { return false; }
    }
    private string SelectQuery(DocumentTab doc, DaxRunScope scope)
    {
        // The language service owns lexical statement boundaries; it preserves DEFINE
        // context and never treats EVALUATE inside strings or comments as a new query.
        var text = doc.Editor.Text;
        if (doc.IsExpression && scope == DaxRunScope.All)
            text = doc.TableExpression ? "EVALUATE\n" + text : "EVALUATE\n    ROW ( \"Value\",\n" + text + "\n    )";
        return DaxQueryPlanner.Prepare(new DaxDocument(doc.Title, text),
            (DaxExecutionMode)Enum.Parse(typeof(DaxExecutionMode), scope.ToString()),
            doc.Editor.CaretOffset, new TextSpan(doc.Editor.SelectionStart, doc.Editor.SelectionLength)).QueryText;
    }
    public void DisplayResults(QueryResult result)
    {
        resultTabs.Items.Clear();
        foreach (var set in result.Results)
        {
            var panel = new DockPanel();
            var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
            toolbar.Children.Add(new TextBlock { Text = $"{set.Rows.Count:N0} rows{(set.IsTruncated ? " · display limit reached" : "")}", Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center });
            toolbar.Children.Add(Button("Export CSV…", () => ExportCsv(set)));
            var data = new DataGrid { ItemsSource = set.ToDataTable().DefaultView, AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader };
            data.AutoGeneratingColumn += (_, e) => e.Column.Header = set.Columns.FirstOrDefault(c => c.Key == e.PropertyName)?.Name ?? e.PropertyName;
            panel.Children.Add(data); resultTabs.Items.Add(new TabItem { Header = set.Name, Content = panel });
        }
        if (resultTabs.Items.Count > 0) resultTabs.SelectedIndex = 0;
        status.Text = $"{result.Results.Count} result sets · {result.Elapsed.TotalMilliseconds:N0} ms · {result.Server} / {result.Database}\n" + string.Join("\n", result.Warnings);
        log($"DAX query completed: {result.Results.Count} result sets in {result.Elapsed.TotalMilliseconds:N0} ms.");
    }
    private async void ExportCsv(QueryResultSet set)
    {
        try { var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "query-result.csv" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) await QueryCsv.ExportAsync(set, dialog.FileName, lifetime.Token); }
        catch (OperationCanceledException) { } catch (Exception ex) { ShowError(ex); }
    }
    private void CancelRun() { running?.Cancel(); status.Text = "Canceling the query's independent engine session…"; }
    private void ShowError(Exception ex) { status.Text = ex.Message; log("DAX action needs attention: " + ex.Message); }
    private void RestoreDocuments()
    {
        try
        {
            if (!File.Exists(statePath) || new FileInfo(statePath).Length > 8 * 1024 * 1024) return;
            var saved = JsonSerializer.Deserialize<List<SavedDocument>>(File.ReadAllText(statePath));
            if (saved == null) return;
            var index = 0;
            foreach (var doc in saved.Take(12))
            {
                if (doc.Text == null || doc.Text.Length > 500000) continue;
                var restored = index++ == 0 ? tabs[0] : AddDocument(new DaxScratchEditor(), doc.Title ?? "Recovered query", doc.Path);
                restored.Editor.Text = doc.Text; restored.Title = doc.Title ?? "Recovered query"; restored.Path = doc.Path;
                restored.SavedText = doc.SavedText ?? doc.Text; restored.IsExpression = doc.IsExpression; restored.TableExpression = doc.TableExpression;
                restored.Label.Text = restored.Title + (restored.Editor.Text == restored.SavedText ? "" : " •");
                if (restored.IsExpression) restored.Editor.SetDocumentContext(DaxDocumentKind.Expression, null);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) { log("DAX document recovery was unavailable; the scratch query remains available."); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel(); running?.Cancel();
        try
        {
            var saved = tabs.Take(12).Where(t => t.Editor.Text.Length <= 500000).Select(t => new SavedDocument { Title = t.Title, Path = t.Path, Text = t.Editor.Text, SavedText = t.SavedText, IsExpression = t.IsExpression, TableExpression = t.TableExpression }).ToArray();
            File.WriteAllText(statePath, JsonSerializer.Serialize(saved));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { log("DAX document recovery could not be saved."); }
        foreach (var doc in tabs) { doc.Tab.Content = null; doc.Editor.Dispose(); }
        lifetime.Dispose();
    }
    private sealed class DocumentTab
    {
        public DaxScratchEditor Editor { get; set; } = null!;
        public TabItem Tab { get; set; } = null!;
        public TextBlock Label { get; set; } = null!;
        public string Title { get; set; } = "Query";
        public string? Path { get; set; }
        public string SavedText { get; set; } = "";
        public long Revision { get; set; }
        public Action<string>? ApplyExpression { get; set; }
        public bool IsExpression { get; set; }
        public bool TableExpression { get; set; }
    }
    public sealed class SavedDocument
    {
        public string? Title { get; set; }
        public string? Path { get; set; }
        public string? Text { get; set; }
        public string? SavedText { get; set; }
        public bool IsExpression { get; set; }
        public bool TableExpression { get; set; }
    }
}
