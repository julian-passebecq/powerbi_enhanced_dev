using System.IO;
using System.Windows;
using System.Windows.Controls;
using PbiBench.DesignExchange;
using PbiBench.DesignSystem;
using PbiBench.ExternalTools;
using PbiBench.Semantic;

namespace PbiBench.App;

public partial class MainWindow
{
    private DesignExchangeView designExchange = null!;
    private bool selectingModule;
    private string? projectGitBranch;
    private readonly Dictionary<string, (string Json, string Path)> handoffs = new();
    private readonly Dictionary<string, TextBlock> homeToolStatus = new();
    private void InitializeShell()
    {
        PbiBenchTheme.Apply(this);
        foreach (var item in Navigation.Items.Cast<ListBoxItem>())
        { var name = (string)item.Content; item.Tag = name; item.Content = PbiBenchTheme.Label(name, name); }
        designExchange = new(() => editor.Handler == null ? null : ModelContext.Create(AIContextCapture.Capture(editor.Handler)),
            context => Run(() => companionTools.Launch(RefreshTool("report-studio"), context with { ReportFile = reportFile, ProjectFile = projectFile, ProjectContextFile = SaveProjectHandoff("report-studio") })), lifetime.Token);
        DesignExchangeSurface.Content = designExchange;
        void Group(string title, params (string Icon, string Title, string Description, string Status, Action Action)[] cards)
        {
            HomeCards.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 24, 0, 12) });
            var wrap = new WrapPanel(); HomeCards.Children.Add(wrap);
            foreach (var card in cards)
            {
                var content = new StackPanel(); content.Children.Add(PbiBenchTheme.Label(card.Icon, card.Title));
                content.Children.Add(new TextBlock { Text = card.Description, TextWrapping = TextWrapping.Wrap, Foreground = PbiBenchTheme.Secondary, Margin = new Thickness(0, 8, 0, 8), MinHeight = 36 });
                var chip = new TextBlock { Text = card.Status, Foreground = PbiBenchTheme.Secondary, FontSize = 11 };
                content.Children.Add(new Border { Child = chip, Background = PbiBenchTheme.Background, Padding = new Thickness(6, 3, 6, 3), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left });
                var tool = CompanionTools.Catalog.FirstOrDefault(t => t.Name == card.Title); if (tool != null) homeToolStatus[tool.Id] = chip;
                var button = new Button { Content = content, Width = 260, MinHeight = 116, Padding = new Thickness(16), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = card.Description };
                button.Click += (_, _) => Run(card.Action); wrap.Children.Add(button);
            }
        }
        string Status(string id) => toolStatuses[id].Display;
        Group("Build", ("Model", "Semantic Model", "Create, connect and edit your model.", "Available", () => GoTo("Model")),
            ("DAX", "DAX Workbench", "Write queries and explore results.", "Available", () => GoTo("DAX")),
            ("Automate", "Automation Gallery", "Review reusable model improvements.", "Preview + undo", () => { GoTo("Automate"); automationWorkspace.SelectedIndex = 2; }),
            ("Report", "Report Studio", "Explore pages, bindings and local changes.", Status("report-studio"), () => NavigateModule("Report")));
        Group("Project", ("Git", "PBIP / Git", "Review workspace status and file changes.", "Local project", () => GoTo("PBIP / Git")),
            ("Quality", "Validation / Recovery", "Compare, validate and recover workspace edits.", "Local review", () => { GoTo("PBIP / Git"); workspaceExperience.SelectedIndex = 1; }),
            ("Theme", "Design Exchange", "Exchange model, dashboard and theme JSON.", "Offline · v1", () => GoTo("Design Exchange")));
        Group("Platform", ("Fabric", "Fabric Toolbox", "Browse inventory and read-only snapshots.", Status("fabric-toolbox"), () => NavigateModule("Fabric")),
            ("External", "Power BI Desktop", "Open the active project in its final renderer.", Status("powerbi"), () => LaunchCompanion("powerbi")));
        Group("Specialist", ("DAX", "DAX Studio", "Analyze the active DAX in the specialist tool.", "External", () => LaunchDaxStudio(this, new RoutedEventArgs())),
            ("External", "Bravo", "Open the current compatible live model.", Status("bravo"), () => LaunchCompanion("bravo")),
            ("Project", "VS Code", "Open the current project source folder.", Status("vscode"), () => LaunchCompanion("vscode")));
        RefreshProjectStrip();
    }
    internal static IReadOnlyList<string> ModuleNames { get; } = Array.AsReadOnly(new[] { "Home", "Model", "DAX", "Automate", "Report", "Project", "Fabric", "Tools", "Settings", "About" });
    private void NavigateModule(string module)
    {
        if (module == "Fabric") { LaunchCompanion("fabric-toolbox"); return; }
        if (module is "Tools" or "Settings") { OpenApps(this, new RoutedEventArgs()); return; }
        if (module == "About") { CreateAboutWindow(this).ShowDialog(); return; }
        ShowPage(module == "Project" ? "PBIP / Git" : module);
    }
    private void RefreshProjectStrip()
    {
        if (ProjectContextStrip == null) return;
        var formats = projectFile != null ? "PBIP" : "Local";
        if (semanticWorkspaceRoot != null && Directory.Exists(Path.Combine(semanticWorkspaceRoot, "definition"))) formats += " · TMDL";
        if (reportFile != null) formats += " · PBIR";
        ProjectContextStrip.Text = (Path.GetFileName(projectFile) ?? Path.GetFileName(workspaceRoot) ?? "No project") + "   ·   " + formats +
            "   |   Model: " + (editor.Handler?.Database.Name ?? "none loaded") + "   |   Report: " + (reportFile == null ? "none selected" : Path.GetFileName(Path.GetDirectoryName(reportFile))) +
            "   |   " + (editor.Handler?.HasUnsavedChanges == true ? "Loaded: edited" : "Loaded: unchanged") + " · Live: " + (editor.Handler?.IsConnected == true ? "Connected" : "Offline");
    }
    private void RefreshHomeStatus()
    { foreach (var entry in homeToolStatus) { var status = RefreshTool(entry.Key); var applicability = ExternalToolContext.Evaluate(status, CurrentToolContext()); entry.Value.Text = status.Path == null ? "Configure in Tools" : applicability.Enabled ? "Ready" : "Needs project / connection"; entry.Value.ToolTip = status.Display + " · " + applicability.Reason; } }
    private string SaveProjectHandoff(string module)
    {
        var context = new ProjectContext(PbipRoot: workspaceRoot, SemanticModelPath: semanticWorkspaceRoot ?? editor.FilePath, ReportPath: reportFile,
            FabricWorkspaceId: fabricWorkspace?.SelectedWorkspaceId, FabricItemId: fabricWorkspace?.SelectedItemId,
            ModelFingerprint: TryModelFingerprint(), GitBranch: projectGitBranch,
            GitStatus: GitHeader.Text.Length > 512 ? GitHeader.Text.Substring(0, 512) : GitHeader.Text, Source: editor.Handler?.IsConnected == true ? "Live" : editor.Handler != null ? "Loaded" : "Disk");
        context.Validate(); var json = ContractJson.Serialize(context);
        if (handoffs.TryGetValue(module, out var previous) && previous.Json == json && File.Exists(previous.Path) && File.ReadAllText(previous.Path) == json) return previous.Path;
        var folder = Path.Combine(settingsDirectory, "handoffs"); Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "project-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(file, json);
        handoffs[module] = (json, file); return file;
    }
    private string? TryModelFingerprint()
    {
        // The optional metadata export limit must not prevent launching a module on a larger model.
        try { return editor.Handler == null ? null : ModelContext.Create(AIContextCapture.Capture(editor.Handler)).ModelFingerprint; }
        catch (InvalidOperationException) { return null; } catch (InvalidDataException) { return null; }
    }
    private void OpenDesignExchange(object sender, RoutedEventArgs e) => GoTo("Design Exchange");
    private void OpenDataArea(object sender, RoutedEventArgs e) => GoTo("Data");
    private void OpenQualityArea(object sender, RoutedEventArgs e) => GoTo("QA");
    private void OpenFabricAuthoring(object sender, RoutedEventArgs e) => GoTo("Fabric");
}
