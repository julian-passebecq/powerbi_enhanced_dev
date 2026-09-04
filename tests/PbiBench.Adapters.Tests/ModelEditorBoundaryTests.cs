#if NETFRAMEWORK
using System.Reflection;
using System.Windows.Forms;
using PbiBench.ModelEditor;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class ModelEditorBoundaryTests
{
    [Fact]
    public Task NativeExitRequestsShellCloseAndCanceledReplacementKeepsDirtyModel() => RunSta(() =>
    {
        using var temp = new TemporaryDirectory();
        var first = Path.Combine(temp.Root, "first.bim");
        var second = Path.Combine(temp.Root, "second.bim");
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", "pass1-demo.bim"), first);
        File.Copy(first, second);
        var allowDiscard = false;
        var discardChecks = 0;
        var profile = Path.Combine(temp.Root, "profile");
        using var editor = new Te2ModelEditor(() => { discardChecks++; return allowDiscard; }, profile);
        Assert.True(editor.CanClose());
        editor.Open(first);
        Assert.NotNull(editor.Handler);
        Assert.True(editor.CanClose());
        Assert.True(editor.TreeRootCount > 0);
        Assert.True(File.Exists(Path.Combine(profile, "RecentFiles.json")));
        var initial = editor.Handler;
        initial.Model.Tables.First().Description = "Unsaved fixture change";
        Assert.True(initial.HasUnsavedChanges);
        Assert.False(editor.CanClose());
        editor.Open(second);
        Assert.Same(initial, editor.Handler);
        editor.Connect("must-not-attempt-network", "fixture");
        Assert.Same(initial, editor.Handler);
        editor.New();
        Assert.Same(initial, editor.Handler);
        ExecuteNativeAction(editor, "actNewModel");
        Assert.Same(initial, editor.Handler);
        Assert.Equal(5, discardChecks);

        var closeRequests = 0;
        editor.RequestClose = () => closeRequests++;
        ExecuteNativeAction(editor, "actExit");
        Assert.Equal(1, closeRequests);
        Assert.False(editor.View.Child.IsDisposed);
        Assert.Same(initial, editor.Handler);

        allowDiscard = true;
        editor.Open(second);
        Assert.NotSame(initial, editor.Handler);
        Assert.Equal(second, editor.FilePath);
        Assert.True(editor.CanClose());

        // Exercise the complete upstream BPA window, including its actual model binding and scan.
        var hostedForm = editor.View.Child;
        var bpa = (Form)hostedForm.GetType().BaseType!.GetField("BPAForm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hostedForm)!;
        try
        {
            editor.ShowNativeBpa();
            Assert.True(bpa.Visible);
            Assert.Same(editor.Handler!.Model, bpa.GetType().GetProperty("Model")!.GetValue(bpa));
            bpa.GetType().GetMethod("AnalyzeAll", Type.EmptyTypes)!.Invoke(bpa, null);
            bpa.Close();
            Assert.False(bpa.Visible);
            Assert.False(editor.View.Child.IsDisposed);
        }
        finally
        {
            // Upstream Close hides BPA for reuse; explicitly dispose this fixture's auxiliary form.
            bpa.Dispose();
        }
    });

    private static void ExecuteNativeAction(Te2ModelEditor editor, string name)
    {
        var form = editor.View.Child;
        var action = form.GetType().BaseType!.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        action.GetType().GetMethod("DoExecute", Type.EmptyTypes)!.Invoke(action, null);
    }

    private static Task RunSta(Action body)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { body(); result.SetResult(true); }
            catch (Exception error) { result.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }
}
#endif
