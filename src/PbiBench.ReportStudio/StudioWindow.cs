using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PbiBench.Pbir;

namespace PbiBench.ReportStudio;

public sealed class StudioWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly ReportValidator validator = new();
    private readonly ReportChangeEngine engine;
    private readonly ReportActions actions;
    private readonly TreeView tree = new();
    private readonly Canvas canvas = new() { Background = Brushes.White, ClipToBounds = true };
    private readonly TextBox input = new() { Width = 500, Padding = new Thickness(6), VerticalContentAlignment = VerticalAlignment.Center }, raw = ReadOnly(), inspector = ReadOnly(), diff = ReadOnly(), disk = ReadOnly();
    private readonly DataGrid changes = Grid(), validation = Grid(), lineage = Grid();
    private readonly ComboBox gallery = new() { MinWidth = 210, DisplayMemberPath = "Title" };
    private readonly StackPanel parameters = new();
    private readonly Dictionary<string, TextBox> fields = new();
    private readonly TextBlock status = new() { Text = "Open a PBIP or PBIR project to begin.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
    private readonly TextBlock actionInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), MaxWidth = 530 };
    private readonly CheckBox reviewed = new() { Content = "I reviewed the exact files and diff", Margin = new Thickness(8) };
    private readonly Button apply = new() { Content = "Apply reviewed plan", IsEnabled = false, Margin = new Thickness(4), Padding = new Thickness(9) };
    private readonly TabControl bottom = new();
    private readonly Button preview;
    private ReportIndex? report;
    private LocalSemanticCatalog? model;
    private ReportPage? selectedPage;
    private ReportVisual? selectedVisual;
    private string? selectedFile;
    private string? backup;
    private ReportChangePlan? plan;
    private bool busy;
    private int loadRevision;
    public ReportIndex? CurrentReport => report;
    public ReportChangePlan? CurrentPlan => plan;

    public StudioWindow()
    {
        engine = new(validator); actions = new(engine);
        Title = "PbiBench · Report Studio"; Width = 1480; Height = 920; MinWidth = 1050; MinHeight = 650;
        FontFamily = new("Segoe UI"); FontSize = 14; Background = Brush(244, 246, 249); inspector.TextWrapping = TextWrapping.Wrap;
        var root = new DockPanel(); Content = root;
        var header = new StackPanel { Background = Brush(22, 40, 58), Margin = new Thickness(0) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "PbiBench   /   Report Studio", Foreground = Brushes.White, FontSize = 24, Margin = new Thickness(16, 12, 16, 4) });
        var tools = Bar(input, Button("Open file…", ChooseFile), Button("Open folder…", ChooseFolder), Button("Open path", () => OpenAsync(input.Text)), Button("Refresh", () => report == null ? Task.CompletedTask : OpenAsync(report.Root)));
        tools.Margin = new Thickness(12, 4, 12, 10); header.Children.Add(tools);
        var note = new TextBlock { Text = "Local PBIR engineering · Wireframe view · Metadata can contain persisted filter/slicer values. Close this project in Desktop before applying disk edits.", Margin = new Thickness(12, 8, 12, 4), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var body = new System.Windows.Controls.Grid { Margin = new Thickness(10) }; root.Children.Add(body);
        body.RowDefinitions.Add(new() { Height = new GridLength(3, GridUnitType.Star) }); body.RowDefinitions.Add(new() { Height = new GridLength(2, GridUnitType.Star), MinHeight = 210 });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(235) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(340) });
        Place(body, tree, 0, 0);
        var wireframe = new DockPanel { Margin = new Thickness(8, 0, 8, 8) }; wireframe.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = canvas }); Place(body, wireframe, 0, 1);
        var details = new TabControl(); details.Items.Add(new TabItem { Header = "Inspector", Content = inspector }); details.Items.Add(new TabItem { Header = "JSON · read-only", Content = raw }); Place(body, details, 0, 2);
        Place(body, bottom, 1, 0); System.Windows.Controls.Grid.SetColumnSpan(bottom, 3);
        var actionPanel = new DockPanel(); var actionBar = Bar(gallery); DockPanel.SetDock(actionBar, Dock.Top); actionPanel.Children.Add(actionBar);
        preview = Button("Preview exact changes", PreviewAsync); actionBar.Children.Add(preview); actionBar.Children.Add(reviewed); actionBar.Children.Add(apply);
        var parameterPanel = new StackPanel { Orientation = Orientation.Horizontal }; parameterPanel.Children.Add(parameters); parameterPanel.Children.Add(actionInfo); DockPanel.SetDock(parameterPanel, Dock.Top); actionPanel.Children.Add(parameterPanel);
        var split = new System.Windows.Controls.Grid(); split.ColumnDefinitions.Add(new() { Width = new GridLength(310) }); split.ColumnDefinitions.Add(new()); Place(split, changes, 0, 0); Place(split, diff, 0, 1); actionPanel.Children.Add(split);
        bottom.Items.Add(new TabItem { Header = "Actions / Changes", Content = actionPanel }); bottom.Items.Add(new TabItem { Header = "Validation", Content = validation }); bottom.Items.Add(new TabItem { Header = "Lineage", Content = lineage }); bottom.Items.Add(new TabItem { Header = "Git / Disk", Content = disk });
        gallery.ItemsSource = ReportActionGallery.All; gallery.SelectionChanged += (_, _) => ConfigureAction(); gallery.SelectedIndex = 0;
        tree.SelectedItemChanged += (_, _) => { if (tree.SelectedItem is TreeViewItem item && item.Tag is string file) SelectFile(file); };
        changes.SelectionChanged += (_, _) => diff.Text = (changes.SelectedItem as ReportFileChange)?.ExactDiff ?? "";
        changes.AutoGeneratingColumn += (_, e) => { if (e.PropertyName is not ("Path" or "Operation")) e.Cancel = true; };
        lineage.MouseDoubleClick += (_, _) => { if (lineage.SelectedItem is ReportUsage usage) SelectFile(usage.File); };
        reviewed.Checked += (_, _) => apply.IsEnabled = !busy && plan?.CanApply == true; reviewed.Unchecked += (_, _) => apply.IsEnabled = false;
        apply.Click += async (_, _) => await RunAsync(ApplyAsync);
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
    }
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
    private static TextBox ReadOnly() => new() { IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true, FontFamily = new("Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
    private static DataGrid Grid() => new() { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, EnableRowVirtualization = true };
    private static WrapPanel Bar(params UIElement[] items) { var panel = new WrapPanel(); foreach (var item in items) panel.Children.Add(item); return panel; }
    private static void Place(System.Windows.Controls.Grid grid, UIElement element, int row, int column) { System.Windows.Controls.Grid.SetRow(element, row); System.Windows.Controls.Grid.SetColumn(element, column); grid.Children.Add(element); }
    private Button Button(string title, Func<Task> action) { var button = new Button { Content = title, Padding = new Thickness(9), Margin = new Thickness(4) }; button.Click += async (_, _) => await RunAsync(action); return button; }
    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return; busy = true; apply.IsEnabled = false; preview.IsEnabled = false;
        try { await action(); } catch (OperationCanceledException) { } catch (Exception error) { ShowError(error); }
        finally { busy = false; preview.IsEnabled = true; apply.IsEnabled = plan?.CanApply == true && reviewed.IsChecked == true; }
    }
    public void ShowError(Exception error) { Invalidate(); status.Text = error.Message; }
    private Task ChooseFile() { var dialog = new OpenFileDialog { Filter = "Power BI project/report|*.pbip;*.pbir" }; return dialog.ShowDialog(this) == true ? OpenAsync(dialog.FileName) : Task.CompletedTask; }
    private Task ChooseFolder() { var dialog = new OpenFolderDialog(); return dialog.ShowDialog(this) == true ? OpenAsync(dialog.FolderName) : Task.CompletedTask; }
    public async Task OpenAsync(string path)
    {
        Invalidate(); var revision = ++loadRevision;
        var loaded = await ReportIndex.OpenAsync(path, lifetime.Token);
        var catalog = await ReportLineage.ReadLocalModelAsync(loaded.SemanticModelPath, lifetime.Token);
        var issues = await Task.Run(() => validator.Validate(loaded), lifetime.Token);
        if (revision != loadRevision) return;
        report = loaded; model = catalog; input.Text = loaded.ProjectFile ?? loaded.Root; selectedPage = null; selectedVisual = null; selectedFile = null;
        tree.Items.Clear(); var root = Node(loaded.Name, "definition/report.json"); root.IsExpanded = true; tree.Items.Add(root);
        foreach (var page in loaded.Pages) { var p = Node(page.Name, page.File); root.Items.Add(p); foreach (var visual in page.Visuals) p.Items.Add(Node(visual.Type + " · " + visual.Id, visual.File)); }
        foreach (var group in new[] { "bookmarks", "filters", "reportExtensions" })
        {
            var node = new TreeViewItem { Header = group };
            foreach (var file in loaded.Files.Keys.Where(f => f.IndexOf(group, StringComparison.OrdinalIgnoreCase) >= 0)) node.Items.Add(Node(Path.GetFileName(file), file));
            if (group == "filters") foreach (var file in loaded.Files.Values.Where(f => f.ParseError == null && f.Json()["filterConfig"] != null)) node.Items.Add(Node(file.Path, file.Path));
            root.Items.Add(node);
        }
        var resources = new TreeViewItem { Header = "Resources (" + loaded.Resources.Count + ")" }; foreach (var resource in loaded.Resources) resources.Items.Add(new TreeViewItem { Header = resource }); root.Items.Add(resources);
        validation.ItemsSource = issues; lineage.ItemsSource = ReportLineage.Build(loaded, catalog.Fields, catalog.Complete);
        SelectFile(loaded.Pages.FirstOrDefault()?.File ?? "definition/report.json");
        status.Text = loaded.Name + " · " + loaded.Pages.Count + " pages · " + loaded.Pages.Sum(p => p.Visuals.Count) + " visuals · " + issues.Count + " validation issues · " + catalog.Notice;
        await RefreshDiskAsync();
    }
    private static TreeViewItem Node(string label, string file) => new() { Header = label, Tag = file };
    private void SelectFile(string file)
    {
        if (report == null || !report.Files.TryGetValue(file, out var definition)) return;
        Invalidate(); selectedFile = file;
        selectedPage = report.Pages.FirstOrDefault(p => p.File == file || p.Visuals.Any(v => v.File == file)); selectedVisual = selectedPage?.Visuals.FirstOrDefault(v => v.File == file);
        raw.Text = definition.Text;
        var usages = ReportLineage.Build(report, model?.Fields, model?.Complete == true).Where(u => u.File == file).ToArray();
        inspector.Text = file + "\n\nSchema: " + (definition.Schema ?? "Unknown") + "\nPBIR version: " + report.Version + "\nSHA-256: " + definition.Hash + "\n\n" +
            (selectedVisual == null ? selectedPage?.Name ?? report.Name : JsonSerializer.Serialize(selectedVisual, new JsonSerializerOptions { WriteIndented = true })) + "\n\nSemantic references\n" +
            string.Join("\n", usages.Select(u => u.Kind + " · " + u.Table + "[" + u.Name + "] · " + u.Status));
        if (definition.ParseError == null) { var json = definition.Json(); inspector.Text += "\n\nFilters\n" + json["filterConfig"] + "\n\nAnnotations\n" + json["annotations"]; }
        DrawPage();
    }
    public void FocusObject(string? pageId, string? visualId)
    {
        var page = report?.Pages.FirstOrDefault(p => p.Id == pageId); if (page == null) return;
        SelectFile(page.Visuals.FirstOrDefault(v => v.Id == visualId)?.File ?? page.File);
    }
    private void DrawPage()
    {
        canvas.Children.Clear(); canvas.Width = Math.Max(1, selectedPage?.Width ?? 1280); canvas.Height = Math.Max(1, selectedPage?.Height ?? 720);
        foreach (var visual in selectedPage?.Visuals ?? Array.Empty<ReportVisual>())
        {
            var references = report == null ? "" : string.Join("\n", ReportLineage.Build(report).Where(u => u.File == visual.File).Select(u => u.Table + "[" + u.Name + "]").Distinct().Take(4));
            var button = new Button { Width = Math.Max(1, visual.Width), Height = Math.Max(1, visual.Height), Opacity = visual.Hidden ? 0.4 : 1,
                Background = visual == selectedVisual ? Brush(219, 235, 252) : Brush(239, 243, 249), BorderBrush = Brush(97, 126, 151), BorderThickness = new Thickness(visual == selectedVisual ? 3 : 1),
                Content = new TextBlock { Text = visual.Type + "\n" + (visual.Title.Length == 0 ? visual.Id : visual.Title) + "\nz=" + visual.Z + (visual.Hidden ? " · hidden" : "") + "\n" + references, FontSize = 22, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) }, ToolTip = visual.Id };
            button.Click += (_, _) => SelectFile(visual.File); Canvas.SetLeft(button, visual.X); Canvas.SetTop(button, visual.Y); canvas.Children.Add(button);
        }
    }
    private void Invalidate() { plan = null; reviewed.IsChecked = false; apply.IsEnabled = false; changes.ItemsSource = null; diff.Clear(); }
    private void ConfigureAction()
    {
        Invalidate(); parameters.Children.Clear(); fields.Clear(); if (gallery.SelectedItem is not ReportActionCard card) return;
        actionInfo.Text = card.Selection + " · " + card.Purpose + "\nOriginal PbiBench action · Microsoft public PBIR schemas · Local preview + backup";
        string[] names = card.Id switch { "duplicate-page" => new[] { "Page name" }, "title" => new[] { "Title" }, "annotation" => new[] { "Name", "Value" }, "map-field" => new[] { "Kind (Measure/Column)", "From table", "From field", "To table", "To field" }, "copy-visual" => new[] { "Target report path", "Target page ID" }, _ => Array.Empty<string>() };
        foreach (var name in names) { var field = new TextBox { Width = 220, MaxLength = name == "Value" ? 2048 : 512, Margin = new Thickness(3) }; if (name.StartsWith("Kind", StringComparison.Ordinal)) field.Text = "Measure"; field.TextChanged += (_, _) => Invalidate(); fields[name] = field; parameters.Children.Add(Bar(new TextBlock { Text = name, Width = 140, Margin = new Thickness(3) }, field)); }
    }
    private async Task PreviewAsync()
    {
        var current = report ?? throw new InvalidOperationException("Open a PBIR report first."); var card = (ReportActionCard)gallery.SelectedItem;
        string Value(string name) => fields[name].Text;
        ReportChangePlan prepared;
        switch (card.Id)
        {
            case "duplicate-page": prepared = actions.DuplicatePage(current, (selectedPage ?? throw new InvalidOperationException("Select a page.")).Id, Value("Page name")); break;
            case "duplicate-visual": prepared = actions.DuplicateVisual(current, (selectedVisual ?? throw new InvalidOperationException("Select a visual.")).File, selectedPage!.Id); break;
            case "copy-visual": prepared = actions.CopyVisual(current, (selectedVisual ?? throw new InvalidOperationException("Select a visual.")).File, await ReportIndex.OpenAsync(Value("Target report path"), lifetime.Token), Value("Target page ID")); break;
            case "title": prepared = actions.SetTitle(current, (selectedVisual ?? throw new InvalidOperationException("Select a visual.")).File, Value("Title")); break;
            case "annotation": prepared = actions.Annotate(current, selectedFile ?? "definition/report.json", Value("Name"), Value("Value")); break;
            case "map-field":
                var target = new SemanticField(Value("To table"), Value("To field"), Value("Kind (Measure/Column)"));
                if (model?.Fields.Contains(target) != true) throw new InvalidOperationException("Select a target field found in the local semantic model. Unverified mappings are read-only in this gallery.");
                prepared = actions.ReplaceReference(current, new(Value("From table"), Value("From field"), target.Kind), target); break;
            case "restore":
                var dialog = new OpenFileDialog { Filter = "Report backup manifest|manifest.json", InitialDirectory = Path.Combine(current.Root, ".pbibench", "report-backups") };
                if (dialog.ShowDialog(this) != true) return; prepared = await engine.PreviewRestoreAsync(current.Root, dialog.FileName, lifetime.Token); break;
            case "inventory":
                var save = new SaveFileDialog { Filter = "Inventory JSON|*.json", FileName = "report-inventory.json" };
                if (save.ShowDialog(this) == true) await ReportActions.ExportInventoryAsync(current, save.FileName, lifetime.Token); return;
            default: await OpenAsync(current.Root); bottom.SelectedIndex = 1; return;
        }
        ShowPlan(prepared);
    }
    internal void ShowPlan(ReportChangePlan prepared)
    {
        Invalidate(); plan = prepared; changes.ItemsSource = prepared.Changes; changes.SelectedIndex = 0; validation.ItemsSource = prepared.Validation; bottom.SelectedIndex = 0;
        status.Text = prepared.Title + " · " + prepared.Changes.Count + " exact files · Target: " + prepared.Root + (prepared.CanApply ? " · Review before apply." : " · Nothing to apply or validation failed; inspect Validation.");
    }
    private async Task ApplyAsync()
    {
        var reviewedPlan = plan ?? throw new InvalidOperationException("Preview again.");
        if (reviewed.IsChecked != true) throw new InvalidOperationException("Review the exact files and diff.");
        var result = await engine.ApplyAsync(reviewedPlan.Approve(reviewedPlan.Id), lifetime.Token); backup = result.BackupManifest;
        await OpenAsync(reviewedPlan.Root); status.Text = "Applied and validated · Backup: " + backup + " · Restore is a separate reviewed plan.";
    }
    internal async Task ApplySmokePlanAsync() { reviewed.IsChecked = true; await ApplyAsync(); }
    private async Task RefreshDiskAsync()
    {
        if (report == null) return; var current = report;
        var text = current.Root + "\nPBIR: " + current.Version + "\n" + current.Files.Count + " definition files\n\nBackups: .pbibench/report-backups\nLast backup: " + backup + "\n\n";
        try
        {
            using var process = new Process { StartInfo = new("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
            foreach (var arg in new[] { "-C", current.Root, "status", "--short", "--", "." }) process.StartInfo.ArgumentList.Add(arg);
            process.Start(); var stdout = process.StandardOutput.ReadToEndAsync(lifetime.Token); var stderr = process.StandardError.ReadToEndAsync(lifetime.Token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
            text += process.ExitCode == 0 ? "Git status\n" + await stdout : "Git unavailable for this report folder."; await stderr;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is OperationCanceledException) { text += "Git status unavailable; disk snapshots and restore remain available."; }
        if (ReferenceEquals(report, current)) disk.Text = text;
    }
}
