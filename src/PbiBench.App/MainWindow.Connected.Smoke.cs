using System.IO;
using System.Text.Json.Nodes;
using PbiBench.Core.Fabric;
using PbiBench.Core.Refresh;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using PbiBench.Semantic.Workspaces;
using PbiBench.Workspace;

namespace PbiBench.App;

public partial class MainWindow
{
    private async Task RunConnectedSmokeAsync(string outputRoot, List<string> checks)
    {
        var handler = editor.Handler!; var original = new SemanticModelService(handler).Fingerprint();
        var source = new FabricSourceRef("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "Lakehouse", "dbo", "FixtureOrders", "SQL",
            new FabricSqlEndpoint("fixture.datawarehouse.fabric.microsoft.com", "33333333-3333-3333-3333-333333333333"));
        var columns = new[] { new FabricColumnSchema("OrderId", "bigint", false, 0), new FabricColumnSchema("Amount", "float", true, 1) };
        var schema = new FabricTableSchema(source, columns, FabricSchemaRules.Fingerprint(source, columns), DateTimeOffset.UtcNow, new[] { "Explicit offline fixture: no Fabric authentication, schema discovery or query was executed." });
        GoTo("Fabric"); fabricWorkspace!.ShowSchema(schema);
        fabricWorkspace.SelectImportOptions(FabricStorageMode.Import, source.Table, new[] { "OrderId", "Amount" });
        await PaintAsync(); Capture(outputRoot, "fabric-import");
        Check(fabricWorkspace.SourceColumnCount == 2 && fabricWorkspace.SelectedSchema == schema, "Fabric import workspace displays captured source columns with fixture provenance", checks);
        var preview = fabricWorkspace.PrepareImportPreview();
        Check(preview.CanApply && original == new SemanticModelService(handler).Fingerprint(), "Fabric import workspace produces an exact non-mutating local metadata preview", checks);
        preview.Apply(handler); var table = handler.Model.Tables["FixtureOrders"];
        Check(table.Columns.Count == 2 && table.Partitions.Count == 1, "Fabric import applies mapped columns and one partition in the hosted TE2 model", checks);
        var comparison = new FabricImportService(handler).CompareSchema(table.Name, schema);
        Check(comparison.Count == 0, "Fabric schema comparison recognizes the reviewed source mapping", checks);
        handler.UndoManager.Undo(); Check(original == new SemanticModelService(handler).Fingerprint(), "One native Undo restores the complete pre-import model", checks);

        GoTo("Deploy"); advancedRefresh!.Preview(new RefreshRequest { Kind = RefreshKind.Full, Objects = new[] { new RefreshObject("Sales") }, MaxParallelism = 2 });
        Check(advancedRefresh.LastPlan != null && !advancedRefresh.LastPlan.CanExecute && advancedRefresh.LastPlan.Tmsl.Contains("Sales") && advancedRefresh.LastPlan.Tmsl.Contains("maxParallelism"), "Advanced refresh previews exact table TMSL while blocking offline execution", checks);
        await PaintAsync(); Capture(outputRoot, "advanced-refresh");
        await UpdateSessionAsync(); await PaintAsync();

        // Exercise the actual workspace page against generated TMDL, without a live server.
        var codec = new TmdlWorkspaceCodec(); var baseline = codec.CaptureLoaded(handler);
        var directory = Path.Combine(outputRoot, "WorkspaceSmoke.SemanticModel", "definition"); Directory.CreateDirectory(directory);
        foreach (var file in codec.Serialize(baseline, false)) WriteDefinition(file);
        GoTo("PBIP / Git"); workspaceExperience.SelectedIndex = 1; workspaceSync!.Configure(directory, null, null);
        var comparisonTask = workspaceSync.CompareAsync(); UpdateConnectedContext(); ShowPage("PBIP / Git");
        await comparisonTask;
        File.WriteAllText(Path.Combine(outputRoot, "workspace-smoke-state.txt"), workspaceSync.Status + "\nComparison: " + (workspaceSync.LastComparison == null ? "absent" : workspaceSync.LastComparison.Changes.Count + " changes; baseline " + workspaceSync.LastComparison.BaselineSource));
        await PaintAsync(); Capture(outputRoot, "workspace-sync-initial");
        Check(workspaceSync.LastComparison != null && workspaceSync.LastComparison.Live == null && workspaceSync.LastComparison.Changes.Count == 0, "Workspace page captures real TMDL disk state with an explicit offline live state", checks);
        var modified = JsonNode.Parse(baseline.DatabaseJson)!; modified["model"]!["description"] = "Explicit smoke disk metadata edit";
        foreach (var file in codec.Serialize(codec.Normalize(modified.ToJsonString()), false)) WriteDefinition(file);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (workspaceSync.LastComparison != null && DateTime.UtcNow < deadline) await Task.Delay(50);
        Check(workspaceSync.LastComparison == null, "Workspace watcher invalidates an earlier comparison after a disk edit", checks);
        await workspaceSync.CompareAsync();
        Check(workspaceSync.LastComparison!.Changes.Any(change => change.Property == "description" && change.Disk?.Contains("Explicit smoke disk") == true), "Workspace page displays the semantic disk property change against its baseline", checks);
        await PaintAsync(); Capture(outputRoot, "workspace-sync");
        workspaceExperience.SelectedIndex = 0; await UpdateSessionAsync(); GoTo("Model");

        void WriteDefinition(WorkspaceFile file)
        {
            var path = WorkspaceDiskStore.SafePath(directory, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, file.Content);
        }
    }
}
