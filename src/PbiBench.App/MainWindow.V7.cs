using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PbiBench.Automation;
using PbiBench.Core.Commands;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public partial class MainWindow
{
    private readonly WorkbenchCommandRegistry commands = new();
    private readonly DiagramView diagram = new();
    private Canvas DiagramCanvas => diagram.Canvas;
    private LayoutStateStore layoutStore = null!;
    private AppLayoutState layoutState = new();
    private ChangePreview? scannedAction;
    private IReadOnlyList<BpaFinding>? currentFindings;
    private readonly HashSet<string> ignoredFindings = new(StringComparer.Ordinal);
    private readonly Dictionary<AutomationActionId, string> lastActions = new();
    private string activePage = "Home";

    private void InitializeV7()
    {
        layoutStore = new LayoutStateStore(settingsDirectory);
        layoutState = layoutStore.Load();
        Width = Math.Min(layoutState.Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Min(layoutState.Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        if (layoutState.Left is double left && layoutState.Top is double top &&
            new Rect(left, top, Width, Height).IntersectsWith(SystemParameters.WorkArea)) { Left = left; Top = top; }
        if (layoutState.Maximized) WindowState = WindowState.Maximized;
        InspectorColumn.Width = new GridLength(Math.Min(layoutState.InspectorWidth, Math.Max(210, Width - 775)));
        OutputRow.Height = new GridLength(Math.Min(layoutState.OutputHeight, Math.Max(80, Height - 460)));
        commands.Register(WorkbenchCommandId.Open, () => OpenModel(this, new RoutedEventArgs()));
        commands.Register(WorkbenchCommandId.Connect, () => ConnectModel(this, new RoutedEventArgs()));
        commands.Register(WorkbenchCommandId.Save, () => { if (DaxPage.Visibility == Visibility.Visible) SaveScratch(this, new RoutedEventArgs()); else SaveModel(this, new RoutedEventArgs()); });
        commands.Register(WorkbenchCommandId.Undo, () => Undo(this, new RoutedEventArgs()));
        commands.Register(WorkbenchCommandId.Redo, () => Redo(this, new RoutedEventArgs()));
        commands.Register(WorkbenchCommandId.RunBpa, () => { GoTo("QA"); ScanBpa(this, new RoutedEventArgs()); });
        commands.Register(WorkbenchCommandId.Automate, () => GoTo("Automate"));
        commands.Register(WorkbenchCommandId.DaxStudio, () => { if (DaxPage.Visibility == Visibility.Visible) LaunchDaxStudio(this, new RoutedEventArgs()); else LaunchActiveExpression(this, new RoutedEventArgs()); });
        commands.Register(WorkbenchCommandId.Diagram, () => GoTo("Model diagram"));
        commands.Register(WorkbenchCommandId.Scripts, () => { RequireModel(); GoTo("Model"); editor.ShowScriptEditor(); });
        commands.Register(WorkbenchCommandId.Dependencies, () => { RequireModel(); editor.ShowDependencies(); });
        commands.Register(WorkbenchCommandId.FormatDax, () => { RequireModel(); ReviewPreview(automation!.Preview(AutomationActionId.FormatMeasures, editor.Selection)); });
        commands.Register(WorkbenchCommandId.NewModel, () => { GoTo("Model"); editor.New(); });
        editor.ConfigureCommands(commands);
        editor.ShowLegacyCommands(false);
        DiagramPage.Content = diagram;
        RefreshRecents();
        RefreshGallery();
        ApplyPaneVisibility();
    }

    private void CommandClick(object sender, RoutedEventArgs e) => Run(() => commands.Execute((WorkbenchCommandId)Enum.Parse(typeof(WorkbenchCommandId), (string)((Button)sender).Tag)));
    private void ToggleLegacy(object sender, RoutedEventArgs e) => Run(() => { GoTo("Model"); editor.ShowLegacyCommands(!editor.LegacyCommandsVisible); });
    private void ToggleInspector(object sender, RoutedEventArgs e)
    {
        if (InspectorPane.Visibility == Visibility.Visible) layoutState.InspectorWidth = InspectorColumn.ActualWidth;
        layoutState.InspectorVisible = !layoutState.InspectorVisible;
        ApplyPaneVisibility();
    }
    private void ToggleOutput(object sender, RoutedEventArgs e)
    {
        if (OutputTabs.Visibility == Visibility.Visible) layoutState.OutputHeight = OutputRow.ActualHeight;
        layoutState.OutputVisible = !layoutState.OutputVisible;
        ApplyPaneVisibility();
    }
    private void ApplyPaneVisibility()
    {
        var inspectorVisible = layoutState.InspectorVisible && activePage != "Home" && activePage != "PBIP / Git";
        InspectorPane.Visibility = InspectorSplitter.Visibility = inspectorVisible ? Visibility.Visible : Visibility.Collapsed;
        InspectorColumn.Width = new GridLength(inspectorVisible ? Math.Min(layoutState.InspectorWidth, Math.Max(210, ActualWidth - 775)) : 0);
        InspectorSplitterColumn.Width = new GridLength(inspectorVisible ? 5 : 0);
        OutputTabs.Visibility = OutputSplitter.Visibility = layoutState.OutputVisible ? Visibility.Visible : Visibility.Collapsed;
        OutputRow.Height = new GridLength(layoutState.OutputVisible ? Math.Min(layoutState.OutputHeight, Math.Max(80, ActualHeight - 460)) : 0);
        OutputSplitterRow.Height = new GridLength(layoutState.OutputVisible ? 5 : 0);
    }
    private void SaveLayout()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        layoutState.Width = bounds.Width; layoutState.Height = bounds.Height;
        layoutState.Left = bounds.Left; layoutState.Top = bounds.Top; layoutState.Maximized = WindowState == WindowState.Maximized;
        layoutState.SelectedPage = activePage;
        layoutState.NativePaneFractions = editor.CapturePaneFractions();
        if (InspectorPane.Visibility == Visibility.Visible) layoutState.InspectorWidth = InspectorColumn.ActualWidth;
        if (OutputTabs.Visibility == Visibility.Visible) layoutState.OutputHeight = OutputRow.ActualHeight;
        if (!layoutStore.TrySave(layoutState, out var error)) Log(error!);
    }
    private void RememberProject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        layoutState.RememberProject(path!); RefreshRecents();
    }
    private void RefreshRecents()
    {
        RecentProjects.ItemsSource = layoutState.RecentProjects.ToArray();
        RecentEmpty.Visibility = layoutState.RecentProjects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OpenRecent(object sender, MouseButtonEventArgs e) => OpenRecentProject();
    private void RecentKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; OpenRecentProject(); } }
    private void OpenRecentProject() => Run(async () =>
    {
        if (RecentProjects.SelectedItem is not string path) return;
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("This recent project has moved or is unavailable. Use Open to select its current location.", path);
        if (path.EndsWith(".pbip", StringComparison.OrdinalIgnoreCase) || Directory.Exists(path)) { GoTo("PBIP / Git"); await UpdateWorkspaceAsync(path); }
        else { GoTo("Model"); editor.Open(path); }
    });
    private void CommonMeasures(object sender, RoutedEventArgs e) { GoTo("Automate"); ActionPicker.SelectedItem = AutomationService.Actions.First(a => a.Id == AutomationActionId.CreateSumMeasures); }
    private void AnalyzeFromHome(object sender, RoutedEventArgs e) { GoTo("DAX"); RefreshDaxStudioStatus(); }
    private void ReviewGit(object sender, RoutedEventArgs e) => GoTo("PBIP / Git");

    private void UpdateInspector()
    {
        var snapshot = SelectionInspector.Create(editor.Selection, currentFindings == null ? null : obj => currentFindings.Count(f => ReferenceEquals(f.Object, obj)));
        InspectorTitle.Text = snapshot.Title;
        InspectorSelection.Text = snapshot.Kind + (snapshot.Path.Length == 0 ? "" : "\n" + snapshot.Path);
        InspectorExpression.Text = snapshot.Expression;
        InspectorExpression.Visibility = string.IsNullOrWhiteSpace(snapshot.Expression) ? Visibility.Collapsed : Visibility.Visible;
        InspectorFields.ItemsSource = snapshot.Fields.Concat(new[] { new InspectorField("Dependencies / usages", $"{snapshot.DependencyCount} / {snapshot.ReferenceCount}"), new InspectorField("BPA findings", snapshot.BpaFindingCount?.ToString() ?? "Not scanned") });
        InspectorActions.Children.Clear();
        foreach (var action in snapshot.Actions.Distinct())
        {
            var label = action switch { InspectorAction.EditDax => "Edit DAX", InspectorAction.FormatDax => "Format…", InspectorAction.Dependencies => "Dependencies", InspectorAction.BestPractices => "Run BPA", InspectorAction.PreviewSafeFixes => "Safe fixes…", InspectorAction.AnalyzeInDaxStudio => "DAX Studio", InspectorAction.ShowDiagram => "Diagram", InspectorAction.GoToFromTable => "From table", _ => "To table" };
            var button = new Button { Content = label, FontSize = 11 };
            button.Click += (_, _) => Run(() => InspectorCommand(action)); InspectorActions.Children.Add(button);
        }
        if (editor.Selection.Count == 1 && editor.Selection[0] is SingleColumnRelationship relationship)
        {
            var edit = new Button { Content = "Edit relationship…", FontSize = 11 };
            edit.Click += (_, _) => Run(() => { ConfigureDiagramAuthoring(); diagram.EditRelationship(relationship); }); InspectorActions.Children.Add(edit);
        }
        if (editor.Selection.Count == 1 && editor.Selection[0] is Table table)
        {
            var membership = PbiBench.Semantic.ModelAuthoring.TableGroupService.Read(table);
            InspectorFields.ItemsSource = ((IEnumerable<InspectorField>)InspectorFields.ItemsSource).Concat(new[] { new InspectorField("Table group", membership.Issue ?? membership.Group ?? "Ungrouped") });
            var group = new Button { Content = "Table group…", FontSize = 11 };
            group.Click += (_, _) => Run(() => { ConfigureDiagramAuthoring(); diagram.EditTableGroups(table); }); InspectorActions.Children.Add(group);
            var preview = new Button { Content = "Preview Data", FontSize = 11 };
            preview.Click += (_, _) => Run(() => editor.RequestPreviewData?.Invoke(table.Name)); InspectorActions.Children.Add(preview);
        }
    }
    private void InspectorCommand(InspectorAction action)
    {
        switch (action)
        {
            case InspectorAction.EditDax: OpenRichExpression(); break;
            case InspectorAction.FormatDax: commands.Execute(WorkbenchCommandId.FormatDax); break;
            case InspectorAction.Dependencies: commands.Execute(WorkbenchCommandId.Dependencies); break;
            case InspectorAction.AnalyzeInDaxStudio: LaunchActiveExpression(this, new RoutedEventArgs()); break;
            case InspectorAction.ShowDiagram: GoTo("Model diagram"); break;
            case InspectorAction.GoToFromTable:
            case InspectorAction.GoToToTable:
                if (editor.Selection.FirstOrDefault() is SingleColumnRelationship relationship)
                { var table = action == InspectorAction.GoToFromTable ? relationship.FromTable : relationship.ToTable; if (table != null) { editor.Select(table); GoTo("Model"); } } break;
            default: commands.Execute(WorkbenchCommandId.RunBpa); break;
        }
    }

    private void GallerySelectionChanged(object sender, SelectionChangedEventArgs e) { if (ready) RefreshGallery(); }
    private void RefreshGallery()
    {
        scannedAction = null; PreviewActionButton.IsEnabled = false; ActionPreviewGrid.ItemsSource = null;
        if (ActionPicker.SelectedItem is not AutomationAction action) return;
        GalleryTitle.Text = action.Name; GalleryDetails.Text = action.Description + "\n\nScope: " + action.Selection;
        var risk = action.Id == AutomationActionId.SetSummarizeByNone || action.Id == AutomationActionId.LastRefreshScaffold ? "REVIEW" : "SAFE · LOCAL PREVIEW";
        GalleryRisk.Text = risk + "\n" + action.Risk;
        AutomationResult.Text = lastActions.TryGetValue(action.Id, out var last) ? "Last run: " + last : "Not run in this session. Scan to see the exact affected objects.";
        AllMeasures.IsEnabled = action.Id == AutomationActionId.FormatMeasures || action.Id == AutomationActionId.OrganizeMeasures || action.Id == AutomationActionId.AddDescriptions;
    }
    private ChangePreview BuildActionPreview()
    {
        RequireModel();
        if (ActionPicker.SelectedItem is not AutomationAction action) throw new InvalidOperationException("Choose an action in the gallery.");
        var selected = AllMeasures.IsChecked == true && AllMeasures.IsEnabled ? editor.Handler!.Model.AllMeasures.Cast<TabularNamedObject>().ToArray() : editor.Selection;
        return automation!.Preview(action.Id, selected, new AutomationOptions { MeasureTableName = MeasureTableName.Text.Trim(), DisplayFolder = DisplayFolderName.Text, AllMeasuresWhenSelectionEmpty = false });
    }
    private void ScanAction(object sender, RoutedEventArgs e) => Run(() =>
    {
        scannedAction = BuildActionPreview(); ActionPreviewGrid.ItemsSource = scannedAction.Changes;
        PreviewActionButton.IsEnabled = true;
        AutomationResult.Text = $"{scannedAction.Changes.Select(c => c.ObjectPath).Distinct().Count()} affected objects · {scannedAction.Changes.Count} changes\n" + string.Join("\n", scannedAction.Notices);
        ValidationDetails.Text = "Plan: apply the exact reviewed metadata in one undo batch, check each resulting property, and roll back the batch if validation fails.\nNo server write occurs until Save is separately requested.";
    });
    private void AcceptAutomation(object sender, RoutedEventArgs e)
    {
        scannedAction = null; PreviewActionButton.IsEnabled = false;
        AutomationResult.Text = "Result accepted. Local edits remain available in TE2 undo history. Save when ready.";
    }
    private void RecordAction(ChangePreview preview, ApplyResult result)
    {
        var entry = $"{DateTime.Now:HH:mm:ss} · {preview.Action.Name} · {result.ChangedObjects} objects · validated";
        lastActions[preview.Action.Id] = entry; ActionHistory.Items.Insert(0, entry);
        AutomationResult.Text = result.Message + "\nValidated locally. Undo to restore or accept the result.";
        ValidationDetails.Text = result.Message + "\nAll planned property checks passed; one undo batch is available.";
        PreviewActionButton.IsEnabled = false;
    }

    private void FilterBpa(object sender, RoutedEventArgs e)
    {
        if (!ready || currentFindings == null) return;
        var severity = (BpaSeverity.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "All severities";
        var category = (BpaCategory.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "All categories";
        BpaGrid.ItemsSource = currentFindings.Where(f => (severity.StartsWith("All", StringComparison.Ordinal) || f.Severity.ToString() == severity) &&
            (category.StartsWith("All", StringComparison.Ordinal) || f.Category == category) && (ShowIgnored.IsChecked == true || !ignoredFindings.Contains(FindingKey(f)))).ToArray();
    }
    private static string FindingKey(BpaFinding f) => f.RuleId + "|" + f.ObjectPath;
    private void GoToFinding(object sender, RoutedEventArgs e) => Run(() => { if (BpaGrid.SelectedItem is BpaFinding f) { editor.Select(f.Object); GoTo("Model"); } });
    private void IgnoreFinding(object sender, RoutedEventArgs e)
    {
        if (BpaGrid.SelectedItem is not BpaFinding f) return;
        var key = FindingKey(f); if (!ignoredFindings.Remove(key)) ignoredFindings.Add(key);
        Log("Changed this finding's session visibility. Model rules and metadata are unchanged."); FilterBpa(sender, e);
    }
}
