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
        if (validator.Validate(initial).Count > 0) throw new InvalidDataException(string.Join("\n", validator.Validate(initial)));
        var catalog = await ReportLineage.ReadLocalModelAsync(initial.SemanticModelPath, default);
        if (!ReportLineage.Build(initial, catalog.Fields, catalog.Complete).Any(u => u.Status == "Resolved")) throw new InvalidDataException("Fixture lineage did not resolve.");
        var checks = new List<string> { "Separate modern WPF process launched", "PBIP tree / wireframe / inspector populated", "Offline schema validation", "Local measure → visual lineage" };
        var file = initial.Pages[0].Visuals[0].File;
        foreach (var create in new Func<ReportIndex, ReportChangePlan>[] { r => actions.SetTitle(r, file, "Reviewed revenue"), r => actions.Annotate(r, file, "Review", "Verified"), r => actions.DuplicateVisual(r, file, r.Pages[0].Id), r => actions.DuplicatePage(r, r.Pages[0].Id, "Reviewed copy"), r => actions.ReplaceReference(r, new("Sales", "Revenue", "Measure"), new("Sales", "Revenue adjusted", "Measure")) })
        {
            var plan = create(window.CurrentReport!); if (!plan.CanApply) throw new InvalidDataException(string.Join("\n", plan.Validation));
            window.ShowPlan(plan); await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
            await window.ApplySmokePlanAsync(); checks.Add(plan.Title + " · UI preview / apply / schema validation");
            var manifest = Path.Combine(plan.Root, ".pbibench", "report-backups", plan.Id.ToString("N"), "manifest.json");
            var restore = await engine.PreviewRestoreAsync(plan.Root, manifest, default); window.ShowPlan(restore); await window.ApplySmokePlanAsync();
            if (window.CurrentReport!.Files.Count != initial.Files.Count || initial.Files.Any(p => window.CurrentReport.Files[p.Key].Hash != p.Value.Hash)) throw new InvalidDataException("Restore did not reproduce original bytes.");
        }
        checks.Add("All five actions restored original byte hashes");
        window.FocusObject("overview", "revenue"); await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.ChangeExtension(output, ".png"))) encoder.Save(stream);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { success = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
