using System.IO;
using System.Windows.Controls;
using PbiBench.ExternalTools;

namespace PbiBench.FabricToolbox;

public sealed partial class ToolboxWindow
{
    private readonly TextBlock projectContextStrip = Note("Project: none selected · Sign in explicitly in Settings.");
    public ProjectContext? ProjectContext { get; private set; }
    public async Task AcceptProjectContextAsync(string path, CancellationToken ct = default)
    {
        var context = await ExternalTools.ProjectContext.LoadAsync(path, ct);
        ProjectContext = context;
        projectContextStrip.Text = (context.PbipRoot == null ? "No local project" : Path.GetFileName(context.PbipRoot)) + " · " + context.Source + " · " + context.GitStatus +
            "\nWorkspace: " + (context.FabricWorkspaceId ?? "none selected") + " · Item: " + (context.FabricItemId ?? "none selected") + " · Sign in and load inventory explicitly.";
    }
    public void ShowHandoffError(string message) => projectContextStrip.Text = "Project context unavailable: " + message;
}
