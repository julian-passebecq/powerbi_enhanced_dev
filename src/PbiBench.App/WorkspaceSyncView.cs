using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PbiBench.Core.Domain;
using PbiBench.Core.Tasks;
using PbiBench.Core.Workspaces;
using PbiBench.Git;
using PbiBench.Semantic.Workspaces;
using PbiBench.Workspace;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public sealed class WorkspaceSyncView : UserControl, IDisposable
{
    private readonly Func<TabularModelHandler?> currentHandler;
    private readonly Action changed;
    private readonly BackgroundTaskQueue queue; private readonly bool ownsQueue;
    private readonly string settingsDirectory;
    private readonly WorkspaceDiskStore diskStore = new(); private readonly TmdlWorkspaceCodec codec = new(); private readonly TomWorkspaceSyncService liveService = new();
    private readonly TextBox folder = new() { MinWidth = 360, Margin = new Thickness(4) };
    private readonly TextBlock state = Note("Choose a PBIP semantic-model folder and compare Disk, Live and Git."), status = Note("Watchers invalidate previews; they never synchronize automatically.");
    private readonly DataGrid changes = new() { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, Margin = new Thickness(6) };
    private readonly DataGrid gitChanges = new() { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, Margin = new Thickness(6) };
    private readonly CheckBox resolve = new() { Content = "I reviewed the conflicts; the chosen Pull or Push source should replace them.", Margin = new Thickness(6) };
    private readonly TextBox details = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap };
    private string? configuredRoot, definitionDirectory, server, database; private TabularModelHandler? configuredHandler;
    private WorkspaceWatcher? watcher; private WorkspaceBaselineStore? baselineStore; private WorkspaceDiskSnapshot? diskFiles; private WorkspaceLiveCapture? live;
    private WorkspaceSemanticSnapshot? baseline, loaded; private string baselineLabel = "Session baseline"; private long liveSequence; private int generation; private bool disposed;
    private CancellationTokenSource? pending;
    public WorkspaceComparison? LastComparison { get; private set; }
    public string Status => status.Text;
    public IReadOnlyList<WorkspaceChange> LastGitChanges { get; private set; } = Array.Empty<WorkspaceChange>();
    public TabularModelHandler? LastExecutionOwner { get; private set; }
    public WorkspaceConnection? LastExecutionConnection { get; private set; }
    public string? LastExecutionDatabaseId { get; private set; }
    public event EventHandler? RemoteWriteCompleted;
    public WorkspaceSyncView(Func<TabularModelHandler?> currentHandler, Action changed, BackgroundTaskQueue? backgroundTasks = null, string? settingsDirectory = null)
    {
        this.currentHandler = currentHandler; this.changed = changed; queue = backgroundTasks ?? new BackgroundTaskQueue(); ownsQueue = backgroundTasks == null;
        this.settingsDirectory = Path.GetFullPath(settingsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PbiBench"));
        var root = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); top.Children.Add(state);
        top.Children.Add(Bar(folder, Button("Use model folder", () => { Configure(folder.Text, server, database); return CompareAsync(); }), Button("Browse…", BrowseAsync), Button("Compare", CompareAsync), Button("Cancel", () => { pending?.Cancel(); return Task.CompletedTask; })));
        top.Children.Add(Bar(Button("Preview Pull Live → Disk", PullAsync), Button("Preview Push Disk → Live", PushAsync), Button("Set current matching state as baseline", AcceptBaselineAsync)));
        top.Children.Add(resolve); top.Children.Add(Note("Pull writes only semantic definition files and keeps a recovery backup. Push replaces metadata on the named live database in a private XMLA transaction; changed partitions may need refresh. Loaded editor drafts remain separate."));
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var results = new TabControl(); results.Items.Add(new TabItem { Header = "Synchronization changes", Content = changes }); results.Items.Add(new TabItem { Header = "Git semantic diff", Content = gitChanges }); grid.Children.Add(results);
        var splitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(splitter, 1); grid.Children.Add(splitter); Grid.SetRow(details, 2); grid.Children.Add(details); root.Children.Add(grid); Content = root;
        changes.SelectionChanged += (_, _) => { if (changes.SelectedItem is WorkspaceDisplayChange row) details.Text = row.Object + " / " + row.Property + "\n\nBASELINE\n" + row.Baseline + "\n\nDISK\n" + row.Disk + "\n\nLIVE\n" + row.Live; };
        gitChanges.SelectionChanged += (_, _) => { if (gitChanges.SelectedItem is WorkspaceDisplayChange row) details.Text = row.Object + " / " + row.Property + "\n\nGIT HEAD\n" + row.Baseline + "\n\nWORKING DISK\n" + row.Disk; };
    }
    public void Configure(string? workspaceRoot, string? server, string? database)
    {
        if (disposed) return; var handler = currentHandler();
        if (configuredRoot == workspaceRoot && this.server == server && this.database == database && ReferenceEquals(configuredHandler, handler)) return;
        pending?.Cancel(); generation++; configuredRoot = workspaceRoot; this.server = server; this.database = database; configuredHandler = handler; folder.Text = workspaceRoot ?? "";
        watcher?.Dispose(); watcher = null; definitionDirectory = null; baseline = null; baselineStore = null; LastComparison = null; LastGitChanges = Array.Empty<WorkspaceChange>(); diskFiles = null; live = null; loaded = null; changes.ItemsSource = null; gitChanges.ItemsSource = null; resolve.IsChecked = false;
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            try { definitionDirectory = WorkspaceDiskStore.ResolveDefinitionDirectory(workspaceRoot!); folder.Text = definitionDirectory; baselineStore = new(this.settingsDirectory, definitionDirectory, server, database); watcher = new WorkspaceWatcher(definitionDirectory); watcher.Changed += OnDiskChanged; status.Text = "Definition watcher active. Compare to capture current metadata."; }
            catch (Exception error) { status.Text = error.Message; }
        }
        state.Text = "Disk: " + (definitionDirectory ?? "choose a model folder") + "\nLive: " + (server == null || database == null ? "offline" : server + " / " + database) + "\nGit: compare to read the baseline";
    }
    private void OnDiskChanged(object? sender, EventArgs args)
    {
        if (disposed || !ReferenceEquals(sender, watcher) || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (disposed || !ReferenceEquals(sender, watcher)) return;
            // The immediate sequence may already have been incorporated by a comparison
            // before this debounced notification reaches the dispatcher.
            if (LastComparison != null && LastComparison.DiskSequence == watcher!.Sequence) return;
            LastComparison = null; resolve.IsChecked = false; status.Text = "External or local definition edit detected (sequence " + watcher?.Sequence + "). Compare again; no file or model was overwritten. " + watcher?.LastChange;
        }));
    }
    public async Task CompareAsync()
    {
        if (disposed) return; var directory = Definition(); var version = generation; var handler = currentHandler(); loaded = handler == null ? null : codec.CaptureLoaded(handler);
        var connection = Connection(); var currentLoaded = loaded; var loadedHasUnsaved = handler?.HasUnsavedChanges == true; var sequence = watcher?.Sequence ?? 0; var existingBaseline = baseline; var capturedBaselineStore = baselineStore!;
        pending?.Cancel(); var cancellation = pending = new CancellationTokenSource(); LastComparison = null; resolve.IsChecked = false; status.Text = "Capturing disk, Git and a fresh independent live session…";
        try
        {
            var job = queue.Enqueue("Compare semantic workspace", async context =>
            {
                var token = context.CancellationToken; var files = diskStore.Capture(directory, token); var disk = codec.Parse(files, token); context.Report(25, "Parsed captured disk metadata");
                WorkspaceLiveCapture? remote = null; string? remoteError = null;
                if (connection != null) { try { remote = await liveService.CaptureAsync(connection, token); } catch (OperationCanceledException) { throw; } catch (Exception error) { remoteError = error.Message; } }
                var original = existingBaseline ?? await capturedBaselineStore.LoadAsync(token); var label = existingBaseline != null ? baselineLabel : original != null ? "Saved synchronization baseline" : "Initial disk baseline"; string gitStatus; IReadOnlyList<WorkspaceChange> gitRows = Array.Empty<WorkspaceChange>();
                var git = await new GitClient().GetStatusAsync(directory, ct: token);
                if (git.IsRepository && git.RepositoryRoot != null)
                {
                    try { var head = await new GitSemanticBaselineReader().ReadAsync(git.RepositoryRoot, directory, files.IsBim, token); var headSnapshot = codec.Parse(new WorkspaceDiskSnapshot(directory, head.Files.Select(file => new WorkspaceFile(file.Path, file.Content)), files.IsBim), token); gitRows = WorkspaceSemanticDiff.Between(headSnapshot, disk); gitStatus = "HEAD " + head.Commit.Substring(0, 8) + ": " + gitRows.Count + " semantic property changes"; if (original == null) { original = headSnapshot; label = "Git HEAD " + head.Commit.Substring(0, 8); } }
                    catch (OperationCanceledException) { throw; } catch (Exception error) { gitStatus = "Git semantic baseline unavailable: " + error.Message; }
                }
                else gitStatus = git.Summary;
                original ??= disk; context.Report(90, "Comparing semantic properties");
                return new CapturedWorkspace(files, disk, remote, original, label, gitStatus, remoteError, currentLoaded != null && remote != null && currentLoaded.Hash != remote.Snapshot.Hash, gitRows);
            }, cancellation.Token);
            var result = await job.Completion;
            if (disposed || version != generation || !ReferenceEquals(handler, currentHandler())) return;
            if (sequence != (watcher?.Sequence ?? 0)) { status.Text = "The workspace changed during capture. Compare again. " + watcher?.LastChange; return; }
            diskFiles = result.Files; live = result.Live; baseline = result.Baseline; baselineLabel = result.BaselineLabel; liveSequence++;
            LastComparison = WorkspaceSemanticDiff.Compare(baseline, result.Disk, live?.Snapshot, sequence, liveSequence, loadedHasUnsaved, baselineLabel);
            changes.ItemsSource = LastComparison.Changes.Select(row => new WorkspaceDisplayChange(row.ObjectPath, row.Property, row.Kind.ToString(), WorkspaceSemanticDiff.DisplayValue(row.ObjectPath + "/" + row.Property, row.Baseline), WorkspaceSemanticDiff.DisplayValue(row.ObjectPath + "/" + row.Property, row.Disk), live == null ? "(not connected)" : WorkspaceSemanticDiff.DisplayValue(row.ObjectPath + "/" + row.Property, row.Live))).ToArray();
            LastGitChanges = result.GitChanges; gitChanges.ItemsSource = LastGitChanges.Select(row => new WorkspaceDisplayChange(row.ObjectPath, row.Property, "Git HEAD → Disk", WorkspaceSemanticDiff.DisplayValue(row.ObjectPath + "/" + row.Property, row.Baseline), WorkspaceSemanticDiff.DisplayValue(row.ObjectPath + "/" + row.Property, row.Disk), "")).ToArray();
            state.Text = "Disk: " + result.Files.Files.Count + " definition files · sequence " + sequence + "\nLive: " + (live == null ? result.LiveError ?? "offline" : live.DatabaseName + " · capture " + liveSequence) + "\nLoaded editor: " + (handler == null ? "none" : loadedHasUnsaved ? "unsaved edits" : result.LoadedDiffers ? "differs from Live" : "matches Live / clean") + "\nGit: " + result.GitStatus + "\nBaseline: " + baselineLabel;
            status.Text = LastComparison.HasConflicts ? "Divergent changes require explicit source resolution in the review." : "Comparison complete. " + (loadedHasUnsaved ? "Loaded editor has unsaved edits; Push is blocked until saved or discarded." : result.LoadedDiffers && handler?.IsConnected == true ? "The connected editor is stale against actual Live; reload before Push." : "Choose a direction to preview exact destination changes.");
        }
        finally { cancellation.Dispose(); if (ReferenceEquals(pending, cancellation)) pending = null; }
    }
    private async Task PullAsync()
    {
        var comparison = RequireComparison(); var capture = live ?? throw new InvalidOperationException("Connect and capture actual Live metadata first."); var connection = Connection() ?? throw new InvalidOperationException("A live target is required.");
        if (comparison.HasConflicts && resolve.IsChecked != true) throw new InvalidOperationException("Review the conflict rows and explicitly choose source replacement before Pull.");
        var directory = Definition(); var before = diskFiles!; var version = generation;
        var prepared = await queue.Enqueue("Prepare live-to-disk diff", context => Task.FromResult(diskStore.Prepare(before, codec.Serialize(capture.Snapshot, before.IsBim, context.CancellationToken)))).Completion;
        if (version != generation || !ReferenceEquals(comparison, LastComparison)) throw new InvalidOperationException("Workspace state changed. Preview again.");
        var rows = DestinationRows(comparison.Disk, capture.Snapshot);
        if (!PreviewDialog.Show(Window.GetWindow(this), "Pull Live → Disk", "Source: " + connection + "\nDestination: " + directory + "\nThis replaces semantic definition metadata, including destination edits shown below. PBIR, DAXQueries, cache and unknown files remain untouched. A full definition backup is saved before writes. Loaded editor metadata is not reloaded.", rows, rows.Count > 0, "Apply reviewed disk changes")) return;
        if (version != generation || !ReferenceEquals(comparison, LastComparison)) throw new InvalidOperationException("The workspace changed during review. Compare again.");
        var complete = CaptureSynchronizationCompletion();
        using var cancellation = new CancellationTokenSource(); pending = cancellation;
        try
        {
            await queue.Enqueue("Pull live metadata to disk", async context => { var fresh = await liveService.CaptureAsync(connection, context.CancellationToken); if (fresh.DatabaseId != capture.DatabaseId || fresh.Snapshot.Hash != capture.Snapshot.Hash) throw new InvalidOperationException("Live changed after review. Compare again."); return await diskStore.ApplyAsync(prepared, new ApprovedChangePlan(prepared.Plan, DateTimeOffset.UtcNow, Environment.UserName), context.CancellationToken); }, cancellation.Token).Completion;
            await complete(capture.Snapshot);
        }
        finally { if (ReferenceEquals(pending, cancellation)) pending = null; }
    }
    private async Task PushAsync()
    {
        var comparison = RequireComparison(); var capture = live ?? throw new InvalidOperationException("Capture actual Live metadata first."); var connection = Connection() ?? throw new InvalidOperationException("A live target is required.");
        var handler = currentHandler(); if (handler?.HasUnsavedChanges == true) throw new InvalidOperationException("Save or discard loaded editor edits before pushing disk.");
        if (handler?.IsConnected == true && codec.CaptureLoaded(handler).Hash != capture.Snapshot.Hash) throw new InvalidOperationException("The connected editor is stale against actual Live. Reload it before pushing disk.");
        var plan = liveService.PreparePush(comparison, capture, connection, diskFiles!.Hash, resolve.IsChecked == true); var directory = Definition(); var version = generation;
        if (!PreviewDialog.Show(Window.GetWindow(this), "Push Disk → Live", "Target: " + connection + " (database ID " + capture.DatabaseId + ")\nThis replaces the target's full metadata with the captured disk model. Review deletions, security and partition changes carefully. Credentials excluded from model definitions are not supplied by this operation. A refresh may be needed, and a BIM snapshot cannot restore processed data. A private XMLA transaction and pre-run metadata snapshot protect this operation; loaded editor metadata remains unchanged.", plan.Plan.Changes.Select(row => new PreviewRow(row.Target, row.Operation, row.BeforeSummary, row.AfterSummary, string.Join(" ", row.Validation))).ToArray(), true, "Apply reviewed remote metadata")) return;
        if (version != generation || !ReferenceEquals(comparison, LastComparison) || !ReferenceEquals(handler, currentHandler()) || handler?.HasUnsavedChanges == true) throw new InvalidOperationException("The workspace or loaded editor changed during review. Compare again.");
        var complete = CaptureSynchronizationCompletion();
        using var cancellation = new CancellationTokenSource(); pending = cancellation; var dispatched = 0;
        try
        {
            var result = await queue.Enqueue("Push disk metadata to live", context => liveService.ApplyPushAsync(plan, new ApprovedChangePlan(plan.Plan, DateTimeOffset.UtcNow, Environment.UserName), token => diskStore.Capture(directory, token).Hash, Path.Combine(settingsDirectory, "WorkspaceRecovery"), context.CancellationToken, () => Interlocked.Exchange(ref dispatched, 1)), cancellation.Token).Completion;
            if (await complete(result.Live.Snapshot)) details.Text = "Remote push completed. Metadata recovery snapshot: " + result.BackupPath + "\nReload the native editor to inspect the new live state; refresh affected tables if required.";
        }
        finally { if (ReferenceEquals(pending, cancellation)) pending = null; if (Volatile.Read(ref dispatched) != 0) { LastExecutionOwner = handler; LastExecutionConnection = connection; LastExecutionDatabaseId = capture.DatabaseId; RemoteWriteCompleted?.Invoke(this, EventArgs.Empty); } }
    }
    private async Task AcceptBaselineAsync()
    {
        var comparison = RequireComparison(); if (comparison.Live != null && comparison.Disk.Hash != comparison.Live.Hash) throw new InvalidOperationException("Disk and Live must match before accepting a shared baseline.");
        await CaptureSynchronizationCompletion("Saved matching baseline", false)(comparison.Disk);
    }
    // Bind persistence and presentation before dispatch. The user can switch targets while an
    // operation or its baseline write is awaiting; its completed result still belongs to the old store.
    internal Func<WorkspaceSemanticSnapshot, Task<bool>> CaptureSynchronizationCompletion(string label = "Last successful synchronization", bool notifyChanged = true)
    {
        var store = baselineStore ?? throw new InvalidOperationException("Configure a workspace first."); var version = generation; var owner = currentHandler();
        bool IsCurrent() => !disposed && generation == version && ReferenceEquals(owner, currentHandler());
        return async snapshot =>
        {
            await store.SaveAsync(snapshot, CancellationToken.None);
            if (!IsCurrent()) return false;
            baseline = snapshot; baselineLabel = label; if (notifyChanged) changed();
            if (!IsCurrent()) return false;
            await CompareAsync(); return IsCurrent();
        };
    }
    private Task BrowseAsync() { using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a PBIP semantic-model or TMDL definition folder", SelectedPath = definitionDirectory ?? "" }; if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) Configure(dialog.SelectedPath, server, database); return Task.CompletedTask; }
    private WorkspaceComparison RequireComparison() => LastComparison ?? throw new InvalidOperationException("Compare the current workspace before preparing synchronization.");
    private string Definition() => definitionDirectory ?? throw new InvalidOperationException("Choose one semantic-model or TMDL definition folder.");
    private WorkspaceConnection? Connection() => string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database) ? null : new(server!, database!, currentHandler()?.IsConnected == true ? currentHandler()!.Database.Server.ConnectionString : null);
    private static IReadOnlyList<PreviewRow> DestinationRows(WorkspaceSemanticSnapshot before, WorkspaceSemanticSnapshot after) => WorkspaceSemanticDiff.Between(before, after).Select(row => new PreviewRow(row.ObjectPath, row.Property, WorkspaceSemanticDiff.DisplayValue(row.ObjectPath + "/" + row.Property, row.Baseline), WorkspaceSemanticDiff.DisplayValue(row.ObjectPath + "/" + row.Property, row.Disk), "Exact destination metadata difference")).ToArray();
    private Button Button(string title, Func<Task> action) { var button = new Button { Content = title, Margin = new Thickness(3), Padding = new Thickness(8, 4, 8, 4) }; button.Click += async (_, _) => { var version = generation; button.IsEnabled = false; try { await action(); } catch (OperationCanceledException) { if (!disposed && generation == version) status.Text = "Canceled. Compare before retrying any write."; } catch (Exception error) { if (!disposed && generation == version) status.Text = error.Message; } finally { if (!disposed) button.IsEnabled = true; } }; return button; }
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private static WrapPanel Bar(params UIElement[] controls) { var panel = new WrapPanel(); foreach (var control in controls) panel.Children.Add(control); return panel; }
    public void Dispose() { if (disposed) return; disposed = true; pending?.Cancel(); watcher?.Dispose(); if (ownsQueue) queue.Dispose(); }
    private sealed record CapturedWorkspace(WorkspaceDiskSnapshot Files, WorkspaceSemanticSnapshot Disk, WorkspaceLiveCapture? Live, WorkspaceSemanticSnapshot Baseline, string BaselineLabel, string GitStatus, string? LiveError, bool LoadedDiffers, IReadOnlyList<WorkspaceChange> GitChanges);
    private sealed record WorkspaceDisplayChange(string Object, string Property, string Kind, string Baseline, string Disk, string Live);
}
