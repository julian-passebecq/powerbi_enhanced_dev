using System.Windows;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

/// <summary>Every specialized editor uses the existing PbiBench diff dialog and the same guarded local transaction.</summary>
public static class AuthoringReview
{
    public static bool Show(FrameworkElement owner, AuthoringPreview preview, Func<TabularModelHandler?> currentHandler, Action changed)
    {
        var explanation = "Review the exact metadata changes. Apply records one local model undo batch; saving or deploying remains a separate command.\n" +
            string.Join("\n", preview.Issues.Select(i => i.Severity + " · " + i.Message));
        var rows = preview.Changes.Select(c => new PreviewRow(c.ObjectPath, c.Property, c.Before, c.After, c.Reason)).ToArray();
        if (!PreviewDialog.Show(Window.GetWindow(owner), preview.Title, explanation, rows, preview.CanApply, "Apply to model")) return false;
        preview.Apply(currentHandler() ?? throw new InvalidOperationException("The model session is no longer available."));
        changed(); return true;
    }
}
