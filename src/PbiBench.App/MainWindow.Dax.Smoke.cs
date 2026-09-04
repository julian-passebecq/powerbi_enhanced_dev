using System.Windows;
using PbiBench.Core.Queries;
using PbiBench.Dax.LanguageService;
using PbiBench.Semantic;

namespace PbiBench.App;

public partial class MainWindow
{
    private async Task RunDaxWorkspaceSmokeAsync(string outputRoot, List<string> checks)
    {
        var measure = editor.Handler!.Model.Tables["Sales"].Measures["Revenue"];
        editor.Select(measure); GoTo("Model"); await PaintAsync();
        Check(InspectorPane.IsVisible && InspectorColumn.ActualWidth >= 210 && InspectorTitle.Text == "Revenue", "Selection inspector has visible measure context", checks);
        var metadata = DaxMetadataSnapshotProvider.Capture(editor.Handler);
        Check(metadata.Symbols.Any(s => s.Kind == DaxSymbolKind.Measure && s.Name == "Revenue"), "DAX metadata snapshot uses hosted model measures", checks);
        var count = daxWorkspace!.DocumentCount;
        OpenRichExpression(); await PaintAsync();
        Check(daxWorkspace.DocumentCount == count + 1 && scratch.Text == measure.Expression, "Model expression opens in a separate rich DAX document", checks);
        const string query = "DEFINE VAR Marker = 2\nEVALUATE ROW ( \"First\", Marker )\nEVALUATE ROW ( \"Second\", Marker * 2 )";
        daxWorkspace.OpenQuery(query, "Multiple results");
        var position = scratch.Text.LastIndexOf("EVALUATE", StringComparison.Ordinal);
        scratch.SelectSpan(position, scratch.Text.Length - position);
        var partial = DaxQueryPlanner.Prepare(new DaxDocument("smoke", scratch.Text), DaxExecutionMode.CurrentStatement, position);
        Check(partial.QueryText.Contains("DEFINE VAR Marker") && partial.QueryText.Contains("Second") && !partial.QueryText.Contains("First"), "Current statement retains DEFINE and excludes preceding EVALUATE", checks);
        var result = new QueryResult(Guid.NewGuid(), query, "fixture", "fixture", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(12),
            new[] {
                new QueryResultSet(0, "Result 1", new[] { new QueryColumn("C0", "First", "Int64") }, new[] { new object?[] { 2L } }, false),
                new QueryResultSet(1, "Result 2", new[] { new QueryColumn("C0", "Second", "Int64") }, new[] { new object?[] { 4L } }, false)
            }, 0, Array.Empty<string>());
        daxWorkspace.DisplayResults(result); await PaintAsync();
        Check(daxWorkspace.ResultCount == 2, "DAX workspace renders multiple fixture result grids", checks);
        Capture(outputRoot, "dax-workspace");
        GoTo("Home"); await PaintAsync(); Capture(outputRoot, "home");
        Check(RecentProjects.Items.Count > 0 && InspectorPane.Visibility == Visibility.Collapsed, "Task Home includes recents and collapses contextual inspector", checks);
        GoTo("Model");
    }
}
