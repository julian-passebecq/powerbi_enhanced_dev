using System.IO;
using System.Windows;
using System.Windows.Controls;
using PbiBench.Automation;
using PbiBench.Core.Automation;
using PbiBench.Core.Quality;
using PbiBench.Core.Queries;
using PbiBench.Core.Tasks;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public partial class MainWindow
{
    private async Task RunQualitySmokeAsync(string outputRoot, List<string> checks)
    {
        var handler = editor.Handler!; var measure = handler.Model.Tables["Sales"].Measures["Revenue"];
        editor.Select(measure);
        Check(BpaRulePacks.BuiltIn.Count == 8 && BpaRulePacks.Rules.All(rule => !string.IsNullOrEmpty(rule.Risk)), "Eight versioned BPA packs expose explicit rule risk", checks);
        GoTo("QA"); qualityWorkspace.SelectedIndex = 1; await PaintAsync(); Capture(outputRoot, "bpa-packs");
        const string source = "foreach(var m in Selected.Measures) { m.DisplayFolder = \"Smoke script\"; }";
        var service = new ScriptPreviewService(handler); var beforeFolder = measure.DisplayFolder;
        var prepared = service.PrepareScript(source, new[] { measure });
        var work = backgroundTasks.Enqueue("Smoke: compute isolated script preview", context => service.ComputeAsync(prepared, context.CancellationToken));
        var computed = await work.Completion; var preview = service.Materialize(computed);
        Check(preview.CanApply && preview.Changes.Count == 1 && measure.DisplayFolder == beforeFolder, "Safe C# Preview computes a detached diff on the background queue", checks);
        preview.Apply(handler); Check(measure.DisplayFolder == "Smoke script", "Safe C# Preview applies the reviewed model change", checks);
        handler.UndoManager.Undo(); Check(measure.DisplayFolder == beforeFolder, "One undo restores the script preview change", checks);
        Check(!SafeCSharpParser.Parse("System.IO.File.WriteAllText(\"blocked.txt\",\"blocked\");").IsValid, "Safe C# Preview rejects filesystem calls before execution", checks);
        var recorder = new ActionRecorder(); var beforeDescription = measure.Description;
        recorder.Start(handler); measure.Description = "Recorded smoke description";
        var recording = recorder.Stop(handler, "Smoke recorded description"); handler.UndoManager.Undo();
        Check(recording.Recipe.Steps.Count == 1 && measure.Description == beforeDescription, "Action recorder captures supported metadata operations as a typed recipe", checks);
        var recipePath = Path.Combine(outputRoot, "recorded-action.json");
        await RecipeFiles.SaveRecipeAsync(recipePath, recording.Recipe, CancellationToken.None);
        var loaded = await RecipeFiles.LoadRecipeAsync(recipePath, CancellationToken.None);
        var replay = new ScriptPreviewService(handler).PreviewRecipe(loaded, Array.Empty<TabularNamedObject>());
        Check(replay.CanApply && replay.Changes.Single().Property == "Description", "Saved action recipes reload into an exact model preview", checks);
        GoTo("Automate"); automationWorkspace.SelectedIndex = 1;
        await scriptAutomation!.PrepareSafePreviewAsync(source);
        Check(scriptAutomation.LastPreview?.CanApply == true && scriptAutomation.LastPreview.Changes.Count == 1 && measure.DisplayFolder == beforeFolder,
            "Script workspace displays the detached before/after preview without applying it", checks);
        foreach (var tool in new[] { ("Safe C# Preview", "script-preview"), ("Trusted Legacy", "trusted-script"), ("Action recorder", "action-recorder"), ("Macro library", "macro-library") })
        { scriptAutomation!.ShowTool(tool.Item1); await PaintAsync(); Capture(outputRoot, tool.Item2); }

        // Explicit fixture rowsets exercise the real WPF execution/result flow without claiming an engine ran.
        using var fixtureTests = new SemanticTestsView(() => ("Automated fixture", "Fixture values"), () => null, new SmokeQualityQueries(), backgroundTasks);
        fixtureTests.LoadArtifact(new SemanticTestArtifact(1, new[] { new SemanticTestDefinition { Name = "Fixture: exact scalar assertion", Expected = SemanticValue.From(1) } }));
        await fixtureTests.RunAllAsync();
        Check(fixtureTests.LastResults.Count == 1 && fixtureTests.LastResults[0].Outcome == SemanticTestOutcome.Passed, "Semantic test workspace executes and displays an explicitly labeled fixture assertion", checks);
        var testTab = (TabItem)qualityWorkspace.Items[3]; var originalTests = testTab.Content; testTab.Content = fixtureTests;
        GoTo("QA"); qualityWorkspace.SelectedIndex = 3; await PaintAsync(); Capture(outputRoot, "semantic-tests");
        var showOutput = layoutState.OutputVisible; layoutState.OutputVisible = true; ApplyPaneVisibility(); OutputTabs.SelectedIndex = OutputTabs.Items.Count - 1;
        await PaintAsync(); Capture(outputRoot, "background-tasks");
        Check(backgroundTasks.Snapshot().Any(task => task.Id == work.Id && task.State == BackgroundTaskState.Succeeded), "Background task history retains completed operation status", checks);
        layoutState.OutputVisible = showOutput; ApplyPaneVisibility(); testTab.Content = originalTests;
        var fixture = new VertiPaqSnapshot("Explicit automated fixture; no engine measurements", "Fixture storage", null, DateTimeOffset.UtcNow, "fixture-v1", false,
            new[] { new VertiPaqTable("Sales", 200, 1024, 512, 0, 0, 0, "Import", null) },
            new[] { new VertiPaqColumn("Sales", "Amount", "Decimal", 100, 1024, 512, 0, "Hash", null) },
            new[] { new VertiPaqPartition("Sales", "Fixture partition", "Import", null, null) }, Array.Empty<VertiPaqSegment>(), Array.Empty<VertiPaqRelationship>(),
            new[] { "Illustrative fixture only. Live DMV/VPAX results require their own verified capture." });
        vertiPaq!.ShowSnapshot(fixture); qualityWorkspace.SelectedIndex = 2; await PaintAsync(); Capture(outputRoot, "vertipaq");
        Check(vertiPaq.TableCount == 1 && vertiPaq.Snapshot!.TotalBytes == 1536, "VertiPaq workspace displays typed fixture metrics without claiming live capture", checks);
        qualityWorkspace.SelectedIndex = 0; automationWorkspace.SelectedIndex = 0; await UpdateSessionAsync(); GoTo("Model");
    }
    private sealed class SmokeQualityQueries : IDaxQueryService
    {
        public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); request.Validate();
            return Task.FromResult(new QueryResult(Guid.NewGuid(), request.Query, request.Server, request.Database, DateTimeOffset.UtcNow, TimeSpan.Zero,
                new[] { new QueryResultSet(0, "Explicit fixture", new[] { new QueryColumn("C0", "Value", "Int64") }, new[] { new object?[] { 1L } }, false) }, request.DocumentRevision, Array.Empty<string>()));
        }
    }
}
