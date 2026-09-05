using System.IO;
using System.Windows.Controls;
using PbiBench.DesignExchange;
using PbiBench.ExternalTools;

namespace PbiBench.ReportStudio;

public sealed partial class StudioWindow
{
    private readonly TabControl workspaces = new();
    private readonly TextBlock projectStrip = new() { Margin = new System.Windows.Thickness(16, 0, 16, 8), Foreground = PbiBench.DesignSystem.PbiBenchTheme.Secondary, TextWrapping = System.Windows.TextWrapping.Wrap };
    private TabItem? designTab;
    private int designRevision;
    public DesignPreviewView? DesignPreview { get; private set; }
    public async Task OpenDesignAsync(string modelContext, string? dashboardSpec, string? theme, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token); ct = linked.Token;
        var revision = ++designRevision;
        // Clear the previous preview before reading changed input; a failed load must not present stale validation.
        if (designTab != null) workspaces.Items.Remove(designTab); DesignPreview = null; designTab = null;
        var package = await Task.Run(() => DesignPackage.LoadAsync(modelContext, dashboardSpec, theme, ct), ct);
        ct.ThrowIfCancellationRequested(); if (revision != designRevision) return;
        if (!package.IsValid)
        {
            var messages = (package.Dashboard?.Diagnostics ?? Array.Empty<DesignDiagnostic>()).Concat(package.Theme?.Diagnostics ?? Array.Empty<DesignDiagnostic>());
            throw new InvalidDataException("Design Preview blocked: " + string.Join("\n", messages.Where(d => d.Severity == "Error").Select(d => d.Location + " · " + d.Message)));
        }
        DesignPreview = new(package); designTab = new TabItem { Header = "Design Preview", Content = DesignPreview }; workspaces.Items.Add(designTab); workspaces.SelectedItem = designTab;
        status.Text = "Validated design intent · no PBIR mutation · " + package.Model.Model.Name;
    }
    public async Task AcceptProjectContextAsync(string path, CancellationToken ct = default)
    {
        var context = await ProjectContext.LoadAsync(path, ct);
        projectStrip.Text = (context.PbipRoot == null ? "No project" : Path.GetFileName(context.PbipRoot)) + " · " + context.Source + " · " + context.GitStatus +
            "\nModel: " + (context.SemanticModelPath == null ? "none selected" : Path.GetFileName(context.SemanticModelPath)) + " · Report: " + (context.ReportPath == null ? "none selected" : Path.GetFileName(Path.GetDirectoryName(context.ReportPath)));
    }
}
