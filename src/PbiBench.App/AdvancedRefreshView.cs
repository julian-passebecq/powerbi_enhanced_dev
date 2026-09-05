using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PbiBench.Core.Domain;
using PbiBench.Core.Refresh;
using PbiBench.Core.Tasks;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public sealed class AdvancedRefreshView : UserControl, IDisposable
{
    private readonly Func<TabularModelHandler?> currentHandler;
    private readonly BackgroundTaskQueue queue;
    private readonly TomRefreshService service;
    private readonly ComboBox scope = new() { ItemsSource = new[] { "Model", "Tables", "Partitions", "Custom profile scopes" }, SelectedIndex = 0, Width = 165 };
    private readonly ComboBox kind = new() { ItemsSource = Enum.GetValues(typeof(RefreshKind)), SelectedItem = RefreshKind.Full, Width = 125 };
    private readonly ComboBox policy = new() { ItemsSource = new[] { "Engine default", "Apply policy", "Bypass policy" }, SelectedIndex = 0, Width = 140 };
    private readonly TextBox parallelism = new() { Text = "2", Width = 65 };
    private readonly TextBox timeout = new() { Text = "3600", Width = 85 };
    private readonly DatePicker effectiveDate = new() { Width = 140 };
    private readonly ListBox objects = new() { SelectionMode = SelectionMode.Extended, Height = 140 };
    private readonly TextBox tmsl = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), MinHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox overrideText = new() { AcceptsReturn = true, AcceptsTab = true, Height = 110, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock target = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private readonly ListBox issues = new() { MaxHeight = 125 };
    private readonly ObservableCollection<OverrideDraft> overrides = new();
    private readonly DataGrid overrideGrid = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Height = 100, EnableRowVirtualization = true, EnableColumnVirtualization = true };
    private RefreshMetadataSnapshot? metadata;
    private TabularModelHandler? owner;
    private Guid? activeTask;
    private bool loading, disposed;
    private long revision;
    private IReadOnlyList<RefreshObject> customScopes = Array.Empty<RefreshObject>();
    public RefreshPlan? LastPlan { get; private set; }
    public RefreshPlan? LastExecutedPlan { get; private set; }
    public TabularModelHandler? LastExecutionOwner { get; private set; }
    public RefreshRunResult? LastResult { get; private set; }
    public event EventHandler? RefreshCompleted;

    public AdvancedRefreshView(Func<TabularModelHandler?> currentHandler, BackgroundTaskQueue queue, TomRefreshService? service = null)
    {
        this.currentHandler = currentHandler; this.queue = queue; this.service = service ?? new TomRefreshService();
        var panel = new DockPanel { Margin = new Thickness(10) }; var toolbar = new WrapPanel();
        Button(toolbar, "Preview refresh", () => Preview()); Button(toolbar, "Export exact TMSL…", () => RunUi(ExportAsync));
        Button(toolbar, "Execute reviewed refresh…", () => RunUi(ExecuteAsync)); Button(toolbar, "Cancel active refresh", () => { if (activeTask.HasValue) queue.Cancel(activeTask.Value); });
        Button(toolbar, "Load development profile…", () => RunUi(LoadProfileAsync)); Button(toolbar, "Save development profile…", () => RunUi(SaveProfileAsync));
        DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar); DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        var body = new StackPanel(); body.Children.Add(new TextBlock { Text = "Advanced refresh", FontSize = 21, Margin = new Thickness(4) }); body.Children.Add(target);
        body.Children.Add(new TextBlock { Text = "Review exact targets and processing effects before execution. Refresh uses an independent connection. It changes server data and cannot be undone through the model editor.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        var options = new WrapPanel(); Field(options, "Scope", scope); Field(options, "Refresh type", kind); Field(options, "Maximum parallelism", parallelism); Field(options, "Timeout seconds", timeout); Field(options, "Incremental policy", policy); Field(options, "Effective date (optional)", effectiveDate); body.Children.Add(options);
        body.Children.Add(new TextBlock { Text = "Select tables or partitions (Ctrl/Shift selects multiple)", Margin = new Thickness(4) }); body.Children.Add(objects);
        body.Children.Add(new TextBlock { Text = "Temporary development source overrides", FontSize = 15, Margin = new Thickness(4, 12, 4, 4) });
        body.Children.Add(new TextBlock { Text = "Overrides retain an existing Import partition's M or native-query source type. They affect loaded data for this run, while stored source metadata stays unchanged. Source credentials and privacy settings must already be configured.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        var overrideButtons = new WrapPanel(); Button(overrideButtons, "Add override for selected partition", AddOverride); Button(overrideButtons, "Remove override", () => { if (overrideGrid.SelectedItem is OverrideDraft draft) overrides.Remove(draft); Invalidate(); }); body.Children.Add(overrideButtons);
        foreach (var field in new[] { "Table", "Partition", "SourceKind" }) overrideGrid.Columns.Add(new DataGridTextColumn { Header = field, Binding = new Binding(field), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        overrideGrid.ItemsSource = overrides; body.Children.Add(overrideGrid); body.Children.Add(overrideText);
        body.Children.Add(new TextBlock { Text = "Validation and processing effects", FontSize = 15, Margin = new Thickness(4, 12, 4, 4) }); body.Children.Add(issues);
        body.Children.Add(new TextBlock { Text = "Exact generated TMSL (read-only)", FontSize = 15, Margin = new Thickness(4, 12, 4, 4) }); body.Children.Add(tmsl);
        panel.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = panel;
        scope.SelectionChanged += (_, _) => { if (loading) return; PopulateScopes(); Invalidate(); };
        kind.SelectionChanged += (_, _) => Invalidate(); policy.SelectionChanged += (_, _) => Invalidate(); objects.SelectionChanged += (_, _) => Invalidate();
        parallelism.TextChanged += (_, _) => Invalidate(); timeout.TextChanged += (_, _) => Invalidate(); effectiveDate.SelectedDateChanged += (_, _) => Invalidate();
        overrideGrid.SelectionChanged += (_, _) => { loading = true; overrideText.Text = (overrideGrid.SelectedItem as OverrideDraft)?.Expression ?? ""; loading = false; };
        overrideText.TextChanged += (_, _) => { if (loading) return; if (overrideGrid.SelectedItem is OverrideDraft draft) draft.Expression = overrideText.Text; Invalidate(); };
        RefreshModel();
    }
    private void Button(Panel parent, string label, Action action)
    { var button = new Button { Content = label, Margin = new Thickness(3), Padding = new Thickness(8, 5, 8, 5) }; button.Click += (_, _) => { try { action(); } catch (Exception error) { status.Text = UserError(error); } }; parent.Children.Add(button); }
    private static void Field(Panel parent, string label, Control control) { var field = new StackPanel { Margin = new Thickness(4) }; field.Children.Add(new TextBlock { Text = label }); field.Children.Add(control); parent.Children.Add(field); }
    private async void RunUi(Func<Task> action) { try { await action(); } catch (Exception error) { if (!disposed) status.Text = UserError(error); } }
    private static string UserError(Exception error) => error is OperationCanceledException ? "Operation canceled." : error is InvalidOperationException || error is InvalidDataException || error is ArgumentException ? error.Message : error is FormatException || error is OverflowException ? "Enter valid whole numbers for parallelism and timeout." : "The operation failed. Check the connection or file and preview again.";
    private void Invalidate()
    {
        if (loading) return; revision++; LastPlan = null; tmsl.Text = "Options changed. Preview refresh to generate the exact command."; issues.ItemsSource = null;
    }
    public void RefreshModel()
    {
        var handler = currentHandler(); var captured = handler == null ? null : RefreshMetadataProvider.Capture(handler);
        var changed = !ReferenceEquals(owner, handler) || metadata?.Fingerprint != captured?.Fingerprint || metadata?.Server != captured?.Server || metadata?.HasUnsavedChanges != captured?.HasUnsavedChanges || metadata?.IsConnected != captured?.IsConnected;
        if (!changed && metadata != null) return;
        if (activeTask.HasValue) queue.Cancel(activeTask.Value);
        owner = handler; metadata = captured; LastResult = null; Invalidate(); PopulateScopes();
        target.Text = captured == null ? "No model loaded." : $"{(captured.IsConnected ? captured.Server : "Offline model")} / {captured.DatabaseName} · database id {captured.DatabaseId} · compatibility {captured.CompatibilityLevel}";
        status.Text = "Model context updated. Options are retained; preview again before execution.";
    }
    private void PopulateScopes()
    {
        var selected = objects.SelectedItems.Cast<RefreshObject>().ToArray(); loading = true;
        objects.ItemsSource = scope.SelectedIndex == 3 ? customScopes : metadata == null ? Array.Empty<RefreshObject>() : scope.SelectedIndex == 2 ? metadata.Tables.SelectMany(t => t.Partitions.Select(p => new RefreshObject(t.Name, p.Name))).ToArray()
            : scope.SelectedIndex == 1 ? metadata.Tables.Select(t => new RefreshObject(t.Name)).ToArray() : new[] { new RefreshObject() };
        foreach (var item in objects.Items.Cast<RefreshObject>()) if (selected.Contains(item)) objects.SelectedItems.Add(item);
        if (objects.SelectedItems.Count == 0 && objects.Items.Count > 0) objects.SelectedIndex = 0; loading = false;
    }
    public void SetScope(string? table, string? partition = null)
    {
        scope.SelectedIndex = table == null ? 0 : partition == null ? 1 : 2; PopulateScopes(); objects.SelectedItems.Clear();
        var match = objects.Items.Cast<RefreshObject>().FirstOrDefault(o => o.Table == table && o.Partition == partition); if (match != null) objects.SelectedItems.Add(match); Invalidate();
    }
    private RefreshRequest ReadRequest() => new()
    {
        Kind = (RefreshKind)kind.SelectedItem, Objects = objects.SelectedItems.Cast<RefreshObject>().ToArray(), MaxParallelism = int.Parse(parallelism.Text, CultureInfo.InvariantCulture),
        TimeoutSeconds = int.Parse(timeout.Text, CultureInfo.InvariantCulture), ApplyRefreshPolicy = policy.SelectedIndex == 0 ? null : policy.SelectedIndex == 1,
        EffectiveDate = effectiveDate.SelectedDate?.Date, SourceOverrides = overrides.Select(o => new RefreshSourceOverride(o.Table, o.Partition, o.SourceKind, o.Expression)).ToArray()
    };
    public RefreshPlan Preview(RefreshRequest? request = null)
    {
        RefreshModel(); if (metadata == null) throw new InvalidOperationException("Open or connect a model to preview refresh.");
        LastPlan = RefreshPlanner.Build(metadata, request ?? ReadRequest()); tmsl.Text = LastPlan.Tmsl;
        issues.ItemsSource = LastPlan.Issues.Select(i => i.Severity + " · " + i.Message).Prepend(RefreshPlanner.Effect(LastPlan.Request.Kind)).ToArray();
        status.Text = LastPlan.CanExecute ? "Preview ready. Review the exact command and effects, then approve execution." : "Preview contains errors. TMSL can be exported, but execution is blocked."; return LastPlan;
    }
    private void AddOverride()
    {
        if (objects.SelectedItem is not RefreshObject obj || obj.Table == null || obj.Partition == null || metadata == null) throw new InvalidOperationException("Choose Partitions scope and select a partition first.");
        var partition = metadata.Tables.Single(t => t.Name == obj.Table).Partitions.Single(p => p.Name == obj.Partition);
        if (partition.Mode != "Import" || partition.SourceKind != RefreshSourceKind.M && partition.SourceKind != RefreshSourceKind.Query) throw new InvalidOperationException("Choose an Import partition with an M or native Query source.");
        var draft = overrides.FirstOrDefault(o => o.Table == obj.Table && o.Partition == obj.Partition);
        if (draft == null) { draft = new(obj.Table, obj.Partition, partition.SourceKind); overrides.Add(draft); } overrideGrid.SelectedItem = draft; overrideText.Focus(); Invalidate();
    }
    private async Task ExecuteAsync()
    {
        if (activeTask.HasValue) throw new InvalidOperationException("Finish or cancel the active refresh first.");
        var plan = Preview(); if (!plan.CanExecute) throw new InvalidOperationException("Resolve the preview errors before executing refresh.");
        if (MessageBox.Show(Window.GetWindow(this), $"Execute the displayed {RefreshPlanner.TypeName(plan.Request.Kind)} refresh on {plan.Metadata.Server} / {plan.Metadata.DatabaseName}?\n\n{plan.Request.Objects.Count} scope(s), maximum parallelism {plan.Request.MaxParallelism}.\n\nThis changes server data. Incremental policy can change partitions. There is no local Undo, and cancellation does not prove rollback. Confirm that you reviewed the exact TMSL and processing effects.", "Approve exact refresh plan", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        var handler = currentHandler(); if (handler == null || !ReferenceEquals(handler, owner)) throw new InvalidOperationException("The model session changed. Preview again.");
        var current = RefreshMetadataProvider.Capture(handler);
        if (current.Fingerprint != plan.Metadata.Fingerprint || current.Server != plan.Metadata.Server || current.DatabaseId != plan.Metadata.DatabaseId || current.HasUnsavedChanges) throw new InvalidOperationException("The model changed during review. Preview again.");
        var connection = new RefreshConnection(plan.Metadata.Server, plan.Metadata.DatabaseId) { ConnectionString = handler.Database.Server.ConnectionString };
        var approval = new ApprovedChangePlan(plan.ChangePlan, DateTimeOffset.UtcNow, Environment.UserName);
        LastExecutedPlan = plan; LastExecutionOwner = handler;
        var runRevision = revision;
        var job = queue.Enqueue("Refresh " + plan.Metadata.DatabaseName, context => service.ExecuteAsync(plan, approval, connection,
            new Progress<RefreshProgress>(p => context.Report(null, p.Stage + " · " + p.Message)), context.CancellationToken)); activeTask = job.Id;
        status.Text = "Refresh queued. The Background tasks panel shows phase and cancellation status.";
        try
        {
            var result = await job.Completion; if (disposed) return; LastResult = result;
            status.Text = result.Outcome + " · " + result.Message + (result.Details.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, result.Details));
            if (revision != runRevision) status.Text += " The editor context/options changed while this captured command ran.";
            if (result.CommandSubmitted) RefreshCompleted?.Invoke(this, EventArgs.Empty);
        }
        finally { activeTask = null; LastPlan = null; }
    }
    private async Task ExportAsync()
    {
        var plan = LastPlan ?? Preview(); var dialog = new SaveFileDialog { Filter = "TMSL JSON (*.json)|*.json", FileName = "refresh.tmsl.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await queue.Enqueue("Export refresh TMSL", async context => { await RefreshProfileStore.ExportTmslAsync(dialog.FileName, plan, context.CancellationToken).ConfigureAwait(false); return true; }).Completion;
        if (!disposed) status.Text = "Exact generated TMSL exported. No refresh was executed.";
    }
    public void LoadProfile(RefreshDevelopmentProfile profile)
    {
        var checkedProfile = RefreshProfileStore.Deserialize(RefreshProfileStore.Serialize(profile)); var request = checkedProfile.Request;
        loading = true; kind.SelectedItem = request.Kind; parallelism.Text = request.MaxParallelism.ToString(CultureInfo.InvariantCulture); timeout.Text = request.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        policy.SelectedIndex = request.ApplyRefreshPolicy.HasValue ? request.ApplyRefreshPolicy.Value ? 1 : 2 : 0; effectiveDate.SelectedDate = request.EffectiveDate;
        customScopes = request.Objects.ToArray(); scope.SelectedIndex = 3; loading = false; PopulateScopes(); loading = true; objects.SelectedItems.Clear();
        foreach (var item in objects.Items.Cast<RefreshObject>()) if (request.Objects.Contains(item)) objects.SelectedItems.Add(item);
        overrides.Clear(); foreach (var item in request.SourceOverrides) overrides.Add(new(item.Table, item.Partition, item.SourceKind) { Expression = item.Expression }); loading = false; Invalidate();
        status.Text = "Development profile loaded. Validate its scopes against this model before execution.";
    }
    private async Task LoadProfileAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Refresh profile (*.pbibench-refresh.json)|*.pbibench-refresh.json|JSON (*.json)|*.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var captured = revision; var profile = await queue.Enqueue("Load refresh profile", context => RefreshProfileStore.LoadAsync(dialog.FileName, context.CancellationToken)).Completion;
        if (disposed || revision != captured) return; LoadProfile(profile);
    }
    private async Task SaveProfileAsync()
    {
        var request = ReadRequest(); var dialog = new SaveFileDialog { Filter = "Refresh profile (*.pbibench-refresh.json)|*.pbibench-refresh.json", FileName = "development.pbibench-refresh.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var profile = new RefreshDevelopmentProfile(1, Path.GetFileNameWithoutExtension(dialog.FileName), request);
        await queue.Enqueue("Save refresh profile", async context => { await RefreshProfileStore.SaveAsync(dialog.FileName, profile, context.CancellationToken).ConfigureAwait(false); return true; }).Completion;
        if (!disposed) status.Text = "Typed development profile saved without transport credentials. No refresh was executed.";
    }
    public void Dispose() { disposed = true; if (activeTask.HasValue) queue.Cancel(activeTask.Value); }
    private sealed class OverrideDraft
    {
        public OverrideDraft(string table, string partition, RefreshSourceKind kind) { Table = table; Partition = partition; SourceKind = kind; }
        public string Table { get; } public string Partition { get; } public RefreshSourceKind SourceKind { get; } public string Expression { get; set; } = "";
    }
}
