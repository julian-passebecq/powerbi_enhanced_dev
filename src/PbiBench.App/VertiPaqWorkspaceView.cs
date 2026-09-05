using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PbiBench.Core.Quality;
using PbiBench.Core.Queries;
using PbiBench.Core.Tasks;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

/// <summary>Original PbiBench quality workspace over bounded statistics and query services.</summary>
public sealed class VertiPaqWorkspaceView : UserControl, IDisposable
{
    private readonly IVpaxSnapshotReader reader;
    private readonly IVertiPaqSnapshotService metrics;
    private readonly QueryBenchmarkService benchmark;
    private readonly BackgroundTaskQueue queue;
    private readonly bool ownsQueue;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly TextBlock snapshotInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly TextBlock benchmarkInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly TextBox baseline = Editor("EVALUATE ROW(\"Value\", 1)");
    private readonly TextBox candidate = Editor("EVALUATE ROW(\"Value\", 1)");
    private readonly TextBox iterations = new() { Text = "3", Width = 42, Margin = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly DataGrid tableGrid = Grid(), columnGrid = Grid(), partitionGrid = Grid(), segmentGrid = Grid(), relationshipGrid = Grid(), signalGrid = Grid(), benchmarkGrid = Grid();
    private readonly Button capture, import, cancel, runBenchmark, navigateButton, profileButton;
    private TabularModelHandler? handler;
    private string? server, database;
    private string? modelFingerprint;
    private string? selectedTable, selectedColumn;
    private Action<string, string?>? navigate, profile;
    private IReadOnlyList<OptimizationSignal> externalSignals = Array.Empty<OptimizationSignal>();
    private readonly List<OptimizationSignal> userSignals = new();
    private CancellationTokenSource? pending;
    private bool disposed, bound;
    private long revision;
    public VertiPaqSnapshot? Snapshot { get; private set; }
    public QueryBenchmarkEvidence? BenchmarkEvidence { get; private set; }
    public int TableCount => Snapshot?.Tables.Count ?? 0;
    public int SignalCount => signalGrid.Items.Count;
    public bool IsRunning => pending != null;
    public string Status => status.Text;

    public VertiPaqWorkspaceView(BackgroundTaskQueue? backgroundTasks = null, IVpaxSnapshotReader? reader = null,
        IVertiPaqSnapshotService? metrics = null, IDaxQueryService? queries = null)
    {
        this.reader = reader ?? new VpaxSnapshotReader(); this.metrics = metrics ?? new TomVertiPaqSnapshotService();
        benchmark = new QueryBenchmarkService(queries ?? new TomDaxQueryService());
        queue = backgroundTasks ?? new BackgroundTaskQueue(); ownsQueue = backgroundTasks == null;
        var root = new DockPanel();
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var tools = new WrapPanel(); top.Children.Add(tools);
        import = Button("Import VPAX…", async () =>
        {
            var dialog = new OpenFileDialog { Filter = "VertiPaq Analyzer snapshot|*.vpax", CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) await LoadSnapshotAsync(dialog.FileName, CancellationToken.None);
        });
        capture = Button("Capture live metrics", CaptureAsync); cancel = Button("Cancel", () => { pending?.Cancel(); return Task.CompletedTask; });
        tools.Children.Add(import); tools.Children.Add(capture); tools.Children.Add(cancel);
        tools.Children.Add(Button("Use current model for navigation", () => { BindCurrentModel(); return Task.CompletedTask; }));
        navigateButton = Button("Go to object", () => { Navigate(false); return Task.CompletedTask; });
        profileButton = Button("Profile selected column", () => { Navigate(true); return Task.CompletedTask; });
        tools.Children.Add(navigateButton); tools.Children.Add(profileButton);
        tools.Children.Add(Button("Create optimization finding", () => { CreateFinding(); return Task.CompletedTask; }));
        top.Children.Add(snapshotInfo);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var tabs = new TabControl(); root.Children.Add(tabs);
        tabs.Items.Add(new TabItem { Header = "Tables", Content = tableGrid }); tabs.Items.Add(new TabItem { Header = "Columns", Content = columnGrid });
        tabs.Items.Add(new TabItem { Header = "Partitions", Content = partitionGrid }); tabs.Items.Add(new TabItem { Header = "Relationships", Content = relationshipGrid });
        tabs.Items.Add(new TabItem { Header = "Segments / temperature", Content = segmentGrid }); tabs.Items.Add(new TabItem { Header = "Optimization cockpit", Content = signalGrid });
        var benchmarkPage = new DockPanel();
        var benchmarkTools = new WrapPanel(); DockPanel.SetDock(benchmarkTools, Dock.Top); benchmarkPage.Children.Add(benchmarkTools);
        benchmarkTools.Children.Add(new TextBlock { Text = "Iterations per query", VerticalAlignment = VerticalAlignment.Center }); benchmarkTools.Children.Add(iterations);
        runBenchmark = Button("Run A/B benchmark", RunBenchmarkAsync); benchmarkTools.Children.Add(runBenchmark);
        benchmarkTools.Children.Add(Button("Export evidence JSON…", ExportBenchmarkAsync));
        var benchmarkNote = new StackPanel(); DockPanel.SetDock(benchmarkNote, Dock.Bottom); benchmarkPage.Children.Add(benchmarkNote);
        benchmarkNote.Children.Add(new TextBlock { Text = "Alternating read-only DAX executions · 60 seconds per query · 10,000 rows / 250,000 cells per result capture · no cache clearing or server trace. Results must match exactly, including order and types, before timings are compared.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(6) });
        benchmarkNote.Children.Add(benchmarkInfo);
        var benchmarkContent = new System.Windows.Controls.Grid(); benchmarkContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) }); benchmarkContent.RowDefinitions.Add(new RowDefinition());
        var editors = new System.Windows.Controls.Grid(); editors.ColumnDefinitions.Add(new ColumnDefinition()); editors.ColumnDefinitions.Add(new ColumnDefinition());
        editors.Children.Add(Labeled("Baseline DAX", baseline)); var candidatePanel = Labeled("Candidate DAX", candidate); System.Windows.Controls.Grid.SetColumn(candidatePanel, 1); editors.Children.Add(candidatePanel);
        benchmarkContent.Children.Add(editors); System.Windows.Controls.Grid.SetRow(benchmarkGrid, 1); benchmarkContent.Children.Add(benchmarkGrid); benchmarkPage.Children.Add(benchmarkContent);
        tabs.Items.Add(new TabItem { Header = "A/B benchmark", Content = benchmarkPage });
        tableGrid.SelectionChanged += (_, _) => { if (tableGrid.SelectedItem is VertiPaqTable table) Select(table.Name, null); };
        columnGrid.SelectionChanged += (_, _) => { if (columnGrid.SelectedItem is VertiPaqColumn column) Select(column.Table, column.Name); };
        segmentGrid.SelectionChanged += (_, _) => { if (segmentGrid.SelectedItem is VertiPaqSegment segment) Select(segment.Table, segment.Column); };
        partitionGrid.SelectionChanged += (_, _) => { if (partitionGrid.SelectedItem is VertiPaqPartition partition) Select(partition.Table, null); };
        relationshipGrid.SelectionChanged += (_, _) => { if (relationshipGrid.SelectedItem is VertiPaqRelationship relationship) Select(relationship.FromTable, relationship.FromColumn); };
        signalGrid.SelectionChanged += (_, _) => { if (signalGrid.SelectedItem is OptimizationSignal signal) Select(signal.Table, signal.Column); };
        Content = root; snapshotInfo.Text = "Import a VPAX snapshot or connect to a model engine and capture public storage metrics. Values not collected remain blank.";
        UpdateButtons();
    }

    public void Configure(TabularModelHandler? handler, string? server, string? database, Action<string, string?> navigate, Action<string, string?> profile)
    {
        var fingerprint = handler == null ? null : new SemanticModelService(handler).Fingerprint();
        if (!ReferenceEquals(this.handler, handler) || this.server != server || this.database != database || modelFingerprint != fingerprint)
        { revision++; bound = false; pending?.Cancel(); BenchmarkEvidence = null; benchmarkGrid.ItemsSource = null; benchmarkInfo.Text = "Model or connection changed. Run a fresh benchmark for the current target."; }
        this.handler = handler; this.server = server; this.database = database; this.navigate = navigate; this.profile = profile; modelFingerprint = fingerprint;
        UpdateButtons();
    }
    public void SetQualitySignals(IReadOnlyList<OptimizationSignal> signals) { externalSignals = signals ?? Array.Empty<OptimizationSignal>(); UpdateSignals(); }
    public void ShowSnapshot(VertiPaqSnapshot snapshot)
    {
        if (disposed) return;
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot)); bound = false; selectedTable = null; selectedColumn = null; userSignals.Clear();
        tableGrid.ItemsSource = snapshot.Tables.OrderByDescending(table => table.TotalBytes).ThenBy(table => table.Name).ToArray();
        columnGrid.ItemsSource = snapshot.Columns.OrderByDescending(column => column.TotalBytes).ThenBy(column => column.Table).ThenBy(column => column.Name).ToArray();
        partitionGrid.ItemsSource = snapshot.Partitions; segmentGrid.ItemsSource = snapshot.Segments; relationshipGrid.ItemsSource = snapshot.Relationships;
        snapshotInfo.Text = $"{snapshot.Source} · {snapshot.ModelName} · captured {snapshot.CapturedAt:O} · {snapshot.Tables.Count:N0} tables / {snapshot.Columns.Count:N0} columns\n" +
            (snapshot.TotalBytes.HasValue ? $"Captured storage total: {snapshot.TotalBytes:N0} bytes." : "Storage total unavailable because some metric components were not captured.") +
            "\n" + string.Join("\n", snapshot.Warnings);
        status.Text = "Snapshot loaded. Bind it explicitly to the current model to navigate or profile captured object names."; UpdateSignals(); UpdateButtons();
    }
    public async Task LoadSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        await RunWorkAsync("Import VPAX", async token =>
        {
            var snapshot = await reader.ReadAsync(path, token); return () => ShowSnapshot(snapshot);
        }, cancellationToken);
    }
    private Task CaptureAsync()
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database)) throw new InvalidOperationException("Connect to a model engine before capturing storage metrics. Local model files contain no VertiPaq data.");
        var request = new VertiPaqCaptureRequest(server!, database!) { ConnectionString = Transport() };
        return RunWorkAsync("Capture VertiPaq metrics", async token =>
        {
            var snapshot = await metrics.CaptureAsync(request, token);
            return () => { ShowSnapshot(snapshot); if (handler != null && server == request.Server && database == request.Database) BindCurrentModel(); };
        }, CancellationToken.None);
    }
    private Task RunBenchmarkAsync()
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database)) throw new InvalidOperationException("Connect to a model engine to benchmark DAX.");
        if (!int.TryParse(iterations.Text, out var repeat) || repeat < 1 || repeat > 10) throw new InvalidOperationException("Choose 1 to 10 iterations per query.");
        var request = new QueryBenchmarkRequest(server!, database!, baseline.Text, candidate.Text, repeat, ModelFingerprint: handler == null ? null : new SemanticModelService(handler).Fingerprint()) { ConnectionString = Transport() };
        BenchmarkEvidence = null; benchmarkGrid.ItemsSource = null; benchmarkInfo.Text = "A fresh benchmark is queued / running. Previous timings have been cleared.";
        return RunWorkAsync("A/B DAX benchmark", async token =>
        {
            var evidence = await benchmark.RunAsync(request, token);
            return () =>
            {
                if (handler != null && new SemanticModelService(handler).Fingerprint() != request.ModelFingerprint) { status.Text = "Model metadata changed during the benchmark. Rerun before accepting this evidence."; return; }
                BenchmarkEvidence = evidence; benchmarkGrid.ItemsSource = evidence.Samples; benchmarkInfo.Text = evidence.Summary + "\n" + string.Join("\n", evidence.Warnings); status.Text = "Benchmark complete. " + evidence.Summary;
            };
        }, CancellationToken.None);
    }
    private async Task RunWorkAsync(string title, Func<CancellationToken, Task<Action>> work, CancellationToken caller)
    {
        if (disposed) throw new ObjectDisposedException(nameof(VertiPaqWorkspaceView));
        if (pending != null) throw new InvalidOperationException("Cancel or wait for the current operation first.");
        var version = revision; var cancellation = pending = CancellationTokenSource.CreateLinkedTokenSource(caller);
        status.Text = title + " queued / running…"; UpdateButtons();
        try
        {
            var job = queue.Enqueue(title, context => work(context.CancellationToken), cancellation.Token);
            var show = await job.Completion;
            cancellation.Token.ThrowIfCancellationRequested();
            if (!disposed && version == revision) show();
            else if (!disposed) status.Text = "The connection changed during the operation. The stale result was discarded.";
        }
        catch (OperationCanceledException) { if (!disposed) status.Text = "Canceled."; }
        catch (Exception ex) { if (!disposed) status.Text = ex.Message; }
        finally { cancellation.Dispose(); pending = null; if (!disposed) UpdateButtons(); }
    }
    private void BindCurrentModel()
    {
        if (handler == null || Snapshot == null) throw new InvalidOperationException("Open a model and load a snapshot first.");
        var matching = Snapshot.Tables.All(captured => handler.Model.Tables.Any(table => table.Name == captured.Name)) &&
            Snapshot.Columns.All(column => handler.Model.Tables.Any(table => table.Name == column.Table &&
                (column.Name.StartsWith("RowNumber-", StringComparison.Ordinal) || table.Columns.Any(item => item.Name == column.Name))));
        if (!matching) throw new InvalidOperationException("Snapshot object names differ from the current model. Open its matching model before binding navigation.");
        Configure(handler, server, database, navigate!, profile!);
        bound = true; status.Text = "Snapshot object navigation is bound to the current model by your explicit selection. Captured data and current data may differ."; UpdateButtons();
    }
    private void Navigate(bool dataProfile)
    {
        if (!bound || handler == null || selectedTable == null) throw new InvalidOperationException("Select a captured object and bind the snapshot to the current model first.");
        if (new SemanticModelService(handler).Fingerprint() != modelFingerprint)
        { bound = false; UpdateButtons(); throw new InvalidOperationException("Model metadata changed. Bind the snapshot to the current model again before navigating."); }
        var table = handler.Model.Tables.FirstOrDefault(item => item.Name == selectedTable);
        var isColumn = selectedColumn != null && table?.Columns.Any(item => item.Name == selectedColumn) == true;
        if (dataProfile && !isColumn) throw new InvalidOperationException("Select a model column to profile. Measures and internal storage columns cannot be profiled as model columns.");
        if (table == null || (selectedColumn != null && !isColumn && !table.Measures.Any(item => item.Name == selectedColumn)))
            throw new InvalidOperationException("The selected captured object has no editable counterpart in the current model.");
        if (dataProfile) profile?.Invoke(selectedTable, selectedColumn); else navigate?.Invoke(selectedTable, selectedColumn);
    }
    private void CreateFinding()
    {
        var column = Snapshot?.Columns.FirstOrDefault(item => item.Table == selectedTable && item.Name == selectedColumn);
        if (column == null) throw new InvalidOperationException("Select a captured column to create an evidence-based finding.");
        userSignals.Add(new("USER:" + Guid.NewGuid().ToString("N"), "Selected VertiPaq metric", "Size", "BENCHMARK", "Investigate selected column",
            $"Captured size {column.TotalBytes:N0} bytes; cardinality {column.Cardinality:N0}. Unknown values are not zero.", column.Table, column.Name, "Profile values, review intended usage and retain a representative before/after benchmark."));
        UpdateSignals(); status.Text = "Optimization finding created from the captured column. Review it in Optimization cockpit.";
    }
    private void UpdateSignals() => signalGrid.ItemsSource = VertiPaqOptimization.Build(Snapshot, externalSignals.Concat(userSignals));
    private void Select(string? table, string? column) { selectedTable = table; selectedColumn = column; UpdateButtons(); }
    private string? Transport() => handler?.IsConnected == true ? handler.Database.Server.ConnectionString : null;
    private async Task ExportBenchmarkAsync()
    {
        var evidence = BenchmarkEvidence ?? throw new InvalidOperationException("Run a benchmark before exporting its evidence.");
        var dialog = new SaveFileDialog { Filter = "Benchmark evidence JSON|*.json", FileName = "pbibench-benchmark.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) { await QueryBenchmarkStore.SaveAsync(evidence, dialog.FileName, CancellationToken.None); status.Text = "Benchmark evidence saved."; }
    }
    private void UpdateButtons()
    {
        if (capture == null) return;
        import.IsEnabled = capture.IsEnabled = runBenchmark.IsEnabled = !disposed && pending == null;
        cancel.IsEnabled = pending != null; navigateButton.IsEnabled = bound && selectedTable != null;
        profileButton.IsEnabled = bound && selectedColumn != null && handler?.Model.Tables.Any(table =>
            table.Name == selectedTable && table.Columns.Any(column => column.Name == selectedColumn)) == true;
    }
    private Button Button(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { if (!disposed) status.Text = ex.Message; } }; return button;
    }
    private static DataGrid Grid() => new() { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, EnableColumnVirtualization = true, EnableRowVirtualization = true, SelectionMode = DataGridSelectionMode.Single, ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader };
    private static TextBox Editor(string text) => new() { Text = text, AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(5) };
    private static FrameworkElement Labeled(string label, UIElement control)
    {
        var panel = new DockPanel(); var title = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(5) }; DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title); panel.Children.Add(control); return panel;
    }
    public void Dispose() { if (disposed) return; disposed = true; revision++; pending?.Cancel(); if (ownsQueue) queue.Dispose(); }
}
