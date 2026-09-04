using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PbiBench.Automation;
using PbiBench.Core.Domain;
using PbiBench.DaxStudio;
using PbiBench.Git;
using PbiBench.ModelEditor;
using PbiBench.Semantic;
using PbiBench.Workspace;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public partial class MainWindow : Window
{
    private readonly Te2ModelEditor editor;
    private readonly DaxScratchEditor initialScratch;
    private DaxScratchEditor scratch => daxWorkspace?.ActiveEditor ?? initialScratch;
    private readonly PbipWorkspaceScanner scanner = new();
    private readonly GitClient git = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly string settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PbiBench");
    private AutomationService? automation;
    private TabularModelHandler? currentHandler;
    private string? workspaceRoot;
    private string? daxStudioPath;
    private int statusRevision;
    private bool ready;
    private string baseline = "";

    public MainWindow()
    {
        InitializeComponent();
        var launchArgs = Environment.GetCommandLineArgs();
        var smokeIndex = Array.IndexOf(launchArgs, "--smoke-test");
        smokeMode = smokeIndex >= 0;
        if (smokeMode && smokeIndex + 1 < launchArgs.Length) settingsDirectory = Path.Combine(Path.GetFullPath(launchArgs[smokeIndex + 1]), "profile");
        editor = new Te2ModelEditor(smokeMode ? Path.Combine(settingsDirectory, "TE2") : null);
        editor.ReviewWrite = ReviewRemoteWrite;
        editor.RequestClose = () => Dispatcher.BeginInvoke(new Action(Close));
        initialScratch = new DaxScratchEditor();
        ModelSurface.Content = editor.View;
        ActionPicker.ItemsSource = AutomationService.Actions;
        ActionPicker.SelectedIndex = 0;
        Directory.CreateDirectory(settingsDirectory);
        var config = Path.Combine(settingsDirectory, "daxstudio-path.txt");
        if (File.Exists(config)) daxStudioPath = File.ReadAllText(config).Trim();
        var query = Path.Combine(settingsDirectory, "scratch.dax");
        scratch.Text = File.Exists(query) ? File.ReadAllText(query) : "// Draft query — use the active expression or write DAX here.\r\nEVALUATE\r\n    ROW ( \"Result\", 1 )";
        editor.ModelChanged += (_, _) => Run(UpdateSessionAsync);
        editor.SelectionChanged += (_, _) => UpdateSelection();
        InitializeV7();
        InitializeDaxWorkspace();
        ready = true;
        GoTo(layoutState.SelectedPage);
        RefreshDaxStudioStatus();
        Log("PbiBench ready. TE2 2.28.0 is integrated in this process. Bulk actions use preview and TE2 undo.");
        Loaded += (_, _) => Run(async () =>
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Contains("--smoke-test")) { await RunSmokeAsync(args); return; }
            if (args.Length == 1 && File.Exists(args[0])) editor.Open(args[0]);
            else if (args.Length == 2 && args[0] == "--demo") editor.Open(args[1]);
            else if (args.Length == 2 && !args[0].StartsWith("--", StringComparison.Ordinal)) editor.Connect(args[0], args[1]);
            await UpdateSessionAsync();
            if (args.Length > 0) GoTo("Model");
            else ApplyPaneVisibility();
            editor.RestorePaneFractions(layoutState.NativePaneFractions);
        });
    }

    private void Log(string message)
    {
        Output.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        Output.ScrollToEnd();
    }
    private bool ReviewRemoteWrite(string operation, string target, string proposed)
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(() => ReviewRemoteWrite(operation, target, proposed));
        var before = operation.StartsWith("Deploy", StringComparison.Ordinal)
            ? "Destination metadata is not loaded in this preview. The After column contains the actual deployment command."
            : baseline;
        var plan = new ChangePlan(Guid.NewGuid(), DateTimeOffset.UtcNow, ApprovalLevel.RemoteModelWrite,
            new ResourceRef("xmla", null, null, null, "SemanticModel", target),
            new[] { new PlannedChange(target, operation, before, proposed, new[] { "TE2 conflict check and server validation" }) },
            "Original model metadata held for review in this session", "Local edits can be undone. Remote rollback requires a separately reviewed deployment.");
        var approved = PreviewDialog.Show(this, operation + " · " + target,
            "Review the complete metadata / command before writing to this connection. Server-side validation may reject the request.\n" + plan.RollbackStrategy,
            new[] { new PreviewRow(target, operation, before, proposed, "Remote semantic model write") }, true, "Approve and write to server");
        if (!approved) return false;
        var approval = new ApprovedChangePlan(plan, DateTimeOffset.UtcNow, Environment.UserName);
        Log("Approved remote write plan " + approval.Plan.Id + ". Server execution follows; approval alone is not a success result.");
        return true;
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { ReportError(ex); }
    }
    private async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReportError(ex); }
    }
    private void ReportError(Exception ex)
    {
        if (smokeMode) throw new InvalidOperationException("Smoke UI action failed", ex);
        ValidationStatus.Text = "Action needs attention";
        Log("Action could not complete (" + ex.GetType().Name + ").");
        MessageBox.Show(this, ex.Message, "PbiBench", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void RequireModel()
    {
        if (editor.Handler == null) throw new InvalidOperationException("Open a semantic model or connect to Power BI Desktop / XMLA first.");
        editor.AcceptExpression();
    }
    private async Task UpdateSessionAsync()
    {
        if (!ready) return;
        var handler = editor.Handler;
        if (handler != currentHandler)
        {
            workspaceRoot = null;
            currentHandler = handler;
            automation = handler == null ? null : new AutomationService(handler);
            baseline = handler == null ? "" : Microsoft.AnalysisServices.Tabular.JsonSerializer.SerializeDatabase(handler.Database);
            if (handler != null)
            {
                handler.UndoManager.UndoStateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateModelStatus));
            }
            currentFindings = null; ignoredFindings.Clear(); scannedAction = null;
            BpaGrid.ItemsSource = null;
            FindingDetails.Text = "";
            ActionPreviewGrid.ItemsSource = null;
        }
        RememberProject(editor.FilePath);
        UpdateModelStatus();
        UpdateSelection();
        daxWorkspace?.RefreshMetadata();
        if (handler == null) { await UpdateWorkspaceAsync(null); return; }
        await UpdateWorkspaceAsync(workspaceRoot ?? editor.FilePath);
    }
    private void UpdateModelStatus()
    {
        if (!ready || editor.Handler == null) return;
        var h = editor.Handler;
        ModelTitle.Text = h.Database.Name + (h.HasUnsavedChanges ? "  •" : "");
        InspectorModel.Text = h.Database.Name;
        InspectorDetails.Text = $"{h.Model.Tables.Count} tables\n{h.Model.AllMeasures.Count()} measures\n{h.Model.Relationships.Count} relationships\nCompatibility {h.CompatibilityLevel}\n\n{(h.HasUnsavedChanges ? "Unsaved local edits" : "No pending edits")}\nUndo: {h.UndoManager.UndoSteps} steps";
        ConnectionStatus.Text = h.IsConnected ? $"{(h.IsPbiDesktop ? "Power BI Desktop" : "XMLA")} · {editor.Server} · {editor.Database}" : $"Local · {editor.FilePath ?? "unsaved model"}";
    }
    private void UpdateSelection()
    {
        if (!ready) return;
        var selection = editor.Selection;
        InspectorSelection.Text = selection.Count == 0 ? "Select objects in the model tree" : string.Join("\n", selection.Take(12).Select(SemanticModelService.ObjectPath)) + (selection.Count > 12 ? $"\n+ {selection.Count - 12} more" : "");
        UpdateInspector();
        SelectionSummary.Text = $"{selection.Count} selected object(s). " + string.Join(", ", selection.Take(6).Select(o => o.Name));
    }
    private void NavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready && Navigation.SelectedItem is ListBoxItem item) Run(() => ShowPage(item.Content.ToString()!));
    }
    internal void ShowPage(string page)
    {
        if (InspectorPane.Visibility == Visibility.Visible && InspectorColumn.ActualWidth >= 210) layoutState.InspectorWidth = InspectorColumn.ActualWidth;
        if (OutputTabs.Visibility == Visibility.Visible && OutputRow.ActualHeight >= 80) layoutState.OutputHeight = OutputRow.ActualHeight;
        activePage = page; ApplyPaneVisibility();
        foreach (var surface in new FrameworkElement[] { ModelSurface, HomePage, DaxPage, AutomationPage, DiagramPage, WorkspacePage, QaPage, LaterPage }) surface.Visibility = Visibility.Collapsed;
        FrameworkElement selected = page switch
        {
            "Home" => HomePage, "Model" => ModelSurface, "DAX" => DaxPage, "Automate" => AutomationPage,
            "Model diagram" => DiagramPage, "PBIP / Git" => WorkspacePage, "QA" => QaPage, _ => LaterPage
        };
        selected.Visibility = Visibility.Visible;
        if (page == "Model diagram") DrawDiagram();
        if (page == "Automate") UpdateSelection();
        if (page == "PBIP / Git") Run(() => UpdateWorkspaceAsync(workspaceRoot ?? editor.FilePath));
        if (selected == LaterPage)
        {
            LaterTitle.Text = page;
            LaterDetails.Text = page switch
            {
                "Knowledge" => "The Senior Playbook and SQLBI reference material are included in the docs folder. The searchable knowledge workspace is scheduled after the integrated Model Editor acceptance gate.",
                "Deploy" => "Use the Model editor's existing deployment wizard for semantic deployments. Remote changes require a reviewed change plan. CI/CD management is a later pass.",
                _ => "This workspace is scheduled for a later implementation pass. Model editing, automation, BPA, DAX, relationships and PBIP/Git are available now."
            };
        }
    }
    private void GoTo(string page)
    {
        var item = Navigation.Items.Cast<ListBoxItem>().First(i => (string)i.Content == page);
        if (ReferenceEquals(Navigation.SelectedItem, item)) ShowPage(page);
        else Navigation.SelectedItem = item;
    }
    private void OpenModel(object sender, RoutedEventArgs e) => Run(() => { GoTo("Model"); editor.OpenDialog(); });
    private void ConnectModel(object sender, RoutedEventArgs e) => Run(() => { GoTo("Model"); editor.Connect(); });
    private void SaveModel(object sender, RoutedEventArgs e) => Run(async () => { RequireModel(); editor.Save(); await UpdateSessionAsync(); });
    private void Undo(object sender, RoutedEventArgs e) => Run(() => { RequireModel(); editor.Undo(); UpdateModelStatus(); Log("Undid the last local model change."); });
    private void Redo(object sender, RoutedEventArgs e) => Run(() => { RequireModel(); editor.Redo(); UpdateModelStatus(); });
    private void OpenDemo(object sender, RoutedEventArgs e) => Run(() =>
    {
        var demo = Path.Combine(settingsDirectory, "demo", Guid.NewGuid().ToString("N"), "demo.bim");
        Directory.CreateDirectory(Path.GetDirectoryName(demo)!);
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", "pass1-demo.bim"), demo);
        GoTo("Model"); editor.Open(demo); Log("Opened a private working copy of the demo model.");
    });

    private void PreviewAction(object sender, RoutedEventArgs e) => Run(() =>
    {
        RequireModel();
        // Recompute from current options before showing an approval, so editing a field after
        // Scan cannot apply an old plan. Apply still validates ownership and fingerprint.
        scannedAction = BuildActionPreview();
        ReviewPreview(scannedAction);
    });
    private void ReviewPreview(ChangePreview preview)
    {
        ActionPreviewGrid.ItemsSource = preview.Changes;
        if (PreviewDialog.Show(this, preview.Action.Name, preview.Action.Risk + " · " + preview.Action.Description + "\n" + string.Join("\n", preview.Notices),
            preview.Changes.Select(c => new PreviewRow(c.ObjectPath, c.Property, c.Before, c.After, c.Reason)).ToArray(), preview.CanApply, "Apply local changes"))
        {
            var result = automation!.Apply(preview);
            RecordAction(preview, result);
            Log(result.Message + " Use Undo to restore the preceding local state.");
            ValidationStatus.Text = "Action applied and validated";
            UpdateModelStatus();
        }
        else if (!preview.CanApply && preview.FocusObject != null) { editor.Select(preview.FocusObject); GoTo("Model"); }
    }
    private void ScanBpa(object sender, RoutedEventArgs e) => Run(() =>
    {
        RequireModel();
        var findings = new BpaService(editor.Handler!, automation!).Scan();
        currentFindings = findings;
        FilterBpa(sender, e);
        UpdateInspector();
        ValidationDetails.Text = $"BPA scan: {findings.Count} findings. SAFE fixes are metadata changes; REVIEW findings require checking model/report intent. Performance changes require separate benchmarks.";
        ValidationStatus.Text = $"BPA companion · {findings.Count} findings";
        Log($"BPA companion scanned model: {findings.Count} findings. Full upstream BPA is available alongside it.");
    });
    private void NativeBpa(object sender, RoutedEventArgs e) => Run(() => { RequireModel(); editor.ShowNativeBpa(); });
    private void BpaSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BpaGrid.SelectedItem is BpaFinding f) FindingDetails.Text = $"{f.RuleId} · {f.Rule}\n{f.Category} · {f.Risk}\n{f.Reason}\n\nProposed: {f.ProposedChange}\nBefore: {f.Before}\nAfter: {f.After}\n\nSource: {f.Source}\n{(f.FixPreview == null ? "Requires author decision; no automatic fix." : "Safe metadata fix available for preview.")}\nDouble-click the finding to select its object.";
    }
    private void NavigateBpa(object sender, MouseButtonEventArgs e) => Run(() => { if (BpaGrid.SelectedItem is BpaFinding f) { editor.Select(f.Object); GoTo("Model"); } });
    private void PreviewBpaFix(object sender, RoutedEventArgs e) => Run(() =>
    {
        RequireModel();
        if (BpaGrid.SelectedItem is not BpaFinding f) throw new InvalidOperationException("Select a finding first.");
        if (f.FixPreview == null) throw new InvalidOperationException("This finding requires an author decision. No automatic fix is offered.");
        ReviewPreview(f.FixPreview);
        ScanBpa(sender, e);
    });

    private void UseExpression(object sender, RoutedEventArgs e) => Run(() =>
    {
        RequireModel();
        var expression = editor.ActiveExpression;
        if (string.IsNullOrWhiteSpace(expression)) throw new InvalidOperationException("Select an expression in Model first.");
        scratch.Text = ToQuery(expression, editor.Selection.FirstOrDefault() is CalculatedTable);
    });
    private static string ToQuery(string expression, bool tableExpression = false) => System.Text.RegularExpressions.Regex.IsMatch(expression, @"\A(?:\s|//[^\r\n]*(?:\r?\n|$)|--[^\r\n]*(?:\r?\n|$)|/\*[\s\S]*?\*/)*(EVALUATE|DEFINE)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        ? expression : tableExpression ? "EVALUATE\r\n" + expression + "\r\n" : "EVALUATE\r\n    ROW ( \"Value\",\r\n" + expression + "\r\n    )";
    private void FormatScratch(object sender, RoutedEventArgs e) => Run(() => { scratch.Text = new LocalDaxFormatter().Format(scratch.Text); Log("Formatted DAX locally; expression text stays on this computer."); });
    private void SaveScratch(object sender, RoutedEventArgs e) => Run(() => daxWorkspace!.SaveActive());
    private void LaunchDaxStudio(object sender, RoutedEventArgs e) => Run(async () =>
    {
        var path = await new DaxStudioBridge(daxStudioPath).OpenQueryAsync(scratch.Text, editor.Server, editor.Server == null ? null : editor.Database, Path.Combine(settingsDirectory, "queries"), lifetime.Token);
        DaxHandoffDetails.Text = $"Server: {editor.Server ?? "(offline)"} · Database: {(editor.Server == null ? "(none)" : editor.Database)}\nQuery file: {path}";
        Log("Opened DAX Studio. " + DaxHandoffDetails.Text);
    });
    private void LaunchActiveExpression(object sender, RoutedEventArgs e) => Run(async () =>
    {
        RequireModel();
        if (string.IsNullOrWhiteSpace(editor.ActiveExpression)) throw new InvalidOperationException("Select a DAX expression in the Model editor first.");
        var query = ToQuery(editor.ActiveExpression, editor.Selection.FirstOrDefault() is CalculatedTable);
        var queryPath = await new DaxStudioBridge(daxStudioPath).OpenQueryAsync(query, editor.Server, editor.Server == null ? null : editor.Database, Path.Combine(settingsDirectory, "queries"), lifetime.Token);
        DaxHandoffDetails.Text = $"DAX Studio · Server: {editor.Server ?? "(offline)"} · Database: {(editor.Server == null ? "(none)" : editor.Database)}\nQuery file: {queryPath}";
        Log("Opened the selected expression in DAX Studio. " + DaxHandoffDetails.Text);
    });
    private void ConfigureDaxStudio(object sender, RoutedEventArgs e) => Run(() =>
    {
        var dialog = new OpenFileDialog { Filter = "DAX Studio executable|DaxStudio.exe|Executables|*.exe", FileName = "DaxStudio.exe" };
        if (dialog.ShowDialog(this) != true) return;
        daxStudioPath = dialog.FileName;
        File.WriteAllText(Path.Combine(settingsDirectory, "daxstudio-path.txt"), daxStudioPath);
        RefreshDaxStudioStatus();
    });
    private void RefreshDaxStudioStatus() => DaxStudioStatus.Text = DaxStudioLocator.Discover(daxStudioPath) is string path ? "DAX Studio · available\n" + path : "DAX Studio · not detected\nChoose its executable in DAX.";

    private void ChooseWorkspace(object sender, RoutedEventArgs e) => Run(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Power BI project|*.pbip|Semantic model|*.bim;*.tmdl" };
        if (dialog.ShowDialog(this) == true) { GoTo("PBIP / Git"); await UpdateWorkspaceAsync(dialog.FileName); RememberProject(dialog.FileName); }
    });
    private void RefreshWorkspace(object sender, RoutedEventArgs e) => Run(() => UpdateWorkspaceAsync(workspaceRoot ?? editor.FilePath));
    private async Task UpdateWorkspaceAsync(string? path)
    {
        var token = lifetime.Token;
        var revision = ++statusRevision;
        if (string.IsNullOrWhiteSpace(path))
        {
            semanticWorkspaceRoot = null;
            workspaceRoot = null; GitHeader.Text = "Git · no project"; SourceStatus.Text = "No PBIP workspace";
            WorkspaceDetails.Text = "Open a model from a PBIP project or choose a .pbip file."; GitDetails.Text = ""; GitFiles.ItemsSource = null; return;
        }
        var inventory = await scanner.DetectAsync(path!, token);
        if (revision != statusRevision || token.IsCancellationRequested) return;
        var root = inventory?.Root ?? (Directory.Exists(path) ? path : Path.GetDirectoryName(path));
        var status = await git.GetStatusAsync(root!, inventory?.SemanticModelFolders, token);
        if (revision != statusRevision || token.IsCancellationRequested) return;
        workspaceRoot = root;
        semanticWorkspaceRoot = inventory?.SemanticModelFolders.FirstOrDefault(folder => editor.FilePath?.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true)
            ?? (inventory?.SemanticModelFolders.Count == 1 ? inventory.SemanticModelFolders[0] : null);
        GitHeader.Text = status.Summary;
        SourceStatus.Text = inventory == null ? "No PBIP project detected\n" + status.Summary : $"PBIP · {inventory.Root}\nTMDL · {(inventory.HasTmdl ? "present" : "absent")}\nPBIR · {(inventory.HasPbir ? "present" : "absent")}\n{status.ChangedSemanticFiles.Count} changed semantic files";
        WorkspaceDetails.Text = inventory == null ? "No PBIP project detected at " + root : $"PBIP root: {inventory.Root}\nSemantic folders: {string.Join(", ", inventory.SemanticModelFolders)}\nTMDL: {inventory.HasTmdl} · PBIR: {inventory.HasPbir} · enhanced PBIR: {inventory.HasEnhancedPbir}\n{status.Summary}\n{string.Join("\\n", inventory.Warnings.Concat(status.Warnings))}";
        GitFiles.ItemsSource = status.Changes.OrderBy(c => c.IsSemantic ? 0 : c.Path.IndexOf(".Report/", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 2).ThenBy(c => c.Path).Select(c => new { Area = c.IsSemantic ? "Model" : c.Path.IndexOf(".Report/", StringComparison.OrdinalIgnoreCase) >= 0 ? "Report" : "Project", c.Status, c.Path, c.OriginalPath }).ToArray();
        GitDetails.Text = status.Summary + "\n\n" + string.Join("\n", (inventory?.Warnings ?? Array.Empty<string>()).Concat(status.Warnings)) + "\n\nChanged files:\n" + string.Join("\n", status.Changes.Select(c => $"{c.Status}  {c.Path}{(c.OriginalPath == null ? "" : " ← " + c.OriginalPath)}{(c.IsSemantic ? "  [semantic]" : "")}"));
    }
    private void RefreshDiagram(object sender, RoutedEventArgs e) => Run(DrawDiagram);
    private void DrawDiagram()
    {
        DiagramCanvas.Children.Clear();
        if (editor.Handler == null) return;
        diagram.Render(new SemanticModelService(editor.Handler).GetGraph(), item => { editor.Select(item); GoTo("Model"); });
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.K) { e.Handled = true; ShowCommands(); }
        else if (e.Key == Key.O) { e.Handled = true; commands.Execute(PbiBench.Core.Commands.WorkbenchCommandId.Open); }
        else if (e.Key == Key.S) { e.Handled = true; commands.Execute(PbiBench.Core.Commands.WorkbenchCommandId.Save); }
    }
    private void ShowCommands()
    {
        var entries = Enum.GetValues(typeof(PbiBench.Core.Commands.WorkbenchCommandId)).Cast<PbiBench.Core.Commands.WorkbenchCommandId>().Where(commands.Contains).ToDictionary(id => id.ToString(), id => new Action(() => Run(() => commands.Execute(id))));
        foreach (var page in Navigation.Items.Cast<ListBoxItem>().Select(i => i.Content.ToString()!)) entries["Workspace · " + page] = () => GoTo(page);
        var panel = new DockPanel { Margin = new Thickness(15) };
        var search = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        var list = new ListBox { ItemsSource = entries.Keys.ToArray(), SelectedIndex = 0 }; panel.Children.Add(list);
        var window = new Window { Title = "PbiBench commands", Icon = Icon, Width = 420, Height = 540, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        void Activate() { if (list.SelectedItem is string key) { window.Close(); entries[key](); } }
        search.TextChanged += (_, _) => { list.ItemsSource = entries.Keys.Where(k => k.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToArray(); list.SelectedIndex = 0; };
        list.MouseDoubleClick += (_, _) => Activate();
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Activate(); } if (e.Key == Key.Escape) window.Close(); };
        window.Loaded += (_, _) => search.Focus();
        window.ShowDialog();
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!ready) return;
        if (!smokeMode && !editor.CanClose()) { e.Cancel = true; return; }
        SaveLayout();
        lifetime.Cancel();
        try { File.WriteAllText(Path.Combine(settingsDirectory, "scratch.dax"), scratch.Text); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        editor.Dispose(); daxWorkspace?.Dispose(); lifetime.Dispose(); ready = false;
    }
}
