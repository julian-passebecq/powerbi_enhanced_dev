using PbiBench.ExternalTools;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PbiBench.Pbir;

namespace PbiBench.ReportStudio;

public sealed partial class StudioWindow
{
    private readonly TextBox search = new() { MinWidth = 240, Margin = new Thickness(4), ToolTip = "Search page, visual type/title/ID or semantic field" };
    private readonly ComboBox pageSelector = new() { MinWidth = 160, DisplayMemberPath = "Name", Margin = new Thickness(4) };
    private readonly TextBlock zoomLabel = new() { Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DataGrid visualSelection = Grid();
    private readonly Dictionary<string, TreeViewItem> treeNodes = new(StringComparer.Ordinal);
    private ReportViewSnapshot? view;
    private bool synchronizing, fit = true;
    private double zoom = 1;
    private readonly CompanionTools companions = new();
    private DockPanel CreateNavigation()
    {
        var panel = new DockPanel { Margin = new Thickness(8, 0, 8, 8) };
        var tools = new StackPanel(); DockPanel.SetDock(tools, Dock.Top); panel.Children.Add(tools);
        tools.Children.Add(Bar(new TextBlock { Text = "Find", Margin = new Thickness(4) }, search, pageSelector));
        tools.Children.Add(Bar(Button("−", () => SetZoom(zoom / 1.25)), Button("+", () => SetZoom(zoom * 1.25)),
            Button("100%", () => SetZoom(1)), Button("Fit page", () => { fit = true; UpdateZoom(); return Task.CompletedTask; }), zoomLabel));
        tools.Children.Add(Bar(Button("Desktop", () => OpenCompanion("powerbi")), Button("VS Code", () => OpenCompanion("vscode")),
            Button("Explorer", () => { if (report != null) { var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true }; start.ArgumentList.Add(report.Root); Process.Start(start); } return Task.CompletedTask; }),
            Button("Load semantic catalog…", LoadCatalogAsync)));
        viewport.Content = canvas; panel.Children.Add(viewport); viewport.SizeChanged += (_, _) => { if (fit) UpdateZoom(); };
        search.TextChanged += (_, _) => { Invalidate(); BuildTree(); DrawPage(); };
        pageSelector.SelectionChanged += (_, _) => { if (!synchronizing && pageSelector.SelectedItem is ReportPage page) SelectFile(page.File); };
        return panel;
    }
    private Task SetZoom(double value) { fit = false; zoom = Math.Clamp(value, 0.1, 4); UpdateZoom(); return Task.CompletedTask; }
    private void UpdateZoom()
    {
        if (fit && viewport.ActualWidth > 20 && viewport.ActualHeight > 20) zoom = Math.Clamp(Math.Min((viewport.ActualWidth - 20) / canvas.Width, (viewport.ActualHeight - 20) / canvas.Height), 0.05, 4);
        canvas.LayoutTransform = new ScaleTransform(zoom, zoom); zoomLabel.Text = (zoom * 100).ToString("0", CultureInfo.InvariantCulture) + "%" + (fit ? " · fit" : "");
    }
    private void SynchronizeSelection()
    {
        synchronizing = true;
        try
        {
            pageSelector.SelectedItem = selectedPage;
            if (selectedFile != null && treeNodes.TryGetValue(selectedFile, out var node)) { node.IsSelected = true; node.BringIntoView(); }
            var row = view?.ForFile(selectedFile ?? "").FirstOrDefault();
            if (lineage.SelectedItem is not ReportUsage selected || selected.File != selectedFile) lineage.SelectedItem = row;
            if (row != null) lineage.ScrollIntoView(row);
            // Single-object navigation does not replace an explicitly prepared multiselection.
            if (visualSelection.SelectedItems.Count <= 1) visualSelection.SelectedItem = selectedVisual;
        }
        finally { synchronizing = false; }
    }
    private string? ChooseReport(IReadOnlyList<string> candidates)
    {
        var choice = new ListBox { ItemsSource = candidates, SelectedIndex = 0, Margin = new Thickness(8) };
        var dialog = new Window { Owner = this, Title = "Choose a report", Width = 780, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel(); var open = new Button { Content = "Open selected report", Margin = new Thickness(8), Padding = new Thickness(8), IsDefault = true };
        DockPanel.SetDock(open, Dock.Bottom); panel.Children.Add(open); panel.Children.Add(choice); dialog.Content = panel;
        open.Click += (_, _) => dialog.DialogResult = choice.SelectedItem != null;
        return dialog.ShowDialog() == true ? choice.SelectedItem as string : null;
    }
    private Task OpenCompanion(string id)
    {
        var current = report ?? throw new InvalidOperationException("Open a report first.");
        var tool = CompanionTools.Catalog.Single(t => t.Id == id);
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PbiBench", "report-studio");
        var config = Path.Combine(settings, id + ".txt");
        var status = companions.Discover(tool, File.Exists(config) ? File.ReadAllText(config) : null, AppContext.BaseDirectory);
        if (status.Path == null)
        {
            var dialog = new OpenFileDialog { Title = "Locate " + tool.ExecutableName, Filter = "Executable|*.exe" };
            if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
            status = companions.Discover(tool, dialog.FileName, AppContext.BaseDirectory);
            Directory.CreateDirectory(settings); File.WriteAllText(config, dialog.FileName);
        }
        companions.Launch(status, new ToolContext(ProjectDirectory: current.Root, ProjectFile: current.ProjectFile, ReportFile: Path.Combine(current.Root, "definition.pbir")));
        return Task.CompletedTask;
    }
    private async Task LoadCatalogAsync()
    {
        var current = report ?? throw new InvalidOperationException("Open a report first.");
        var dialog = new OpenFileDialog { Filter = "Semantic catalog|*.json" }; if (dialog.ShowDialog(this) != true) return;
        var snapshot = await SemanticCatalogSnapshot.ReadAsync(dialog.FileName, lifetime.Token);
        // An imported snapshot may belong to another revision/model. Presence can resolve; absence cannot prove broken.
        model = new(snapshot.Fields, false, "Imported metadata snapshot " + snapshot.CapturedAt.ToString("u") + "; verify model identity and freshness. Absence remains unverified.");
        view = new(current, model, validator.Validate(current)); lineage.ItemsSource = view.Usages; BuildTree();
        if (selectedFile != null) SelectFile(selectedFile); Invalidate(); status.Text = model.Notice;
    }
    private double Offset(string name) => string.IsNullOrWhiteSpace(fields[name].Text) ? 0 : double.Parse(fields[name].Text, CultureInfo.InvariantCulture);
    private static bool? OptionalBool(string value) => value.Trim().ToLowerInvariant() switch
    { "" or "keep" => null, "true" => true, "false" => false, _ => throw new ArgumentException("Use keep, true or false.") };
    internal void VerifyNavigation()
    {
        var current = report ?? throw new InvalidOperationException("Missing report."); var cached = view!.Usages;
        var visual = current.Pages[0].Visuals[0]; search.Text = "Revenue"; FocusObject(visual.PageId, visual.Id);
        if (!ReferenceEquals(pageSelector.SelectedItem, selectedPage) || selectedFile != visual.File || !ReferenceEquals(cached, view.Usages) || tree.SelectedItem is not TreeViewItem item || (string)item.Tag != visual.File)
            throw new InvalidOperationException("Report navigation/cache synchronization failed.");
        search.Text = "no-such-visual"; if (canvas.Children.Count != 0) throw new InvalidOperationException("Search did not filter wireframe.");
        search.Text = ""; SetZoom(1); if (zoom != 1) throw new InvalidOperationException("100% zoom failed."); fit = true; DrawPage();
    }
}
