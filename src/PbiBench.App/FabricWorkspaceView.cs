using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PbiBench.Core.Fabric;
using PbiBench.Core.Tasks;
using PbiBench.Fabric;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

/// <summary>Original Fabric import wizard; network work and metadata mutations require explicit product actions.</summary>
public sealed class FabricWorkspaceView : UserControl, IDisposable
{
    private readonly IFabricAuthenticator auth;
    private readonly IFabricCatalogService catalog;
    private readonly IFabricDataPreviewService preview;
    private readonly BackgroundTaskQueue queue;
    private readonly bool ownsQueue;
    private readonly HttpClient http;
    private readonly TextBox tenant = new() { MinWidth = 245, Margin = new Thickness(4) }, client = new() { MinWidth = 245, Margin = new Thickness(4) };
    private readonly ComboBox workspaces = Choice("Name"), items = Choice("Name"), schemas = Choice(), tables = Choice("DisplayName"), targets = Choice(), modes = Choice();
    private readonly CheckBox useSql = new() { Content = "Browse SQL tables / views", Margin = new Thickness(6) };
    private readonly TextBox tableName = new() { MinWidth = 180, Margin = new Thickness(4) };
    private readonly TextBlock status = Note("Sign in with your organization's registered public-client app to browse Fabric."), schemaInfo = Note("");
    private readonly TextBox query = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DataGrid columns = Grid(false), data = Grid(), differences = Grid();
    private readonly List<ColumnChoice> columnChoices = new();
    private readonly List<Button> workButtons = new();
    private CancellationTokenSource? pending;
    private Func<TabularModelHandler?> currentHandler = () => null;
    private Action changed = () => { };
    private string? modelFingerprint;
    private long revision;
    private bool disposed, populating;
    public FabricTableSchema? SelectedSchema { get; private set; }
    internal string? SelectedWorkspaceId => (workspaces.SelectedItem as FabricWorkspace)?.Id ?? handoff?.WorkspaceId;
    internal string? SelectedItemId => (items.SelectedItem as FabricItem)?.Id ?? handoff?.ItemId;
    public AuthoringPreview? LastPreview { get; private set; }
    public FabricDataPreview? LastDataPreview { get; private set; }
    public string Status => status.Text;
    public bool IsRunning => pending != null;
    public int SourceColumnCount => columnChoices.Count;
    private FabricSelectionHandoff? handoff;
    public void AcceptSelectionHandoff(FabricSelectionHandoff selection)
    {
        // Revalidate even programmatically supplied envelopes; this is never an approval or a remote write.
        handoff = FabricSelectionHandoff.Parse(System.Text.Json.JsonSerializer.Serialize(selection));
        status.Text = "Selection from Fabric Toolbox: " + selection.DisplayName + " (" + selection.ItemType + "). Workspace " + selection.WorkspaceId + ", item " + selection.ItemId + ". Sign in, then Load workspaces to review the source. No import or connection has run.";
    }

    public FabricWorkspaceView(BackgroundTaskQueue? backgroundTasks = null, IFabricAuthenticator? authenticator = null,
        IFabricCatalogService? catalog = null, IFabricDataPreviewService? preview = null)
    {
        auth = authenticator ?? new EntraPublicClientTokenProvider(); http = FabricHttp.CreateClient();
        this.catalog = catalog ?? new FabricCatalogService(http, auth); this.preview = preview ?? new FabricSqlDataService(auth);
        queue = backgroundTasks ?? new BackgroundTaskQueue(); ownsQueue = backgroundTasks == null;
        var root = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var credentials = new WrapPanel(); credentials.Children.Add(Label("Tenant ID", tenant)); credentials.Children.Add(Label("Client ID", client));
        foreach (var entry in new[] { ("Sign in to Fabric", FabricAudience.Fabric), ("Authorize OneLake", FabricAudience.OneLake), ("Authorize SQL preview", FabricAudience.Sql) })
            credentials.Children.Add(ActionButton(entry.Item1, () => SignInAsync(entry.Item2)));
        credentials.Children.Add(ActionButton("Sign out", async () => { Invalidate(); await auth.SignOutAsync(CancellationToken.None); ClearCatalog(); status.Text = "Signed out. The in-memory token cache has been cleared."; }));
        top.Children.Add(new Expander { Header = "Entra sign-in · public-client configuration", IsExpanded = true, Content = credentials });
        top.Children.Add(Note("Use your Entra app registration with http://localhost as a public-client redirect URI. Consent is separate for Fabric, Azure Storage (OneLake), and Azure SQL. Tokens remain in memory."));
        var sourceTools = new WrapPanel();
        sourceTools.Children.Add(ActionButton("Load workspaces", LoadWorkspacesAsync));
        sourceTools.Children.Add(Label("1 · Workspace", workspaces)); sourceTools.Children.Add(Label("2 · Item", items)); sourceTools.Children.Add(useSql);
        sourceTools.Children.Add(Label("3 · Schema", schemas)); sourceTools.Children.Add(Label("4 · Table / view", tables));
        sourceTools.Children.Add(ActionButton("Cancel", () => { pending?.Cancel(); return Task.CompletedTask; }, false)); top.Children.Add(sourceTools);
        top.Children.Add(schemaInfo); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var tabs = new TabControl(); root.Children.Add(tabs);
        var importPage = new DockPanel(); var importTools = new WrapPanel(); DockPanel.SetDock(importTools, Dock.Bottom); importPage.Children.Add(importTools);
        modes.Items.Add(new ComboBoxItem { Content = "Direct Lake on OneLake", Tag = FabricStorageMode.DirectLakeOneLake });
        modes.Items.Add(new ComboBoxItem { Content = "Direct Lake on SQL", Tag = FabricStorageMode.DirectLakeSql });
        modes.Items.Add(new ComboBoxItem { Content = "Import", Tag = FabricStorageMode.Import }); modes.Items.Add(new ComboBoxItem { Content = "DirectQuery", Tag = FabricStorageMode.DirectQuery }); modes.SelectedIndex = 0;
        importTools.Children.Add(Label("5 · Storage mode", modes)); importTools.Children.Add(Label("Model table name", tableName));
        importTools.Children.Add(ActionButton("6 · Review table import…", () => { ReviewImport(); return Task.CompletedTask; }));
        importTools.Children.Add(ActionButton("Select all columns", () => { ChooseAll(true); return Task.CompletedTask; }));
        importTools.Children.Add(ActionButton("Clear columns", () => { ChooseAll(false); return Task.CompletedTask; }));
        columns.Columns.Add(new DataGridCheckBoxColumn { Header = "Include", Binding = new Binding(nameof(ColumnChoice.Include)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        foreach (var name in new[] { nameof(ColumnChoice.Name), nameof(ColumnChoice.SourceType), nameof(ColumnChoice.Nullable), nameof(ColumnChoice.Collation) })
            columns.Columns.Add(new DataGridTextColumn { Header = name, Binding = new Binding(name), IsReadOnly = true });
        importPage.Children.Add(columns); tabs.Items.Add(new TabItem { Header = "Source schema / import", Content = importPage });
        var dataPage = new DockPanel(); var dataTools = new StackPanel(); DockPanel.SetDock(dataTools, Dock.Top); dataPage.Children.Add(dataTools);
        dataTools.Children.Add(ActionButton("Preview selected columns · first 100 rows", PreviewDataAsync));
        dataTools.Children.Add(Note("Source SQL preview uses your SQL identity; it can differ from model RLS and OneLake security. It reads at most 101 source rows (100 displayed), 200 columns, 200,000 cells, and 16 MB. Direct Lake model queries can load columns into capacity memory."));
        dataTools.Children.Add(query); dataPage.Children.Add(data); tabs.Items.Add(new TabItem { Header = "Data preview", Content = dataPage });
        var comparePage = new DockPanel(); var compareTools = new WrapPanel(); DockPanel.SetDock(compareTools, Dock.Top); comparePage.Children.Add(compareTools);
        compareTools.Children.Add(Label("Existing model table", targets));
        compareTools.Children.Add(ActionButton("Compare source schema", () => { Compare(); return Task.CompletedTask; }));
        compareTools.Children.Add(ActionButton("Review selected schema updates…", () => { ReviewSchemaUpdates(); return Task.CompletedTask; }));
        compareTools.Children.Add(ActionButton("Review Import → OneLake…", () => { ReviewConversion(FabricStorageMode.DirectLakeOneLake); return Task.CompletedTask; }));
        compareTools.Children.Add(ActionButton("Review OneLake → Import…", () => { ReviewConversion(FabricStorageMode.Import); return Task.CompletedTask; }));
        var compareNote = Note("Select columns in Source schema / import before reviewing updates. Removed columns, rename candidates, and mapping mismatches remain explicit findings. Conversions show removed partitions and transformation loss before applying local edits.");
        DockPanel.SetDock(compareNote, Dock.Bottom); comparePage.Children.Add(compareNote); comparePage.Children.Add(differences);
        tabs.Items.Add(new TabItem { Header = "Schema compare / conversion", Content = comparePage });
        Content = root;
        workspaces.SelectionChanged += async (_, _) => { if (!populating && workspaces.SelectedItem is FabricWorkspace workspace) await LoadItemsAsync(workspace); };
        items.SelectionChanged += async (_, _) => { if (!populating && items.SelectedItem is FabricItem item) await LoadSchemasAsync(item); };
        useSql.Click += async (_, _) => { if (items.SelectedItem is FabricItem item) await LoadSchemasAsync(item); };
        schemas.SelectionChanged += async (_, _) => { if (!populating && items.SelectedItem is FabricItem item && schemas.SelectedItem is string schema) await LoadTablesAsync(item, schema); };
        tables.SelectionChanged += async (_, _) => { if (!populating && tables.SelectedItem is FabricSourceRef source) await LoadSchemaAsync(source); };
    }
    public void Configure(Func<TabularModelHandler?> currentHandler, Action changed)
    {
        this.currentHandler = currentHandler; this.changed = changed;
        var handler = currentHandler(); var fingerprint = handler == null ? null : new SemanticModelService(handler).Fingerprint();
        if (fingerprint == modelFingerprint) return;
        modelFingerprint = fingerprint; differences.ItemsSource = null; LastPreview = null; var selected = targets.SelectedItem as string;
        targets.ItemsSource = handler?.Model.Tables.Select(table => table.Name).OrderBy(name => name).ToArray() ?? Array.Empty<string>();
        if (selected != null && targets.Items.Contains(selected)) targets.SelectedItem = selected;
    }
    public void ShowSchema(FabricTableSchema schema)
    {
        FabricSchemaRules.Validate(schema); Invalidate(); SelectedSchema = schema; LastPreview = null; LastDataPreview = null; columnChoices.Clear();
        columnChoices.AddRange(schema.Columns.Select(column => new ColumnChoice(column))); columns.ItemsSource = columnChoices.ToArray();
        tableName.Text = schema.Source.Table; data.ItemsSource = null; differences.ItemsSource = null; query.Text = "";
        schemaInfo.Text = $"{schema.Source.ItemKind} · {schema.Source.DisplayName} · {schema.Source.Format ?? "unreported format"} · {schema.Columns.Count} columns · captured {schema.CapturedAt:u}\n" + string.Join("\n", schema.Warnings);
        status.Text = "Source schema loaded. Choose columns and storage mode, then review the exact local metadata changes.";
    }
    private Task SignInAsync(FabricAudience audience)
    {
        var options = new FabricSignInOptions(tenant.Text.Trim(), client.Text.Trim()); options.Validate();
        ClearCatalog(); return Work("Entra authorization", async ct => { await auth.SignInAsync(options, audience, ct); return () => status.Text = "Authorized " + audience + " · " + auth.AccountLabel + ". Load workspaces to continue."; });
    }
    public Task LoadWorkspacesAsync() => Work("Load Fabric workspaces", async ct =>
    { var values = await catalog.ListWorkspacesAsync(ct); return () => { ClearCatalog(); workspaces.ItemsSource = values; status.Text = values.Count + " workspaces. Select a workspace."; if (handoff != null) workspaces.SelectedItem = values.FirstOrDefault(w => w.Id == handoff.WorkspaceId); }; });
    private Task LoadItemsAsync(FabricWorkspace workspace)
    {
        ClearFrom(1); return Work("Load Fabric items", async ct => { var values = await catalog.ListItemsAsync(workspace.Id, ct); return () => { items.ItemsSource = values; status.Text = values.Count + " supported data items. Select an item."; if (handoff?.WorkspaceId == workspace.Id) items.SelectedItem = values.FirstOrDefault(i => i.Id == handoff.ItemId); }; });
    }
    private Task LoadSchemasAsync(FabricItem item)
    {
        ClearFrom(2); var sql = useSql.IsChecked == true;
        return Work("Load source schemas", async ct =>
        {
            var resolved = (await catalog.ResolveItemAsync(item, ct)) with { UseSqlCatalog = sql };
            var values = await catalog.ListSchemasAsync(resolved, ct);
            return () =>
            {
                populating = true;
                try { var available = items.ItemsSource.Cast<FabricItem>().Select(existing => existing.Id == resolved.Id ? resolved : existing).ToArray(); items.ItemsSource = available; items.SelectedItem = resolved; }
                finally { populating = false; }
                schemas.ItemsSource = values; status.Text = values.Count + " source schemas. Select a schema.";
            };
        });
    }
    private Task LoadTablesAsync(FabricItem item, string schema)
    {
        ClearFrom(3); return Work("Load source tables", async ct => { var values = await catalog.ListTablesAsync(item, schema, ct); return () => { tables.ItemsSource = values; status.Text = values.Count + " source objects. Select a table or view."; }; });
    }
    private Task LoadSchemaAsync(FabricSourceRef source)
    {
        ClearFrom(4); return Work("Read source schema", async ct => { var schema = await catalog.GetSchemaAsync(source, ct); return () => ShowSchema(schema); });
    }
    public Task PreviewDataAsync()
    {
        var request = new FabricDataPreviewRequest(RequireSchema(), SelectedColumns()); query.Text = FabricSqlDataService.PreviewSql(request);
        data.ItemsSource = null; LastDataPreview = null;
        return Work("Preview Fabric source data", async ct =>
        {
            var result = await preview.PreviewAsync(request, ct);
            if (result.Source != request.Schema.Source) throw new InvalidDataException("The preview returned a different source context.");
            return () => { LastDataPreview = result; data.ItemsSource = result.Result.ToDataTable().DefaultView; query.Text = result.Query; status.Text = result.Result.Rows.Count + " rows" + (result.Result.IsTruncated ? " · bounded / clipped" : "") + "\n" + string.Join("\n", result.Warnings); };
        });
    }
    public void SelectImportOptions(FabricStorageMode mode, string targetTableName, IReadOnlyList<string>? selectedColumns = null)
    {
        modes.SelectedItem = modes.Items.Cast<ComboBoxItem>().Single(item => (FabricStorageMode)item.Tag == mode); tableName.Text = targetTableName;
        if (selectedColumns != null)
        {
            if (selectedColumns.Any(name => !columnChoices.Any(column => column.Name == name))) throw new ArgumentException("Select columns from the current source schema.");
            foreach (var column in columnChoices) column.Include = selectedColumns.Contains(column.Name); columns.Items.Refresh();
        }
        LastPreview = null;
    }
    public AuthoringPreview PrepareImportPreview() => LastPreview = Service().PreviewImport(new FabricImportRequest(RequireSchema(), SelectedColumns(), (FabricStorageMode)((ComboBoxItem)modes.SelectedItem).Tag, tableName.Text.Trim()));
    private void ReviewImport() => Review(PrepareImportPreview());
    private void Compare() => differences.ItemsSource = Service().CompareSchema(Target(), RequireSchema());
    private void ReviewSchemaUpdates() => Review(Service().PreviewSchemaUpdate(Target(), RequireSchema(), SelectedColumns()));
    private void ReviewConversion(FabricStorageMode mode) => Review(Service().PreviewConversion(Target(), RequireSchema(), mode));
    private void Review(AuthoringPreview plan) { if (AuthoringReview.Show(this, plan, currentHandler, changed)) { status.Text = "Reviewed changes applied to the local model. Remote save and refresh require their own review."; Configure(currentHandler, changed); } }
    private FabricImportService Service() => new(currentHandler() ?? throw new InvalidOperationException("Open a semantic model before importing or updating objects."));
    private string Target() => targets.SelectedItem as string ?? throw new InvalidOperationException("Select an existing model table.");
    private FabricTableSchema RequireSchema() => SelectedSchema ?? throw new InvalidOperationException("Select a source table and load its schema first.");
    private string[] SelectedColumns() { columns.CommitEdit(DataGridEditingUnit.Cell, true); columns.CommitEdit(DataGridEditingUnit.Row, true); return columnChoices.Where(column => column.Include).Select(column => column.Name).ToArray(); }
    private void ChooseAll(bool include) { foreach (var column in columnChoices) column.Include = include; columns.Items.Refresh(); }
    private async Task Work(string title, Func<CancellationToken, Task<Action>> action)
    {
        if (disposed) return; Invalidate(); var version = revision; var cancellation = pending = new CancellationTokenSource(); status.Text = title + "…";
        try
        {
            var job = queue.Enqueue(title, context => action(context.CancellationToken), cancellation.Token); var show = await job.Completion;
            if (!disposed && version == revision && !cancellation.IsCancellationRequested) show();
        }
        catch (OperationCanceledException) { if (!disposed && version == revision) status.Text = "Canceled."; }
        catch (Exception error) { if (!disposed && version == revision) status.Text = error.Message; }
        finally { if (ReferenceEquals(pending, cancellation)) pending = null; cancellation.Dispose(); }
    }
    private void Invalidate() { revision++; pending?.Cancel(); }
    private void ClearCatalog() { ClearFrom(0); }
    private void ClearFrom(int level)
    {
        populating = true;
        try
        {
            if (level <= 0) workspaces.ItemsSource = null; if (level <= 1) items.ItemsSource = null; if (level <= 2) schemas.ItemsSource = null; if (level <= 3) tables.ItemsSource = null;
            SelectedSchema = null; LastPreview = null; LastDataPreview = null; columnChoices.Clear(); columns.ItemsSource = null; data.ItemsSource = null; differences.ItemsSource = null; query.Text = ""; schemaInfo.Text = "";
        }
        finally { populating = false; }
    }
    private Button ActionButton(string text, Func<Task> action, bool track = true)
    {
        var button = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) };
        if (track) workButtons.Add(button);
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) { if (!disposed) status.Text = error.Message; } }; return button;
    }
    private static ComboBox Choice(string? display = null) => new() { DisplayMemberPath = display ?? "", MinWidth = 155, MaxWidth = 250, Margin = new Thickness(4) };
    private static DataGrid Grid(bool readOnly = true) => new() { IsReadOnly = readOnly, AutoGenerateColumns = readOnly, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5) };
    private static FrameworkElement Label(string text, UIElement control) { var panel = new StackPanel(); panel.Children.Add(Note(text)); panel.Children.Add(control); return panel; }
    public sealed class ColumnChoice(FabricColumnSchema column)
    { public bool Include { get; set; } = true; public string Name => column.Name; public string SourceType => column.SourceType; public bool? Nullable => column.IsNullable; public string? Collation => column.Collation; }
    public void Dispose() { if (disposed) return; disposed = true; Invalidate(); http.Dispose(); if (ownsQueue) queue.Dispose(); }
}
