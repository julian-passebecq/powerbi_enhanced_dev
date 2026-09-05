using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PbiBench.Core.Fabric;
using PbiBench.Fabric;

namespace PbiBench.FabricToolbox;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application(); var window = new ToolboxWindow();
        if (args.Length == 2 && args[0] == "--smoke-test")
        {
            window.Loaded += async (_, _) =>
            {
                try
                {
                    await window.Dispatcher.InvokeAsync(() => window.UpdateLayout());
                    window.VerifyOfflineViews();
                    if (window.PageCount != 5 || AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "TOMWrapper" or "TabularEditor" or "PbiBench.ModelEditor" or "PbiBench.Semantic" or "PbiBench.App")) throw new InvalidOperationException("Toolbox process isolation failed.");
                    window.Capture(Path.ChangeExtension(args[1], ".png"));
                    File.WriteAllText(args[1], "Toolbox WPF launch: 5 pages, V0.2 inventory/filter/details and Operations controls, shared Fabric service, no loaded TE2/ModelEditor/Semantic assembly. Offline fixture only; no authenticated target used."); app.Shutdown(0);
                }
                catch (Exception error) { File.WriteAllText(args[1] + ".error", error.ToString()); app.Shutdown(1); }
            };
        }
        return app.Run(window);
    }
}

public sealed partial class ToolboxWindow : Window
{
    private readonly System.Net.Http.HttpClient http;
    private readonly IFabricAuthenticator auth;
    private readonly FabricCatalogService catalog;
    private readonly FabricSqlDataService sql;
    private readonly TabControl pages = new();
    private readonly ComboBox workspaces = new() { DisplayMemberPath = "Name", MinWidth = 240 }, schemas = new() { MinWidth = 140 }, tables = new() { DisplayMemberPath = "DisplayName", MinWidth = 200 };
    private readonly TextBox tenant = new() { MinWidth = 300 }, client = new() { MinWidth = 300 };
    private readonly DataGrid items = Grid(), columns = Grid(), data = Grid();
    private readonly TextBlock status = Note("No active sign-in. Use Settings to authorize your registered Entra public client."), sourceInfo = Note("Select a Lakehouse/Warehouse/data item in Workspaces, then Browse source.");
    private FabricItem? resolved; private FabricTableSchema? schema;
    private CancellationTokenSource? pending;
    private readonly List<Button> actions = new();
    public int PageCount => pages.Items.Count;
    public ToolboxWindow() : this(FabricHttp.CreateClient(), new EntraPublicClientTokenProvider()) { }
    internal ToolboxWindow(System.Net.Http.HttpClient http, IFabricAuthenticator auth)
    {
        this.http = http; this.auth = auth; catalog = new(http, auth); sql = new(auth); operations = new FabricOperationsService(http, auth);
        Title = "PbiBench Fabric Toolbox"; Width = 1160; Height = 780; MinWidth = 800; MinHeight = 550;
        var root = new DockPanel { Margin = new Thickness(18) }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(pages); Content = root;
        Add("Home", Note("Fabric Toolbox V0.2\n\nSearch workspace items, inspect identifiers, export filtered inventory, and review recent job instances in Operations. Use OneLake / Data for bounded SQL source previews.\n\nSign in explicitly in Settings. Operations refresh is read-only and manual; no job starts, retries or cancellations are submitted. Tokens stay in memory."));
        var explorer = new DockPanel(); var bar = Bar(Button("Load workspaces", async ct => { workspaces.ItemsSource = await catalog.ListWorkspacesAsync(ct); }), workspaces,
            Button("Load all items", LoadItemsAsync), Button("Browse source", BrowseAsync), Button("Export selection to Semantic IDE…", ExportSelectionAsync));
        DockPanel.SetDock(bar, Dock.Top); explorer.Children.Add(bar); ConfigureInventory(explorer); Add("Workspaces", explorer);
        workspaces.SelectionChanged += (_, _) => ClearWorkspace();
        var source = new DockPanel(); var sourceBar = new StackPanel(); DockPanel.SetDock(sourceBar, Dock.Top); source.Children.Add(sourceBar); sourceBar.Children.Add(sourceInfo);
        sourceBar.Children.Add(Bar(schemas, Button("Load tables", async ct => { tables.ItemsSource = await catalog.ListTablesAsync(resolved ?? throw new InvalidOperationException("Browse a data item first."), schemas.SelectedItem as string ?? throw new InvalidOperationException("Select a schema."), ct); }), tables,
            Button("Load columns", async ct => { schema = await catalog.GetSchemaAsync(tables.SelectedItem as FabricSourceRef ?? throw new InvalidOperationException("Select a table."), ct); columns.ItemsSource = schema.Columns; sourceInfo.Text = schema.Source.DisplayName + " · " + string.Join(" ", schema.Warnings); }),
            Button("Preview selected columns · 25 rows", PreviewAsync)));
        sourceBar.Children.Add(Note("Select columns before SQL preview. It uses your SQL identity, can differ from model RLS, and returns up to 25 rows. OneLake metadata browsing does not fetch data."));
        var sourceTabs = new TabControl(); sourceTabs.Items.Add(new TabItem { Header = "Columns", Content = columns }); sourceTabs.Items.Add(new TabItem { Header = "Data", Content = data }); source.Children.Add(sourceTabs); Add("OneLake / Data", source);
        Add("Operations", OperationsPage());
        var settings = new StackPanel(); settings.Children.Add(Note("Tenant GUID")); settings.Children.Add(tenant); settings.Children.Add(Note("Public-client app GUID · http://localhost redirect")); settings.Children.Add(client);
        var authBar = new WrapPanel(); foreach (var audience in new[] { FabricAudience.Fabric, FabricAudience.OneLake, FabricAudience.Sql }) authBar.Children.Add(Button("Authorize " + audience, ct => auth.SignInAsync(new(tenant.Text.Trim(), client.Text.Trim()), audience, ct)));
        authBar.Children.Add(Button("Sign out", async ct => { await auth.SignOutAsync(ct); workspaces.ItemsSource = null; ClearWorkspace(); })); settings.Children.Add(authBar);
        settings.Children.Add(Note("Ownership: PbiBench.FabricToolbox 0.2.0. Transport/auth/SQL: shared PbiBench.Fabric; Microsoft.Identity.Client 4.84.2, Microsoft.Data.SqlClient 6.1.6. Independent Fabric update lane.\n\nUse a .pbifabric.json handoff in Semantic IDE Apps / Tools → Import Fabric selection. The file carries selection identifiers only and cannot authorize a write.")); Add("Settings / About", settings);
        var cancel = new Button { Content = "Cancel current request", Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left }; cancel.Click += (_, _) => pending?.Cancel(); DockPanel.SetDock(cancel, Dock.Bottom); root.Children.Insert(1, cancel);
        Closed += (_, _) => { pending?.Cancel(); http.Dispose(); };
    }
    private async Task BrowseAsync(CancellationToken ct)
    {
        var item = items.SelectedItem as FabricItem ?? throw new InvalidOperationException("Select a data item first.");
        resolved = await catalog.ResolveItemAsync(item, ct); schema = null; columns.ItemsSource = null; data.ItemsSource = null;
        schemas.ItemsSource = await catalog.ListSchemasAsync(resolved, ct); pages.SelectedIndex = 2; sourceInfo.Text = resolved.Name + " · " + resolved.Kind;
    }
    private async Task PreviewAsync(CancellationToken ct)
    {
        var current = schema ?? throw new InvalidOperationException("Load table columns first.");
        var names = columns.SelectedItems.Cast<FabricColumnSchema>().Select(c => c.Name).ToArray(); if (names.Length == 0) throw new InvalidOperationException("Select columns to preview.");
        var result = await sql.PreviewAsync(new(current, names, 25), ct); data.ItemsSource = result.Result.ToDataTable().DefaultView;
    }
    private async Task ExportSelectionAsync(CancellationToken ct)
    {
        var item = items.SelectedItem as FabricItem ?? throw new InvalidOperationException("Select an item first.");
        var dialog = new SaveFileDialog { Filter = "PbiBench Fabric selection|*.pbifabric.json", FileName = "fabric-selection.pbifabric.json" }; if (dialog.ShowDialog(this) == true) await FabricSelectionHandoff.For(item).SaveAsync(dialog.FileName, ct);
    }
    private FabricWorkspace Workspace() => workspaces.SelectedItem as FabricWorkspace ?? throw new InvalidOperationException("Select a workspace.");
    private Button Button(string text, Func<CancellationToken, Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(8, 5, 8, 5) }; actions.Add(button);
        button.Click += async (_, _) => { if (pending != null) return; using var ct = new CancellationTokenSource(); pending = ct; SetBusy(true); try { await action(ct.Token); status.Text = "Completed · " + text; } catch (OperationCanceledException) { status.Text = "Canceled."; } catch (Exception error) { status.Text = error.Message; } finally { pending = null; SetBusy(false); } }; return button;
    }
    private void Add(string title, UIElement view) => pages.Items.Add(new TabItem { Header = title, Content = view });
    private static TextBlock Note(string text) => new() { Text = text, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private static WrapPanel Bar(params UIElement[] children) { var bar = new WrapPanel(); foreach (var c in children) bar.Children.Add(c); return bar; }
    private static DataGrid Grid() => new() { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, Margin = new Thickness(6) };
}
