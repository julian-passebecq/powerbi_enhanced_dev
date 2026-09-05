using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed record SemanticRefactorRequest(TabularNamedObject Object, string Operation, string? NewValue);
/// <summary>Uses existing cancellable TE2 events. Recovery/Undo never prompts, and no report is mutated here.</summary>
public sealed class SemanticRefactorGuard : IDisposable
{
    private readonly TabularModelHandler handler;
    private readonly Func<SemanticRefactorRequest, bool> review;
    public SemanticRefactorGuard(TabularModelHandler handler, Func<SemanticRefactorRequest, bool> review)
    { this.handler = handler; this.review = review; handler.ObjectChanging += Changing; handler.ObjectDeleting += Deleting; }
    private bool NeedsReview(ITabularObject obj) => !handler.UndoManager.UndoInProgress && obj is Table or Measure or Column;
    private void Changing(object sender, ObjectChangingEventArgs e)
    {
        if (e.Cancel || !NeedsReview(e.TabularObject) || e.PropertyName is not ("Name" or "Expression" or "DataType" or "SummarizeBy")) return;
        var obj = (TabularNamedObject)e.TabularObject;
        if (e.PropertyName == "Name" && obj.Name == e.NewValue?.ToString()) return;
        e.Cancel = true; // Failure in the callback cannot accidentally authorize a mutation.
        e.Cancel = !review(new(obj, e.PropertyName == "Name" ? "Rename" : "Refactor " + e.PropertyName, e.NewValue?.ToString()));
    }
    private void Deleting(object sender, ObjectDeletingEventArgs e)
    {
        if (e.Cancel || !NeedsReview(e.TabularObject)) return;
        e.Cancel = true; e.Cancel = !review(new((TabularNamedObject)e.TabularObject, "Delete", null));
    }
    public void Dispose() { handler.ObjectChanging -= Changing; handler.ObjectDeleting -= Deleting; }
}
