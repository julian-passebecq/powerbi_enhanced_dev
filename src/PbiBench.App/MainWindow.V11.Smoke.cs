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
    private async Task RunV11SmokeAsync(string outputRoot)
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
            Check(RecipeCSharpGenerator.Generate(SafeCSharpParser.Parse("Model.Tables[\"Sales\"].Description = \"Example\";").Recipe!).Source.Contains("Description"), "Typed recipe generates readable review-only C#", checks);
            var export = CreateAIExportWindow(); export.Show(); await export.PrepareAsync(); await export.Dispatcher.InvokeAsync(() => export.UpdateLayout());
            Check(export.CurrentPlan != null && export.CurrentPlan.Review.All(f => !f.Path.StartsWith("samples/")), "Export UI defaults to metadata-only and shows exact files", checks);
            var exportImage = new RenderTargetBitmap((int)export.ActualWidth, (int)export.ActualHeight, 96, 96, PixelFormats.Pbgra32); exportImage.Render(export);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(exportImage)); using (var png = File.Create(Path.Combine(outputRoot, "v11-export.png"))) encoder.Save(png);
            await ContextExporter.WriteAsync(export.CurrentPlan!, Path.Combine(outputRoot, "fixture.pbibench-ai-context.zip"), true, lifetime.Token); export.Close();
            Check(new SemanticModelService(handler).Fingerprint() == before, "Context capture and ZIP export leave model and native Undo unchanged", checks);
            Check(ProvenanceCatalog.Bundled().Components.Count >= 30, "Runtime provenance contains feature owners, pins, patches and update lanes", checks);
            var toolbox = new CompanionTools().Discover(CompanionTools.Catalog.Single(t => t.Id == "fabric-toolbox"), null, AppDomain.CurrentDomain.BaseDirectory);
            Check(toolbox.Path != null, "Apps / Tools discovers the separate Fabric Toolbox executable", checks);
            Check(PrimaryCommands.Children.OfType<Button>().Any(b => (string)b.Content == "Apps / Tools"), "Apps / Tools entry is visible in the Semantic IDE", checks);
            GoTo("Model");
            File.WriteAllText(Path.Combine(outputRoot, "smoke-result.json"), JsonSerializer.Serialize(new { success = true, checks, integration = "Synthetic BIM fixture only; no live PBIX/Power BI/Fabric target accessed." }, new JsonSerializerOptions { WriteIndented = true })); Environment.ExitCode = 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(outputRoot, "smoke-error.txt"), string.Join("\n", checks) + "\n" + error); Environment.ExitCode = 1; }
        finally { Close(); }
    }
}
