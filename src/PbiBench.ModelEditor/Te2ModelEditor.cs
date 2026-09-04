using System.Reflection;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using PbiBench.Core.Commands;
using TabularEditor;
using TabularEditor.TOMWrapper;

namespace PbiBench.ModelEditor;

/// <summary>The only PbiBench boundary that owns the legacy TE2 window/controller.</summary>
public sealed class Te2ModelEditor : IDisposable
{
    private readonly HostedEditorForm form;
    private readonly Func<bool>? confirmDiscardChanges;
    private WorkbenchCommandRegistry? commands;
    private readonly Dictionary<ToolStripItem, bool> nativeChrome = new();
    private bool legacyCommandsVisible = true;
    public WindowsFormsHost View { get; }
    public TabularModelHandler? Handler => form.UI.Handler;
    public string? FilePath => Handler?.IsConnected == false ? form.UI.File_Current : null;
    public string? Server => Handler?.IsConnected == true ? Handler.Database.Server.Name : null;
    public string? Database => Handler?.Database.Name;
    public string ActiveExpression => form.UI.Elements.ExpressionEditor.Text;
    public IReadOnlyList<TabularNamedObject> Selection => form.UI.Selection.Direct.OfType<TabularNamedObject>().ToArray();
    public event EventHandler? ModelChanged;
    public event EventHandler? SelectionChanged;
    public Func<string, string, string, bool>? ReviewWrite { get; set; }
    public Action? RequestClose { get; set; }
    public bool LegacyCommandsVisible => legacyCommandsVisible;

    public Te2ModelEditor(string? isolatedProfileDirectory = null) : this(null, isolatedProfileDirectory) { }

    internal Te2ModelEditor(Func<bool>? confirmDiscardChanges, string? isolatedProfileDirectory = null)
    {
        this.confirmDiscardChanges = confirmDiscardChanges;
        if (isolatedProfileDirectory != null)
        {
            // The pinned framework editor exposes fixed settings paths. Isolate them before
            // constructing any controls when the host explicitly requests a test profile.
            System.IO.Directory.CreateDirectory(isolatedProfileDirectory);
            SetProfilePath(typeof(TabularEditor.UIServices.Preferences), "PREFERENCES_PATH", System.IO.Path.Combine(isolatedProfileDirectory, "Preferences.json"));
            SetProfilePath(typeof(TabularEditor.UIServices.RecentFiles), "RECENTFILES_PATH", System.IO.Path.Combine(isolatedProfileDirectory, "RecentFiles.json"));
        }
        Application.EnableVisualStyles();
        ScriptEngine.InitScriptEngine(new List<Assembly>());
        form = new HostedEditorForm { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill };
        View = new WindowsFormsHost { Child = form };
        form.UI.ModelLoaded += (_, _) =>
        {
            if (Handler != null)
            {
                Handler.ReviewRemoteWrite = (operation, target, text) => ReviewWrite?.Invoke(operation, target, text) == true;
                Handler.TabularDeployer.ReviewRemoteWrite = (operation, target, text) => ReviewWrite?.Invoke(operation, target, text) == true;
            }
            ModelChanged?.Invoke(this, EventArgs.Empty);
        };
        form.UI.Elements.TreeView.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        ReplaceNativeHandler("actExit", "Execute", "actExit_Execute", () => RequestClose?.Invoke());
        ReplaceNativeHandler("actNewModel", "Execute", "actNewModel_Execute", () => Dispatch(WorkbenchCommandId.NewModel, New));
        ReplaceNativeHandler("actOpenFile", "Execute", "actOpenFile_Execute", () => Dispatch(WorkbenchCommandId.Open, OpenDialog));
        ReplaceNativeHandler("actOpenDB", "Execute", "btnConnect_Click", () => Dispatch(WorkbenchCommandId.Connect, Connect));
        ReplaceNativeHandler("actSave", "Execute", "actSave_Execute", () => Dispatch(WorkbenchCommandId.Save, Save));
        ReplaceNativeHandler("actOpenBPA", "Execute", "actOpenBPA_Execute", () => Dispatch(WorkbenchCommandId.RunBpa, ShowNativeBpa));
        // The upstream BPA menu has both an action and a redundant Click handler.
        // Keep one route so a migrated menu click does not also open the native BPA window.
        ReplaceNativeHandler("bestPracticeAnalyzerToolStripMenuItem", "Click", "bestPracticeAnalyzerToolStripMenuItem_Click", () => { });
        ReplaceNativeHandler("fromFolderToolStripMenuItem", "Click", "fromFolderToolStripMenuItem_Click", () =>
        {
            AcceptExpression();
            form.UI.File_Open(true);
        });
        form.Show();
        AddDaxStudioContextCommand();
    }

    public void Open(string path) { if (CanClose()) form.UI.File_Open(path); }
    public void OpenDialog() { AcceptExpression(); form.UI.File_Open(); }
    public void Connect() { AcceptExpression(); form.UI.Database_Connect(); }
    public void Connect(string server, string database) { if (CanClose()) form.UI.Database_Open(server, database); }
    public void New() { if (CanClose()) form.UI.File_New(); }
    public void Save() { AcceptExpression(); form.UI.Save(); ModelChanged?.Invoke(this, EventArgs.Empty); }
    public void AcceptExpression() => form.UI.ExpressionEditor_AcceptEdit();
    // UIUndoRedoAction.OnExecute owns editor-text/property-grid/model focus handling.
    // Replacing its Execute event would run an additional undo AFTER the native undo.
    // The shell instead calls the very same native implementation used by TE2 shortcuts.
    public void Undo() { if (Handler != null) ExecuteNativeAction("actUndo"); ModelChanged?.Invoke(this, EventArgs.Empty); }
    public void Redo() { if (Handler != null) ExecuteNativeAction("actRedo"); ModelChanged?.Invoke(this, EventArgs.Empty); }

    public void ConfigureCommands(WorkbenchCommandRegistry registry) => commands = registry ?? throw new ArgumentNullException(nameof(registry));

    public Dictionary<string, double> CapturePaneFractions() => new[] { "splitContainer1", "splitContainer2" }.ToDictionary(name => name, name =>
    {
        var split = NativeField<SplitContainer>(name);
        return (double)split.SplitterDistance / Math.Max(1, split.Orientation == Orientation.Vertical ? split.Width : split.Height);
    });

    public void RestorePaneFractions(IReadOnlyDictionary<string, double>? fractions)
    {
        if (fractions == null) return;
        foreach (var name in new[] { "splitContainer1", "splitContainer2" })
        {
            if (!fractions.TryGetValue(name, out var fraction) || double.IsNaN(fraction) || double.IsInfinity(fraction)) continue;
            var split = NativeField<SplitContainer>(name);
            var extent = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            var maximum = extent - split.Panel2MinSize - split.SplitterWidth;
            if (maximum >= split.Panel1MinSize && !split.Panel1Collapsed && !split.Panel2Collapsed)
                split.SplitterDistance = Math.Max(split.Panel1MinSize, Math.Min(maximum, (int)(extent * Math.Max(.1, Math.Min(.9, fraction)))));
        }
    }

    /// <summary>Collapse only duplicated chrome after the shell has registered every primary command.</summary>
    public void ShowLegacyCommands(bool visible)
    {
        if (!visible)
        {
            var required = new[] { WorkbenchCommandId.Open, WorkbenchCommandId.Connect, WorkbenchCommandId.Save,
                WorkbenchCommandId.Undo, WorkbenchCommandId.Redo, WorkbenchCommandId.RunBpa,
                WorkbenchCommandId.Automate, WorkbenchCommandId.DaxStudio, WorkbenchCommandId.Diagram };
            if (commands == null || required.Any(id => !commands.Contains(id)))
                throw new InvalidOperationException("Register the primary PbiBench commands before collapsing the native command surface.");
        }
        if (visible == legacyCommandsVisible) return;
        var menu = NativeField<MenuStrip>("menuStrip1");
        var toolbar = NativeField<ToolStrip>("toolStrip2");
        form.SuspendLayout();
        try
        {
            if (!visible)
            {
                // Keep native perspective/translation selectors, filter and view toggles useful.
                // Every other native command remains intact behind Advanced TE2 Commands.
                foreach (var name in new[] { "toolStripButton8", "btnConnect", "toolStripSeparator3", "btnSave", "toolStripSeparator22" })
                {
                    var item = toolbar.Items[name];
                    nativeChrome[item] = item.Available;
                    item.Available = false;
                }
            }
            else
            {
                foreach (var item in nativeChrome) item.Key.Available = item.Value;
                nativeChrome.Clear();
            }
            menu.Visible = visible;
            legacyCommandsVisible = visible;
        }
        finally { form.ResumeLayout(true); }
    }

    public void ShowScriptEditor()
    {
        var tabs = NativeField<TabControl>("tabCodeEditors");
        var script = NativeField<TabPage>("pgCSharpScript");
        if (!tabs.TabPages.Contains(script)) throw new InvalidOperationException("C# scripts are disabled by the current editor policy.");
        tabs.SelectedTab = script;
        NativeField<Control>("txtAdvanced").Focus();
    }

    public void FocusExpressionEditor()
    {
        NativeField<TabControl>("tabCodeEditors").SelectedIndex = 0;
        form.UI.Elements.ExpressionEditor.Focus();
    }

    public void ShowDependencies()
    {
        if (Selection.FirstOrDefault() is IDaxObject item) form.UI.ShowDependencies(item);
    }
    public void Select(TabularNamedObject item)
    {
        var tree = form.UI.Elements.TreeView;
        var node = tree.FindNode(form.UI.TreeModel.GetPath(item));
        if (node == null) return;
        tree.ClearSelection();
        node.IsSelected = true;
        tree.EnsureVisible(node);
    }

    public void ShowNativeBpa()
    {
        // BPA remains upstream-owned; this reflected field is confined to the compatibility boundary.
        var field = typeof(FormMain).GetField("BPAForm", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var bpa = field.GetValue(form)!;
        bpa.GetType().GetMethod("ShowBPA")!.Invoke(bpa, null);
    }

    public bool CanClose()
    {
        // TE2 returns true when the user CANCELS discarding. Commit the active editor text first
        // so an expression that has not lost focus still participates in the dirty check.
        AcceptExpression();
        if (Handler?.HasUnsavedChanges != true) return true;
        return confirmDiscardChanges?.Invoke() ?? !form.UI.DiscardChangesCheck();
    }
    public int TreeRootCount => form.UI.Elements.TreeView.Root.Children.Count;
    public System.Drawing.Bitmap Capture()
    {
        var bitmap = new System.Drawing.Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }
    public void Dispose()
    {
        var handler = Handler;
        View.Child = null;
        form.Dispose();
        handler?.Dispose();
        TabularModelHandler.Cleanup();
        View.Dispose();
    }

    private void ReplaceNativeHandler(string fieldName, string eventName, string originalMethodName, Action replacement)
    {
        // The pinned TE2 designer owns private action fields. Isolate its adaptation here,
        // keeping menu items, toolbar buttons and keyboard shortcuts on the same action.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var field = typeof(FormMain).GetField(fieldName, flags)
            ?? throw new MissingFieldException(typeof(FormMain).FullName, fieldName);
        var nativeAction = field.GetValue(form)!;
        var nativeEvent = nativeAction.GetType().GetEvent(eventName)
            ?? throw new MissingMemberException(nativeAction.GetType().FullName, eventName);
        var originalMethod = typeof(FormMain).GetMethod(originalMethodName, flags)
            ?? throw new MissingMethodException(typeof(FormMain).FullName, originalMethodName);
        nativeEvent.RemoveEventHandler(nativeAction, Delegate.CreateDelegate(nativeEvent.EventHandlerType!, form, originalMethod));
        EventHandler handler = (_, _) =>
        {
            try { replacement(); }
            catch (Exception ex) { MessageBox.Show(form, ex.Message, "PbiBench", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        nativeEvent.AddEventHandler(nativeAction, handler);
    }

    private void Dispatch(WorkbenchCommandId id, Action fallback)
    {
        if (commands?.Contains(id) == true) commands.Execute(id);
        else fallback();
    }

    private T NativeField<T>(string name) where T : class =>
        (T)(typeof(FormMain).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(form)
            ?? throw new MissingFieldException(typeof(FormMain).FullName, name));

    private void ExecuteNativeAction(string name)
    {
        var action = NativeField<object>(name);
        action.GetType().GetMethod("DoExecute", Type.EmptyTypes)!.Invoke(action, null);
    }

    private void AddDaxStudioContextCommand()
    {
        var menu = form.UI.Elements.TreeView.ContextMenuStrip;
        var analyze = new ToolStripMenuItem("Analyze in DAX Studio") { Name = "pbibenchAnalyzeDax", Available = false };
        analyze.Click += (_, _) => Dispatch(WorkbenchCommandId.DaxStudio, () => { });
        menu.Items.Insert(0, analyze);
        menu.Opening += (_, _) =>
        {
            analyze.Available = Selection.Count == 1 && Selection[0] is Measure;
            analyze.Enabled = commands?.CanExecute(WorkbenchCommandId.DaxStudio) == true;
        };
    }

    private static void SetProfilePath(Type type, string fieldName, string path)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        type.GetField(fieldName, flags)!.SetValue(null, path);
        type.GetField("_current", flags)!.SetValue(null, null);
    }

    private sealed class HostedEditorForm : FormMain
    {
        // Shell owns command-line loading/lifecycle; do not run TE2's update prompt or auto-connect.
        protected override void OnShown(EventArgs e) { }
    }
}
