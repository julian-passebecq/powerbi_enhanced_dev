using System.Windows;
using System.Windows.Controls;
using PbiBench.Core.DataExploration;
using PbiBench.Core.Queries;
using PbiBench.Semantic;

namespace PbiBench.App;

public sealed class DataWorkspaceView : UserControl, IDisposable
{
    private readonly Func<DataModelSchema> schema;
    private readonly Func<(string? Server, string? Database)> connection;
    private readonly Func<string?> transport;
    private readonly Func<string> artifactDirectory;
    private readonly IDaxQueryService queries;
    private readonly TabControl tabs = new();
    private readonly ComboBox tables = new() { MinWidth = 140, DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 8, 6) };
    private readonly ComboBox columns = new() { MinWidth = 140, DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 8, 6) };
    private readonly ComboBox relationships = new() { MinWidth = 160, DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 8, 6) };
    private readonly CheckBox advanced = new() { Content = "Advanced profile (full scan)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
    private readonly List<IDisposable> owned = new();
    private readonly List<DataQueryView> panels = new();
    public int DocumentCount => tabs.Items.Count;
    public DataQueryView? ActiveQuery => tabs.SelectedItem is TabItem item ? item.Tag as DataQueryView : null;
    public PivotLabView? ActivePivot => (tabs.SelectedItem as TabItem)?.Content as PivotLabView;
    public DataWorkspaceView(Func<DataModelSchema> schema, Func<(string? Server, string? Database)> connection,
        Func<string?> transport, Func<string> artifactDirectory, IDaxQueryService? queries = null)
    {
        this.schema = schema; this.connection = connection; this.transport = transport; this.artifactDirectory = artifactDirectory; this.queries = queries ?? new TomDaxQueryService();
        var root = new DockPanel();
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Data exploration", FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        var actions = new WrapPanel(); top.Children.Add(actions);
        actions.Children.Add(tables); actions.Children.Add(Action("Preview table", () => OpenPreview(SelectedTable().Name)));
        actions.Children.Add(columns); actions.Children.Add(Action("Profile column", () => OpenColumnProfile(SelectedTable(), (columns.SelectedItem as DataColumnSchema)?.Name ?? throw new InvalidOperationException("Choose a column."))));
        actions.Children.Add(advanced);
        var second = new WrapPanel(); top.Children.Add(second); second.Children.Add(relationships);
        second.Children.Add(Action("Relationship coverage", () => OpenRelationshipProfile(relationships.SelectedItem as DataRelationshipSchema ?? throw new InvalidOperationException("Choose a relationship."))));
        second.Children.Add(Action("New Pivot Lab", OpenPivot));
        top.Children.Add(status); root.Children.Add(tabs); Content = root;
        tables.SelectionChanged += (_, _) => { columns.ItemsSource = (tables.SelectedItem as DataTableSchema)?.Columns; columns.SelectedIndex = 0; };
        RefreshSchema();
    }
    private DataTableSchema SelectedTable() => tables.SelectedItem as DataTableSchema ?? throw new InvalidOperationException("Open a semantic model and choose a table.");
    private Button Action(string title, System.Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 6) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }; return button;
    }
    public void RefreshSchema()
    {
        var model = schema(); var selected = (tables.SelectedItem as DataTableSchema)?.Name;
        tables.ItemsSource = model.Tables; tables.SelectedItem = model.Tables.FirstOrDefault(t => t.Name == selected) ?? model.Tables.FirstOrDefault();
        relationships.ItemsSource = model.Relationships; relationships.SelectedIndex = 0;
        foreach (var panel in panels) panel.Invalidate();
        foreach (var preview in owned.OfType<DataPreviewView>()) preview.Invalidate();
        status.Text = model.Tables.Count == 0 ? "Open or connect to a semantic model to choose tables, columns and measures." :
            "Queries run in independent sessions. Preview pages are bounded; profiles evaluate engine data and may scan complete tables.";
    }
    public void OpenPreview(string table)
    {
        var query = NewQuery(); var preview = new DataPreviewView(schema, table, connection, transport, queries, query);
        AddTab(table + " · Preview", preview, query, preview);
    }
    private DataQueryView NewQuery()
    {
        var query = new DataQueryView(connection, transport, queries); panels.Add(query); return query;
    }
    private void OpenColumnProfile(DataTableSchema table, string column)
    {
        var plan = DataProfileBuilder.Column(table, column, new DataProfileOptions { IncludeAdvanced = advanced.IsChecked == true });
        OpenProfile(plan);
    }
    private void OpenRelationshipProfile(DataRelationshipSchema relationship) => OpenProfile(DataProfileBuilder.Relationship(schema(), relationship, new DataProfileOptions { IncludeAdvanced = advanced.IsChecked == true }));
    private void OpenProfile(DataProfilePlan plan)
    {
        var query = NewQuery(); query.RowLimit = 1000; query.SetPlan(plan.Query, plan.Warnings, plan.ResultNames);
        AddTab(plan.Title, query, query, query);
    }
    public void OpenPivot()
    {
        var query = NewQuery(); var pivot = new PivotLabView(schema, query, artifactDirectory);
        AddTab("Pivot Lab", pivot, query, pivot);
    }
    private void AddTab(string title, FrameworkElement view, DataQueryView query, IDisposable lifetime)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal }; header.Children.Add(new TextBlock { Text = title, Margin = new Thickness(3, 4, 7, 4) });
        var tab = new TabItem { Header = header, Content = view, Tag = query };
        var close = new Button { Content = "×", Margin = new Thickness(0), Padding = new Thickness(4, 0, 4, 0) }; header.Children.Add(close);
        close.Click += (_, _) => { lifetime.Dispose(); panels.Remove(query); owned.Remove(lifetime); tab.Content = null; tabs.Items.Remove(tab); };
        owned.Add(lifetime); tabs.Items.Add(tab); tabs.SelectedItem = tab;
    }
    public void Dispose() { foreach (var view in owned) view.Dispose(); owned.Clear(); panels.Clear(); }
}
