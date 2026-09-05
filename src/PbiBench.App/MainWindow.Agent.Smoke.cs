using System.IO;
using System.Windows.Controls;
using PbiBench.Core.Agent;
using PbiBench.Core.Automation;
using PbiBench.Core.Commands;
using PbiBench.Core.Quality;
using PbiBench.Core.Packages;
using PbiBench.Core.Workspaces;
using PbiBench.Semantic;

namespace PbiBench.App;

public partial class MainWindow
{
    private async Task RunAgentSmokeAsync(string outputRoot, List<string> checks)
    {
        var handler = editor.Handler!; var measure = handler.Model.Tables["Sales"].Measures["Revenue"];
        editor.Select(measure); GoTo("Agent");
        var before = new SemanticModelService(handler).Fingerprint(); var previousFolder = measure.DisplayFolder;
        agentWorkspace!.CaptureContext(new(SelectedObjects: true, Capabilities: true));
        Check(agentWorkspace.SharedContextJson.Contains("Revenue") && !agentWorkspace.SharedContextJson.Contains(outputRoot), "Agent context captures selected metadata without the model file path", checks);
        await agentWorkspace.GenerateAsync("Review selected metadata offline.");
        Check(agentWorkspace.Proposal?.Kind == AgentProposalKind.Review && before == new SemanticModelService(handler).Fingerprint(), "Offline Agent produces an explicitly local review without model changes", checks);
        await PaintAsync(); Capture(outputRoot, "agent-context");
        var recipe = new ActionRecipe("Smoke measure organization", new[] { new RecipeStep(new(RecipeScope.Measure, "Sales", "Revenue"), RecipeOperation.SetProperty, "DisplayFolder", RecipeValue.Literal("Agent review fixture")) });
        agentWorkspace.LoadProposal(AgentProposalJson.Serialize(new(1, AgentProposalKind.Action, recipe.Name, "Explicit offline smoke proposal. Review the exact folder change before application.", recipe, null, null)));
        await agentWorkspace.PreparePreviewAsync(); var prepared = agentWorkspace.LastPreview!;
        Check(prepared.Review.CanApply && prepared.Review.Changes.Count == 1 && measure.DisplayFolder == previousFolder, "Agent action uses the shared command engine to preview one exact local change", checks);
        agentWorkspace.ShowProposal();
        await PaintAsync(); Capture(outputRoot, "agent-preview");
        var result = await agentWorkspace.ApplyPreviewAsync(prepared.Review.Hash, "Instrumented launch fixture");
        Check(result.Status == CommandStatus.Succeeded && measure.DisplayFolder == "Agent review fixture", "Reviewed Agent command applies through the shared native editing engine", checks);
        handler.UndoManager.Undo();
        Check(measure.DisplayFolder == previousFolder && new SemanticModelService(handler).Fingerprint() == before, "One native Undo restores the complete Agent command", checks);
        await UpdateSessionAsync(); GoTo("Agent");
        agentWorkspace.CaptureContext(new());
        const string query = "EVALUATE ROW ( \"Agent draft\", 42 )";
        agentWorkspace.LoadProposal(AgentProposalJson.Serialize(new(1, AgentProposalKind.Query, "Smoke query draft", "Staged fixture; no engine execution.", null, query, null)));
        var documents = daxWorkspace!.DocumentCount; agentWorkspace.StageProposal();
        Check(activePage == "DAX" && daxWorkspace.DocumentCount == documents + 1 && scratch.Text == query, "Agent query proposal opens a real DAX document without executing it", checks);
        GoTo("Agent"); agentWorkspace.CaptureContext(new());
        agentWorkspace.LoadProposal(AgentProposalJson.Serialize(new(1, AgentProposalKind.Test, "Smoke assertion draft", "Review the expected result before running.", null, null,
            new("Agent scalar draft", query, SemanticComparison.Equal, new(SemanticValueKind.Number, "42")))));
        var existingTests = semanticTests!.CaptureArtifact().Tests.Count; agentWorkspace.StageProposal();
        Check(activePage == "QA" && qualityWorkspace.SelectedIndex == 3 && semanticTests.LastResults.Count == 0 && semanticTests.CaptureArtifact().Tests.Count == existingTests + 1,
            "Agent test proposal appends to the actual QA suite without removing drafts or generating results", checks);

        GoTo("Model tools"); ((TabControl)AuthoringPage.Content).SelectedIndex = 2; semanticPrototypes!.ShowTool("Semantic compiler");
        semanticPrototypes.SelectTargetTable("Sales"); await semanticPrototypes.CompileAsync(SemanticPrototypeView.SampleYaml);
        var compilation = semanticPrototypes.LastCompilation!; var preview = semanticPrototypes.PreviewMeasures();
        Check(compilation.CanProposeMetadata && preview.CanApply && preview.Changes.Count > 0, "Compiler prototype produces reviewable intent and explicit mapped aggregate proposals", checks);
        File.WriteAllText(Path.Combine(outputRoot, "semantic-intent.json"), compilation.ToJson());
        await PaintAsync(); Capture(outputRoot, "semantic-compiler");
        before = new SemanticModelService(handler).Fingerprint(); preview.Apply(handler);
        Check(handler.Model.Tables["Sales"].Measures.Any(item => item.Name == "Imported quantity"), "Compiler proposal creates native measures only after review", checks);
        handler.UndoManager.Undo(); Check(new SemanticModelService(handler).Fingerprint() == before, "One native Undo restores the compiler proposal and metadata", checks);
        await semanticPrototypes.CompileAsync(SemanticPrototypeView.SampleYaml + "filter: Quantity > 0\n");
        Check(semanticPrototypes.LastCompilation?.CanProposeMetadata == false, "Unsupported compiler filter semantics produce blocking diagnostics", checks);
        semanticPrototypes.ShowTool("DAX packages");
        var packageDirectory = Path.Combine(outputRoot, "package-fixture"); Directory.CreateDirectory(packageDirectory);
        const string function = "(value : NUMERIC) => value * 2";
        File.WriteAllText(Path.Combine(packageDirectory, "double.dax"), function, new System.Text.UTF8Encoding(false));
        var manifest = new DaxPackageManifest(1, "pbibench.smoke", "1.0.0", "MIT", "Original local smoke fixture; no package feed or source query was used.",
            Array.Empty<DaxPackageDependency>(), new[] { new DaxPackageFunction("pbibench.smoke.Double", "double.dax", WorkspaceSemanticSnapshot.HashText(function), "Returns the supplied value times two.", false) });
        File.WriteAllText(Path.Combine(packageDirectory, "pbibench.package.json"), CommandJson.Serialize(manifest));
        await semanticPrototypes.LoadPackageAsync(packageDirectory);
        var packagePreview = semanticPrototypes.PreviewInstall();
        Check(semanticPrototypes.LastPackage?.Manifest.Version == "1.0.0" && !packagePreview.CanApply && handler.CompatibilityLevel == 1600,
            "Local package verifies manifest and function hashes, then blocks UDF install at the demo's compatibility level", checks);
        await PaintAsync(); Capture(outputRoot, "dax-packages");
        await UpdateSessionAsync(); GoTo("Model");
    }
}
