using System.IO;
using PbiBench.Core.DataExploration;
using PbiBench.Core.Queries;
using PbiBench.Semantic;

namespace PbiBench.App;

public partial class MainWindow
{
    private DataWorkspaceView? dataWorkspace;
    private void InitializeDataWorkspace()
    {
        dataWorkspace = new DataWorkspaceView(
            () => editor.Handler == null ? new DataModelSchema("", Array.Empty<DataTableSchema>(), Array.Empty<DataRelationshipSchema>()) : DataModelSchemaProvider.Capture(editor.Handler),
            () => (editor.Server, editor.Server == null ? null : editor.Database),
            () => editor.Handler?.IsConnected == true ? editor.Handler.Database.Server.ConnectionString : null,
            () => semanticWorkspaceRoot == null ? settingsDirectory : Path.Combine(semanticWorkspaceRoot, "PbiBench"));
        DataPage.Content = dataWorkspace;
        editor.RequestPreviewData = table => { dataWorkspace.OpenPreview(table); GoTo("Data"); };
    }
    private async Task RunDataWorkspaceSmokeAsync(string outputRoot, List<string> checks)
    {
        var count = dataWorkspace!.DocumentCount;
        editor.RequestPreviewData!("Sales"); dataWorkspace.OpenPreview("Product"); await PaintAsync();
        Check(dataWorkspace.DocumentCount == count + 2, "Native table handoff opens multiple Data Preview documents", checks);
        Check(dataWorkspace.ActiveQuery!.Query.Contains("TOPN") && !dataWorkspace.ActiveQuery.Query.Contains("WINDOW"), "Unverified Import preview uses explicit first-N query", checks);
        Capture(outputRoot, "data-preview");
        var model = DataModelSchemaProvider.Capture(editor.Handler!);
        var profile = DataProfileBuilder.Relationship(model, model.Relationships.Single());
        Check(profile.Query.Contains("EXCEPT") && profile.Warnings.Count > 0, "Relationship coverage creates reviewable engine query and cost notes", checks);
        dataWorkspace.OpenPivot();
        var layout = new PivotLayout { Rows = new[] { new PivotAxisField("Product", "Product") }, Values = new[] { new PivotValue("Sales", "Revenue") } };
        dataWorkspace.ActivePivot!.LoadLayout(layout);
        var plan = PivotQueryBuilder.Build(layout, model);
        var row = plan.ResultColumns.Select(c => c.Role switch { PivotResultRole.Row => (object?)"Desk", PivotResultRole.Value => 120.0, _ => false }).ToArray();
        dataWorkspace.ActiveQuery!.ShowResults(new QueryResult(Guid.NewGuid(), plan.Dax, "fixture", "fixture", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(5),
            new[] { new QueryResultSet(0, "Pivot fixture", plan.ResultColumns.Select((c, i) => new QueryColumn("C" + i, c.Key, "Object")).ToArray(), new[] { row }, false) }, 0, Array.Empty<string>()));
        Check(dataWorkspace.ActiveQuery.ResultCount == 1 && dataWorkspace.ActiveQuery.Query.Contains("SUMMARIZECOLUMNS"), "Pivot layout generates DAX and renders fixture data", checks);
        await PaintAsync(); Capture(outputRoot, "pivot-lab");
        GoTo("Model");
    }
}
