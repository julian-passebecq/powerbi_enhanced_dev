using System.Windows;
using System.Windows.Controls;
using PbiBench.Core.DataExploration;
using PbiBench.Core.Queries;

namespace PbiBench.App;

public sealed class DataPreviewView : UserControl, IDisposable
{
    private readonly Func<DataModelSchema> schema;
    private readonly string tableName;
    private readonly Func<(string? Server, string? Database)> connection;
    private readonly Func<string?> transport;
    private readonly IDaxQueryService queries;
    private readonly DataQueryView query;
    private readonly ComboBox sort = new() { Width = 150, DisplayMemberPath = "Name" };
    private readonly CheckBox descending = new() { Content = "Descending", Margin = new Thickness(6) };
    private readonly ComboBox filter = new() { Width = 140, DisplayMemberPath = "Name" };
    private readonly ComboBox operation = new() { Width = 135, ItemsSource = Enum.GetValues(typeof(DataFilterOperator)) };
    private readonly TextBox filterValue = new() { Width = 115, ToolTip = "Typed value: invariant number, ISO date/time, true/false or text. Empty text is a text value, not BLANK." };
    private readonly CheckBox filterEnabled = new() { Content = "Filter", Margin = new Thickness(6) };
    private readonly TextBox size = new() { Width = 65, Text = "200" };
    private readonly TextBlock badge = new() { Margin = new Thickness(4, 5, 4, 10), TextWrapping = TextWrapping.Wrap };
    private readonly Button next;
    private readonly Button previous;
    private DataPreviewCapabilities capabilities = DataPreviewCapabilities.Unverified;
    private (string? Server, string? Database) verifiedTarget;
    private int offset;
    private CancellationTokenSource? verification;
    private bool disposed;
    private int metadataRevision;
    private bool canPage;

    public DataPreviewView(Func<DataModelSchema> schema, string tableName, Func<(string? Server, string? Database)> connection,
        Func<string?> transport, IDaxQueryService queries, DataQueryView query)
    {
        this.schema = schema; this.tableName = tableName; this.connection = connection; this.transport = transport; this.queries = queries; this.query = query;
        var table = schema().GetTable(tableName);
        var root = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(badge);
        var ordering = new WrapPanel(); top.Children.Add(ordering);
        ordering.Children.Add(new TextBlock { Text = "Sort / paging key", Margin = new Thickness(5) }); ordering.Children.Add(sort); ordering.Children.Add(descending);
        ordering.Children.Add(new TextBlock { Text = "Rows", Margin = new Thickness(5) }); ordering.Children.Add(size);
        ordering.Children.Add(Action("Verify paging key…", async () => await VerifyAsync()));
        ordering.Children.Add(Action("Cancel verification", () => { verification?.Cancel(); return Task.CompletedTask; }));
        var filters = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) }; top.Children.Add(filters);
        filters.Children.Add(filterEnabled); filters.Children.Add(filter); filters.Children.Add(operation); filters.Children.Add(filterValue);
        filters.Children.Add(Action("Apply sort / filter", async () => { offset = 0; BuildPlan(); await query.RunAsync(); }));
        previous = Action("Previous page", async () => { offset = Math.Max(0, offset - PageSize()); BuildPlan(); await query.RunAsync(); });
        next = Action("Next page", async () => { offset = checked(offset + PageSize()); BuildPlan(); await query.RunAsync(); });
        var pages = new WrapPanel(); pages.Children.Add(previous); pages.Children.Add(next); top.Children.Add(pages);
        sort.ItemsSource = table.Columns; sort.SelectedItem = table.Columns.FirstOrDefault(c => table.CandidateKeyColumns.Contains(c.Name)) ?? table.Columns.FirstOrDefault();
        filter.ItemsSource = table.Columns; filter.SelectedIndex = 0; operation.SelectedItem = DataFilterOperator.Equals;
        root.Children.Add(query); Content = root;
        query.RefreshRequested += (_, _) => { offset = 0; BuildPlan(); };
        query.Completed += (_, _) => next.IsEnabled = canPage && (query.LastResult?.Results.FirstOrDefault()?.Rows.Count ?? 0) >= PageSize();
        BuildPlan();
    }
    private Button Action(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(5, 0, 0, 6) };
        button.Click += async (_, _) => { try { await action(); } catch (OperationCanceledException) { } catch (Exception ex) { query.SetError(ex); } }; return button;
    }
    private int PageSize() => int.TryParse(size.Text, out var count) && count >= 1 && count <= 10000 ? count : throw new InvalidOperationException("Choose 1 to 10,000 displayed rows.");
    private void BuildPlan()
    {
        var model = schema(); var table = model.GetTable(tableName);
        if (connection() != verifiedTarget) capabilities = DataPreviewCapabilities.Unverified;
        var filters = filterEnabled.IsChecked == true && filter.SelectedItem is DataColumnSchema column
            ? new[] { new DataFilter(tableName, column.Name, (DataFilterOperator)operation.SelectedItem, filterValue.Text) } : Array.Empty<DataFilter>();
        var request = new DataPreviewRequest(tableName, offset, PageSize()) { Sort = sort.SelectedItem is DataColumnSchema order ? new[] { new DataSort(order.Name, descending.IsChecked == true) } : Array.Empty<DataSort>(), Filters = filters };
        var plan = DataPreviewBuilder.Build(model, request, capabilities); offset = plan.Offset; canPage = plan.CanPage;
        query.RowLimit = plan.PageSize; query.SetPlan(plan.Query, plan.Warnings);
        previous.IsEnabled = plan.CanPage && offset > 0; next.IsEnabled = plan.CanPage;
        badge.Text = tableName + " · " + table.StorageMode + " · " + (plan.CanPage ? $"WINDOW rows {offset + 1:N0}–{offset + plan.PageSize:N0}" : $"First {plan.PageSize:N0} rows") + "\n" + capabilities.VerificationMessage;
    }
    private async Task VerifyAsync()
    {
        if (verification != null) throw new InvalidOperationException("A paging verification is already running.");
        var target = connection(); var table = schema().GetTable(tableName); var revision = metadataRevision;
        if (string.IsNullOrWhiteSpace(target.Server) || string.IsNullOrWhiteSpace(target.Database)) throw new InvalidOperationException("Connect to an engine before verifying paging.");
        if (sort.SelectedItem is DataColumnSchema key) table = table with { CandidateKeyColumns = new[] { key.Name } };
        badge.Text = "Verifying engine WINDOW support and key uniqueness. This checks the whole key column; cancellation is available.";
        var cancellation = verification = new CancellationTokenSource();
        try
        {
            var request = new QueryRequest(target.Server!, target.Database!, "EVALUATE ROW ( \"Probe\", 1 )", 2, 60) { ConnectionString = transport() };
            var verified = await new DataPreviewCapabilityService(queries).VerifyPagingAsync(request, table, cancellation.Token);
            if (disposed || connection() != target || revision != metadataRevision) return;
            capabilities = verified; verifiedTarget = target; offset = 0; BuildPlan();
        }
        finally { verification = null; cancellation.Dispose(); }
    }
    public void Invalidate() { metadataRevision++; verification?.Cancel(); capabilities = DataPreviewCapabilities.Unverified; query.Invalidate(); previous.IsEnabled = false; next.IsEnabled = false; }
    public void Dispose() { disposed = true; verification?.Cancel(); query.Dispose(); }
}
