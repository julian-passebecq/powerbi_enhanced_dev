using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using FastColoredTextBoxNS;
using PbiBench.Dax.LanguageService;

namespace PbiBench.ModelEditor;

public enum DaxRunScope { All, Selection, CurrentStatement }
public sealed class DaxRunRequestEventArgs(DaxRunScope scope) : EventArgs { public DaxRunScope Scope { get; } = scope; }
public sealed class DaxDefinitionRequestEventArgs(DaxSymbolLocation location, bool peek) : EventArgs
{
    public DaxSymbolLocation Location { get; } = location;
    public bool Peek { get; } = peek;
}
public sealed class DaxReferencesRequestEventArgs(IReadOnlyList<DaxReference> locations) : EventArgs
{
    public IReadOnlyList<DaxReference> Locations { get; } = locations;
}

/// <summary>Native editing with immutable, cancellable PbiBench language analysis.</summary>
public sealed class DaxScratchEditor : IDisposable
{
    private readonly FastColoredTextBox editor;
    private readonly Panel panel;
    private readonly Label status;
    private readonly ToolTip tooltip = new() { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };
    private readonly AutocompleteMenu completion;
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 180 };
    private readonly DaxLanguageService language = new();
    private readonly TextStyle keyword = new(Brushes.MediumBlue, null, FontStyle.Bold);
    private readonly TextStyle literal = new(Brushes.DarkRed, null, FontStyle.Regular);
    private readonly TextStyle comment = new(Brushes.SeaGreen, null, FontStyle.Italic);
    private readonly TextStyle identifier = new(Brushes.DarkSlateBlue, null, FontStyle.Regular);
    private readonly WavyLineStyle error = new(255, Color.Firebrick);
    private readonly WavyLineStyle warning = new(255, Color.DarkGoldenrod);
    private readonly Stack<int> back = new();
    private readonly Stack<int> forward = new();
    private CancellationTokenSource? analysisCancellation;
    private DaxMetadataSnapshot metadata = DaxMetadataSnapshot.Empty;
    private DaxAnalysis? analysis;
    private string documentId = Guid.NewGuid().ToString("N");
    private DaxDocumentKind documentKind = DaxDocumentKind.Query;
    private string? currentTable;
    private int version;
    private bool disposed;
    private bool applyingAnalysis;
    private bool explicitCompletion;
    private int lastTooltipOffset = -1;
    public WindowsFormsHost View { get; }
    public string Text { get => editor.Text; set => editor.Text = value ?? ""; }
    public string SelectedText => editor.SelectedText;
    public int CaretOffset => editor.PlaceToPosition(editor.Selection.Start);
    public int SelectionStart => editor.SelectionStart;
    public int SelectionLength => editor.SelectionLength;
    public DaxDocument Document => new(documentId, Text, version, documentKind, currentTable);
    public DaxAnalysis? LatestAnalysis => analysis;
    public IReadOnlyList<DaxDiagnostic> Diagnostics => analysis?.Diagnostics ?? Array.Empty<DaxDiagnostic>();
    public event EventHandler? TextChanged;
    public event EventHandler? DiagnosticsChanged;
    public event EventHandler<DaxDefinitionRequestEventArgs>? DefinitionRequested;
    public event EventHandler<DaxReferencesRequestEventArgs>? ReferencesRequested;
    public event EventHandler<DaxRunRequestEventArgs>? RunRequested;

    public Bitmap Capture()
    {
        var bitmap = new Bitmap(Math.Max(1, panel.Width), Math.Max(1, panel.Height));
        panel.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }
    public DaxScratchEditor()
    {
        editor = new FastColoredTextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 11), ShowLineNumbers = true, BackColor = Color.White, AutoIndent = true, AccessibleName = "DAX source editor" };
        status = new Label { Dock = DockStyle.Bottom, Height = 25, Padding = new Padding(8, 4, 4, 2), BackColor = Color.FromArgb(244, 246, 248), ForeColor = Color.FromArgb(51, 65, 76), Font = new Font("Segoe UI", 9), AutoEllipsis = true, Text = "Ctrl+Space complete · F12 definition · Alt+F12 peek · F5 run" };
        panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(editor); panel.Controls.Add(status);
        View = new WindowsFormsHost { Child = panel };
        completion = new AutocompleteMenu(editor) { MinFragmentLength = 1, AppearInterval = 250, AllowTabKey = true, SearchPattern = @"[\w\[\]'.]", Font = new Font("Consolas", 10), MinimumSize = new Size(340, 180), AlwaysShowTooltip = true };
        completion.Items.SetAutocompleteItems(CurrentCompletionItems());
        completion.Opening += (_, e) => e.Cancel = analysis?.Document.Version != version || !editor.Focused;
        completion.Selecting += (_, e) =>
        {
            if (e.Item.Tag is not DaxCompletion item) return;
            e.Cancel = true; completion.Close();
            if (e.Item is not LanguageCompletionItem selected || selected.DocumentVersion != version) return;
            SelectSpan(item.ReplaceSpan.Start, item.ReplaceSpan.Length);
            ReplaceSelection(item.InsertText);
        };
        editor.TextChanged += (_, _) =>
        {
            if (applyingAnalysis) return;
            version++; QueueAnalysis(); TextChanged?.Invoke(this, EventArgs.Empty);
        };
        editor.SelectionChanged += (_, _) => { if (!applyingAnalysis) UpdateSignature(); };
        editor.KeyDown += EditorKeyDown;
        editor.MouseMove += EditorMouseMove;
        debounce.Tick += async (_, _) =>
        {
            debounce.Stop();
            try { await RefreshAnalysisAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!disposed) status.Text = "DAX analysis unavailable: " + ex.Message; }
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Go to definition   F12", null, (_, _) => GoToDefinition(false));
        menu.Items.Add("Peek definition   Alt+F12", null, (_, _) => GoToDefinition(true));
        menu.Items.Add("Find references   Shift+F12", null, (_, _) => FindReferences());
        menu.Items.Add("Preview code action   Ctrl+.", null, (_, _) => ShowCodeActions());
        menu.Items.Add("Rename local variable   F2", null, (_, _) => RenameLocalVariable());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Complete   Ctrl+Space", null, (_, _) => ShowCompletion());
        menu.Items.Add("Back   Alt+Left", null, (_, _) => NavigateBack());
        menu.Items.Add("Forward   Alt+Right", null, (_, _) => NavigateForward());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Run all   F5", null, (_, _) => RequestRun(DaxRunScope.All));
        menu.Items.Add("Run selection", null, (_, _) => RequestRun(DaxRunScope.Selection));
        menu.Items.Add("Run current statement   Alt+Enter", null, (_, _) => RequestRun(DaxRunScope.CurrentStatement));
        editor.ContextMenuStrip = menu;
        QueueAnalysis();
    }

    public void SetMetadata(DaxMetadataSnapshot snapshot)
    {
        metadata = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        version++; QueueAnalysis();
    }
    public void SetDocumentContext(string id, DaxDocumentKind kind = DaxDocumentKind.Query, string? table = null)
    {
        documentId = id ?? throw new ArgumentNullException(nameof(id));
        documentKind = kind; currentTable = table; version++; QueueAnalysis();
    }
    public void SetDocumentContext(DaxDocumentKind kind, string? table = null) => SetDocumentContext(documentId, kind, table);
    public void Focus() => editor.Focus();
    public void SelectSpan(int start, int length)
    {
        start = Math.Max(0, Math.Min(Text.Length, start));
        length = Math.Max(0, Math.Min(Text.Length - start, length));
        editor.Selection = editor.GetRange(start, start + length);
        editor.DoSelectionVisible();
    }
    public void ReplaceSelection(string text) => editor.InsertText(text ?? "");

    public async Task<DaxAnalysis?> RefreshAnalysisAsync(CancellationToken cancellationToken = default)
    {
        if (disposed) return null;
        debounce.Stop();
        analysisCancellation?.Cancel(); analysisCancellation?.Dispose();
        var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        analysisCancellation = pending;
        var document = Document; var snapshot = metadata; var token = pending.Token;
        var result = await Task.Run(() => language.Analyze(document, snapshot, token), token);
        if (disposed || token.IsCancellationRequested || document.Version != version || !ReferenceEquals(snapshot, metadata)) return null;
        analysis = result; lastTooltipOffset = -1; ApplyHighlighting(result); UpdateSignature();
        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
        if (editor.Focused && (explicitCompletion || ShouldOfferCompletion()))
        {
            explicitCompletion = false;
            completion.Items.SetAutocompleteItems(CurrentCompletionItems()); completion.Show(true);
        }
        return result;
    }
    public void ShowCompletion()
    {
        if (analysis?.Document.Version == version) { completion.Items.SetAutocompleteItems(CurrentCompletionItems()); completion.Show(true); }
        else { explicitCompletion = true; QueueAnalysis(); }
    }
    public void GoToDefinition(bool peek = false)
    {
        if (analysis?.Document.Version != version) { status.Text = "Analyzing the current edit; try definition navigation again."; return; }
        var location = language.FindDefinition(analysis, CaretOffset);
        if (location == null) { status.Text = "No definition at the current caret."; return; }
        if (!peek && location.DocumentId == documentId && location.Span is TextSpan span) { RememberPosition(); SelectSpan(span.Start, span.Length); }
        DefinitionRequested?.Invoke(this, new(location, peek));
    }
    public void FindReferences()
    {
        if (analysis?.Document.Version != version) return;
        var locations = language.FindReferences(analysis, CaretOffset);
        if (ReferencesRequested != null) { ReferencesRequested.Invoke(this, new(locations)); return; }
        if (locations.Count == 0) { status.Text = "No references at the current caret."; return; }
        using var dialog = new Form { Text = $"{locations.Count} references in this DAX document", Width = 720, Height = 420, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
        foreach (var location in locations)
        {
            var start = Math.Min(Text.Length, location.Span.Start);
            var line = Text.Substring(0, start).Count(character => character == '\n') + 1;
            var lineStart = Text.LastIndexOf('\n', Math.Max(0, start - 1));
            var lineEnd = Text.IndexOf('\n', start);
            if (lineEnd < 0) lineEnd = Text.Length;
            list.Items.Add($"Line {line}{(location.IsDefinition ? " · definition" : "")}  {Text.Substring(lineStart + 1, lineEnd - lineStart - 1).Trim()}");
        }
        var go = new Button { Text = "Go to selected reference", Dock = DockStyle.Bottom, Height = 34 };
        void Navigate()
        {
            if (list.SelectedIndex < 0) return;
            var target = locations[list.SelectedIndex]; RememberPosition(); SelectSpan(target.Span.Start, target.Span.Length); dialog.Close(); Focus();
        }
        go.Click += (_, _) => Navigate(); list.DoubleClick += (_, _) => Navigate();
        dialog.Controls.Add(list); dialog.Controls.Add(go); list.SelectedIndex = 0;
        dialog.ShowDialog(editor);
    }
    public IReadOnlyList<DaxCodeAction> GetCodeActions() => analysis?.Document.Version == version
        ? language.GetCodeActions(analysis, new TextSpan(SelectionStart, SelectionLength)) : Array.Empty<DaxCodeAction>();

    public void ShowCodeActions()
    {
        var actions = GetCodeActions();
        if (actions.Count == 0) { status.Text = "No safe code action here. Select a model reference, or use F2 on a local variable."; return; }
        PreviewActions(actions);
    }

    public void RenameLocalVariable()
    {
        if (analysis?.Document.Version != version) return;
        using var prompt = new Form { Text = "Rename local DAX variable", Width = 460, Height = 160, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var name = new TextBox { Dock = DockStyle.Top, Margin = new Padding(12), Font = new Font("Segoe UI", 11) };
        var instructions = new Label { Dock = DockStyle.Top, Height = 34, Text = "New variable name (the next step previews every change):", Padding = new Padding(8) };
        var next = new Button { Text = "Preview…", Dock = DockStyle.Bottom, Height = 34, DialogResult = DialogResult.OK };
        prompt.Controls.Add(name); prompt.Controls.Add(instructions); prompt.Controls.Add(next); prompt.AcceptButton = next;
        if (prompt.ShowDialog(editor) != DialogResult.OK) return;
        try { PreviewActions(new[] { language.RenameLocalVariable(analysis, CaretOffset, name.Text.Trim()) }); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { MessageBox.Show(editor, ex.Message, "DAX rename", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    public void ApplyCodeAction(DaxCodeAction action)
    {
        var updated = action.Apply(Document);
        editor.BeginAutoUndo();
        try { editor.SelectAll(); editor.InsertText(updated); }
        finally { editor.EndAutoUndo(); }
    }

    private void PreviewActions(IReadOnlyList<DaxCodeAction> actions)
    {
        using var dialog = new Form { Text = "Preview DAX changes", Width = 1040, Height = 650, MinimumSize = new Size(680, 420), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = true };
        var picker = new ComboBox { Dock = DockStyle.Top, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Title", DataSource = actions.ToArray() };
        var explanation = new Label { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8), AutoEllipsis = true };
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6 };
        var before = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 10), BackColor = Color.White, Text = Text };
        var after = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 10), BackColor = Color.White };
        var beforeGroup = new GroupBox { Dock = DockStyle.Fill, Text = "Before" }; beforeGroup.Controls.Add(before);
        var afterGroup = new GroupBox { Dock = DockStyle.Fill, Text = "After" }; afterGroup.Controls.Add(after);
        split.Panel1.Controls.Add(beforeGroup); split.Panel2.Controls.Add(afterGroup);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 42, Padding = new Padding(5) };
        var apply = new Button { Text = "Apply to editor", Width = 135, Height = 28 };
        var cancel = new Button { Text = "Cancel", Width = 100, Height = 28, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(apply); buttons.Controls.Add(cancel);
        void UpdatePreview()
        {
            var selected = (DaxCodeAction)picker.SelectedItem;
            try { after.Text = selected.Apply(Document); explanation.Text = selected.Description + "  Editor changes can be undone with Ctrl+Z."; apply.Enabled = true; }
            catch (InvalidOperationException ex) { explanation.Text = ex.Message; apply.Enabled = false; }
        }
        picker.SelectedIndexChanged += (_, _) => UpdatePreview();
        apply.Click += (_, _) =>
        {
            try { ApplyCodeAction((DaxCodeAction)picker.SelectedItem); dialog.DialogResult = DialogResult.OK; dialog.Close(); }
            catch (InvalidOperationException ex) { explanation.Text = ex.Message; apply.Enabled = false; }
        };
        dialog.Controls.Add(split); dialog.Controls.Add(explanation); dialog.Controls.Add(picker); dialog.Controls.Add(buttons);
        dialog.CancelButton = cancel; dialog.Shown += (_, _) => split.SplitterDistance = Math.Max(100, split.Width / 2);
        UpdatePreview(); dialog.ShowDialog(editor);
    }
    public void NavigateBack()
    {
        if (back.Count == 0) return;
        forward.Push(CaretOffset); SelectSpan(back.Pop(), 0);
    }
    public void NavigateForward()
    {
        if (forward.Count == 0) return;
        back.Push(CaretOffset); SelectSpan(forward.Pop(), 0);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; debounce.Stop(); debounce.Dispose();
        analysisCancellation?.Cancel(); analysisCancellation?.Dispose();
        completion.Dispose(); tooltip.Dispose();
        View.Child = null; panel.Dispose(); View.Dispose();
        keyword.Dispose(); literal.Dispose(); comment.Dispose(); identifier.Dispose(); error.Dispose(); warning.Dispose();
    }
    private void QueueAnalysis()
    {
        if (disposed) return;
        analysisCancellation?.Cancel(); debounce.Stop(); debounce.Start();
    }
    private void RememberPosition()
    {
        if (back.Count == 0 || back.Peek() != CaretOffset) back.Push(CaretOffset);
        forward.Clear();
    }
    private IEnumerable<AutocompleteItem> CurrentCompletionItems()
    {
        if (analysis?.Document.Version != version) yield break;
        foreach (var item in language.Complete(analysis, CaretOffset).Take(150)) yield return new LanguageCompletionItem(item, version);
    }
    private bool ShouldOfferCompletion()
    {
        var text = Text; var caret = CaretOffset;
        if (caret <= 0 || caret > text.Length) return false;
        var previous = text[caret - 1];
        return previous == '[' || previous == '.' || char.IsLetterOrDigit(previous) || previous == '_';
    }
    private void ApplyHighlighting(DaxAnalysis result)
    {
        applyingAnalysis = true;
        try
        {
            editor.Range.ClearStyle(keyword, literal, comment, identifier, error, warning);
            foreach (var token in result.Tokens)
            {
                var style = token.Kind switch
                {
                    DaxTokenKind.Keyword => keyword,
                    DaxTokenKind.String or DaxTokenKind.Number or DaxTokenKind.Date => literal,
                    DaxTokenKind.Comment => comment,
                    DaxTokenKind.QuotedIdentifier or DaxTokenKind.BracketIdentifier => identifier,
                    _ => null
                };
                if (style != null) editor.GetRange(token.Span.Start, token.Span.End).SetStyle(style);
            }
            foreach (var diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity == DaxDiagnosticSeverity.Information) continue;
                var start = Math.Max(0, Math.Min(Text.Length, diagnostic.Span.Start));
                var end = Math.Max(start, Math.Min(Text.Length, start + Math.Max(1, diagnostic.Span.Length)));
                editor.GetRange(start, end).SetStyle(diagnostic.Severity == DaxDiagnosticSeverity.Error ? error : warning);
            }
        }
        finally { applyingAnalysis = false; }
    }
    private void UpdateSignature()
    {
        if (analysis?.Document.Version != version) return;
        var signature = language.GetSignatureHelp(analysis, CaretOffset);
        var errors = analysis.Diagnostics.Count(diagnostic => diagnostic.Severity == DaxDiagnosticSeverity.Error);
        var warnings = analysis.Diagnostics.Count(diagnostic => diagnostic.Severity == DaxDiagnosticSeverity.Warning);
        if (signature != null)
        {
            var index = Math.Min(signature.ActiveParameter, signature.Signature.Parameters.Count - 1);
            var parameter = index >= 0 ? signature.Signature.Parameters[index] : "";
            status.Text = signature.Signature.Label + (parameter.Length > 0 ? "    • " + parameter : "");
            tooltip.SetToolTip(status, signature.Signature.Description);
        }
        else
        {
            status.Text = $"{errors} errors · {warnings} warnings    Ctrl+Space complete · F12 definition · F5 run";
            tooltip.SetToolTip(status, "Static editor checks use the model metadata snapshot. The connected engine validates execution.");
        }
    }
    private void EditorMouseMove(object? sender, MouseEventArgs e)
    {
        if (analysis?.Document.Version != version) return;
        var offset = editor.PointToPosition(e.Location);
        if (lastTooltipOffset == offset) return;
        lastTooltipOffset = offset;
        var diagnostic = analysis.Diagnostics.FirstOrDefault(item => item.Span.Contains(offset));
        if (diagnostic != null) { tooltip.SetToolTip(editor, diagnostic.Message); return; }
        var location = language.FindDefinition(analysis, offset);
        tooltip.SetToolTip(editor, location == null ? "" : location.Name + "\n" + (location.Description ?? location.Expression ?? location.Kind.ToString()));
    }
    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Space) ShowCompletion();
        else if (e.Control && e.KeyCode == Keys.OemPeriod) ShowCodeActions();
        else if (e.KeyCode == Keys.F2) RenameLocalVariable();
        else if (e.KeyCode == Keys.F12 && e.Shift) FindReferences();
        else if (e.KeyCode == Keys.F12) GoToDefinition(e.Alt);
        else if (e.Alt && e.KeyCode == Keys.Left) NavigateBack();
        else if (e.Alt && e.KeyCode == Keys.Right) NavigateForward();
        else if (e.KeyCode == Keys.F5) RequestRun(DaxRunScope.All);
        else if (e.Alt && e.KeyCode == Keys.Enter) RequestRun(DaxRunScope.CurrentStatement);
        else if (e.Control && e.KeyCode == Keys.Enter) RequestRun(SelectionLength > 0 ? DaxRunScope.Selection : DaxRunScope.CurrentStatement);
        else return;
        e.Handled = true; e.SuppressKeyPress = true;
    }
    private void RequestRun(DaxRunScope scope) => RunRequested?.Invoke(this, new(scope));
    private sealed class LanguageCompletionItem : AutocompleteItem
    {
        public int DocumentVersion { get; }
        public LanguageCompletionItem(DaxCompletion item, int documentVersion) : base(item.InsertText)
        {
            DocumentVersion = documentVersion;
            MenuText = item.Label; ToolTipTitle = item.Kind.ToString(); ToolTipText = item.Detail; Tag = item;
        }
        public override CompareResult Compare(string fragmentText) => CompareResult.VisibleAndSelected;
    }
}
