using System.IO;
using System.Windows.Controls;
using PbiBench.DesignExchange;
using PbiBench.ExternalTools;
using PbiBench.Semantic;

namespace PbiBench.App;

public partial class MainWindow
{
    private async Task RunPass3SmokeAsync(string outputRoot, List<string> checks)
    {
        Check(Navigation.Items.Cast<ListBoxItem>().Select(i => (string)i.Tag).SequenceEqual(ModuleNames), "Compact module rail matches product navigation", checks);
        GoTo("Home"); await PaintAsync(); Capture(outputRoot, "v3-home");
        Check(HomeCards.Children.Count == 8 && ProjectContextStrip.Text.Contains("PBIP"), "Home groups and project context render together", checks);
        var model = ModelContext.Create(AIContextCapture.Capture(editor.Handler!));
        var folder = Path.Combine(outputRoot, "design-exchange"); Directory.CreateDirectory(folder);
        var modelPath = Path.Combine(folder, "pbibench-model-context.json"); await model.SaveAsync(modelPath, lifetime.Token);
        var spec = new DashboardSpec(1, new("Revenue overview", "Executive"), new[] {
            new DesignPage("summary", "Executive Summary", new(1280, 720), new[] {
                new DesignVisual("revenue", "card", new Dictionary<string, DesignBinding> { ["value"] = new("Measure", "Sales", "Revenue") }, Region: "top"),
                new DesignVisual("trend", "line", new Dictionary<string, DesignBinding> { ["value"] = new("Measure", "Sales", "Revenue") }, Region: "middle")
            }) }, model.ModelFingerprint);
        var specPath = Path.Combine(folder, "dashboard-spec.json"); var themePath = Path.Combine(folder, "theme.json");
        await ContractJson.WriteNewAsync(specPath, ContractJson.Serialize(spec), lifetime.Token);
        await ContractJson.WriteNewAsync(themePath, "{\"name\":\"PbiBench neutral\",\"dataColors\":[\"#315DA8\",\"#626B78\",\"#89A5D0\"]}", lifetime.Token);
        GoTo("Design Exchange"); designExchange.SetInputs(modelPath, specPath, themePath); await designExchange.ValidateAsync();
        Check(designExchange.CurrentPackage?.IsValid == true && designExchange.CurrentPackage.Dashboard!.Bindings.All(b => b.Status == "Valid"), "Exported model, dashboard bindings and pinned theme validate in Project > Design Exchange", checks);
        await PaintAsync(); Capture(outputRoot, "v3-design-exchange");
        var manifest = ComponentsManifest.Find(AppDomain.CurrentDomain.BaseDirectory);
        Check(manifest != null && ComponentsManifest.Load(manifest).Components.Count == 3, "One package declares three independently versioned application components", checks);
    }
}
