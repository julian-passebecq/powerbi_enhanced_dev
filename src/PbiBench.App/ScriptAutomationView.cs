using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PbiBench.Core.Automation;
using PbiBench.Core.Tasks;
using PbiBench.ModelEditor;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

/// <summary>Safe interpreted model edits and explicitly unrestricted legacy scripts are separate product flows.</summary>
public sealed class ScriptAutomationView : UserControl, IDisposable
{
    private const string Example = "// Safe Preview interprets this approved model-edit subset.\nforeach (var m in Model.AllMeasures)\n{\n    m.DisplayFolder = \"Finance\";\n    m.Description = \"Measure: \" + m.Name;\n}";
    private readonly Func<TabularModelHandler?> currentHandler;
    private readonly Func<IReadOnlyList<TabularNamedObject>> selection;
    private readonly Action changed;
    private readonly BackgroundTaskQueue queue;
    private readonly bool ownsQueue;
    private readonly TabControl tabs = new();
    private readonly TextBox safeSource = Editor(Example), trustedSource = Editor("// Trusted Legacy: unrestricted existing TE2 C# scripts.\n// Review every line before explicitly opting in and running.\n"), trustedOutput = Editor("", true), recipeSource = Editor("", true), macroSource = Editor("", true);
    private readonly DataGrid diff = Grid(), recipeSteps = Grid(), macros = Grid();
    private readonly TextBlock status = Note("Open a model, then preview supported edits on detached metadata."), safeNotice = Note("Safe Preview: interpreted model-edit subset. No C# compilation or file/network/process API exists in this interpreter.");
    private readonly CheckBox trust = new() { Content = "I trust this script and permit its unrestricted file, network and process effects.", Margin = new Thickness(6) };
    private readonly TextBox recipeName = new() { Text = "Recorded model actions", MinWidth = 240 }, macroName = new() { Text = "My macro", MinWidth = 210 };
    private readonly ActionRecorder recorder = new();
    private readonly List<ScriptMacro> library = new();
    private readonly string libraryPath, snapshotDirectory;
    private TabularModelHandler? handler;
    private ActionRecipe? recipeDraft;
    private RecordedActionRecipe? recorded;
    private CancellationTokenSource? pending;
    private int draftVersion;
    private bool disposed, loadingSource, initialized;
    public AuthoringPreview? LastPreview { get; private set; }

    public ScriptAutomationView(Func<TabularModelHandler?> currentHandler, Func<IReadOnlyList<TabularNamedObject>> selection, Action changed, BackgroundTaskQueue? backgroundTasks = null, string? settingsDirectory = null)
    {
        this.currentHandler = currentHandler; this.selection = selection; this.changed = changed; queue = backgroundTasks ?? new BackgroundTaskQueue(); ownsQueue = backgroundTasks == null;
        var profileDirectory = Path.GetFullPath(settingsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PbiBench"));
        libraryPath = Path.Combine(profileDirectory, "macros.json"); snapshotDirectory = Path.Combine(profileDirectory, "TrustedScriptSnapshots");
        var root = new DockPanel(); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(tabs); Content = root;
        tabs.Items.Add(new TabItem { Header = "Safe C# Preview", Content = SafePage() }); tabs.Items.Add(new TabItem { Header = "Trusted Legacy", Content = TrustedPage() }); tabs.Items.Add(new TabItem { Header = "Action recorder", Content = RecorderPage() }); tabs.Items.Add(new TabItem { Header = "Macro library", Content = MacroPage() });
        safeSource.TextChanged += (_, _) => { if (!loadingSource) { draftVersion++; LastPreview = null; diff.ItemsSource = null; pending?.Cancel(); } };
        trustedSource.TextChanged += (_, _) => trust.IsChecked = false;
        macros.SelectionChanged += (_, _) => { if (macros.SelectedItem is ScriptMacro macro) macroSource.Text = macro.Mode == MacroMode.Recipe ? JsonSerializer.Serialize(macro.Recipe, new JsonSerializerOptions { WriteIndented = true }) : macro.Source; };
        Loaded += async (_, _) => { if (initialized) return; initialized = true; try { if (File.Exists(libraryPath)) { var saved = await RecipeFiles.LoadLibraryAsync(libraryPath, CancellationToken.None); if (!disposed) { library.AddRange(saved.Macros); RefreshLibrary(); } } } catch (Exception error) { status.Text = "Macro library could not be loaded: " + error.Message; } };
        RefreshModel();
    }
    public void ShowTool(string tool)
    { tabs.SelectedItem = tabs.Items.Cast<TabItem>().FirstOrDefault(tab => string.Equals(Convert.ToString(tab.Header), tool, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Unknown script tool: " + tool, nameof(tool)); }
    public void RefreshModel()
    {
        if (disposed) return; var next = currentHandler();
        if (!ReferenceEquals(handler, next)) { pending?.Cancel(); recorder.Discard(); recorded = null; recipeSteps.ItemsSource = null; recipeSource.Text = ""; trust.IsChecked = false; draftVersion++; }
        handler = next; LastPreview = null; diff.ItemsSource = null;
        if (handler == null) status.Text = "Open a semantic model before previewing or recording model actions.";
    }
    public async Task PrepareSafePreviewAsync(string source)
    {
        ShowTool("Safe C# Preview"); recipeDraft = null; safeSource.IsReadOnly = false; safeSource.Text = source; await PreparePreviewAsync();
    }
    private async Task PreparePreviewAsync()
    {
        var active = Handler(); var version = draftVersion; var service = new ScriptPreviewService(active);
        var captured = recipeDraft == null ? service.PrepareScript(safeSource.Text, selection()) : service.PrepareRecipe(recipeDraft, selection());
        pending?.Cancel(); var cancellation = pending = new CancellationTokenSource(); LastPreview = null; diff.ItemsSource = null;
        status.Text = "Computing the model diff on detached metadata…";
        try
        {
            var job = queue.Enqueue("Safe script preview", context => { context.Report(10, "Interpreting approved edits on detached metadata"); return service.ComputeAsync(captured, context.CancellationToken); }, cancellation.Token);
            var computed = await job.Completion;
            if (disposed || version != draftVersion || !ReferenceEquals(active, currentHandler())) return;
            LastPreview = service.Materialize(computed); diff.ItemsSource = LastPreview.Changes;
            safeNotice.Text = "Detached preview: " + LastPreview.Changes.Count + " exact changes. " + string.Join(" ", LastPreview.Issues.Select(issue => issue.Message)); status.Text = "Review the before/after rows, then use Review / apply for the local undo transaction.";
        }
        finally { cancellation.Dispose(); if (ReferenceEquals(pending, cancellation)) pending = null; }
    }
    private UIElement SafePage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(safeNotice); top.Children.Add(Bar(Button("Preview", PreparePreviewAsync), Button("Cancel", () => pending?.Cancel()), Button("Review / apply…", () => { var preview = LastPreview ?? throw new InvalidOperationException("Prepare a current preview first."); if (AuthoringReview.Show(this, preview, currentHandler, changed)) { LastPreview = null; diff.ItemsSource = null; status.Text = "Reviewed changes applied locally. Native Undo restores the batch."; } }),
            Button("Example / script mode", () => { recipeDraft = null; safeSource.IsReadOnly = false; safeSource.Text = Example; }), Button("Open C#…", OpenSafeAsync), Button("Save C#…", SaveSafeAsync), Button("Open recipe…", OpenRecipeAsync)));
        top.Children.Add(Note("Supported: literal property assignments; Name/Table.Name concatenation; foreach over approved Model/Selected tables, columns or measures; explicit Model.Tables[\"T\"] indexing; AddMeasure and measure Delete. Unsupported C# is rejected in full."));
        panel.Children.Add(Split(safeSource, diff)); return panel;
    }
    private UIElement TrustedPage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var warning = Note("TRUSTED LEGACY — unrestricted C# through the existing TE2 compiler. File, network and process effects cannot be previewed or undone. PbiBench creates a model snapshot before running; model Undo is available only where the script preserves native undo state. Execution uses the native UI thread and cannot be forcibly canceled."); warning.Foreground = Brushes.Firebrick; warning.FontWeight = FontWeights.SemiBold; top.Children.Add(warning); top.Children.Add(trust);
        top.Children.Add(Bar(Button("Snapshot and run trusted script", RunTrustedAsync), Button("Open legacy C#…", async () => { var text = await ReadSourceAsync(); if (text != null) trustedSource.Text = text; }), Button("Save legacy C#…", async () => await WriteSourceAsync(trustedSource.Text, "legacy.csx"))));
        panel.Children.Add(Split(trustedSource, trustedOutput)); return panel;
    }
    private UIElement RecorderPage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Note("Start a checkpoint, perform supported model edits in PbiBench or native TE2, then stop. The recorder tracks object identity and produces typed property/rename/measure-create/delete operations. It does not record UI gestures. Unsupported metadata changes are reported."));
        top.Children.Add(Bar(recipeName, Button("Start recording", () => { recorder.Start(Handler()); status.Text = "Recording model changes. Return here and stop when the operation is complete."; }), Button("Stop / generate recipe", StopRecordingAsync), Button("Discard recording", () => { recorder.Discard(); status.Text = "Recording discarded."; }), Button("Review recipe", () => { UseRecipe(recorded?.Recipe ?? throw new InvalidOperationException("Record or load a recipe first.")); }), Button("Save recipe…", SaveRecipeAsync)));
        panel.Children.Add(Split(recipeSteps, recipeSource)); return panel;
    }
    private UIElement MacroPage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Note("Local macros keep their explicit Safe Script, Typed Recipe or Trusted Legacy mode. Loading a macro never executes it, and loading trusted code resets its trust acknowledgment."));
        top.Children.Add(Bar(macroName, Button("Save safe draft", () => SaveMacroAsync(MacroMode.SafeScript)), Button("Save recorded recipe", () => SaveMacroAsync(MacroMode.Recipe)), Button("Save trusted draft", () => SaveMacroAsync(MacroMode.TrustedLegacy)), Button("Load selected", LoadMacro), Button("Remove selected", RemoveMacroAsync), Button("Export library…", ExportLibraryAsync), Button("Import library…", ImportLibraryAsync)));
        macros.AutoGeneratingColumn += (_, e) => { if (e.PropertyName is "Source" or "Recipe" or "Id") e.Cancel = true; }; panel.Children.Add(Split(macros, macroSource)); return panel;
    }
    private async Task RunTrustedAsync()
    {
        if (trust.IsChecked != true) throw new InvalidOperationException("Review the source and explicitly acknowledge unrestricted access before running.");
        var active = Handler(); var source = trustedSource.Text; status.Text = "Writing the pre-run model snapshot…";
        var ticket = await TrustedScriptRunner.PrepareAsync(active, source, selection(), snapshotDirectory, CancellationToken.None);
        if (disposed || source != trustedSource.Text || !ReferenceEquals(active, currentHandler()) || trust.IsChecked != true) throw new InvalidOperationException("The trusted source/session changed while preparing the snapshot. Review again.");
        status.Text = "Running unrestricted legacy C#. Native UI-thread execution cannot be canceled.";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var result = TrustedScriptRunner.Run(ticket, active, true); trust.IsChecked = false;
        trustedOutput.Text = (result.Succeeded ? "Execution completed." : "Execution failed.") + "\nSnapshot: " + result.SnapshotPath + "\nModel Undo available: " + result.UndoAvailable + "\n\n" + string.Join("\n", result.Diagnostics) + "\n\nConsole output\n" + result.ConsoleOutput;
        status.Text = "Trusted run finished. Snapshot and diagnostics are shown below; external effects are not reversible by PbiBench."; changed();
    }
    private async Task StopRecordingAsync()
    {
        var active = Handler(); var prepared = recorder.PrepareStop(active, recipeName.Text.Trim());
        var job = queue.Enqueue("Generate recorded action recipe", context => ActionRecorder.ComputeAsync(prepared, context.CancellationToken)); var result = await job.Completion;
        if (disposed || !ReferenceEquals(active, currentHandler())) return; recorded = result; recipeSteps.ItemsSource = result.Recipe.Steps; recipeSource.Text = JsonSerializer.Serialize(result.Recipe, new JsonSerializerOptions { WriteIndented = true }); status.Text = result.Recipe.Steps.Count + " supported operations. " + string.Join(" ", result.Notices);
    }
    private void UseRecipe(ActionRecipe recipe)
    {
        ActionRecipeRules.Validate(recipe); ShowTool("Safe C# Preview"); loadingSource = true; try { recipeDraft = recipe; safeSource.IsReadOnly = true; safeSource.Text = JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true }); } finally { loadingSource = false; }
        draftVersion++; LastPreview = null; diff.ItemsSource = null; safeNotice.Text = "Typed recipe mode. Preview interprets these explicit operations; Example / script mode returns to editable C# subset source.";
    }
    private async Task OpenRecipeAsync() { var dialog = new OpenFileDialog { Filter = "PbiBench action recipe|*.pbiaction;*.json|All files|*.*" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) UseRecipe(await RecipeFiles.LoadRecipeAsync(dialog.FileName, CancellationToken.None)); }
    private async Task SaveRecipeAsync() { var recipe = recorded?.Recipe ?? recipeDraft ?? throw new InvalidOperationException("Record or load a recipe first."); var dialog = new SaveFileDialog { Filter = "PbiBench action recipe|*.pbiaction", FileName = "model-actions.pbiaction" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) await RecipeFiles.SaveRecipeAsync(dialog.FileName, recipe, CancellationToken.None); }
    private async Task OpenSafeAsync() { var source = await ReadSourceAsync(); if (source != null) { recipeDraft = null; safeSource.IsReadOnly = false; safeSource.Text = source; } }
    private async Task SaveSafeAsync() { if (recipeDraft != null) { await SaveRecipeAsync(); return; } await WriteSourceAsync(safeSource.Text, "safe-model-edits.csx"); }
    private async Task<string?> ReadSourceAsync()
    {
        var dialog = new OpenFileDialog { Filter = "C# scripts|*.csx;*.cs|All files|*.*" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return null;
        if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new InvalidOperationException("Script files are limited to 1 MB."); using var reader = File.OpenText(dialog.FileName); return await reader.ReadToEndAsync();
    }
    private async Task WriteSourceAsync(string source, string name)
    { var dialog = new SaveFileDialog { Filter = "C# script|*.csx", FileName = name }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return; await PbiBench.Dax.LanguageService.DaxScriptFile.SaveAsync(dialog.FileName, source, CancellationToken.None); }
    private async Task SaveMacroAsync(MacroMode mode)
    {
        var name = macroName.Text.Trim(); if (name.Length == 0 || name.Length > 128) throw new InvalidOperationException("Enter a macro name up to 128 characters.");
        if (library.Count >= 256) throw new InvalidOperationException("Remove a macro before adding more than 256 entries.");
        var recipe = mode == MacroMode.Recipe ? recorded?.Recipe ?? recipeDraft ?? throw new InvalidOperationException("Record or load a recipe first.") : null;
        if (mode == MacroMode.SafeScript && recipeDraft != null) throw new InvalidOperationException("The safe draft contains a typed recipe. Save it as a recorded recipe macro.");
        var macro = new ScriptMacro(Guid.NewGuid().ToString(), name, mode, mode == MacroMode.TrustedLegacy ? trustedSource.Text : mode == MacroMode.SafeScript ? safeSource.Text : "", recipe);
        var updated = library.Concat(new[] { macro }).ToArray(); await RecipeFiles.SaveLibraryAsync(libraryPath, new MacroLibrary(updated), CancellationToken.None); library.Add(macro); RefreshLibrary(); status.Text = "Saved " + mode + " macro locally. It has not been executed.";
    }
    private void LoadMacro()
    {
        var macro = macros.SelectedItem as ScriptMacro ?? throw new InvalidOperationException("Select a macro first.");
        if (macro.Mode == MacroMode.Recipe) UseRecipe(macro.Recipe!);
        else if (macro.Mode == MacroMode.TrustedLegacy) { ShowTool("Trusted Legacy"); trustedSource.Text = macro.Source; trust.IsChecked = false; }
        else { ShowTool("Safe C# Preview"); recipeDraft = null; safeSource.IsReadOnly = false; safeSource.Text = macro.Source; }
        status.Text = "Loaded " + macro.Name + " as " + macro.Mode + ". Review before any preview or run.";
    }
    private async Task RemoveMacroAsync() { var macro = macros.SelectedItem as ScriptMacro ?? throw new InvalidOperationException("Select a macro first."); var updated = library.Where(item => item.Id != macro.Id).ToArray(); await RecipeFiles.SaveLibraryAsync(libraryPath, new MacroLibrary(updated), CancellationToken.None); library.Remove(macro); RefreshLibrary(); }
    private async Task ExportLibraryAsync() { var dialog = new SaveFileDialog { Filter = "PbiBench macro library|*.json", FileName = "pbibench-macros.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) await RecipeFiles.SaveLibraryAsync(dialog.FileName, new MacroLibrary(library.ToArray()), CancellationToken.None); }
    private async Task ImportLibraryAsync()
    {
        var dialog = new OpenFileDialog { Filter = "PbiBench macro library|*.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return; var imported = await RecipeFiles.LoadLibraryAsync(dialog.FileName, CancellationToken.None);
        var merged = library.Concat(imported.Macros.Where(item => library.All(existing => existing.Id != item.Id))).ToArray(); await RecipeFiles.SaveLibraryAsync(libraryPath, new MacroLibrary(merged), CancellationToken.None); library.Clear(); library.AddRange(merged); RefreshLibrary(); status.Text = "Imported macros; nothing was executed. Each entry retains its explicit mode.";
    }
    private void RefreshLibrary() { macros.ItemsSource = library.ToArray(); }
    private TabularModelHandler Handler() => handler ?? throw new InvalidOperationException("Open a semantic model first.");
    private Button Button(string title, Action action) => Button(title, () => { action(); return Task.CompletedTask; });
    private Button Button(string title, Func<Task> action) { var button = new Button { Content = title, Margin = new Thickness(3), Padding = new Thickness(8, 4, 8, 4) }; button.Click += async (_, _) => { try { button.IsEnabled = false; await action(); } catch (OperationCanceledException) { status.Text = "Canceled."; } catch (Exception error) { status.Text = error.Message; } finally { if (!disposed) button.IsEnabled = true; } }; return button; }
    private static WrapPanel Bar(params UIElement[] controls) { var panel = new WrapPanel { Margin = new Thickness(3) }; foreach (var control in controls) panel.Children.Add(control); return panel; }
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private static TextBox Editor(string text, bool readOnly = false) => new() { Text = text, IsReadOnly = readOnly, AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(4) };
    private static DataGrid Grid() => new() { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, Margin = new Thickness(4), SelectionMode = DataGridSelectionMode.Single };
    private static UIElement Split(UIElement first, UIElement second) { var grid = new System.Windows.Controls.Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); grid.Children.Add(first); var splitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; System.Windows.Controls.Grid.SetRow(splitter, 1); grid.Children.Add(splitter); System.Windows.Controls.Grid.SetRow(second, 2); grid.Children.Add(second); return grid; }
    public void Dispose() { if (disposed) return; disposed = true; pending?.Cancel(); recorder.Discard(); if (ownsQueue) queue.Dispose(); }
}
