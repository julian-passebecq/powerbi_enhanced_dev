using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PbiBench.Core.Fabric;

namespace PbiBench.FabricToolbox;

public sealed partial class ToolboxWindow
{
    private readonly IFabricOperationsService operations;
    private readonly TextBox itemSearch = new() { MinWidth = 200, MaxLength = 512 }, jobSearch = new() { MinWidth = 160, MaxLength = 512 };
    private readonly ComboBox itemType = new() { MinWidth = 140 }, jobStatus = new() { MinWidth = 120 }, jobItem = new() { MinWidth = 210, DisplayMemberPath = "Name" };
    private readonly DataGrid jobs = Grid();
    private readonly TextBox itemDetail = DetailBox(), jobDetail = DetailBox();
    private readonly TextBlock inventoryNotice = Note("Load a workspace inventory."), jobNotice = Note("Choose an item and Refresh. No request runs when opening this page.");
    private IReadOnlyList<FabricItem> inventory = Array.Empty<FabricItem>();
    private IReadOnlyList<FabricJobInstance> history = Array.Empty<FabricJobInstance>();
    private void ConfigureInventory(DockPanel explorer)
    {
        var bar = Bar(Note("Search name / type"), itemSearch, itemType, Button("Copy stable IDs", _ => { CopyIds(); return Task.CompletedTask; }), Button("Export filtered inventory…", ExportInventoryAsync), Button("View recent jobs", _ => { jobItem.SelectedItem = items.SelectedItem; pages.SelectedIndex = 3; return Task.CompletedTask; }));
        DockPanel.SetDock(bar, Dock.Top); explorer.Children.Add(bar);
        DockPanel.SetDock(inventoryNotice, Dock.Top); explorer.Children.Add(inventoryNotice);
        DockPanel.SetDock(itemDetail, Dock.Bottom); explorer.Children.Add(itemDetail); explorer.Children.Add(items);
        items.SelectionMode = DataGridSelectionMode.Single; items.AutoGenerateColumns = false;
        Column(items, "Name", nameof(FabricItem.Name)); Column(items, "Type", nameof(FabricItem.Kind)); Column(items, "Item ID", nameof(FabricItem.Id));
        itemType.ItemsSource = new[] { "All types" }; itemType.SelectedIndex = 0;
        itemSearch.TextChanged += (_, _) => FilterItems(); itemType.SelectionChanged += (_, _) => FilterItems();
        items.SelectionChanged += (_, _) => itemDetail.Text = items.SelectedItem is FabricItem item ? item.Name + " · " + item.Kind + "\nWorkspace ID: " + item.WorkspaceId + "\nItem ID: " + item.Id + "\n" + FabricJobSupport.Describe(item.Kind) : "Select an item to inspect it.";
    }
    private UIElement OperationsPage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Note("Recent item jobs · read-only · maximum 10 pages / 1,000 instances per refresh. History depends on Fabric retention."));
        var cancel = new Button { Content = "Cancel request", Margin = new Thickness(4) }; cancel.Click += (_, _) => pending?.Cancel();
        top.Children.Add(Bar(jobItem, Button("Refresh jobs", RefreshJobsAsync), cancel, Button("Show item in inventory", _ => { LinkJobItem(); return Task.CompletedTask; })));
        top.Children.Add(Bar(Note("Filter type / status / item"), jobSearch, jobStatus)); top.Children.Add(jobNotice);
        DockPanel.SetDock(jobDetail, Dock.Bottom); panel.Children.Add(jobDetail); panel.Children.Add(jobs);
        jobs.SelectionMode = DataGridSelectionMode.Single; jobs.AutoGenerateColumns = false;
        foreach (var pair in new[] { ("Item", "ItemName"), ("Job type", "JobType"), ("Status", "Status"), ("Start UTC", "StartTimeUtc"), ("End UTC", "EndTimeUtc"), ("Duration", "Duration") }) Column(jobs, pair.Item1, pair.Item2);
        jobItem.SelectionChanged += (_, _) => { ClearJobs(); jobNotice.Text = jobItem.SelectedItem is FabricItem i ? FabricJobSupport.Describe(i.Kind) : "Select an item from a loaded workspace inventory."; };
        jobSearch.TextChanged += (_, _) => FilterJobs(); jobStatus.SelectionChanged += (_, _) => FilterJobs();
        jobs.SelectionChanged += (_, _) => jobDetail.Text = (jobs.SelectedItem as FabricJobInstance)?.Detail ?? "Select a job to inspect its status, failure and correlation IDs.";
        ClearJobs(); return panel;
    }
    private async Task LoadItemsAsync(CancellationToken ct)
    {
        var workspace = Workspace(); ClearWorkspace(); var result = await catalog.ListAllItemsAsync(workspace.Id, ct); ct.ThrowIfCancellationRequested();
        if (workspaces.SelectedItem is FabricWorkspace current && current.Id == workspace.Id) SetInventory(result);
    }
    internal void SetInventory(IReadOnlyList<FabricItem> result)
    {
        inventory = result; itemType.ItemsSource = new[] { "All types" }.Concat(result.Select(i => i.Kind).Distinct().OrderBy(k => k)).ToArray(); itemType.SelectedIndex = 0;
        jobItem.ItemsSource = inventory; FilterItems();
    }
    private void FilterItems()
    {
        var selected = items.SelectedItem as FabricItem; var visible = FabricInventoryExport.Filter(inventory, itemSearch.Text, itemType.SelectedIndex > 0 ? itemType.SelectedItem as string : null);
        items.ItemsSource = visible; items.SelectedItem = visible.FirstOrDefault(i => i.Id == selected?.Id) ?? visible.FirstOrDefault(); inventoryNotice.Text = visible.Count + " / " + inventory.Count + " items · export includes these filtered rows.";
    }
    internal async Task RefreshJobsAsync(CancellationToken ct)
    {
        var item = jobItem.SelectedItem as FabricItem ?? throw new InvalidOperationException("Select an item first."); ClearJobs(); jobNotice.Text = "Reading recent jobs…";
        try
        {
            var result = await operations.ListRecentAsync(item, new(), ct); ct.ThrowIfCancellationRequested();
            if (!Equals(jobItem.SelectedItem, item)) return;
            history = result.Jobs; jobStatus.ItemsSource = new[] { "All statuses" }.Concat(history.Select(j => j.Status).Distinct().OrderBy(s => s)).ToArray(); jobStatus.SelectedIndex = 0;
            FilterJobs(); jobNotice.Text = result.Notice + " " + history.Count + " instances.";
        }
        catch (OperationCanceledException) { jobNotice.Text = "Job history request canceled; refresh to read again."; throw; }
        catch { jobNotice.Text = "Job history was not loaded. Check the status message and refresh after resolving access or service availability."; throw; }
    }
    private void FilterJobs() => jobs.ItemsSource = history.Where(j => (jobStatus.SelectedIndex <= 0 || j.Status == jobStatus.SelectedItem as string) &&
        (j.ItemName + " " + j.JobType + " " + j.Status).IndexOf(jobSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
    private void ClearJobs() { history = Array.Empty<FabricJobInstance>(); jobs.ItemsSource = history; jobDetail.Text = ""; jobStatus.ItemsSource = new[] { "All statuses" }; jobStatus.SelectedIndex = 0; }
    private void ClearWorkspace()
    {
        inventory = Array.Empty<FabricItem>(); items.ItemsSource = inventory; jobItem.ItemsSource = inventory; ClearJobs();
        resolved = null; schema = null; columns.ItemsSource = null; data.ItemsSource = null; schemas.ItemsSource = null; tables.ItemsSource = null;
        itemDetail.Text = ""; inventoryNotice.Text = "Load the selected workspace inventory."; sourceInfo.Text = "Select a data item and Browse source.";
    }
    private void CopyIds()
    {
        var item = items.SelectedItem as FabricItem ?? throw new InvalidOperationException("Select an item first."); Clipboard.SetText("Workspace ID: " + item.WorkspaceId + "\nItem ID: " + item.Id);
    }
    private async Task ExportInventoryAsync(CancellationToken ct)
    {
        var snapshot = items.Items.Cast<FabricItem>().ToArray(); if (snapshot.Length == 0) throw new InvalidOperationException("No filtered items to export.");
        var dialog = new SaveFileDialog { Filter = "Inventory JSON|*.json|Inventory CSV|*.csv", FileName = "fabric-inventory.json" };
        if (dialog.ShowDialog(this) == true) await FabricInventoryExport.SaveAsync(dialog.FileName, snapshot, dialog.FilterIndex == 2, ct);
    }
    private void LinkJobItem()
    {
        var id = (jobs.SelectedItem as FabricJobInstance)?.ItemId ?? (jobItem.SelectedItem as FabricItem)?.Id;
        if (id == null) throw new InvalidOperationException("Select an item or job first."); itemSearch.Text = ""; itemType.SelectedIndex = 0;
        items.SelectedItem = inventory.Single(i => i.Id == id); pages.SelectedIndex = 1; items.ScrollIntoView(items.SelectedItem);
    }
    private void SetBusy(bool busy)
    { foreach (var b in actions) b.IsEnabled = !busy; foreach (var c in new Control[] { workspaces, jobItem, schemas, tables, tenant, client }) c.IsEnabled = !busy; }
    private static TextBox DetailBox() => new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 115, MaxHeight = 230, Margin = new Thickness(6) };
    private static void Column(DataGrid grid, string label, string path) => grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 100 });
    internal void VerifyOfflineViews()
    {
        var item = new FabricItem("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "Daily sales pipeline", "DataPipeline");
        SetInventory(new[] { item }); itemSearch.Text = "sales";
        if (items.Items.Count != 1 || !itemDetail.Text.Contains(item.Id)) throw new InvalidOperationException("Inventory filter/details smoke failed.");
        jobItem.SelectedItem = item; history = new[] { new FabricJobInstance("33333333-3333-3333-3333-333333333333", item.WorkspaceId, item.Id, item.Name, item.Kind, "Pipeline", "Completed", "Scheduled", DateTimeOffset.UtcNow.AddMinutes(-3), DateTimeOffset.UtcNow, null, null) };
        FilterJobs(); jobs.SelectedIndex = 0; pages.SelectedIndex = 3;
        if (jobs.Items.Count != 1 || !jobDetail.Text.Contains(item.Id)) throw new InvalidOperationException("Operations details smoke failed.");
        jobNotice.Text = "Offline smoke fixture · no authenticated target used.";
    }
    internal void Capture(string path)
    {
        UpdateLayout(); var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
