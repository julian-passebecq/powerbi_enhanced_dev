using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PbiBench.AI.ContextExport;
using PbiBench.Core.Automation;
using PbiBench.Core.Platform;
using PbiBench.DaxStudio;
using PbiBench.ModelEditor;
using PbiBench.Semantic;

namespace PbiBench.App;
public partial class MainWindow
{
    private async Task RunV11SmokeAsync(string outputRoot, bool gen2 = false)
    {
        var checks = new List<string>();
        try
        {
            var fixture = Path.Combine(outputRoot, "fixture.bim"); File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", "pass1-demo.bim"), fixture);
            editor.Open(fixture); await UpdateSessionAsync(); await PaintAsync();
            var handler = editor.Handler!; var measure = handler.Model.Tables["Sales"].Measures["Revenue"]; var before = new SemanticModelService(handler).Fingerprint();
            editor.Select(measure); measure.Description = "V11 smoke"; editor.Undo();
            Check(new SemanticModelService(handler).Fingerprint() == before && editor.TreeRootCount > 0, "TE2 2.28 editor, tree, selection and native Undo remain usable", checks);
            GoTo("Model"); await PaintAsync(); Capture(outputRoot, "v11-model");
            GoTo("Model diagram"); await PaintAsync(); Check(new SemanticModelService(handler).GetGraph().Relationships.Count == 1, "Relationship diagram retains fixture metadata", checks);
            GoTo("DAX"); Check(daxWorkspace != null, "Internal DAX/query workspace remains available", checks);
            GoTo("QA"); ScanBpa(this, new RoutedEventArgs()); Check(BpaGrid.Items.Count > 0, "BPA still reports findings on the native model", checks);
            GoTo("Automate"); automationWorkspace.SelectedIndex = 1;
            await scriptAutomation!.PrepareSafePreviewAsync("foreach (var m in Selected.Measures) { m.DisplayFolder = \"V11\"; }");
            Check(scriptAutomation.LastPreview?.CanApply == true && scriptAutomation.LastPreview.Changes.Count == 1, "New C# editor hosts existing detached safe preview", checks);
            Check(new SemanticModelService(handler).Fingerprint() == before, "Script text and preview do not mutate metadata", checks);
            await PaintAsync(); Capture(outputRoot, "v11-scripts");
            Check(TrustedScriptRunner.Validate("Model.DoesNotExist();").Any(d => !d.IsWarning && d.Line > 0), "TE2 compiler returns positioned diagnostics without executing", checks);
            scriptAutomation.ShowTool("Trusted Legacy"); await PaintAsync();
            var trustedEditor = scriptAutomation.VisibleEditors.Single(); trustedEditor.Text = "// Compile-only fixture\nModel.DoesNotExist();";
            var problems = TrustedScriptRunner.Validate(trustedEditor.Text); trustedEditor.SetDiagnostics(problems);
            var diagnostic = trustedEditor.Problems.First(p => !p.Diagnostic.IsWarning); trustedEditor.NewDocument("// Other draft");
            Check(trustedEditor.NavigateProblem(diagnostic) && trustedEditor.CaretOffset >= "// Compile-only fixture\n".Length, "Compiler Problems activates the originating script and navigates without execution", checks);
            Check(new SemanticModelService(handler).Fingerprint() == before, "Compiler Problems navigation leaves the model unchanged", checks);
            await PaintAsync(); Check(trustedEditor.NativeView.ActualHeight >= 80, "Compiler Problems retains a visible script editing area", checks); Capture(outputRoot, "v11-csharp-problems");
            Check(RecipeCSharpGenerator.Generate(SafeCSharpParser.Parse("Model.Tables[\"Sales\"].Description = \"Example\";").Recipe!).Source.Contains("Description"), "Typed recipe generates readable review-only C#", checks);
            var export = CreateAIExportWindow(); export.Show(); await export.PrepareAsync(); await export.Dispatcher.InvokeAsync(() => export.UpdateLayout());
            Check(export.CurrentPlan != null && export.CurrentPlan.Review.All(f => !f.Path.StartsWith("samples/")), "Export UI defaults to metadata-only and shows exact files", checks);
            var exportImage = new RenderTargetBitmap((int)export.ActualWidth, (int)export.ActualHeight, 96, 96, PixelFormats.Pbgra32); exportImage.Render(export);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(exportImage)); using (var png = File.Create(Path.Combine(outputRoot, "v11-export.png"))) encoder.Save(png);
            await ContextExporter.WriteAsync(export.CurrentPlan!, Path.Combine(outputRoot, "fixture.pbibench-ai-context.zip"), true, lifetime.Token); export.Close();
            Check(new SemanticModelService(handler).Fingerprint() == before, "Context capture and ZIP export leave model and native Undo unchanged", checks);
            Check(ProvenanceCatalog.Bundled().Components.Count >= 30, "Runtime provenance contains feature owners, pins, patches and update lanes", checks);
            var featureWindow = CreateAboutWindow(this);
            try
            {
                featureWindow.Show(); await PaintAsync();
                Check(featureWindow.Map.VisibleRows.Count == FeatureCatalog.Bundled().Features.Count && featureWindow.Pages.Items.Count == 2, "Apps / Tools About opens the offline Feature Map and preserves Provenance", checks);
                featureWindow.Map.SelectFilter(FeatureMapFilter.Labs);
                Check(featureWindow.Map.VisibleRows.Count == 4 && featureWindow.Map.VisibleRows.All(r => r.Status is "Labs" or "Future"), "Feature Map shows incubating and future areas with evolvable lifecycles", checks);
                featureWindow.Map.SelectFilter(FeatureMapFilter.Te3Gaps);
                Check(featureWindow.Map.VisibleRows.Any(r => r.Feature.Id == "dax-debugger" && r.Status == "Gap"), "Feature Map records the DAX debugger gap without adding a debugger", checks);
                featureWindow.Map.SelectFilter(FeatureMapFilter.All); await featureWindow.Dispatcher.InvokeAsync(() => featureWindow.UpdateLayout());
                var catalog = FeatureCatalog.Bundled();
                Check(FeatureMapWindow.ReadDetailedCatalog(AppDomain.CurrentDomain.BaseDirectory).Replace("\r\n", "\n") == catalog.ToMarkdown(ProvenanceCatalog.Bundled()), "Packaged detailed catalog matches the embedded feature/provenance sources", checks);
                var mapImage = new RenderTargetBitmap((int)featureWindow.ActualWidth, (int)featureWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32); mapImage.Render(featureWindow);
                var mapEncoder = new PngBitmapEncoder(); mapEncoder.Frames.Add(BitmapFrame.Create(mapImage)); using var mapPng = File.Create(Path.Combine(outputRoot, "v11-feature-map.png")); mapEncoder.Save(mapPng);
            }
            finally { featureWindow.Close(); }
            var toolbox = new CompanionTools().Discover(CompanionTools.Catalog.Single(t => t.Id == "fabric-toolbox"), null, AppDomain.CurrentDomain.BaseDirectory);
            Check(toolbox.Path != null, "Apps / Tools discovers the separate Fabric Toolbox executable", checks);
            Check(PrimaryCommands.Children.OfType<Button>().Any(b => (string)b.Content == "Apps / Tools"), "Apps / Tools entry is visible in the Semantic IDE", checks);
            if (gen2) await RunGen2SmokeAsync(outputRoot, checks);
            GoTo("Model");
            File.WriteAllText(Path.Combine(outputRoot, "smoke-result.json"), JsonSerializer.Serialize(new { success = true, checks, integration = "Synthetic BIM fixture only; no live PBIX/Power BI/Fabric target accessed." }, new JsonSerializerOptions { WriteIndented = true })); Environment.ExitCode = 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(outputRoot, "smoke-error.txt"), string.Join("\n", checks) + "\n" + error); Environment.ExitCode = 1; }
        finally { Close(); }
    }
}
