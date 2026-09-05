using TabularEditor.TOMWrapper;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PbiBench.Semantic.Tests")]

namespace PbiBench.Semantic.ModelAuthoring;

public sealed record AuthoringChange(string ObjectPath, string Property, string Before, string After, string Reason);
public enum AuthoringIssueSeverity { Information, Warning, Error }
public sealed record AuthoringIssue(string Code, string Message, AuthoringIssueSeverity Severity, string? ObjectPath = null);
internal sealed record AuthoringEdit(AuthoringChange Change, Action Apply, Func<bool> Validate);

/// <summary>An exact, single-use local edit plan. Present Changes and Issues before calling Apply.</summary>
public sealed class AuthoringPreview
{
    private readonly TabularModelHandler owner;
    private readonly string fingerprint;
    private readonly AuthoringEdit[] edits;
    private bool consumed;
    private AuthoringPreview(TabularModelHandler handler, string title, IEnumerable<AuthoringEdit> edits, IEnumerable<AuthoringIssue> issues)
    {
        owner = handler; Title = title; this.edits = edits.ToArray();
        Changes = Array.AsReadOnly(this.edits.Select(edit => edit.Change).ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
        fingerprint = new SemanticModelService(handler).Fingerprint();
    }
    public string Title { get; }
    public IReadOnlyList<AuthoringChange> Changes { get; }
    public IReadOnlyList<AuthoringIssue> Issues { get; }
    public bool CanApply => !consumed && edits.Length > 0 && !Issues.Any(issue => issue.Severity == AuthoringIssueSeverity.Error);
    internal static AuthoringPreview Create(TabularModelHandler handler, string title, IEnumerable<AuthoringEdit> edits, IEnumerable<AuthoringIssue>? issues = null) =>
        new(handler ?? throw new ArgumentNullException(nameof(handler)), title, edits, issues ?? Array.Empty<AuthoringIssue>());

    /// <summary>Uses the existing TE2 undo framework; does not save files or write to a server.</summary>
    public void Apply(TabularModelHandler handler)
    {
        if (!ReferenceEquals(handler, owner)) throw new InvalidOperationException("The preview belongs to another model session. Preview again.");
        if (!CanApply) throw new InvalidOperationException("This preview has no applicable changes, has validation errors, or was already applied.");
        if (!handler.UndoManager.Enabled || handler.UndoManager.BatchDepth != 0 || handler.UpdateInProgress)
            throw new InvalidOperationException("Finish the current editor operation and enable Undo before applying.");
        if (new SemanticModelService(handler).Fingerprint() != fingerprint)
            throw new InvalidOperationException("The model changed after this preview. Preview again.");
        var undoSize = handler.UndoManager.UndoSize;
        var undoSteps = handler.UndoManager.UndoSteps;
        var undoHistory = handler.UndoManager.GetHistory();
        var finalizing = false;
        handler.BeginUpdate("PbiBench: " + Title);
        try
        {
            foreach (var edit in edits) edit.Apply();
            var invalid = edits.FirstOrDefault(edit => !edit.Validate());
            if (invalid != null) throw new InvalidOperationException("The model did not match the preview for " + invalid.Change.ObjectPath + " / " + invalid.Change.Property + ". Changes were rolled back.");
            if (handler.UndoManager.BatchDepth != 1) throw new InvalidOperationException("An edit left an unexpected nested undo operation open.");
            finalizing = true;
            handler.EndUpdate();
            consumed = true;
        }
        catch (Exception failure)
        {
            var recoveryErrors = new List<Exception>();
            if (handler.UndoManager.BatchDepth > 0)
            {
                // EndBatch may itself notify a failing observer after it has reduced depth.
                // Continue only while each attempt makes progress through our own batches.
                while (handler.UndoManager.BatchDepth > 0)
                {
                    var depth = handler.UndoManager.BatchDepth;
                    try { handler.UndoManager.EndBatch(rollback: true); }
                    catch (Exception recovery) { recoveryErrors.Add(recovery); }
                    if (handler.UndoManager.BatchDepth >= depth) break;
                }
            }
            else if (finalizing && handler.UndoManager.UndoSize > undoSize && handler.UndoManager.UndoSteps == undoSteps + 1 &&
                handler.UndoManager.GetHistory().StartsWith(undoHistory, StringComparison.Ordinal))
            {
                // TE2 commits the undo step before dependency/tree notifications. Recover
                // only one new step whose earlier history still matches our captured boundary.
                try { handler.UndoManager.Undo(); }
                catch (Exception recovery) { recoveryErrors.Add(recovery); }
            }
            try
            {
                handler.EndUpdateAll();
                // EndUpdateAll clears tree locks but can leave PostponeOperations set when
                // no dependency rebuild was needed. A balanced public update resets it.
                if (handler.UpdateInProgress) { handler.BeginUpdate(null!); handler.EndUpdate(undoable: false); }
            }
            catch (Exception recovery) { recoveryErrors.Add(recovery); }
            if (handler.UndoManager.BatchDepth != 0 || handler.UpdateInProgress || handler.UndoManager.UndoSize != undoSize ||
                handler.UndoManager.UndoSteps != undoSteps || handler.UndoManager.GetHistory() != undoHistory ||
                new SemanticModelService(handler).Fingerprint() != fingerprint)
            {
                consumed = true;
                throw new AggregateException("The authoring operation failed and automatic rollback could not restore its exact undo boundary. Inspect the pending model changes before continuing; unrelated undo history was not unwound.", new[] { failure }.Concat(recoveryErrors));
            }
            throw;
        }
    }
}

internal static class AuthoringObjects
{
    internal static IEnumerable<TabularNamedObject> All(TabularModelHandler handler) => handler.Model.Tables
        .SelectMany(table => new TabularNamedObject[] { table }.Concat(table.Columns).Concat(table.Measures)
            .Concat(table.Hierarchies).Concat(table.Hierarchies.SelectMany(hierarchy => hierarchy.Levels)))
        .Concat(handler.Model.Perspectives);
    internal static string Id(TabularNamedObject obj) => obj.GetObjectPath();
    internal static TabularNamedObject Resolve(TabularModelHandler handler, string id) => All(handler).FirstOrDefault(obj => Id(obj) == id)
        ?? throw new ArgumentException("The model object no longer exists: " + id);
    internal static void Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 512 || name.Any(char.IsControl)) throw new ArgumentException("Enter a nonblank name without control characters (at most 512 characters).");
    }
}
