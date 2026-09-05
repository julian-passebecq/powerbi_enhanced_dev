using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PbiBench.Core.Compiler;
using PbiBench.Core.Packages;
using PbiBench.Core.Tasks;
using PbiBench.Semantic.Compiler;
using PbiBench.Semantic.ModelAuthoring;
using PbiBench.Semantic.Packages;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

/// <summary>Original, explicitly bounded local prototypes. All model changes use the existing review.</summary>
public sealed class SemanticPrototypeView : UserControl, IDisposable
{
    public const string SampleYaml = "version: 1.1\nsource: example.sales.orders\ncomment: Review the source mapping before creating measures.\nfields:\n  - name: Quantity\n    expr: Quantity\nmeasures:\n  - name: Imported quantity\n    expr: SUM(Quantity)\n  - name: Imported rows\n    expr: COUNT(*)\n";
    private readonly Func<TabularModelHandler?> currentHandler; private readonly Action changed; private readonly BackgroundTaskQueue queue; private readonly bool ownsQueue;
    private readonly TabControl tools = new(); private readonly TextBox yaml = Editor(), ir = Editor(true), packageDetails = Editor(true), lockDetails = Editor(true);
    private readonly TextBox packageFolder = new() { MinWidth = 300, Margin = new Thickness(4) };
    private readonly ComboBox table = new() { MinWidth = 180, Margin = new Thickness(4) }, installed = new() { MinWidth = 190, Margin = new Thickness(4) };
    private readonly DataGrid diagnostics = new() { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, EnableRowVirtualization = true, Margin = new Thickness(4) };
    private readonly TextBlock status = Note("Local prototypes · no remote feed, source SQL execution or automatic deployment.");
    private int compilationVersion, packageVersion; private bool disposed; private TabularModelHandler? owner;
    public SemanticCompilation? LastCompilation { get; private set; }
    public LocalDaxPackage? LastPackage { get; private set; }
    public AuthoringPreview? LastPreview { get; private set; }
    public SemanticPrototypeView(Func<TabularModelHandler?> currentHandler, Action changed, BackgroundTaskQueue? backgroundTasks = null, string? settingsDirectory = null)
    {
        this.currentHandler = currentHandler; this.changed = changed; queue = backgroundTasks ?? new BackgroundTaskQueue(); ownsQueue = backgroundTasks == null;
        var root = new DockPanel(); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(tools); Content = root;
        var compiler = new DockPanel(); var compilerTop = new StackPanel(); DockPanel.SetDock(compilerTop, Dock.Top); compiler.Children.Add(compilerTop);
        compilerTop.Children.Add(Note("SEMANTIC COMPILER PROTOTYPE · Import bounded Metric View YAML into reviewable intent. SQL and DAX semantics are not assumed equivalent. Unsupported semantics block measure proposals; the complete YAML remains in exported IR."));
        compilerTop.Children.Add(Bar(Button("Open YAML…", OpenYamlAsync), Button("Compile intent", () => CompileAsync(yaml.Text)), Button("Export IR JSON…", () => ExportAsync(RequireCompilation().ToJson(), "semantic-intent.json")), Note("Map source to existing table:"), table, Button("Review measure proposals", () => ReviewAsync(PreviewMeasures))));
        var outputs = new TabControl(); outputs.Items.Add(new TabItem { Header = "Intent JSON", Content = ir }); outputs.Items.Add(new TabItem { Header = "Diagnostics", Content = diagnostics });
        compiler.Children.Add(Split(yaml, outputs)); tools.Items.Add(new TabItem { Header = "Semantic compiler", Content = compiler }); yaml.Text = SampleYaml;
        yaml.TextChanged += (_, _) => { compilationVersion++; LastCompilation = null; LastPreview = null; };
        var packages = new DockPanel(); var packageTop = new StackPanel(); DockPanel.SetDock(packageTop, Dock.Top); packages.Children.Add(packageTop);
        packageTop.Children.Add(Note("LOCAL DAX PACKAGE PROTOTYPE · Read pbibench.package.json and hash-pinned .dax function bodies from a local folder. Review version, license, dependencies and every function before installation. No installer scripts or arbitrary code are run."));
        packageTop.Children.Add(Bar(packageFolder, Button("Browse folder…", BrowsePackageAsync), Button("Read package", () => LoadPackageAsync(packageFolder.Text)), Button("Review install / update", () => ReviewAsync(PreviewInstall))));
        packageTop.Children.Add(Bar(Note("Installed package:"), installed, Button("Review removal", () => ReviewAsync(PreviewRemove)), Button("Export lock JSON…", () => ExportAsync(Service().CaptureLock().ToJson(), "pbibench.packages.lock.json"))));
        packages.Children.Add(Split(packageDetails, lockDetails)); tools.Items.Add(new TabItem { Header = "DAX packages", Content = packages });
        packageFolder.TextChanged += (_, _) => { packageVersion++; LastPackage = null; LastPreview = null; };
        RefreshModel();
    }
    public void ShowTool(string tool) { tools.SelectedIndex = tool switch { "Semantic compiler" => 0, "DAX packages" => 1, _ => throw new ArgumentException("Unknown semantic prototype tool.", nameof(tool)) }; }
    public void SelectTargetTable(string name)
    {
        if (!Handler().Model.Tables.Any(item => item.Name == name)) throw new ArgumentException("Select an existing model table.", nameof(name));
        RefreshModel(); table.SelectedItem = name; LastPreview = null;
    }
    public void RefreshModel()
    {
        if (disposed) return; var handler = currentHandler(); if (!ReferenceEquals(owner, handler)) { LastPreview = null; owner = handler; }
        var selectedTable = table.SelectedItem as string; table.ItemsSource = handler?.Model.Tables.Select(item => item.Name).ToArray() ?? Array.Empty<string>(); table.SelectedItem = selectedTable; if (table.SelectedIndex < 0 && table.Items.Count > 0) table.SelectedIndex = 0;
        var selectedPackage = installed.SelectedItem as string;
        try { var state = handler == null ? new DaxPackageLock() : Service().CaptureLock(); installed.ItemsSource = state.Packages.Select(item => item.Id).ToArray(); lockDetails.Text = "MODEL PACKAGE LOCK · Export explicitly for a Git-reviewed artifact. Native Undo also restores this record.\n\n" + state.ToJson(); }
        catch (Exception error) { installed.ItemsSource = Array.Empty<string>(); lockDetails.Text = "Package lock needs attention: " + error.Message; }
        installed.SelectedItem = selectedPackage; if (installed.SelectedIndex < 0 && installed.Items.Count > 0) installed.SelectedIndex = 0;
    }
    public async Task CompileAsync(string source)
    {
        if (disposed) return; yaml.Text = source; var version = ++compilationVersion; status.Text = "Compiling bounded semantic intent…";
        var result = await queue.Enqueue("Compile metric-view intent", context => { context.CancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new MetricViewCompiler().Compile(source)); }).Completion;
        if (disposed || version != compilationVersion) return; LastCompilation = result; LastPreview = null; ir.Text = result.ToJson(); diagnostics.ItemsSource = result.Diagnostics;
        status.Text = result.CanProposeMetadata ? "Intent compiled. Map its source to an existing table and review the aggregate proposals. Validate data semantics before deployment." : "Intent exported with diagnostics. Unsupported or incomplete semantics block model proposals.";
    }
    public async Task LoadPackageAsync(string directory)
    {
        if (disposed) return; packageFolder.Text = directory; var version = ++packageVersion; LastPackage = null; LastPreview = null; status.Text = "Reading local manifest and verifying file hashes…";
        var package = await queue.Enqueue("Review local DAX package", context => new LocalDaxPackageReader().ReadAsync(directory, context.CancellationToken)).Completion;
        if (disposed || version != packageVersion) return; LastPackage = package;
        packageDetails.Text = "PACKAGE " + package.Manifest.Id + " " + package.Manifest.Version + "\nLICENSE " + package.Manifest.License + "\nCONTENT SHA-256 " + package.ContentHash + "\n\n" + package.Manifest.Description + "\n\nDEPENDENCIES\n" +
            string.Join("\n", package.Manifest.Dependencies.Select(item => item.Id + " " + item.Version + " · " + item.Sha256)) + "\n\nCAPTURED FUNCTIONS\n" +
            string.Join("\n\n", package.Manifest.Functions.Select(item => item.Name + " · " + item.Path + "\nSHA-256 " + item.Sha256 + "\n" + item.Description + "\n" + package.Functions[item.Name]));
        status.Text = "Captured " + package.Manifest.Functions.Count + " hash-verified UDF bodies. Review installation to validate compatibility, ownership and exact dependency pins.";
    }
    public AuthoringPreview PreviewMeasures() => LastPreview = new SemanticCompilerService(Handler()).Preview(RequireCompilation(), table.SelectedItem as string ?? "");
    public AuthoringPreview PreviewInstall() => LastPreview = Service().PreviewInstall(LastPackage ?? throw new InvalidOperationException("Read a local package first."));
    public AuthoringPreview PreviewRemove() => LastPreview = Service().PreviewRemove(installed.SelectedItem as string ?? throw new InvalidOperationException("Select an installed package."));
    private Task ReviewAsync(Func<AuthoringPreview> prepare) { if (AuthoringReview.Show(this, prepare(), currentHandler, changed)) { RefreshModel(); status.Text = "Reviewed local metadata applied in one native Undo batch. Save/deploy and lock-file export remain explicit actions."; } return Task.CompletedTask; }
    private async Task OpenYamlAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "YAML (*.yaml;*.yml)|*.yaml;*.yml", CheckFileExists = true }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return; var path = dialog.FileName; var version = compilationVersion;
        var source = await queue.Enqueue("Read metric-view YAML", context => Task.Run(() => { context.CancellationToken.ThrowIfCancellationRequested(); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); if (stream.Length > 1024 * 1024) throw new InvalidDataException("YAML input is limited to 1 MiB."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }, context.CancellationToken)).Completion;
        if (!disposed && version == compilationVersion) await CompileAsync(source);
    }
    private async Task BrowsePackageAsync() { using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a local PbiBench DAX package folder" }; if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) await LoadPackageAsync(dialog.SelectedPath); }
    private async Task ExportAsync(string content, string fileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = fileName, Filter = "JSON (*.json)|*.json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = true }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return; var path = dialog.FileName;
        await queue.Enqueue("Export reviewed prototype artifact", context => Task.Run(() => { context.CancellationToken.ThrowIfCancellationRequested(); File.WriteAllText(path, content, new System.Text.UTF8Encoding(false)); return true; }, context.CancellationToken)).Completion;
        if (!disposed) status.Text = "Exported " + path + ". Review its Git diff with the project tools.";
    }
    private SemanticCompilation RequireCompilation() => LastCompilation ?? throw new InvalidOperationException("Compile the current YAML first.");
    private TabularModelHandler Handler() => currentHandler() ?? throw new InvalidOperationException("Open a model for a reviewed metadata proposal.");
    private DaxPackageService Service() => new(Handler());
    private Button Button(string text, Func<Task> action) { var button = new Button { Content = text, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(3) }; button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception error) { if (!disposed) status.Text = error.Message; } finally { if (!disposed) button.IsEnabled = true; } }; return button; }
    private static TextBox Editor(bool readOnly = false) => new() { IsReadOnly = readOnly, AcceptsReturn = true, AcceptsTab = !readOnly, FontFamily = new FontFamily("Consolas"), FontSize = 13, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(5) };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
    private static WrapPanel Bar(params UIElement[] children) { var result = new WrapPanel(); foreach (var child in children) result.Children.Add(child); return result; }
    private static Grid Split(UIElement left, UIElement right) { var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(5) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.Children.Add(left); var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(splitter, 1); grid.Children.Add(splitter); Grid.SetColumn(right, 2); grid.Children.Add(right); return grid; }
    public void Dispose() { if (disposed) return; disposed = true; compilationVersion++; packageVersion++; if (ownsQueue) queue.Dispose(); }
}
