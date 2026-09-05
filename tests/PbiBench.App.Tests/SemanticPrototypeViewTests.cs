using System.Windows.Controls;
using System.Windows.Threading;
using PbiBench.App;
using PbiBench.Core.Tasks;
using TabularEditor.TOMWrapper;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class SemanticPrototypeViewTests
{
    [Fact] public Task CompilerMapsAnExplicitTableAndPreservesItsDraftAcrossModelRefresh() => Sta(async () =>
    {
        using var handler = new TabularModelHandler(1600); handler.Model.AddTable("Sales").AddDataColumn("Amount", "Amount", dataType: DataType.Decimal);
        using var view = new SemanticPrototypeView(() => handler, () => { }); var source = SemanticPrototypeView.SampleYaml.Replace("Quantity", "Amount"); await view.CompileAsync(source); view.SelectTargetTable("Sales");
        Assert.True(view.PreviewMeasures().CanApply); var compilation = view.LastCompilation; handler.Model.Description = "unrelated metadata refresh"; view.RefreshModel(); Assert.Same(compilation, view.LastCompilation);
        Assert.Throws<ArgumentException>(() => view.SelectTargetTable("Absent")); view.ShowTool("Semantic compiler"); view.ShowTool("DAX packages"); Assert.Throws<ArgumentException>(() => view.ShowTool("unknown"));
    });
    [Fact] public Task QueuedCompilerResultCannotOverwriteAChangedYamlDraft() => Sta(async () =>
    {
        using var queue = new BackgroundTaskQueue(1); var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var earlier = queue.Enqueue("Earlier operation", async _ => { started.TrySetResult(true); return await release.Task; }); await started.Task;
        using var view = new SemanticPrototypeView(() => null, () => { }, queue); var pending = view.CompileAsync(SemanticPrototypeView.SampleYaml);
        try { var editor = Field<TextBox>(view, "yaml"); editor.Text = "version: 1.1\n# unsaved newer draft"; }
        finally { release.TrySetResult(true); }
        await earlier.Completion; await pending; Assert.Null(view.LastCompilation); Assert.Contains("unsaved newer draft", Field<TextBox>(view, "yaml").Text);
    });
    private static T Field<T>(SemanticPrototypeView view, string name) => (T)typeof(SemanticPrototypeView).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
    private static async Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher)); dispatcher.BeginInvoke(new Action(async () => { try { await action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } })); Dispatcher.Run(); }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); if (await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(30))) != completion.Task) throw new TimeoutException("Prototype STA test timed out."); await completion.Task;
    }
}
