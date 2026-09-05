using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PbiBench.Core.Quality;
using PbiBench.Core.Queries;
using PbiBench.Core.Tasks;

namespace PbiBench.App;

/// <summary>Project test drafts and explicit baseline capture; no live model wrappers are edited here.</summary>
public sealed class SemanticTestsView : UserControl, IDisposable
{
    private readonly Func<(string? Server, string? Database)> connection;
    private readonly Func<string?> transport;
    private readonly SemanticTestService service;
    private readonly BackgroundTaskQueue queue;
    private readonly List<SemanticTestDefinition> tests = new();
    private readonly ListBox list = new() { DisplayMemberPath = "Name", MinWidth = 150 };
    private readonly TextBox name = new();
    private readonly ComboBox kind = new() { ItemsSource = Enum.GetValues(typeof(SemanticTestKind)) };
    private readonly ComboBox comparison = new() { ItemsSource = Enum.GetValues(typeof(SemanticComparison)) };
    private readonly ComboBox valueKind = new() { ItemsSource = Enum.GetValues(typeof(SemanticValueKind)) };
    private readonly TextBox expected = new();
    private readonly TextBox column = new() { Text = "1", Width = 55 };
    private readonly TextBox rowCount = new() { Text = "1", Width = 90 };
    private readonly TextBox absolute = new() { Text = "0", Width = 90 };
    private readonly TextBox relative = new() { Text = "0", Width = 90 };
    private readonly TextBox limit = new() { Text = "10000", Width = 90 };
    private readonly TextBox timeout = new() { Text = "60", Width = 60 };
    private readonly CheckBox ordered = new() { Content = "I confirm deterministic row order for snapshots and A/B results", Margin = new Thickness(4) };
    private readonly TextBox query = QueryBox();
    private readonly TextBox secondQuery = QueryBox();
    private readonly TextBlock baseline = new() { Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid results = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, MinHeight = 130 };
    private SemanticTestDefinition? selected;
    private long revision;
    private bool loading;
    private bool disposed;
    private Guid? activeTask;
    private IReadOnlyList<SemanticTestResult> lastResults = Array.Empty<SemanticTestResult>();
    private (string? Server, string? Database)? resultTarget;
    public IReadOnlyList<SemanticTestResult> LastResults => resultTarget.HasValue && resultTarget.Value == connection() ? lastResults : Array.Empty<SemanticTestResult>();
    public event EventHandler? ResultsChanged;

    public SemanticTestsView(Func<(string? Server, string? Database)> connection, Func<string?> transport, IDaxQueryService queries, BackgroundTaskQueue queue)
    {
        this.connection = connection; this.transport = transport; service = new SemanticTestService(queries); this.queue = queue;
        var outer = new DockPanel { Margin = new Thickness(8) }; var tools = new WrapPanel();
        Button(tools, "New", () => { CommitDraft(); tests.Add(new SemanticTestDefinition()); RefreshList(tests.Last()); });
        Button(tools, "Remove", () => { if (selected != null) tests.RemoveAll(test => test.Id == selected.Id); revision++; ClearResults(); RefreshList(tests.FirstOrDefault()); });
        Button(tools, "Load tests…", () => RunUi(LoadAsync)); Button(tools, "Save tests…", () => RunUi(SaveAsync));
        Button(tools, "Run selected", () => RunUi(() => RunTestsAsync(false))); Button(tools, "Run all", () => RunUi(() => RunTestsAsync(true)));
        Button(tools, "Cancel", () => { if (activeTask.HasValue) queue.Cancel(activeTask.Value); });
        Button(tools, "Export report…", () => RunUi(ExportReportAsync));
        DockPanel.SetDock(tools, Dock.Top); outer.Children.Add(tools); DockPanel.SetDock(status, Dock.Bottom); outer.Children.Add(status);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(list); var editor = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        editor.Children.Add(new TextBlock { Text = "Semantic DAX tests", FontSize = 20, Margin = new Thickness(4) });
        editor.Children.Add(new TextBlock { Text = "Run read-only assertions against the connected model. Test files and reports contain DAX and expected values, without transport credentials. Table assertions check every row in one column; ordered snapshots and A/B comparisons check the entire schema and result.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        Row(editor, ("Name", name), ("Assertion", kind), ("Comparison", comparison));
        Row(editor, ("Expected type", valueKind), ("Expected value", expected), ("Column (1-based)", column), ("Expected rows", rowCount));
        Row(editor, ("Absolute tolerance", absolute), ("Relative tolerance", relative), ("Row limit", limit), ("Timeout seconds", timeout));
        editor.Children.Add(ordered); editor.Children.Add(new TextBlock { Text = "DAX query A", Margin = new Thickness(4) }); editor.Children.Add(query);
        editor.Children.Add(new TextBlock { Text = "DAX query B (CompareQueries only)", Margin = new Thickness(4) }); editor.Children.Add(secondQuery);
        var snapshotTools = new WrapPanel(); Button(snapshotTools, "Capture expected snapshot…", () => RunUi(CaptureAsync)); editor.Children.Add(snapshotTools); editor.Children.Add(baseline);
        foreach (var field in new[] { "Name", "Outcome", "Evidence", "ElapsedMilliseconds" }) results.Columns.Add(new DataGridTextColumn { Header = field, Binding = new Binding(field), Width = field == "Evidence" ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(140) });
        editor.Children.Add(results); var scroll = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll); outer.Children.Add(body); Content = outer;
        foreach (var box in new[] { name, expected, column, rowCount, absolute, relative, limit, timeout, query, secondQuery }) box.TextChanged += (_, _) => DraftChanged();
        foreach (var combo in new[] { kind, comparison, valueKind }) combo.SelectionChanged += (_, _) => DraftChanged();
        ordered.Checked += (_, _) => DraftChanged(); ordered.Unchecked += (_, _) => DraftChanged();
        list.SelectionChanged += (_, _) => { if (loading) return; var nextId = (list.SelectedItem as SemanticTestDefinition)?.Id; try { CommitDraft(); RefreshList(tests.FirstOrDefault(test => test.Id == nextId)); } catch (Exception error) { status.Text = error.Message; loading = true; list.SelectedItem = list.Items.Cast<SemanticTestDefinition>().FirstOrDefault(test => test.Id == selected?.Id); loading = false; } };
        tests.Add(new SemanticTestDefinition()); RefreshList(tests[0]);
        status.Text = "Choose a connected model, author a test, and run it. A test passes only after a complete matching engine result satisfies its assertion.";
    }

    private static TextBox QueryBox() => new() { AcceptsReturn = true, AcceptsTab = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 13, Height = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(4) };
    private static void Row(Panel panel, params (string Label, Control Control)[] fields)
    {
        var row = new WrapPanel(); foreach (var field in fields) { var item = new StackPanel { Margin = new Thickness(4) }; item.Children.Add(new TextBlock { Text = field.Label }); field.Control.MinWidth = Math.Max(65, field.Control.MinWidth); if (double.IsNaN(field.Control.Width)) field.Control.Width = 180; item.Children.Add(field.Control); row.Children.Add(item); } panel.Children.Add(row);
    }
    private void Button(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(8, 5, 8, 5) }; button.Click += (_, _) => { try { action(); } catch (Exception error) { status.Text = error.Message; } }; panel.Children.Add(button);
    }
    private async void RunUi(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { if (!disposed) status.Text = "Operation canceled; no passing result or baseline was produced."; }
        catch (Exception error) { if (!disposed) status.Text = error is FormatException || error is OverflowException ? "Enter valid invariant numbers for column, row count, tolerances and query limits." : error is InvalidDataException || error is ArgumentException || error is InvalidOperationException ? error.Message : "The operation failed. Check the file, connection and DAX, then retry."; }
    }
    private void DraftChanged() { if (!loading) { revision++; ClearResults(); } }
    private void ClearResults()
    {
        var changed = lastResults.Count != 0 || resultTarget.HasValue;
        lastResults = Array.Empty<SemanticTestResult>(); resultTarget = null; results.ItemsSource = lastResults;
        if (changed) ResultsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void RefreshModel()
    {
        revision++; ClearResults();
        if (activeTask.HasValue) queue.Cancel(activeTask.Value);
        status.Text = "The model context changed. Test drafts are retained; run again to obtain current results.";
    }
    public void LoadArtifact(SemanticTestArtifact artifact)
    {
        SemanticTestArtifactStore.Validate(artifact); tests.Clear(); tests.AddRange(artifact.Tests.Select(test => test.Snapshot == null ? test : test with
        { Snapshot = test.Snapshot with { Columns = test.Snapshot.Columns.ToArray(), Rows = test.Snapshot.Rows.Select(row => (IReadOnlyList<SemanticValue>)row.ToArray()).ToArray() } }));
        revision++; ClearResults(); RefreshList(tests.FirstOrDefault());
        status.Text = $"Loaded {tests.Count} tests. No query has run.";
    }
    /// <summary>Stages proposed tests alongside the user's current suite and unsaved editor draft.</summary>
    public void AppendArtifact(SemanticTestArtifact artifact)
    {
        var frozen = SemanticTestArtifactStore.Deserialize(SemanticTestArtifactStore.Serialize(artifact));
        if (frozen.Tests.Count == 0 || tests.Count + frozen.Tests.Count > 200) throw new InvalidDataException("Stage at least one test without exceeding the 200-test suite limit.");
        CommitDraft();
        var ids = new HashSet<string>(tests.Select(test => test.Id), StringComparer.Ordinal);
        var added = frozen.Tests.Select(test => { var id = test.Id; while (!ids.Add(id)) id = Guid.NewGuid().ToString("N"); return test with { Id = id }; }).ToArray();
        tests.AddRange(added); revision++; if (activeTask.HasValue) queue.Cancel(activeTask.Value);
        ClearResults(); RefreshList(added[0]);
        status.Text = $"Staged {added.Length} new test drafts; {tests.Count} tests retained in this suite. No query has run.";
    }
    public SemanticTestArtifact CaptureArtifact()
    {
        CommitDraft(); return SemanticTestArtifactStore.Deserialize(SemanticTestArtifactStore.Serialize(new(1, tests.ToArray())));
    }
    public Task RunAllAsync() => RunTestsAsync(true);
    public Task RunSelectedAsync() => RunTestsAsync(false);
    private SemanticTestDefinition ReadDraft()
    {
        if (selected == null) throw new InvalidOperationException("Create or select a test first.");
        return selected with
        {
            Name = name.Text, Kind = (SemanticTestKind)kind.SelectedItem, Comparison = (SemanticComparison)comparison.SelectedItem,
            Expected = new((SemanticValueKind)valueKind.SelectedItem, (SemanticValueKind)valueKind.SelectedItem == SemanticValueKind.Blank ? null : expected.Text),
            ColumnIndex = int.Parse(column.Text, CultureInfo.InvariantCulture) - 1, ExpectedRowCount = long.Parse(rowCount.Text, CultureInfo.InvariantCulture),
            AbsoluteTolerance = double.Parse(absolute.Text, CultureInfo.InvariantCulture), RelativeTolerance = double.Parse(relative.Text, CultureInfo.InvariantCulture),
            RowLimit = int.Parse(limit.Text, CultureInfo.InvariantCulture), TimeoutSeconds = int.Parse(timeout.Text, CultureInfo.InvariantCulture),
            Query = query.Text, ComparisonQuery = secondQuery.Text, OrderIsDeterministic = ordered.IsChecked == true
        };
    }
    private void CommitDraft()
    {
        if (selected == null) return;
        var updated = ReadDraft(); var index = tests.FindIndex(test => test.Id == selected.Id); if (index >= 0) tests[index] = updated; selected = updated;
    }
    private void RefreshList(SemanticTestDefinition? selection)
    {
        loading = true; list.ItemsSource = null; list.ItemsSource = tests.ToArray(); list.SelectedItem = selection; loading = false; Select(selection);
    }
    private void Select(SemanticTestDefinition? test)
    {
        selected = test; if (test == null) { baseline.Text = "Create a test to begin."; return; }
        loading = true;
        name.Text = test.Name; kind.SelectedItem = test.Kind; comparison.SelectedItem = test.Comparison; valueKind.SelectedItem = test.Expected.Kind; expected.Text = test.Expected.Value ?? "";
        column.Text = (test.ColumnIndex + 1).ToString(CultureInfo.InvariantCulture); rowCount.Text = test.ExpectedRowCount.ToString(CultureInfo.InvariantCulture);
        absolute.Text = test.AbsoluteTolerance.ToString("R", CultureInfo.InvariantCulture); relative.Text = test.RelativeTolerance.ToString("R", CultureInfo.InvariantCulture);
        limit.Text = test.RowLimit.ToString(CultureInfo.InvariantCulture); timeout.Text = test.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        ordered.IsChecked = test.OrderIsDeterministic; query.Text = test.Query; secondQuery.Text = test.ComparisonQuery ?? "";
        baseline.Text = test.Snapshot == null ? "No expected snapshot captured." : $"Expected snapshot: {test.Snapshot.Rows.Count:N0} ordered rows × {test.Snapshot.Columns.Count} columns. DAX changes require a new baseline."; loading = false;
    }
    private QueryRequest Target()
    {
        var target = connection(); if (string.IsNullOrWhiteSpace(target.Server) || string.IsNullOrWhiteSpace(target.Database)) throw new InvalidOperationException("Connect to a semantic model before running tests or capturing a baseline.");
        return new QueryRequest(target.Server!, target.Database!, "EVALUATE ROW(\"Value\", 1)", DocumentRevision: revision) { ConnectionString = transport() };
    }
    private bool IsCurrent(QueryRequest request, long capturedRevision)
    {
        var current = connection(); return !disposed && revision == capturedRevision && current.Server == request.Server && current.Database == request.Database;
    }
    private async Task RunTestsAsync(bool all)
    {
        if (activeTask.HasValue) throw new InvalidOperationException("Finish or cancel the current semantic test operation first.");
        CommitDraft(); var definitions = all ? tests.ToArray() : selected == null ? Array.Empty<SemanticTestDefinition>() : new[] { selected };
        if (definitions.Length == 0) throw new InvalidOperationException("Create a test first.");
        var target = Target(); var capturedRevision = revision; status.Text = "Semantic tests queued…";
        var job = queue.Enqueue("Run semantic tests", async context =>
        {
            foreach (var definition in definitions) SemanticTestService.Validate(definition);
            var run = new List<SemanticTestResult>();
            for (var i = 0; i < definitions.Length; i++) { context.CancellationToken.ThrowIfCancellationRequested(); context.Report(100.0 * i / definitions.Length, $"Test {i + 1} of {definitions.Length}"); run.Add(await service.RunAsync(definitions[i], target, context.CancellationToken).ConfigureAwait(false)); }
            return (IReadOnlyList<SemanticTestResult>)run;
        }); activeTask = job.Id;
        try
        {
            var run = await job.Completion;
            if (!IsCurrent(target, capturedRevision)) { if (!disposed) status.Text = "The model or test draft changed during execution. Run again to obtain current results."; return; }
            lastResults = run; resultTarget = (target.Server, target.Database); results.ItemsSource = run; ResultsChanged?.Invoke(this, EventArgs.Empty);
            status.Text = $"{run.Count(r => r.Outcome == SemanticTestOutcome.Passed)} passed · {run.Count(r => r.Outcome == SemanticTestOutcome.Failed)} failed · {run.Count(r => r.Outcome == SemanticTestOutcome.Error)} errors. Results describe this completed run only.";
        }
        finally { activeTask = null; }
    }
    private async Task CaptureAsync()
    {
        if (activeTask.HasValue) throw new InvalidOperationException("Finish or cancel the current semantic test operation first.");
        CommitDraft(); if (selected == null) return; var definition = selected; var target = Target(); var capturedRevision = revision;
        status.Text = "Snapshot capture queued. Review the returned values before accepting the expected baseline.";
        var job = queue.Enqueue("Capture semantic snapshot", context => service.CaptureSnapshotAsync(definition, target, context.CancellationToken)); activeTask = job.Id;
        try
        {
            var snapshot = await job.Completion;
            if (!IsCurrent(target, capturedRevision) || selected?.Id != definition.Id) { if (!disposed) status.Text = "The model or draft changed. Snapshot discarded; capture again."; return; }
            if (!ReviewSnapshot(snapshot)) { status.Text = "Snapshot not accepted; the previous expected baseline is retained."; return; }
            if (!IsCurrent(target, capturedRevision) || selected?.Id != definition.Id) { status.Text = "The model or draft changed during review. Snapshot discarded; capture again."; return; }
            var updated = definition with { Kind = SemanticTestKind.Snapshot, Snapshot = snapshot }; tests[tests.FindIndex(test => test.Id == definition.Id)] = updated; revision++; ClearResults(); RefreshList(updated);
            status.Text = "Expected snapshot captured. Save the test artifact to retain it, then run the assertion independently.";
        }
        finally { activeTask = null; }
    }
    private bool ReviewSnapshot(SemanticSnapshot snapshot)
    {
        var dialog = new Window { Title = "Review expected snapshot", Owner = Window.GetWindow(this), Width = 980, Height = 580, MinWidth = 650, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(12) };
        var explanation = new TextBlock { Text = $"Accept these {snapshot.Rows.Count:N0} ordered rows × {snapshot.Columns.Count} columns as the expected baseline? Accepting replaces this test's previous baseline. It does not prove the values are correct or change the model.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 12) };
        DockPanel.SetDock(explanation, Dock.Top); panel.Children.Add(explanation);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4, 12, 4, 4) };
        Button(controls, "Accept expected baseline", () => dialog.DialogResult = true); Button(controls, "Cancel", () => dialog.DialogResult = false); DockPanel.SetDock(controls, Dock.Bottom); panel.Children.Add(controls);
        var preview = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, ItemsSource = snapshot.Rows };
        for (var index = 0; index < snapshot.Columns.Count; index++) preview.Columns.Add(new DataGridTextColumn { Header = snapshot.Columns[index].Name + " · " + snapshot.Columns[index].DataType, Binding = new Binding("[" + index + "]"), MinWidth = 100 });
        panel.Children.Add(preview); dialog.Content = panel; return dialog.ShowDialog() == true;
    }
    private async Task LoadAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Semantic tests (*.pbibench-tests.json)|*.pbibench-tests.json|JSON files (*.json)|*.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var capturedRevision = revision; var job = queue.Enqueue("Load semantic test artifact", context => SemanticTestArtifactStore.LoadAsync(dialog.FileName, context.CancellationToken)); var artifact = await job.Completion;
        if (disposed || revision != capturedRevision) { if (!disposed) status.Text = "Draft changed while loading; existing tests retained."; return; }
        LoadArtifact(artifact);
    }
    private async Task SaveAsync()
    {
        CommitDraft(); var artifact = new SemanticTestArtifact(1, tests.ToArray());
        var dialog = new SaveFileDialog { Filter = "Semantic tests (*.pbibench-tests.json)|*.pbibench-tests.json", FileName = "model.pbibench-tests.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var job = queue.Enqueue("Save semantic test artifact", async context => { await SemanticTestArtifactStore.SaveAsync(dialog.FileName, artifact, context.CancellationToken).ConfigureAwait(false); return true; }); await job.Completion; if (!disposed) status.Text = "Versioned semantic test artifact saved.";
    }
    private async Task ExportReportAsync()
    {
        if (LastResults.Count == 0) throw new InvalidOperationException("Run tests successfully to obtain an exportable report first.");
        var report = new SemanticTestReport(1, LastResults.ToArray()); var dialog = new SaveFileDialog { Filter = "JSON report (*.json)|*.json", FileName = "semantic-test-report.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var job = queue.Enqueue("Export semantic test report", async context => { await SemanticTestArtifactStore.SaveReportAsync(dialog.FileName, report, context.CancellationToken).ConfigureAwait(false); return true; }); await job.Completion; if (!disposed) status.Text = "Semantic test report exported without connection credentials.";
    }
    public void Dispose() { disposed = true; if (activeTask.HasValue) queue.Cancel(activeTask.Value); }
}
