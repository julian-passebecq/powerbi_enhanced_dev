using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PbiBench.Pbir;

namespace PbiBench.ReportStudio;

internal static class StudioSmoke
{
    public static async Task RunAsync(StudioWindow window, string output)
    {
        output = Path.GetFullPath(output); var root = Path.Combine(Path.GetDirectoryName(output)!, "report-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var fixture = Path.Combine(AppContext.BaseDirectory, "examples", "reportstudio-demo");
        foreach (var source in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories)) { var target = Path.Combine(root, Path.GetRelativePath(fixture, source)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target); }
        await window.OpenAsync(Path.Combine(root, "Sales.pbip")); var validator = new ReportValidator(); var engine = new ReportChangeEngine(validator); var actions = new ReportActions(engine);
        var initial = window.CurrentReport!;
        window.VerifyNavigation();
        if (validator.Validate(initial).Count > 0) throw new InvalidDataException(string.Join("\n", validator.Validate(initial)));
        var catalog = await ReportLineage.ReadLocalModelAsync(initial.SemanticModelPath, default);
        if (!ReportLineage.Build(initial, catalog.Fields, catalog.Complete).Any(u => u.Status == "Resolved")) throw new InvalidDataException("Fixture lineage did not resolve.");
        var checks = new List<string> { "Separate modern WPF process launched", "PBIP tree / wireframe / inspector populated", "Offline schema validation", "Local measure → visual lineage", "Search / page / tree / lineage selection synchronization and cached snapshot", "Zoom 100% / fit page" };
        var file = initial.Pages[0].Visuals[0].File;
        foreach (var create in new Func<ReportIndex, ReportChangePlan>[] { r => actions.SetTitle(r, file, "Reviewed revenue"), r => actions.Annotate(r, file, "Review", "Verified"), r => actions.DuplicateVisual(r, file, r.Pages[0].Id, 20, 20), r => actions.DuplicatePage(r, r.Pages[0].Id, "Reviewed copy"), r => actions.ReplaceReference(r, new("Sales", "Revenue", "Measure"), new("Sales", "Revenue adjusted", "Measure")), r => actions.BatchVisualProperties(r, new[] { file }, true, false, "Batch review"), r => actions.ApplyDisplayNames(r, new(new[] { new DisplayNameMapping(new("Sales", "Revenue", "Measure"), "Revenue label", r.Name, "overview", "revenue") })) })
        {
            var plan = create(window.CurrentReport!); if (!plan.CanApply) throw new InvalidDataException(string.Join("\n", plan.Validation));
            window.ShowPlan(plan); await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
            await window.ApplySmokePlanAsync(); checks.Add(plan.Title + " · UI preview / apply / schema validation");
            var manifest = Path.Combine(plan.Root, ".pbibench", "report-backups", plan.Id.ToString("N"), "manifest.json");
            var restore = await engine.PreviewRestoreAsync(plan.Root, manifest, default); window.ShowPlan(restore); await window.ApplySmokePlanAsync();
            if (window.CurrentReport!.Files.Count != initial.Files.Count || initial.Files.Any(p => window.CurrentReport.Files[p.Key].Hash != p.Value.Hash)) throw new InvalidDataException("Restore did not reproduce original bytes.");
        }
        checks.Add("All seven actions restored original byte hashes");
        window.FocusObject("overview", "revenue"); await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.ChangeExtension(output, ".png"))) encoder.Save(stream);
        var context = PbiBench.DesignExchange.ModelContext.Create(new PbiBench.AI.ContextExport.ContextModel("Sales", 1600,
            new[] { new PbiBench.AI.ContextExport.ContextObject(PbiBench.AI.ContextExport.ContextModel.ObjectId("Table", null, "Sales"), "Table", "Sales"),
                new PbiBench.AI.ContextExport.ContextObject(PbiBench.AI.ContextExport.ContextModel.ObjectId("Measure", "Sales", "Revenue"), "Measure", "Revenue", "Sales") },
            Array.Empty<PbiBench.AI.ContextExport.ContextRelationship>(), Array.Empty<PbiBench.AI.ContextExport.ContextDependency>()));
        var designRoot = Path.Combine(Path.GetDirectoryName(output)!, "design-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(designRoot);
        var modelPath = Path.Combine(designRoot, "pbibench-model-context.json"); await context.SaveAsync(modelPath, default);
        var spec = new PbiBench.DesignExchange.DashboardSpec(1, new("Commercial Performance", "Executive"), new[] {
            new PbiBench.DesignExchange.DesignPage("summary", "Executive Summary", new(1280, 720), new[] {
                new PbiBench.DesignExchange.DesignVisual("revenue", "card", new Dictionary<string, PbiBench.DesignExchange.DesignBinding> { ["value"] = new("Measure", "Sales", "Revenue") }, "Current revenue", "top"),
                new PbiBench.DesignExchange.DesignVisual("trend", "line", new Dictionary<string, PbiBench.DesignExchange.DesignBinding> { ["value"] = new("Measure", "Sales", "Revenue") }, "Revenue trend", "middle")
            }) }, context.ModelFingerprint);
        var specPath = Path.Combine(designRoot, "dashboard-spec.json"); var themePath = Path.Combine(designRoot, "theme.json");
        await File.WriteAllTextAsync(specPath, PbiBench.ExternalTools.ContractJson.Serialize(spec)); await File.WriteAllTextAsync(themePath, "{\"name\":\"PbiBench\",\"dataColors\":[\"#315DA8\",\"#626B78\",\"#89A5D0\"]}");
        await window.OpenDesignAsync(modelPath, specPath, themePath); await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        if (window.DesignPreview?.VisualCount != 2 || !window.DesignPreview.Package.IsValid || window.CurrentPlan != null) throw new InvalidDataException("Design preview did not preserve the read-only boundary.");
        if (initial.Files.Any(p => !File.ReadAllBytes(Path.Combine(initial.Root, p.Key)).SequenceEqual(p.Value.Bytes()))) throw new InvalidDataException("Design preview changed PBIR bytes.");
        var previewBitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); previewBitmap.Render(window);
        var previewEncoder = new PngBitmapEncoder(); previewEncoder.Frames.Add(BitmapFrame.Create(previewBitmap)); using (var stream = File.Create(Path.ChangeExtension(output, ".design.png"))) previewEncoder.Save(stream);
        checks.Add("Design handoff revalidates model/spec/theme, renders proposed layout and leaves every PBIR byte unchanged");
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { success = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
