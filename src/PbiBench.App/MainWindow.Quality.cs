using System.IO;
using System.Windows;
using System.Windows.Controls;
using PbiBench.Automation;
using PbiBench.Core.DataExploration;
using PbiBench.Core.Quality;
using PbiBench.Core.Tasks;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public partial class MainWindow
{
    private readonly BackgroundTaskQueue backgroundTasks = new();
    private readonly TabControl qualityWorkspace = new() { Visibility = Visibility.Collapsed };
    private readonly TabControl automationWorkspace = new() { Visibility = Visibility.Collapsed };
    private readonly BpaRuleProfile bpaProfile = new();
    private readonly SemaphoreSlim bpaProfileWrites = new(1, 1);
    private BpaWorkspaceContext? bpaWorkspaceContext;
    private BpaRulesView? bpaRules;
    private VertiPaqWorkspaceView? vertiPaq;
    private SemanticTestsView? semanticTests;
    private BackgroundTasksView? backgroundTasksView;
    private ScriptAutomationView? scriptAutomation;
    private TabularModelHandler? qualityHandler;
    private string? qualityFingerprint, qualityServer, qualityDatabase, bpaScanFingerprint;

    private void InitializeQualityWorkspace()
    {
        var parent = (Panel)QaPage.Parent;
        parent.Children.Remove(QaPage); QaPage.Visibility = Visibility.Visible;
        qualityWorkspace.Items.Add(new TabItem { Header = "BPA findings", Content = QaPage });
        bpaRules = new BpaRulesView(bpaProfile, () => { Run(SaveBpaProfileAsync); if (editor.Handler != null) ScanBpa(this, new RoutedEventArgs()); });
        qualityWorkspace.Items.Add(new TabItem { Header = "Rule packs", Content = bpaRules });
        vertiPaq = new VertiPaqWorkspaceView(backgroundTasks);
        qualityWorkspace.Items.Add(new TabItem { Header = "VertiPaq / optimization", Content = vertiPaq });
        semanticTests = new SemanticTestsView(() => (editor.Server, editor.Server == null ? null : editor.Database),
            () => editor.Handler?.IsConnected == true ? editor.Handler.Database.Server.ConnectionString : null, new TomDaxQueryService(), backgroundTasks);
        semanticTests.ResultsChanged += (_, _) => UpdateQualitySignals();
        qualityWorkspace.Items.Add(new TabItem { Header = "Semantic tests", Content = semanticTests }); parent.Children.Add(qualityWorkspace);
        parent.Children.Remove(AutomationPage); AutomationPage.Visibility = Visibility.Visible;
        automationWorkspace.Items.Add(new TabItem { Header = "Automation Gallery", Content = AutomationPage });
        scriptAutomation = new ScriptAutomationView(() => editor.Handler, () => editor.Selection, () => Run(UpdateSessionAsync), backgroundTasks, settingsDirectory);
        automationWorkspace.Items.Add(new TabItem { Header = "Scripts / recorder / macros", Content = scriptAutomation }); parent.Children.Add(automationWorkspace);
        automationWorkspace.Items.Add(new TabItem { Header = "Power BI C# Gallery", Content = new PowerBiGalleryView(() => editor.Handler, () => editor.Selection,
            () => Run(UpdateSessionAsync), recipe => { automationWorkspace.SelectedIndex = 1; scriptAutomation.InsertGalleryRecipe(recipe); },
            (card, values) => Run(() =>
            {
                RequireModel();
                if (card.Id == "profile") { GoTo("Data"); return; }
                if (card.Id == "references") { GoTo("QA"); ScanBpa(this, new RoutedEventArgs()); return; }
                var options = new AutomationOptions { AllMeasuresWhenSelectionEmpty = false };
                if (card.Id == "measure-table") options.MeasureTableName = values["Table name"];
                ReviewPreview(automation!.Preview(card.Id == "measure-table" ? AutomationActionId.CreateMeasureTable : AutomationActionId.FormatMeasures, editor.Selection, options));
            })) });
        backgroundTasksView = new BackgroundTasksView(backgroundTasks);
        OutputTabs.Items.Add(new TabItem { Header = "Background tasks", Content = backgroundTasksView });
        BpaCategory.Items.Clear(); BpaCategory.Items.Add(new ComboBoxItem { Content = "All categories" });
        foreach (var pack in BpaRulePacks.BuiltIn) BpaCategory.Items.Add(new ComboBoxItem { Content = pack.Category }); BpaCategory.SelectedIndex = 0;
        Loaded += (_, _) => Run(LoadBpaProfileAsync);
    }
    private void RefreshQualityModel()
    {
        var handler = editor.Handler; var server = editor.Server; var database = server == null ? null : editor.Database;
        var fingerprint = handler == null ? null : new SemanticModelService(handler).Fingerprint();
        if (!ReferenceEquals(handler, qualityHandler) || fingerprint != qualityFingerprint || server != qualityServer || database != qualityDatabase)
        {
            qualityHandler = handler; qualityFingerprint = fingerprint; qualityServer = server; qualityDatabase = database;
            scriptAutomation?.RefreshModel(); semanticTests?.RefreshModel();
            vertiPaq?.Configure(handler, server, database, NavigateQualityObject, OpenQualityProfile);
            if (currentFindings != null && bpaScanFingerprint != fingerprint)
            {
                currentFindings = null; BpaGrid.ItemsSource = null; FindingDetails.Text = "The model changed. Scan BPA again for current findings.";
                ValidationStatus.Text = "Model changed · run BPA";
            }
        }
        UpdateQualitySignals();
    }
    private void UpdateQualitySignals()
    {
        if (vertiPaq == null) return;
        var signals = (currentFindings ?? Array.Empty<BpaFinding>()).Where(finding => !IsBpaSuppressed(finding)).Select(finding => new OptimizationSignal(
            finding.RuleId + "|" + finding.ObjectPath, "BPA " + finding.Version, finding.Category, finding.Risk, finding.Rule,
            finding.Reason, finding.Object is ITabularTableObject child ? child.Table.Name : (finding.Object as Table)?.Name,
            finding.Object is Column or TabularEditor.TOMWrapper.Measure ? finding.Object.Name : null, finding.ProposedChange));
        signals = signals.Concat((semanticTests?.LastResults ?? Array.Empty<SemanticTestResult>()).Select(result => new OptimizationSignal(
            "test|" + result.TestId, "Semantic tests", "correctness", "MANUAL", result.Name + " · " + result.Outcome,
            result.Evidence + " · " + result.ElapsedMilliseconds.ToString("N1") + " ms; run " + result.StartedAt.ToString("u"))));
        vertiPaq.SetQualitySignals(signals.ToArray());
    }
    private bool IsBpaSuppressed(BpaFinding finding) => editor.Handler != null && automation != null && bpaProfile.Suppressions.Contains(new BpaService(editor.Handler, automation).SuppressionKey(finding));
    private void NavigateQualityObject(string tableName, string? member)
    {
        RequireModel(); var table = editor.Handler!.Model.Tables.FirstOrDefault(item => item.Name == tableName)
            ?? throw new InvalidOperationException("The table is not in the current model. Match the analysis source before navigating.");
        TabularNamedObject obj = table;
        if (member != null) obj = (TabularNamedObject?)table.Columns.FirstOrDefault(item => item.Name == member) ?? table.Measures.FirstOrDefault(item => item.Name == member)
            ?? throw new InvalidOperationException("The analyzed column or measure is not in this model.");
        editor.Select(obj); GoTo("Model");
    }
    private void OpenQualityProfile(string tableName, string? column)
    {
        RequireModel(); GoTo("Data");
        if (column == null) { dataWorkspace!.OpenPreview(tableName); return; }
        var schema = DataModelSchemaProvider.Capture(editor.Handler!);
        var table = schema.Tables.Single(item => item.Name == tableName);
        dataWorkspace!.OpenProfile(DataProfileBuilder.Column(table, column, new DataProfileOptions()));
    }
    private async Task LoadBpaProfileAsync()
    {
        if (smokeMode) return;
        var path = Path.Combine(settingsDirectory, "bpa-profile.json"); if (!File.Exists(path)) return;
        var loaded = await BpaRuleProfile.LoadAsync(path, lifetime.Token);
        bpaProfile.Enabled = loaded.Enabled; bpaProfile.Severities = loaded.Severities; bpaProfile.Suppressions = loaded.Suppressions; bpaRules?.Refresh();
    }
    private async Task SaveBpaProfileAsync()
    {
        await bpaProfileWrites.WaitAsync(lifetime.Token);
        try { await bpaProfile.SaveAsync(Path.Combine(settingsDirectory, "bpa-profile.json"), lifetime.Token); }
        finally { bpaProfileWrites.Release(); }
    }
    private void AddQualityCommands(IDictionary<string, Action> entries)
    {
        entries["QA · Rule packs"] = () => { GoTo("QA"); qualityWorkspace.SelectedIndex = 1; };
        entries["QA · VertiPaq / optimization"] = () => { GoTo("QA"); qualityWorkspace.SelectedIndex = 2; };
        entries["QA · Semantic tests"] = () => { GoTo("QA"); qualityWorkspace.SelectedIndex = 3; };
        foreach (var tool in new[] { "Safe C# Preview", "Trusted Legacy", "Action recorder", "Macro library" })
            entries["Automate · " + tool] = () => { GoTo("Automate"); automationWorkspace.SelectedIndex = 1; scriptAutomation!.ShowTool(tool); };
        entries["Show background tasks"] = () => { layoutState.OutputVisible = true; ApplyPaneVisibility(); OutputTabs.SelectedIndex = OutputTabs.Items.Count - 1; };
    }
}
