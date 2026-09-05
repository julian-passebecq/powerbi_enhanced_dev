using PbiBench.ExternalTools;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PbiBench.Core.Fabric;
using PbiBench.Fabric;

namespace PbiBench.FabricToolbox;

public sealed partial class ToolboxWindow
{
    private readonly TextBlock snapshotNotice = Note("Report snapshots: explicitly retrieve a public definition into a new local directory, then edit locally in Report Studio. PBIR-Legacy remains read-only. Definition files can contain saved filters. No remote report writes.");
    private FabricReportSnapshot? lastSnapshot;
    private async Task GetReportSnapshotAsync(CancellationToken ct)
    {
        var item = items.SelectedItem as FabricItem ?? throw new InvalidOperationException("Select a Fabric Report first.");
        if (item.Kind != "Report") throw new InvalidOperationException("Select a Fabric Report first.");
        var folder = new OpenFolderDialog { Title = "Select parent folder for a NEW report snapshot" };
        if (folder.ShowDialog(this) != true) return;
        var destination = Path.Combine(folder.FolderName, "Report-" + item.Id + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        await RetrieveReportSnapshotAsync(item, destination, ct);
        OpenReportSnapshot();
    }
    internal async Task<FabricReportSnapshot> RetrieveReportSnapshotAsync(FabricItem item, string destination, CancellationToken ct)
    {
        var snapshot = await new FabricReportSnapshotService(http, auth).GetSnapshotAsync(item, destination, ct);
        lastSnapshot = snapshot; snapshotNotice.Text = snapshot.Format + " snapshot · " + snapshot.PartCount + " parts\n" + snapshot.Directory + "\nManifest records retrieval time and hashes. Authentication remains in Toolbox; subsequent edits are local only.";
        return snapshot;
    }
    private void OpenReportSnapshot()
    {
        var snapshot = lastSnapshot ?? throw new InvalidOperationException("Get a report definition first.");
        var tools = new CompanionTools(); var tool = CompanionTools.Catalog.Single(t => t.Id == "report-studio");
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PbiBench", "fabric-toolbox");
        var config = Path.Combine(settings, "report-studio-path.txt");
        // Packaged siblings live under the main application folder.
        var status = tools.Discover(tool, File.Exists(config) ? File.ReadAllText(config) : null, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")));
        if (status.Path == null)
        {
            var dialog = new OpenFileDialog { Title = "Locate PbiBench.ReportStudio.exe", Filter = "Report Studio|PbiBench.ReportStudio.exe" };
            if (dialog.ShowDialog(this) != true) return;
            status = tools.Discover(tool, dialog.FileName, AppContext.BaseDirectory); Directory.CreateDirectory(settings); File.WriteAllText(config, dialog.FileName);
        }
        tools.Launch(status, new ToolContext(ReportFile: snapshot.DefinitionFile));
    }
}
