using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PbiBench.Core.Queries;

namespace PbiBench.App;

/// <summary>A cancellable, independently connected read-only query panel shared by exploration tools.</summary>
public sealed class DataQueryView : UserControl, IDisposable
{
    private readonly Func<(string? Server, string? Database)> connection;
    private readonly Func<string?> transport;
    private readonly IDaxQueryService queries;
    private readonly TabControl results = new();
    private readonly TextBox dax = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly Button run = new() { Content = "Run / refresh", Margin = new Thickness(0, 0, 8, 6) };
    private readonly Button cancel = new() { Content = "Cancel", IsEnabled = false, Margin = new Thickness(0, 0, 8, 6) };
    private CancellationTokenSource? pending;
    private long revision;
    private bool disposed;
    private bool validPlan;
    private IReadOnlyList<string> resultNames = Array.Empty<string>();
    public QueryResult? LastResult { get; private set; }
    public string Query => dax.Text;
    public string Status => status.Text;
    public int ResultCount => results.Items.Count;
    public bool IsRunning => pending != null;
    public int RowLimit { get; set; } = 1000;
    public event EventHandler? Completed;
    public event EventHandler? RefreshRequested;

    public DataQueryView(Func<(string? Server, string? Database)> connection, Func<string?> transport, IDaxQueryService queries)
    {
        this.connection = connection; this.transport = transport; this.queries = queries;
        var root = new DockPanel();
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var buttons = new WrapPanel(); buttons.Children.Add(run); buttons.Children.Add(cancel); top.Children.Add(buttons); top.Children.Add(notice);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var output = new TabControl(); output.Items.Add(new TabItem { Header = "Results", Content = results }); output.Items.Add(new TabItem { Header = "Generated DAX", Content = dax }); root.Children.Add(output);
        Content = root;
        run.Click += async (_, _) => { try { RefreshRequested?.Invoke(this, EventArgs.Empty); await RunAsync(); } catch (OperationCanceledException) { } catch (Exception ex) { SetError(ex); } };
        cancel.Click += (_, _) => Cancel();
    }

    public void SetPlan(string query, IEnumerable<string> warnings, IReadOnlyList<string>? names = null)
    {
        if (disposed) return;
        revision++; validPlan = true; run.IsEnabled = pending == null; dax.Text = query; notice.Text = string.Join("\n", warnings); resultNames = names ?? Array.Empty<string>();
        LastResult = null; results.Items.Clear();
        status.Text = "Ready. Generated DAX is available for review. Display caps do not limit engine work.";
    }

    public async Task RunAsync()
    {
        if (disposed) throw new ObjectDisposedException(nameof(DataQueryView));
        if (pending != null) throw new InvalidOperationException("Cancel or wait for this query before running it again.");
        if (!validPlan) throw new InvalidOperationException("The model changed. Reopen this exploration to generate a current query.");
        var target = connection();
        if (string.IsNullOrWhiteSpace(target.Server) || string.IsNullOrWhiteSpace(target.Database)) throw new InvalidOperationException("Connect to a model engine to inspect data. Local BIM/TMDL files contain metadata only.");
        var version = revision;
        var request = new QueryRequest(target.Server!, target.Database!, Query, RowLimit, 60, version) { ConnectionString = transport(), MaximumCells = 250000 };
        var cancellation = pending = new CancellationTokenSource();
        run.IsEnabled = false; cancel.IsEnabled = true; status.Text = "Running on " + target.Server + " / " + target.Database + ". Timeout: 60 seconds.";
        try
        {
            var result = await queries.ExecuteAsync(request, cancellation.Token);
            if (disposed) return;
            if (version != revision || connection() != target) { status.Text = "Query completed after its plan or connection changed. Refresh to obtain current results."; return; }
            ShowResults(result);
        }
        catch (OperationCanceledException) { if (!disposed) status.Text = "Canceled."; throw; }
        catch (Exception ex) { if (!disposed) SetError(ex); throw; }
        finally { cancellation.Dispose(); pending = null; if (!disposed) { run.IsEnabled = validPlan; cancel.IsEnabled = false; } }
    }

    public void ShowResults(QueryResult result)
    {
        LastResult = result; results.Items.Clear();
        foreach (var set in result.Results)
        {
            var panel = new DockPanel();
            var export = new Button { Content = "Export CSV…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(5) };
            export.Click += async (_, _) =>
            {
                try { var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "data-result.csv" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) await QueryCsv.ExportAsync(set, dialog.FileName, CancellationToken.None); }
                catch (Exception ex) { SetError(ex); }
            };
            DockPanel.SetDock(export, Dock.Top); panel.Children.Add(export);
            var grid = new DataGrid { ItemsSource = set.ToDataTable().DefaultView, AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader };
            grid.AutoGeneratingColumn += (_, e) => e.Column.Header = set.Columns.FirstOrDefault(c => c.Key == e.PropertyName)?.Name ?? e.PropertyName;
            panel.Children.Add(grid);
            var title = set.Index < resultNames.Count ? resultNames[set.Index] : set.Name;
            results.Items.Add(new TabItem { Header = title + " · " + set.Rows.Count + (set.IsTruncated ? "+" : ""), Content = panel });
        }
        if (results.Items.Count > 0) results.SelectedIndex = 0;
        status.Text = $"{result.Elapsed.TotalMilliseconds:N0} ms · {result.Results.Sum(s => s.Rows.Count):N0} displayed rows · {result.Server} / {result.Database}" +
            (result.Results.Any(s => s.IsTruncated) ? "\nDisplay limit reached. Sorting in a result grid sorts retained rows only." : "") + "\n" + string.Join("\n", result.Warnings);
        Completed?.Invoke(this, EventArgs.Empty);
    }
    public void Cancel() { pending?.Cancel(); if (!disposed) status.Text = "Canceling this query session…"; }
    public void Invalidate() { revision++; validPlan = false; run.IsEnabled = false; Cancel(); LastResult = null; results.Items.Clear(); status.Text = "Model metadata changed. Reopen this exploration or refresh its plan."; }
    public void SetError(Exception error) { if (!disposed) status.Text = error.Message; }
    public void Dispose() { if (disposed) return; disposed = true; revision++; pending?.Cancel(); }
}
