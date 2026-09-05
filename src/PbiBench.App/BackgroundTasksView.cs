using System.Windows;
using System.Windows.Controls;
using PbiBench.Core.Tasks;

namespace PbiBench.App;

public sealed class BackgroundTasksView : UserControl, IDisposable
{
    private readonly BackgroundTaskQueue queue;
    private readonly DataGrid grid = new() { IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single,
        EnableRowVirtualization = true, EnableColumnVirtualization = true, CanUserAddRows = false };
    private readonly TextBlock status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private bool disposed;
    private int refreshPending;
    public BackgroundTasksView(BackgroundTaskQueue queue)
    {
        this.queue = queue;
        var panel = new DockPanel { Margin = new Thickness(8) };
        var toolbar = new WrapPanel();
        var cancel = new Button { Content = "Cancel selected", Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
        cancel.Click += (_, _) => { if (grid.SelectedItem is BackgroundTaskInfo item) queue.Cancel(item.Id); };
        var clear = new Button { Content = "Clear completed", Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
        clear.Click += (_, _) => queue.ClearCompleted(); toolbar.Children.Add(cancel); toolbar.Children.Add(clear);
        DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar); DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        foreach (var field in new[] { "Title", "State", "Progress", "Message", "QueuedAt", "StartedAt", "FinishedAt", "CancellationRequested", "Error" })
            grid.Columns.Add(new DataGridTextColumn { Header = field, Binding = new System.Windows.Data.Binding(field), Width = field == "Title" || field == "Message" ? 220 : 130 });
        panel.Children.Add(grid); Content = panel; queue.Changed += QueueChanged; Refresh();
    }
    private void QueueChanged(object? sender, EventArgs args)
    {
        if (disposed || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) Refresh();
        else if (Interlocked.Exchange(ref refreshPending, 1) == 0)
            Dispatcher.BeginInvoke(new Action(() => { Interlocked.Exchange(ref refreshPending, 0); Refresh(); }));
    }
    private void Refresh()
    {
        if (disposed) return;
        var id = (grid.SelectedItem as BackgroundTaskInfo)?.Id; var snapshot = queue.Snapshot(); grid.ItemsSource = snapshot;
        if (id.HasValue) grid.SelectedItem = snapshot.FirstOrDefault(item => item.Id == id.Value);
        var running = snapshot.Count(i => i.State == BackgroundTaskState.Running); var waiting = snapshot.Count(i => i.State == BackgroundTaskState.Queued);
        status.Text = $"{running} running · {waiting} queued · Cancellation is cooperative. Completed operations keep their actual result even if cancellation arrived too late.";
    }
    public void Dispose() { disposed = true; queue.Changed -= QueueChanged; }
}
