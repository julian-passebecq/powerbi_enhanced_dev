using System.IO;
using PbiBench.CSharp.LanguageService;
using PbiBench.Pbir;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public partial class MainWindow
{
    private async Task RunGen2SmokeAsync(string outputRoot, List<string> checks)
    {
        var handler = editor.Handler!; var service = new SemanticModelService(handler); var before = service.Fingerprint();
        var table = handler.Model.Tables["Sales"]; var measure = table.Measures["Revenue"]; var column = table.Columns["Amount"];
        foreach (var card in PowerBiGallery.All.Where(c => c.Mode == "SAFE RECIPE"))
        {
            var kind = card.Selection.Split('/')[0]; TabularNamedObject obj = kind == "Table" ? table : kind == "Column" ? column : measure;
            var values = card.Parameters.ToDictionary(p => p.Name, p => p.Default);
            if (card.Id == "clean") { values["Find"] = "Revenue"; values["Replace"] = "Reviewed Revenue"; }
            if (card.Id == "format") values["Format string"] = "0.0000";
            var recipe = PowerBiGallery.Generate(card, new[] { new AutomationSymbol(kind, obj.Name, kind == "Table" ? null : table.Name, true, column.DataType.ToString()) }, values);
            if (card.Id == "dynamic-format" && handler.CompatibilityLevel < 1601)
            {
                var rejected = false;
                try { _ = new ScriptPreviewService(handler).PreviewRecipe(recipe, new[] { obj }); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && service.Fingerprint() == before, "Dynamic-format gallery refuses incompatible models without upgrading or editing", checks);
                continue;
            }
            var preview = new ScriptPreviewService(handler).PreviewRecipe(recipe, new[] { obj });
            Check(preview.CanApply && service.Fingerprint() == before, "Gallery " + card.Id + " produces an isolated affected-object preview", checks);
            preview.Apply(handler); Check(service.Fingerprint() != before, "Gallery " + card.Id + " applies real model metadata", checks);
            handler.UndoManager.Undo(); Check(service.Fingerprint() == before, "Gallery " + card.Id + " restores with one native Undo", checks);
        }
        editor.Select(measure); GoTo("Semantic View"); diagram.ShowSemanticMode("Dependencies"); await PaintAsync(); Capture(outputRoot, "v2-dependencies");
        Check(SelectionInspector.Create(editor.Selection).DependencyCount > 0, "Semantic View uses native dependency evidence", checks);
        diagram.ShowSemanticMode("Model"); await PaintAsync(); Capture(outputRoot, "v2-semantic-view");
        GoTo("DAX"); await PaintAsync(); Capture(outputRoot, "v2-dax-workbench");
        Check(!ToolState("bravo").Enabled, "Bravo is disabled without a compatible live connection", checks);
        GoTo("Automate"); automationWorkspace.SelectedIndex = 2; await PaintAsync(); Capture(outputRoot, "v2-csharp-gallery");
        var root = Path.Combine(outputRoot, "report-project"); var source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", "reportstudio-demo");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { var target = Path.Combine(root, file.Substring(source.Length + 1)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
        await UpdateWorkspaceAsync(Path.Combine(root, "Sales.pbip")); GoTo("Semantic View"); diagram.ShowSemanticMode("Report Usage"); await PaintAsync(); Capture(outputRoot, "v2-report-usage");
        Check(reportIndexes.Count == 1 && ReportLineage.Build(reportIndexes[0]).Any(u => u.Name == "Revenue"), "PBIP report usage is available in the existing Semantic View", checks);
        var entries = new Dictionary<string, Action>(); AddQuickOpenEntries(entries);
        Check(entries.Keys.Any(k => k.Contains("Revenue")) && entries.ContainsKey("Automate · Power BI C# Gallery"), "Quick Open indexes real model objects and the curated gallery", checks);
        diagram.ShowSemanticMode("Model");
        await RunPass3SmokeAsync(outputRoot, checks);
    }
}
