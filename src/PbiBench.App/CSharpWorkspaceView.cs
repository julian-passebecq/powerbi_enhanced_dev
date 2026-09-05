using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using FastColoredTextBoxNS;
using Microsoft.Win32;
using PbiBench.CSharp.LanguageService;
using Forms = System.Windows.Forms;

namespace PbiBench.App;

/// <summary>WPF document chrome over the already bundled FCTB control. Language assistance cannot execute scripts.</summary>
public sealed class CSharpWorkspaceView : UserControl, IDisposable
{
    private readonly FastColoredTextBox editor = new() { Dock = Forms.DockStyle.Fill, Language = FastColoredTextBoxNS.Language.CSharp, ShowLineNumbers = true, AutoIndent = true, Font = new System.Drawing.Font("Consolas", 11), LeftBracket = '(', RightBracket = ')', LeftBracket2 = '{', RightBracket2 = '}' };
    private readonly TabControl tabs = new() { Height = 33 };
    private readonly TextBlock status = new() { Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox completions = new() { MinWidth = 150, DisplayMemberPath = "Signature" };
    private readonly DataGrid problems = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
    private readonly List<ScriptProblem> problemRows = new();
    private readonly Expander problemsPanel = new() { Header = "Compile / review risks to populate compiler Problems", IsExpanded = true };
    private readonly List<ScriptDocument> documents = new();
    private readonly CSharpLanguageService language = new();
    private readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SemaphoreSlim writes = new(1, 1);
    private Func<IReadOnlyList<AutomationSymbol>> symbols = () => Array.Empty<AutomationSymbol>();
    private string? recoveryPath; private bool loading, restored, disposed;
    private string activeId = "";
    private string? completionSource; private int completionOffset;
    private bool fileOperation;
    public event EventHandler? TextChanged;
    public event Action<GeneratedSnippet>? SemanticSnippetRequested;
    public string Text { get => editor.Text; set => editor.Text = value ?? ""; }
    public bool IsReadOnly { get => editor.ReadOnly; set => editor.ReadOnly = value; }
    public int DocumentCount => documents.Count;
    public WindowsFormsHost NativeView { get; }
    internal FrameworkElement CompilerProblemsView => problemsPanel;
    public System.Drawing.Bitmap Capture()
    { var bitmap = new System.Drawing.Bitmap(Math.Max(1, editor.Width), Math.Max(1, editor.Height)); editor.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); return bitmap; }
    public bool ActiveDirty => documents.First(d => d.Id == activeId).IsDirty;
    internal ScriptDocument ActiveDocument => documents.First(d => d.Id == activeId);
    public CSharpWorkspaceView(string source)
    {
        var root = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bar = new WrapPanel(); top.Children.Add(bar);
        void Button(string text, Func<Task> action) { var b = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(5, 3, 5, 3) }; b.Click += async (_, _) => await Work(action); bar.Children.Add(b); }
        Button("New", () => { NewDocument(); return Task.CompletedTask; }); Button("Open…", OpenAsync); Button("Save", () => SaveAsync(false)); Button("Save as…", () => SaveAsync(true)); Button("Close tab", CloseDocumentAsync);
        Button("Find / replace", () => { editor.ShowReplaceDialog(); return Task.CompletedTask; });
        Button("Comment", () => { editor.InsertLinePrefix("//"); return Task.CompletedTask; }); Button("Uncomment", () => { editor.RemoveLinePrefix("//"); return Task.CompletedTask; });
        Button("Complete", () => { Complete(); return Task.CompletedTask; }); bar.Children.Add(completions);
        Button("Insert member", () => { InsertCompletion(); return Task.CompletedTask; });
        var snippets = new ComboBox { ItemsSource = ScriptSnippets.All, MinWidth = 170, SelectedIndex = 0 }; bar.Children.Add(snippets);
        Button("Insert snippet", () => { if (!editor.ReadOnly && snippets.SelectedItem is ScriptSnippet s) editor.InsertText(s.Source); return Task.CompletedTask; });
        var semantic = new ComboBox { ItemsSource = SemanticSnippets.All, MinWidth = 190, SelectedIndex = 0 }; bar.Children.Add(semantic);
        Button("Generate from selection", () => { if (semantic.SelectedItem is SemanticSnippet s) { var generated = SemanticSnippets.Generate(s, symbols()); status.Text = generated.Reason; if (generated.Enabled) SemanticSnippetRequested?.Invoke(generated); } return Task.CompletedTask; });
        foreach (var pair in new[] { ("Script", "Script"), ("Severity", "Severity"), ("Code", "Code"), ("Line", "Line"), ("Column", "Column"), ("Message", "Message") })
            problems.Columns.Add(new DataGridTextColumn { Header = pair.Item1, Binding = new Binding(pair.Item2), Width = pair.Item1 == "Message" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
        problemsPanel.Content = problems;
        problems.SelectionChanged += (_, _) => { if (problems.SelectedItem is ScriptProblem p) NavigateProblem(p); };
        top.Children.Add(tabs); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); NativeView = new WindowsFormsHost { Child = editor }; root.Children.Add(NativeView); Content = root;
        tabs.SelectionChanged += (_, _) => { if (!loading && tabs.SelectedItem is TabItem tab && tab.Tag is string id) SelectDocument(id); };
        editor.TextChanged += (_, _) => { if (loading) return; var index = documents.FindIndex(d => d.Id == activeId); documents[index] = documents[index] with { Text = editor.Text }; Headers(); ScheduleRecovery(); TextChanged?.Invoke(this, EventArgs.Empty); };
        editor.SelectionChanged += (_, _) => { try { status.Text = language.Signature(Text, editor.SelectionStart) ?? "Ctrl+Space completion · Ctrl+F find · Ctrl+H replace · Ctrl+S save · Ctrl+N new. Snippets insert text only."; } catch (ArgumentException error) { status.Text = error.Message; } };
        editor.KeyDown += async (_, e) =>
        {
            if (!e.Control) return;
            if (e.KeyCode == Forms.Keys.Space) { Complete(); e.Handled = true; }
            else if (e.KeyCode == Forms.Keys.S) { e.Handled = true; await Work(() => SaveAsync(e.Shift)); }
            else if (e.KeyCode == Forms.Keys.N) { e.Handled = true; await Work(() => { NewDocument(); return Task.CompletedTask; }); }
            else if (e.KeyCode == Forms.Keys.O) { e.Handled = true; await Work(OpenAsync); }
        };
        completions.SelectionChanged += (_, _) => { if (completions.SelectedItem is CSharpCompletion item) status.Text = item.Kind + " · " + item.Description; };
        recoveryTimer.Tick += async (_, _) => { recoveryTimer.Stop(); await Work(SaveRecoveryAsync); };
        NewDocument(source);
        Loaded += async (_, _) => await Work(() => RestoreAsync(CancellationToken.None));
    }
    internal async Task RestoreAsync(CancellationToken ct)
    {
        if (restored || recoveryPath == null) return;
        if (!File.Exists(recoveryPath)) { restored = true; return; }
        var saved = await ScriptWorkspaceFiles.LoadRecoveryAsync(recoveryPath, ct); if (disposed) return;
        documents.Clear(); documents.AddRange(saved.Documents); problemRows.Clear(); problems.ItemsSource = problemRows.ToArray(); restored = true; RebuildTabs(saved.ActiveId);
        status.Text = "Recovered detached drafts. Save As or reopen the source file; execution trust has not been restored.";
    }
    public void Configure(string path, Func<IReadOnlyList<AutomationSymbol>> metadata) { recoveryTimer.Stop(); recoveryPath = path; symbols = metadata; restored = !File.Exists(path); }
    public void NewDocument(string source = "")
    {
        if (documents.Count >= 24) throw new InvalidOperationException("Close a script before opening more than 24 tabs.");
        var document = new ScriptDocument(Guid.NewGuid().ToString(), "Script " + (documents.Count + 1) + ".csx", source); documents.Add(document); RebuildTabs(document.Id); ScheduleRecovery();
    }
    internal sealed record ScriptProblem(string DocumentId, string Script, string CompiledSource, CSharpDiagnostic Diagnostic)
    {
        public string Severity => Diagnostic.Severity; public string Code => Diagnostic.Code; public int Line => Diagnostic.Line; public int Column => Diagnostic.Column; public string Message => Diagnostic.Message;
    }
    internal IReadOnlyList<ScriptProblem> Problems => problemRows;
    internal int CaretOffset => editor.SelectionStart;
    internal void SetDiagnostics(IReadOnlyList<CSharpDiagnostic> diagnostics)
    {
        problemRows.RemoveAll(p => p.DocumentId == activeId);
        problemRows.AddRange(diagnostics.Take(1000).Select(d => new ScriptProblem(activeId, ActiveDocument.Name, Text, d)));
        problems.ItemsSource = problemRows.ToArray(); problemsPanel.Visibility = Visibility.Visible;
        problemsPanel.Header = "Compiler Problems · " + diagnostics.Count + " in active script · select to navigate";
    }
    internal bool NavigateProblem(ScriptProblem problem)
    {
        var document = documents.FirstOrDefault(d => d.Id == problem.DocumentId);
        if (document == null || document.Text != problem.CompiledSource) { status.Text = "This diagnostic is stale or its script was closed. Compile the current source again."; return false; }
        SelectDocument(document.Id);
        var offset = 0; var line = 1;
        while (line < Math.Max(1, problem.Line) && offset < Text.Length) { var next = Text.IndexOf('\n', offset); if (next < 0) { offset = Text.Length; break; } offset = next + 1; line++; }
        var end = Text.IndexOf('\n', offset); if (end < 0) end = Text.Length;
        if (end > offset && Text[end - 1] == '\r') end--;
        editor.SelectionStart = (int)Math.Min(end, (long)offset + Math.Max(0L, (long)problem.Column - 1)); editor.SelectionLength = 0;
        editor.DoSelectionVisible(); editor.Focus(); status.Text = problem.Severity + " " + problem.Code + ": " + problem.Message; return true;
    }
    public async Task SaveRecoveryAsync()
    {
        if (recoveryPath == null || !restored) return; var snapshot = new ScriptRecovery(documents.ToArray(), activeId);
        await writes.WaitAsync().ConfigureAwait(false); try { await ScriptWorkspaceFiles.SaveRecoveryAsync(recoveryPath, snapshot, CancellationToken.None).ConfigureAwait(false); } finally { writes.Release(); }
    }
    private async Task OpenAsync()
    {
        var dialog = new OpenFileDialog { Filter = "C# script|*.csx;*.cs" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var document = await ScriptWorkspaceFiles.OpenAsync(dialog.FileName, CancellationToken.None);
        if (documents.Count >= 24) throw new InvalidOperationException("Close a script before opening more than 24 tabs.");
        documents.Add(document); RebuildTabs(document.Id); ScheduleRecovery();
    }
    private async Task SaveAsync(bool saveAs)
    {
        if (fileOperation) return;
        fileOperation = true;
        try
        {
            var document = documents.First(d => d.Id == activeId); var path = saveAs ? null : document.FilePath;
            ScriptFileConflictException? overwrite = null;
            while (true)
            {
                if (path == null)
                {
                    var dialog = new SaveFileDialog { Filter = "C# script|*.csx|C# source|*.cs", FileName = document.Name, OverwritePrompt = false };
                    if (dialog.ShowDialog(Window.GetWindow(this)) != true) return; path = dialog.FileName;
                }
                try
                {
                    var saved = await ScriptWorkspaceFiles.SaveAsync(document, path, CancellationToken.None, overwrite);
                    var index = documents.FindIndex(d => d.Id == document.Id); if (index < 0) return;
                    documents[index] = saved with { Text = documents[index].Text }; Headers(); ScheduleRecovery(); status.Text = "Saved script."; return;
                }
                catch (ScriptFileConflictException conflict)
                {
                    switch (ReviewConflict(conflict))
                    {
                        case "Reload":
                            var reloaded = await ScriptWorkspaceFiles.OpenAsync(path, CancellationToken.None);
                            var index = documents.FindIndex(d => d.Id == document.Id); if (index < 0) return;
                            // Keep any edits made while the reload was reading the file.
                            if (documents[index].Text != document.Text) throw new InvalidOperationException("Script text changed during reload; reload canceled.");
                            documents[index] = reloaded with { Id = document.Id }; RebuildTabs(activeId); ScheduleRecovery(); return;
                        case "Save As": path = null; overwrite = null; break;
                        case "Overwrite": overwrite = conflict; break;
                        default: return;
                    }
                }
            }
        }
        finally { fileOperation = false; }
    }
    private string? ReviewConflict(ScriptFileConflictException conflict)
    {
        var window = new Window { Owner = Window.GetWindow(this), Title = "Script file conflict", Width = 620, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = conflict.FilePath + "\n\nThe destination changed or this draft is detached. Reload discards this draft's text and reads the file. Save As chooses another destination. Overwrite replaces the file with this draft only if the reviewed disk version still matches.", TextWrapping = TextWrapping.Wrap });
        string? choice = null; var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(buttons);
        foreach (var label in new[] { "Reload", "Save As", "Overwrite", "Cancel" })
        {
            var button = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5), IsCancel = label == "Cancel", IsDefault = label == "Cancel", IsEnabled = label != "Reload" || conflict.ObservedHash != null };
            button.Click += (_, _) => { choice = label; window.Close(); }; buttons.Children.Add(button);
        }
        window.ShowDialog(); return choice;
    }
    private async Task CloseDocumentAsync()
    {
        if (ActiveDirty && MessageBox.Show(Window.GetWindow(this), "Discard the unsaved text in this script tab?", "Close script", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        documents.RemoveAll(d => d.Id == activeId); problemRows.RemoveAll(p => p.DocumentId == activeId); problems.ItemsSource = problemRows.ToArray();
        if (documents.Count == 0) NewDocument(); else RebuildTabs(documents[0].Id); await SaveRecoveryAsync();
    }
    private void RebuildTabs(string id)
    {
        loading = true; try { tabs.Items.Clear(); foreach (var d in documents) tabs.Items.Add(new TabItem { Tag = d.Id, Header = d.Name }); } finally { loading = false; }
        SelectDocument(id);
    }
    private void SelectDocument(string id)
    {
        loading = true; try { activeId = id; editor.ReadOnly = false; editor.Text = documents.First(d => d.Id == id).Text; tabs.SelectedItem = tabs.Items.Cast<TabItem>().First(t => (string)t.Tag == id); Headers(); } finally { loading = false; }
        TextChanged?.Invoke(this, EventArgs.Empty); ScheduleRecovery();
    }
    private void Headers() { foreach (TabItem tab in tabs.Items) { var d = documents.First(v => v.Id == (string)tab.Tag); tab.Header = d.Name + (d.IsRecovered ? " [Recovered · detached]" : "") + (d.IsDirty ? " •" : ""); tab.ToolTip = d.IsRecovered ? "Advisory source: " + (d.RecoveredFrom ?? "unsaved draft") : d.FilePath; } }
    private void Complete() { completionSource = Text; completionOffset = editor.SelectionStart; completions.ItemsSource = language.Complete(completionSource, completionOffset, symbols()); completions.SelectedIndex = 0; completions.IsDropDownOpen = true; }
    private void InsertCompletion()
    {
        if (editor.ReadOnly || completions.SelectedItem is not CSharpCompletion item) return;
        if (completionSource != Text || completionOffset != editor.SelectionStart) { Complete(); return; }
        var end = editor.SelectionStart; var start = end; while (start > 0 && (char.IsLetterOrDigit(Text[start - 1]) || Text[start - 1] == '_')) start--;
        if (item.ReplaceStart.HasValue) { start = item.ReplaceStart.Value; end = start + item.ReplaceLength; }
        if (start < 0 || end > Text.Length) { Complete(); return; }
        editor.SelectionStart = start; editor.SelectionLength = end - start; editor.InsertText(item.Text); editor.Focus();
    }
    private void ScheduleRecovery() { if (!disposed) { recoveryTimer.Stop(); recoveryTimer.Start(); } }
    private async Task Work(Func<Task> action) { try { await action(); } catch (Exception error) { status.Text = error.Message; } }
    public void Dispose() { if (disposed) return; recoveryTimer.Stop(); SaveRecoveryAsync().GetAwaiter().GetResult(); disposed = true; editor.Dispose(); }
}
