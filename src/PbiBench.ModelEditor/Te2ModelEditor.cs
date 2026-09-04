using System.Reflection;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using TabularEditor;
using TabularEditor.TOMWrapper;

namespace PbiBench.ModelEditor;

/// <summary>The only PbiBench boundary that owns the legacy TE2 window/controller.</summary>
public sealed class Te2ModelEditor : IDisposable
{
    private readonly HostedEditorForm form;
    private readonly Func<bool>? confirmDiscardChanges;
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
        ReplaceNativeHandler("actNewModel", "Execute", "actNewModel_Execute", New);
        ReplaceNativeHandler("actOpenFile", "Execute", "actOpenFile_Execute", OpenDialog);
        ReplaceNativeHandler("actOpenDB", "Execute", "btnConnect_Click", Connect);
        ReplaceNativeHandler("fromFolderToolStripMenuItem", "Click", "fromFolderToolStripMenuItem_Click", () =>
        {
            AcceptExpression();
            form.UI.File_Open(true);
        });
        form.Show();
    }

    public void Open(string path) { if (CanClose()) form.UI.File_Open(path); }
    public void OpenDialog() { AcceptExpression(); form.UI.File_Open(); }
    public void Connect() { AcceptExpression(); form.UI.Database_Connect(); }
    public void Connect(string server, string database) { if (CanClose()) form.UI.Database_Open(server, database); }
    public void New() { if (CanClose()) form.UI.File_New(); }
    public void Save() { AcceptExpression(); form.UI.Save(); ModelChanged?.Invoke(this, EventArgs.Empty); }
    public void AcceptExpression() => form.UI.ExpressionEditor_AcceptEdit();
    public void Undo() { if (Handler != null) Handler.UndoManager.Undo(); }
    public void Redo() { if (Handler != null) Handler.UndoManager.Redo(); }
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
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
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
