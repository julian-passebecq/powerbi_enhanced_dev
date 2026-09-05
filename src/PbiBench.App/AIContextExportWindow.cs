using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PbiBench.AI.ContextExport;

namespace PbiBench.App;

public sealed class AIContextExportWindow : Window
{
    private readonly ContextModel model;
    private readonly IContextSampler? sampler;
    private readonly IReadOnlyList<ContextEvidence> evidence;
    private readonly List<ObjectChoice> objects;
    private readonly List<TableChoice> tables;
    private readonly CheckBox sample = Check("Include data samples (OFF by default)"), sampleReview = Check("I authorize these bounded sample queries; rows can be sensitive and are not anonymized."), roles = Check("Include RLS filters (no members)"), automation = Check("Include automation API reference"), reviewed = Check("I reviewed the exact files and accept their sensitive content."), selected = Check("Selected scope plus dependency context");
    private readonly CheckBox bpa = Check("BPA findings"), metrics = Check("VertiPaq statistics"), tests = Check("Semantic test results"), workspace = Check("Workspace semantic diff");
    private readonly TextBox maximumBytes = new() { Text = "32", Width = 65 }, maximumRows = new() { Text = "250", Width = 65 }, maximumCells = new() { Text = "100000", Width = 85 };
    private readonly DataGrid objectGrid, tableGrid;
    private readonly DataGrid files = Grid(true);
    private readonly TextBox content = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock status = Note("Choose scope, then Review files. No data query runs unless samples and query review are both enabled.");
    private readonly TabControl pages = new();
    private ContextExportPlan? plan;
    private CancellationTokenSource? pending;
    private readonly StackPanel settings = new();
    public ContextExportPlan? CurrentPlan => plan;
    public AIContextExportWindow(ContextModel model, IReadOnlyList<string> treeSelection, IContextSampler? sampler, IReadOnlyList<ContextEvidence>? evidence = null)
    {
        this.model = model; this.sampler = sampler; this.evidence = evidence ?? Array.Empty<ContextEvidence>();
        Title = "AI Context Export · " + model.Name; Width = 1120; Height = 800; MinWidth = 800; MinHeight = 580; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        objects = model.Objects.Select(o => new ObjectChoice(o)).Concat(model.Relationships.Select(r => new ObjectChoice(new ContextObject(r.Id, "Relationship", r.Name)))).ToList();
        tables = model.Objects.Where(o => o.Kind == "Table").Select(t => new TableChoice(t.Name)).ToList();
        objectGrid = Grid(false); tableGrid = Grid(false); objectGrid.ItemsSource = objects; tableGrid.ItemsSource = tables;
        objectGrid.AutoGeneratingColumn += (_, e) => { if (e.PropertyName is "Id") e.Cancel = true; else if (e.PropertyName is not ("Include" or "Exclude" or "Sample")) e.Column.IsReadOnly = true; };
        tableGrid.AutoGeneratingColumn += (_, e) => { if (e.PropertyName == "Table") e.Column.IsReadOnly = true; };
        var root = new DockPanel { Margin = new Thickness(12) }; var bar = new WrapPanel(); DockPanel.SetDock(bar, Dock.Bottom); root.Children.Add(bar);
        bar.Children.Add(Button("Review files", PrepareAsync)); bar.Children.Add(Button("Cancel", () => { pending?.Cancel(); return Task.CompletedTask; }));
        bar.Children.Add(reviewed); bar.Children.Add(Button("Export ZIP…", ExportAsync));
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        settings.Children.Add(Note(ContextExporter.PrivacyNotice)); settings.Children.Add(selected);
        settings.Children.Add(Button("Use current tree selection", () => { selected.IsChecked = true; foreach (var o in objects) o.Include = treeSelection.Contains(o.Id); objectGrid.Items.Refresh(); return Task.CompletedTask; }));
        settings.Children.Add(Note("Include selects metadata. In full-model mode, unchecking a table excludes its children. In selected mode, checked objects select roots; required dependencies appear explicitly in the file review. Sample selects individual columns; table row counts enable sampling."));
        var options = new WrapPanel(); foreach (var c in new[] { roles, automation, bpa, metrics, tests, workspace }) options.Children.Add(c); settings.Children.Add(options);
        settings.Children.Add(sample); settings.Children.Add(sampleReview);
        settings.Children.Add(Note("First-N uses an explicit order column (or first selected column); ties are not deterministic and rows are not representative. Source work can exceed returned row counts for DirectQuery/Direct Lake. Sampling requires a connected model."));
        var limits = new WrapPanel(); foreach (var c in new UIElement[] { Note("ZIP cap MiB (max 128)"), maximumBytes, Note("Rows/table cap (max 1000)"), maximumRows, Note("Cell cap (max 1,000,000)"), maximumCells }) limits.Children.Add(c); settings.Children.Add(limits);
        var scope = new DockPanel(); DockPanel.SetDock(settings, Dock.Top); scope.Children.Add(settings); scope.Children.Add(objectGrid); pages.Items.Add(new TabItem { Header = "Scope / privacy", Content = scope });
        pages.Items.Add(new TabItem { Header = "Samples per table · 0 = excluded", Content = tableGrid });
        var review = new System.Windows.Controls.Grid(); review.RowDefinitions.Add(new() { Height = new GridLength(180) }); review.RowDefinitions.Add(new()); review.Children.Add(files); System.Windows.Controls.Grid.SetRow(content, 1); review.Children.Add(content); pages.Items.Add(new TabItem { Header = "Exact file review", Content = review });
        files.SelectionChanged += (_, _) => { if (files.SelectedItem is ContextFileReview f && plan != null) { var text = plan.ReadText(f.Path); content.Text = text.Length <= 1000000 ? text : text.Substring(0, 1000000) + "\n[Preview truncated at 1,000,000 characters; narrow the export for complete review.]"; } };
        root.Children.Add(pages); Content = root;
        foreach (var check in new[] { sample, sampleReview, roles, automation, selected, bpa, metrics, tests, workspace }) check.Click += (_, _) => Invalidate();
        foreach (var box in new[] { maximumBytes, maximumRows, maximumCells }) box.TextChanged += (_, _) => Invalidate();
        objectGrid.CellEditEnding += (_, _) => Invalidate(); tableGrid.CellEditEnding += (_, _) => Invalidate();
        Closing += (_, _) => pending?.Cancel();
    }
    private void Invalidate() { plan = null; reviewed.IsChecked = false; files.ItemsSource = null; content.Text = ""; }
    public async Task PrepareAsync()
    {
        objectGrid.CommitEdit(DataGridEditingUnit.Row, true); tableGrid.CommitEdit(DataGridEditingUnit.Row, true); Invalidate();
        if (sample.IsChecked == true && sampleReview.IsChecked != true) throw new InvalidOperationException("Review and authorize sample queries before fetching rows.");
        if (tables.Any(t => t.Rows < 0)) throw new InvalidOperationException("Sample row counts cannot be negative; use zero to exclude a table.");
        var categories = new List<string>(); if (bpa.IsChecked == true) categories.Add("BPA"); if (metrics.IsChecked == true) categories.Add("VertiPaq"); if (tests.IsChecked == true) categories.Add("Tests"); if (workspace.IsChecked == true) categories.Add("Workspace");
        var options = new ContextExportOptions
        {
            SelectedScope = selected.IsChecked == true, SelectedIds = objects.Where(o => o.Include && !o.Exclude).Select(o => o.Id).ToArray(),
            ExcludedIds = objects.Where(o => o.Exclude || selected.IsChecked != true && !o.Include).Select(o => o.Id).ToArray(),
            IncludeSamples = sample.IsChecked == true, IncludeRoles = roles.IsChecked == true, IncludeAutomation = automation.IsChecked == true,
            MaximumBytes = checked(long.Parse(maximumBytes.Text) * 1024 * 1024), MaximumRowsPerTable = int.Parse(maximumRows.Text), MaximumSampleCells = int.Parse(maximumCells.Text),
            Samples = tables.Where(t => t.Rows > 0).Select(t => new SampleRequest(t.Table, objects.Where(o => o.Kind == "Column" && o.Table == t.Table && o.Sample).Select(o => o.Name).ToArray(), t.Rows, t.IncludeHidden, string.IsNullOrWhiteSpace(t.OrderColumn) ? null : t.OrderColumn)).ToArray(),
            Evidence = evidence.Where(e => categories.Contains(e.Category)).ToArray()
        };
        pending?.Cancel(); using var cancellation = new CancellationTokenSource(); pending = cancellation; settings.IsEnabled = false; objectGrid.IsEnabled = false; tableGrid.IsEnabled = false;
        try
        {
            status.Text = "Preparing bounded context files…";
            plan = await Task.Run(() => ContextExporter.PrepareAsync(model, options, sampler, cancellation.Token), cancellation.Token);
            files.ItemsSource = plan.Review; files.SelectedIndex = 0; pages.SelectedIndex = 2;
            status.Text = $"{plan.Review.Count} files · conservative ZIP estimate {plan.EstimatedBytes:N0} bytes. Review the file list/content, then acknowledge sensitive content and Export ZIP.";
        }
        finally { pending = null; settings.IsEnabled = true; objectGrid.IsEnabled = true; tableGrid.IsEnabled = true; }
    }
    private async Task ExportAsync()
    {
        var reviewedPlan = plan ?? throw new InvalidOperationException("Prepare a current file review first."); if (reviewed.IsChecked != true) throw new InvalidOperationException("Review the exact files and acknowledge sensitive content first.");
        var name = string.Concat(model.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new SaveFileDialog { Filter = "AI context ZIP|*.zip", FileName = name + ".pbibench-ai-context.zip" }; if (dialog.ShowDialog(this) != true) return;
        using var cancellation = new CancellationTokenSource(); pending = cancellation;
        try { await ContextExporter.WriteAsync(reviewedPlan, dialog.FileName, true, cancellation.Token); status.Text = "Exported the reviewed ZIP. No AI provider was contacted."; } finally { pending = null; }
    }
    private Button Button(string text, Func<Task> action) { var b = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) }; b.Click += async (_, _) => { try { b.IsEnabled = false; await action(); } catch (OperationCanceledException) { status.Text = "Canceled; no ZIP committed."; } catch (Exception error) { status.Text = error.Message; } finally { b.IsEnabled = true; } }; return b; }
    private static CheckBox Check(string text) => new() { Content = text, Margin = new Thickness(4) };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private static DataGrid Grid(bool readOnly) => new() { IsReadOnly = readOnly, AutoGenerateColumns = true, CanUserAddRows = false, Margin = new Thickness(4) };
    public sealed class ObjectChoice(ContextObject obj)
    { public string Id => obj.Id; public string Kind => obj.Kind; public string? Table => obj.Table; public string Name => obj.Name; public bool Hidden => obj.Hidden; public bool Include { get; set; } = true; public bool Exclude { get; set; } public bool Sample { get; set; } = obj.Kind == "Column" && !obj.Hidden; }
    public sealed class TableChoice(string name)
    { public string Table => name; public int Rows { get; set; } public bool IncludeHidden { get; set; } public string OrderColumn { get; set; } = ""; }
}
