using System.Windows;
using System.Windows.Controls;
using PbiBench.Core.Agent;
using PbiBench.Core.Workspaces;

namespace PbiBench.App;

public partial class MainWindow
{
    private AgentWorkspaceView? agentWorkspace;
    private SemanticPrototypeView? semanticPrototypes;
    private void InitializeAgentWorkspace()
    {
        agentWorkspace = new AgentWorkspaceView(() => editor.Handler, () => editor.Selection, () => Run(UpdateSessionAsync),
            StageAgentQuery, StageAgentTests, CaptureAgentExtras, backgroundTasks) { Visibility = Visibility.Collapsed };
        ((Panel)LaterPage.Parent).Children.Add(agentWorkspace);
        semanticPrototypes = new SemanticPrototypeView(() => editor.Handler, () => Run(UpdateSessionAsync), backgroundTasks, settingsDirectory);
        ((TabControl)AuthoringPage.Content).Items.Add(new TabItem { Header = "Compiler / packages", Content = semanticPrototypes });
    }
    private void StageAgentQuery(string query)
    {
        daxWorkspace!.OpenQuery(query, "Agent proposal"); GoTo("DAX");
        Log("Agent query staged for review. Run it explicitly from the DAX workspace.");
    }
    private void StageAgentTests(PbiBench.Core.Quality.SemanticTestArtifact artifact)
    {
        semanticTests!.AppendArtifact(artifact); GoTo("QA"); qualityWorkspace.SelectedIndex = 3;
        Log("Agent test staged in Semantic tests. Review its query and assertion before running.");
    }
    private AgentContextExtras CaptureAgentExtras() => new(scratch.Text,
        currentFindings?.Where(finding => !IsBpaSuppressed(finding)).Select(finding => new AgentContextFinding(finding.RuleId, finding.ObjectPath, finding.Severity.ToString(), finding.Reason)).ToArray(),
        workspaceSync?.LastGitChanges.Select(change => new AgentContextDiff(change.ObjectPath, change.Property,
            WorkspaceSemanticDiff.DisplayValue(change.Key, change.Baseline), WorkspaceSemanticDiff.DisplayValue(change.Key, change.Disk))).ToArray(),
        semanticTests?.LastResults.Select(result => new AgentContextTest(result.Name, result.Outcome.ToString(), result.Evidence)).ToArray(),
        editor.Handler == null ? Array.Empty<string>() : editor.Handler.IsConnected
            ? new[] { "ReadMetadata", "WriteMetadata", "QueryDax" } : new[] { "ReadMetadata", "WriteMetadata" });
    private void AddAgentCommands(IDictionary<string, Action> entries)
    {
        entries["Agent · Review proposals"] = () => GoTo("Agent");
        foreach (var tool in new[] { "Semantic compiler", "DAX packages" })
            entries["Model tools · " + tool + " prototype"] = () =>
            { GoTo("Model tools"); ((TabControl)AuthoringPage.Content).SelectedIndex = 2; semanticPrototypes!.ShowTool(tool); };
    }
}
