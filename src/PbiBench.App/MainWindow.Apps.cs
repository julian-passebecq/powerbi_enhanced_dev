using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PbiBench.AI.ContextExport;
using PbiBench.Core.Fabric;
using PbiBench.Core.Platform;
using PbiBench.DaxStudio;
using PbiBench.Semantic;

namespace PbiBench.App;

public partial class MainWindow
{
    private void OpenApps(object sender, RoutedEventArgs e) => Run(() =>
    {
        var window = new Window { Owner = this, Title = "PbiBench · Apps / Tools", Width = 940, Height = 700, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(18) }; window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Apps / Tools", FontSize = 26, Margin = new Thickness(4) });
        void Action(string title, string description, Action run)
        { var button = new Button { Content = title, Margin = new Thickness(4), Padding = new Thickness(9), HorizontalContentAlignment = HorizontalAlignment.Left }; button.Click += (_, _) => Run(run); panel.Children.Add(button); panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5) }); }
        Action("▣  Semantic IDE / TE2++", "Current app · model engineering, DAX/query, data exploration, automation, QA and PBIP/Git. TE2 2.28 / net48.", () => { window.Close(); GoTo("Model"); });
        Action("↗  AI Context Export", "Semantic utility · review metadata, selected scope and optional samples for any external AI.", () => { RequireModel(); window.Close(); CreateAIExportWindow().ShowDialog(); });
        var launcher = new CompanionTools();
        foreach (var tool in CompanionTools.Catalog)
        {
            var config = Path.Combine(settingsDirectory, tool.Id + "-path.txt");
            CompanionStatus Detect() => launcher.Discover(tool, File.Exists(config) ? File.ReadAllText(config).Trim() : null, AppDomain.CurrentDomain.BaseDirectory);
            var row = new StackPanel(); var label = new TextBlock { Text = tool.Ownership + " · " + Detect().Display, Margin = new Thickness(5) };
            var bar = new WrapPanel(); var open = new Button { Content = "↗  " + tool.Name, Margin = new Thickness(4), Padding = new Thickness(8) };
            open.Click += (_, _) => Run(() => launcher.Launch(Detect(), workspaceRoot)); bar.Children.Add(open);
            var configure = new Button { Content = "Configure path…", Margin = new Thickness(4) }; configure.Click += (_, _) => Run(() => { var dialog = new OpenFileDialog { Filter = "Windows executable|*.exe", Title = "Choose " + tool.Name }; if (dialog.ShowDialog(window) == true) { File.WriteAllText(config, dialog.FileName); label.Text = tool.Ownership + " · " + Detect().Display; } }); bar.Children.Add(configure);
            row.Children.Add(bar); row.Children.Add(label); panel.Children.Add(row);
        }
        Action("↗  DAX Studio", "External specialist · uses the current server/database and active DAX through the existing handoff.", () => { window.Close(); LaunchDaxStudio(this, new RoutedEventArgs()); });
        Action("⇄  Import Fabric selection…", "Versioned selection handoff only. Nothing connects, imports or writes automatically.", () => Run(async () =>
        {
            var dialog = new OpenFileDialog { Filter = "Fabric selection|*.pbifabric.json;*.json" }; if (dialog.ShowDialog(window) != true) return;
            var handoff = await FabricSelectionHandoff.LoadAsync(dialog.FileName, lifetime.Token);
            window.Close(); GoTo("Fabric"); fabricWorkspace!.AcceptSelectionHandoff(handoff);
        }));
        Action("ⓘ  Provenance / About", "Feature owners, implementation sources, exact dependency pins, local TE2 patches and update lanes.", () =>
        {
            var catalog = ProvenanceCatalog.Bundled(); var about = new Window { Owner = window, Title = "PbiBench " + catalog.ProductVersion + " · Provenance", Width = 1120, Height = 640, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            about.Content = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, ItemsSource = catalog.Components.Select(c => new { c.Feature, c.OwnerProject, c.SourceType, c.Pin, c.License, c.UpdateLane, Patches = string.Join("; ", c.LocalPatches), Tests = string.Join("; ", c.ProtectingTests) }).ToArray() }; about.ShowDialog();
        });
        window.ShowDialog();
    });
    internal AIContextExportWindow CreateAIExportWindow()
    {
        RequireModel(); var model = AIContextCapture.Capture(editor.Handler!, includeRoles: true); var evidence = new List<ContextEvidence>();
        foreach (var finding in currentFindings ?? Array.Empty<PbiBench.Automation.BpaFinding>())
        {
            var obj = finding.Object == null ? null : model.Objects.FirstOrDefault(o => o.Id == AIContextCapture.Id(finding.Object));
            if (obj != null) evidence.Add(new("BPA", obj.Id, finding.RuleId, finding.Severity.ToString(), finding.Reason));
        }
        if (vertiPaq?.Snapshot is { } snapshot && snapshot.ModelName == model.Name)
            foreach (var table in snapshot.Tables) evidence.Add(new("VertiPaq", ContextModel.ObjectId("Table", null, table.Name), table.Name, "Captured statistics", "Rows=" + table.Rows + "; TotalBytes=" + table.TotalBytes + "; RI violations=" + table.RiViolations));
        if (semanticTests != null) foreach (var result in semanticTests.LastResults) evidence.Add(new("Tests", "", result.Name, result.Outcome.ToString(), "Assertion outcome only; query result values and server context omitted."));
        if (workspaceSync != null) foreach (var change in workspaceSync.LastGitChanges)
            if (new[] { "Name", "Description", "DisplayFolder", "IsHidden", "FormatString" }.Contains(change.Property, StringComparer.OrdinalIgnoreCase) && !new[] { "partition", "datasource", "credential", "annotation", "expression", "extendedpropert" }.Any(s => change.ObjectPath.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                evidence.Add(new("Workspace", "", change.Property, "Changed", "Presentation-property difference exists; raw paths and file contents omitted."));
        var sampler = editor.Handler?.IsConnected == true && editor.Server != null ? new SemanticContextSampler(new TomDaxQueryService(), editor.Server, editor.Database!, editor.Handler.Database.Server.ConnectionString) : null;
        return new(model, editor.Selection.Select(AIContextCapture.Id).Where(id => model.Objects.Any(o => o.Id == id) || model.Relationships.Any(r => r.Id == id)).ToArray(), sampler, evidence) { Owner = this };
    }
}
