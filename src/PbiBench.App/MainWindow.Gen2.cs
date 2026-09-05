using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PbiBench.Dax.LanguageService;
using PbiBench.DaxStudio;
using PbiBench.Pbir;
using PbiBench.Semantic;
using PbiBench.Workspace;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public partial class MainWindow
{
    private string? projectFile, reportFile;
    private readonly CompanionTools companionTools = new();
    private readonly Dictionary<string, CompanionStatus> toolStatuses = new(StringComparer.Ordinal);
    private IReadOnlyList<ReportIndex> reportIndexes = Array.Empty<ReportIndex>();
    private readonly Dictionary<string, LocalSemanticCatalog> reportModels = new(StringComparer.OrdinalIgnoreCase);
    private int reportRevision;
    private sealed record DependencyRow(string Direction, string Object, string Kind);
    private readonly Dictionary<string, TabularNamedObject> dependencyObjects = new(StringComparer.Ordinal);
    private void InitializeGen2()
    {
        foreach (var tool in CompanionTools.Catalog) RefreshTool(tool.Id);
        daxWorkspace!.AddWorkbenchCommand("Analyze in DAX Studio", () => LaunchDaxStudio(this, new RoutedEventArgs()));
        daxWorkspace.AddWorkbenchCommand("Open in Bravo", () => LaunchCompanion("bravo"), () => ToolState("bravo"));
        daxWorkspace.AddWorkbenchCommand("Report Studio", () => LaunchCompanion("report-studio"), () => ToolState("report-studio"));
        daxWorkspace.AddWorkbenchCommand("Power BI Desktop", () => LaunchCompanion("powerbi"), () => ToolState("powerbi"));
        daxWorkspace.AddWorkbenchCommand("VS Code", () => LaunchCompanion("vscode"), () => ToolState("vscode"));
        daxWorkspace.AddWorkbenchCommand("Quick Open · Ctrl+P", ShowCommands);
        daxWorkspace.AddWorkbenchCommand("Semantic tests", () => { GoTo("QA"); qualityWorkspace.SelectedIndex = 3; });
        diagram.SemanticModeRequested += _ => RefreshSemanticMode();
        diagram.SemanticRowActivated += row => Run(() =>
        {
            if (row is DependencyRow dependency && dependencyObjects.TryGetValue(dependency.Object, out var obj)) { editor.Select(obj); OpenSelectedModelObject(); }
            else if (row is ReportUsage usage)
            {
                var report = reportIndexes.First(r => r.Root == usage.ReportRoot); var page = report.Pages.FirstOrDefault(p => p.File == usage.File || p.Visuals.Any(v => v.File == usage.File));
                companionTools.Launch(toolStatuses["report-studio"], CurrentToolContext() with { ReportFile = Path.Combine(report.Root, "definition.pbir"), PageId = page?.Id, VisualId = page?.Visuals.FirstOrDefault(v => v.File == usage.File)?.Id });
            }
        });
    }
    private ToolContext CurrentToolContext() => new(editor.Handler?.IsConnected == true ? editor.Server : null, editor.Handler?.IsConnected == true ? editor.Database : null, workspaceRoot, projectFile, reportFile);
    private CompanionStatus RefreshTool(string id)
    {
        var tool = CompanionTools.Catalog.Single(t => t.Id == id); var config = Path.Combine(settingsDirectory, id + "-path.txt");
        return toolStatuses[id] = companionTools.Discover(tool, File.Exists(config) ? File.ReadAllText(config).Trim() : null, AppDomain.CurrentDomain.BaseDirectory);
    }
    private (bool Enabled, string Reason) ToolState(string id)
    { var state = ExternalToolContext.Evaluate(toolStatuses[id], CurrentToolContext()); return (state.Enabled, state.Reason); }
    private void LaunchCompanion(string id) => Run(() => companionTools.Launch(RefreshTool(id), CurrentToolContext()));
    private void ChooseReportContext() => ChooseReportContext(false);
    private void ChooseReportContext(bool launch) => Run(async () =>
    {
        var dialog = new OpenFileDialog { Title = "Choose PBIP / PBIR report context", Filter = "Power BI project or report|*.pbip;*.pbir" }; if (dialog.ShowDialog(this) != true) return;
        var index = await ReportIndex.OpenAsync(dialog.FileName, lifetime.Token); reportFile = Path.Combine(index.Root, "definition.pbir"); projectFile = index.ProjectFile;
        await UpdateWorkspaceAsync(index.ProjectFile ?? Path.GetDirectoryName(index.Root)); reportFile = Path.Combine(index.Root, "definition.pbir");
        if (launch) LaunchCompanion("report-studio");
    });
    private async Task UpdateReportIndexesAsync(PbipInventory? inventory)
    {
        var revision = ++reportRevision; var reports = new List<ReportIndex>(); var models = new Dictionary<string, LocalSemanticCatalog>(StringComparer.OrdinalIgnoreCase);
        projectFile = inventory?.PbipFiles.Count == 1 ? inventory.PbipFiles[0] : null;
        var candidates = inventory?.PbirFiles.Where(p => Path.GetFileName(p).Equals("definition.pbir", StringComparison.OrdinalIgnoreCase)).Take(50).ToArray() ?? Array.Empty<string>();
        if (reportFile == null || !candidates.Contains(reportFile, StringComparer.OrdinalIgnoreCase)) reportFile = candidates.Length == 1 ? candidates[0] : null;
        foreach (var path in candidates)
        {
            try { var report = await ReportIndex.OpenAsync(path, lifetime.Token); reports.Add(report); models[report.Root] = await ReportLineage.ReadLocalModelAsync(report.SemanticModelPath, lifetime.Token); }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is System.Text.Json.JsonException || error is InvalidOperationException) { Log("PBIR index unavailable: " + Path.GetFileName(Path.GetDirectoryName(path)) + " · " + error.Message); }
        }
        if (revision != reportRevision) return; reportIndexes = reports.AsReadOnly(); reportModels.Clear(); foreach (var pair in models) reportModels.Add(pair.Key, pair.Value); RefreshSemanticMode();
    }
    private void RefreshSemanticMode()
    {
        if (diagram.SemanticMode == "Model") return;
        if (diagram.SemanticMode == "Dependencies")
        {
            dependencyObjects.Clear(); var rows = new List<DependencyRow>();
            foreach (var obj in editor.Selection.OfType<IDaxDependantObject>().SelectMany(o => o.DependsOn.Keys).OfType<TabularNamedObject>()) { var path = SemanticModelService.ObjectPath(obj); dependencyObjects[path] = obj; rows.Add(new("Depends on →", path, obj.ObjectTypeName)); }
            foreach (var obj in editor.Selection.OfType<IDaxObject>().SelectMany(o => o.ReferencedBy).OfType<TabularNamedObject>()) { var path = SemanticModelService.ObjectPath(obj); dependencyObjects[path] = obj; rows.Add(new("← Used by", path, obj.ObjectTypeName)); }
            diagram.SetSemanticRows(rows.Distinct().ToArray(), "Selected links from the existing TE2 dependency graph. Double-click to open the existing editor; native Dependencies remains available."); return;
        }
        var all = reportIndexes.SelectMany(r =>
        {
            var local = reportModels[r.Root]; var matches = editor.Handler != null && semanticWorkspaceRoot != null && string.Equals(r.SemanticModelPath, semanticWorkspaceRoot, StringComparison.OrdinalIgnoreCase);
            var fields = matches ? DaxMetadataSnapshotProvider.Capture(editor.Handler!).Symbols.Where(s => s.Kind is DaxSymbolKind.Measure or DaxSymbolKind.Column).Select(s => new SemanticField(s.Table!, s.Name, s.Kind.ToString())).ToArray() : local.Fields;
            return ReportLineage.Build(r, fields, matches || local.Complete);
        }).ToArray();
        if (diagram.SemanticMode == "Issues")
        {
            diagram.SetSemanticRows((currentFindings ?? Array.Empty<PbiBench.Automation.BpaFinding>()).Select(f => new { Area = "BPA", Object = f.ObjectPath, Issue = f.Reason }).Concat(all.Where(u => u.Status == "Broken reference").Select(u => new { Area = "Report", Object = u.Report + "/" + u.Page + "/" + u.Visual, Issue = u.Table + "[" + u.Name + "] · " + u.Status })).ToArray(), "Current BPA findings and broken local report references. Run BPA and refresh PBIP/Git to update evidence."); return;
        }
        var selected = editor.Selection; var usage = selected.Count == 0 ? all : all.Where(u => selected.Any(o => o is Table t ? t.Name == u.Table : o.Name == u.Name && (o as ITabularTableObject)?.Table.Name == u.Table && (o is Measure ? u.Kind == "Measure" : u.Kind == "Column"))).ToArray();
        diagram.SetSemanticRows(usage, reportIndexes.Count == 0 ? "Open a PBIP project through PBIP / Git or Apps / Tools to index report usage." : usage.Length + " references for the current selection. Double-click to open the visual in Report Studio. Refresh PBIP/Git after external edits.");
    }
    private void AddQuickOpenEntries(IDictionary<string, Action> entries)
    {
        entries["Apps · choose PBIP / report context"] = ChooseReportContext;
        entries["Automate · Power BI C# Gallery"] = () => { GoTo("Automate"); automationWorkspace.SelectedIndex = 2; };
        foreach (var tool in CompanionTools.Catalog) { var id = tool.Id; entries["Apps · " + tool.Name] = () => LaunchCompanion(id); }
        if (editor.Handler != null) foreach (var symbol in DaxMetadataSnapshotProvider.Capture(editor.Handler).Symbols.Take(10000))
            entries["Object · " + symbol.Kind + " · " + symbol.QualifiedName] = () => Run(() =>
            {
                var obj = DaxMetadataSnapshotProvider.Resolve(editor.Handler!, symbol);
                if (obj != null) { editor.Select(obj); OpenSelectedModelObject(); }
            });
        if (semanticWorkspaceRoot != null)
        {
            var queryFolder = Path.Combine(semanticWorkspaceRoot, "DAXQueries");
            if (Directory.Exists(queryFolder)) foreach (var file in Directory.EnumerateFiles(queryFolder, "*.dax", SearchOption.TopDirectoryOnly).Take(500)) entries["Query · " + Path.GetFileName(file)] = () => Run(() => { daxWorkspace!.OpenDocument(file); GoTo("DAX"); });
        }
    }
    private void OpenSelectedModelObject() { if (editor.Selection.FirstOrDefault() is IExpressionObject) OpenRichExpression(); else GoTo("Model"); }
}
