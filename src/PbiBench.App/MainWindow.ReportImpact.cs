using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PbiBench.Dax.LanguageService;
using PbiBench.DaxStudio;
using PbiBench.Pbir;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public partial class MainWindow
{
    private SemanticRefactorGuard? reportImpactGuard;
    private TabularModelHandler? impactHandler;
    private IReadOnlyList<string> reportImpactPaths = Array.Empty<string>();
    private void RefreshReportImpactGuard()
    {
        if (ReferenceEquals(impactHandler, editor.Handler)) return;
        reportImpactGuard?.Dispose(); impactHandler = editor.Handler;
        reportImpactGuard = impactHandler == null ? null : new SemanticRefactorGuard(impactHandler, ReviewReportImpact);
    }
    private static SemanticField[] ImpactFields(TabularNamedObject obj) => obj is Table table
        ? table.Measures.Select(m => new SemanticField(table.Name, m.Name, "Measure")).Concat(table.Columns.Select(c => new SemanticField(table.Name, c.Name, "Column"))).ToArray()
        : obj is TabularEditor.TOMWrapper.Measure or Column ? new[] { new SemanticField(((ITabularTableObject)obj).Table.Name, obj.Name, obj is TabularEditor.TOMWrapper.Measure ? "Measure" : "Column") } : Array.Empty<SemanticField>();
    private bool ReviewReportImpact(SemanticRefactorRequest request)
    {
        if (reportImpactPaths.Count == 0) return true;
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(() => ReviewReportImpact(request));
        // A refactor checks fresh disk snapshots; the browsing cache is advisory only.
        var fresh = new List<ReportIndex>(); var errors = new List<string>();
        foreach (var path in reportImpactPaths)
        {
            try { var index = ReportIndex.OpenAsync(path, lifetime.Token).GetAwaiter().GetResult();
                if (semanticWorkspaceRoot == null || index.SemanticModelPath == null || string.Equals(index.SemanticModelPath, semanticWorkspaceRoot, StringComparison.OrdinalIgnoreCase)) fresh.Add(index); }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is JsonException || error is InvalidOperationException) { errors.Add(Path.GetFileName(Path.GetDirectoryName(path)) + ": " + error.Message); }
        }
        var fields = ImpactFields(request.Object); var usages = ReportImpact.Find(fresh, fields);
        var uncertain = fresh.Any(r => !r.Enhanced || r.Files.Values.Any(f => f.ParseError != null) || ReportLineage.Build(r).Any(u => u.Table == null));
        if (fresh.Count == 0 && errors.Count == 0) return true;
        var detail = request.Operation + " · " + SemanticModelService.ObjectPath(request.Object) + "\n" + ReportOccurrenceImpact.From(usages) +
            "\nReview report impact before continuing the local semantic change. PBIR files will not be changed. Apply any report mapping separately with its own preview, backup and restore; the two layers are not atomic.";
        if (request.Operation == "Rename") detail += "\nProposed name: " + request.NewValue;
        detail += "\nKnown structured usages are advisory; a zero count does not prove a refactor safe.";
        if (semanticWorkspaceRoot == null || fresh.Any(r => r.SemanticModelPath == null)) detail += "\nModel association is unverified for some reports; qualified-name matches are advisory.";
        if (uncertain) detail += "\nLegacy/malformed definitions or unresolved aliases cannot establish complete usage coverage.";
        if (errors.Count > 0) detail += "\nIncomplete report scan:\n" + string.Join("\n", errors);
        var window = new Window { Owner = this, Title = "Review semantic / report impact", Width = 1060, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(14) }; window.Content = panel;
        var note = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) }; DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        var rows = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, ItemsSource = usages };
        var bar = new WrapPanel(); DockPanel.SetDock(bar, Dock.Bottom); panel.Children.Add(bar); panel.Children.Add(rows);
        var open = new Button { Content = "Open selected usage in Report Studio", Padding = new Thickness(8), Margin = new Thickness(4) };
        open.Click += (_, _) => Run(() => { if (rows.SelectedItem is ReportUsage usage) OpenReportUsage(usage, fresh); }); bar.Children.Add(open);
        var export = new Button { Content = "Export impact / handoff…", Padding = new Thickness(8), Margin = new Thickness(4) };
        export.Click += (_, _) => Run(async () =>
        {
            var dialog = new SaveFileDialog { Filter = "Impact plan|*.json", FileName = "pbibench-report-impact.json" }; if (dialog.ShowDialog(window) != true) return;
            var plans = fields.Select(field => new ReportImpactHandoff(request.Operation, field,
                request.Operation == "Rename" ? request.Object is Table ? field with { Table = request.NewValue! } : field with { Name = request.NewValue! } : null, fresh)).ToArray();
            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Version = 1, Plans = plans }, new JsonSerializerOptions { WriteIndented = true }));
            using var stream = new FileStream(dialog.FileName, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
            await stream.WriteAsync(bytes, 0, bytes.Length, lifetime.Token);
        }); bar.Children.Add(export);
        var cancel = new Button { Content = "Cancel semantic change", IsCancel = true, Padding = new Thickness(8), Margin = new Thickness(4) }; bar.Children.Add(cancel);
        var proceed = new Button { Content = "Reviewed · continue semantic change", Padding = new Thickness(8), Margin = new Thickness(4) };
        proceed.Click += (_, _) => window.DialogResult = true; bar.Children.Add(proceed);
        return window.ShowDialog() == true;
    }
    private void OpenReportUsage(ReportUsage usage, IEnumerable<ReportIndex> indexes)
    {
        var report = indexes.First(r => r.Root == usage.ReportRoot); var page = report.Pages.FirstOrDefault(p => p.File == usage.File || p.Visuals.Any(v => v.File == usage.File));
        companionTools.Launch(RefreshTool("report-studio"), CurrentToolContext() with { ReportFile = Path.Combine(report.Root, "definition.pbir"), PageId = page?.Id, VisualId = page?.Visuals.FirstOrDefault(v => v.File == usage.File)?.Id });
    }
    private async Task ExportSemanticCatalogAsync()
    {
        RequireModel(); var fields = DaxMetadataSnapshotProvider.Capture(editor.Handler!).Symbols.Where(s => s.Kind is DaxSymbolKind.Measure or DaxSymbolKind.Column).Select(s => new SemanticField(s.Table!, s.Name, s.Kind.ToString()));
        var snapshot = new SemanticCatalogSnapshot(fields, true, DateTimeOffset.UtcNow);
        var dialog = new SaveFileDialog { Filter = "Semantic catalog|*.json", FileName = "pbibench-semantic-catalog.json" };
        if (dialog.ShowDialog(this) == true) await snapshot.SaveAsync(dialog.FileName, lifetime.Token);
    }
    private async Task ImportDisplayNamesAsync()
    {
        RequireModel(); var handler = editor.Handler!; var dialog = new OpenFileDialog { Filter = "Display-name mappings|*.json", FileName = "pbibench-display-names.json" };
        if (dialog.ShowDialog(this) != true) return;
        var manifest = await DisplayNameManifest.ReadAsync(dialog.FileName, lifetime.Token);
        if (!ReferenceEquals(handler, editor.Handler)) throw new InvalidOperationException("Model session changed; import again.");
        var edits = new List<SemanticAnnotationRequest>();
        foreach (var group in manifest.Mappings.GroupBy(m => m.Field))
        {
            if (group.Select(m => m.DisplayName).Distinct().Count() != 1) throw new InvalidDataException("Conflicting display names for " + group.Key + ". Resolve them in the mapping file first.");
            var field = group.Key; var table = handler.Model.Tables.FirstOrDefault(t => t.Name == field.Table);
            TabularNamedObject? obj = field.Kind == "Measure" ? table?.Measures.FirstOrDefault(m => m.Name == field.Name) : table?.Columns.FirstOrDefault(c => c.Name == field.Name);
            if (obj == null) throw new InvalidDataException("Unmatched semantic field: " + field.Table + "[" + field.Name + "]");
            var value = JsonSerializer.Serialize(new { Version = 1, DisplayName = group.First().DisplayName, Sources = group.Select(m => new { m.Report, m.Page, m.Visual }) });
            edits.Add(new(obj, "PbiBench.DisplayName", value));
        }
        AuthoringReview.Show(this, new SemanticAnnotationService(handler).Preview(edits), () => editor.Handler, () => Run(UpdateSessionAsync));
    }
}
