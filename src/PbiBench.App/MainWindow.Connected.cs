using System.Windows;
using System.Windows.Controls;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public partial class MainWindow
{
    private readonly TabControl workspaceExperience = new() { Visibility = Visibility.Collapsed };
    private readonly DockPanel refreshExperience = new() { Visibility = Visibility.Collapsed };
    private WorkspaceSyncView? workspaceSync;
    private FabricWorkspaceView? fabricWorkspace;
    private AdvancedRefreshView? advancedRefresh;
    private (string? Root, string? Server, string? Database, TabularModelHandler? Handler)? configuredWorkspaceContext;
    private TabularModelHandler? reloadRequiredHandler;
    private readonly TextBlock reloadNotice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };

    private void InitializeConnectedWorkspaces()
    {
        var parent = (Panel)WorkspacePage.Parent;
        parent.Children.Remove(WorkspacePage); WorkspacePage.Visibility = Visibility.Visible;
        workspaceExperience.Items.Add(new TabItem { Header = "Project / Git", Content = WorkspacePage });
        workspaceSync = new WorkspaceSyncView(() => editor.Handler, () => Run(UpdateSessionAsync), backgroundTasks, settingsDirectory);
        workspaceExperience.Items.Add(new TabItem { Header = "Disk / Live / Git", Content = workspaceSync }); parent.Children.Add(workspaceExperience);
        workspaceSync.RemoteWriteCompleted += (_, _) => MarkConnectionStale(workspaceSync.LastExecutionOwner,
            workspaceSync.LastExecutionConnection?.Server, workspaceSync.LastExecutionDatabaseId, "Workspace push changed or may have changed the live model.");
        fabricWorkspace = new FabricWorkspaceView(backgroundTasks) { Visibility = Visibility.Collapsed };
        fabricWorkspace.Configure(() => editor.Handler, () => Run(UpdateSessionAsync)); parent.Children.Add(fabricWorkspace);
        advancedRefresh = new AdvancedRefreshView(() => editor.Handler, backgroundTasks);
        advancedRefresh.RefreshCompleted += (_, _) => MarkConnectionStale(advancedRefresh.LastExecutionOwner,
            advancedRefresh.LastExecutedPlan?.Metadata.Server, advancedRefresh.LastExecutedPlan?.Metadata.DatabaseId, "Refresh changed or may have changed the live model's partitions.");
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); refreshExperience.Children.Add(top);
        top.Children.Add(reloadNotice);
        var buttons = new WrapPanel { Margin = new Thickness(5) };
        var reconnect = new Button { Content = "Reconnect / reload model…", Margin = new Thickness(3), Padding = new Thickness(8, 5, 8, 5) };
        reconnect.Click += (_, _) => Run(() => { GoTo("Model"); editor.Connect(); }); buttons.Children.Add(reconnect);
        var native = new Button { Content = "Native deployment tools", Margin = new Thickness(3), Padding = new Thickness(8, 5, 8, 5) };
        native.Click += (_, _) => Run(() => { GoTo("Model"); editor.ShowLegacyCommands(true); }); buttons.Children.Add(native);
        top.Children.Add(buttons); refreshExperience.Children.Add(advancedRefresh); parent.Children.Add(refreshExperience);
        UpdateConnectedContext();
    }
    private void MarkConnectionStale(TabularModelHandler? owner, string? server, string? databaseId, string reason)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => MarkConnectionStale(owner, server, databaseId, reason)); return; }
        var current = editor.Handler;
        if (current == null || !ReferenceEquals(owner, current) &&
            !(current.IsConnected && SameConnectedTarget(server, databaseId, editor.Server, current.Database.ID))) return;
        reloadRequiredHandler = current; reloadNotice.Text = reason + " Reconnect to reload server metadata before saving this model. Unsaved local edits are not discarded automatically.";
        ValidationStatus.Text = "Live metadata changed · reconnect required"; Log(reason + " Reconnect before writing model metadata.");
        advancedRefresh?.RefreshModel();
    }
    internal static bool SameConnectedTarget(string? submittedServer, string? submittedDatabaseId, string? currentServer, string? currentDatabaseId) =>
        !string.IsNullOrWhiteSpace(submittedServer) && !string.IsNullOrWhiteSpace(submittedDatabaseId) &&
        string.Equals(submittedServer!.Trim(), currentServer?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(submittedDatabaseId, currentDatabaseId, StringComparison.Ordinal);
    internal static bool CanFinishWriteReview(object? capturedOwner, object? currentOwner, object? reloadRequiredOwner) =>
        capturedOwner != null && ReferenceEquals(capturedOwner, currentOwner) && !ReferenceEquals(currentOwner, reloadRequiredOwner);
    private void UpdateConnectedContext()
    {
        if (reloadRequiredHandler != null && !ReferenceEquals(reloadRequiredHandler, editor.Handler)) reloadRequiredHandler = null;
        if (reloadRequiredHandler == null) reloadNotice.Text = "Advanced refresh uses an independent server connection. Review the exact TMSL before executing; metadata deployment remains available in native TE2.";
        var context = (Root: semanticWorkspaceRoot ?? workspaceRoot, Server: editor.Server,
            Database: editor.Server == null ? null : editor.Database, Handler: editor.Handler);
        // Retain a folder explicitly selected in the sync page while the shell's source
        // context is unchanged. Routine status updates must not cancel that comparison.
        if (configuredWorkspaceContext == null || configuredWorkspaceContext.Value != context)
        {
            configuredWorkspaceContext = context;
            workspaceSync?.Configure(context.Root, context.Server, context.Database);
        }
        fabricWorkspace?.Configure(() => editor.Handler, () => Run(UpdateSessionAsync));
    }
    private void AddConnectedCommands(IDictionary<string, Action> entries)
    {
        entries["Fabric · Browse / import tables"] = () => GoTo("Fabric");
        entries["Workspace · Disk / Live / Git"] = () => { GoTo("PBIP / Git"); workspaceExperience.SelectedIndex = 1; };
        entries["Refresh · Advanced refresh"] = () => { GoTo("Deploy"); advancedRefresh!.SetScope(null); };
        entries["Refresh · Selected table"] = () =>
        {
            RequireModel(); var selected = editor.Selection.FirstOrDefault(); var table = selected as Table ?? (selected as ITabularTableObject)?.Table;
            if (table == null) throw new InvalidOperationException("Select a table or one of its model objects first.");
            GoTo("Deploy"); advancedRefresh!.SetScope(table.Name);
        };
    }
}
