using TabularEditor.TOMWrapper;

namespace PbiBench.Automation;

public enum AutomationActionId { FormatMeasures, CreateSumMeasures, CreateMeasureTable, SetSummarizeByNone, OrganizeMeasures, AddDescriptions, LastRefreshScaffold }
public sealed record AutomationAction(AutomationActionId Id, string Name, string Description, string Selection, string Risk);
public sealed class AutomationOptions
{
    public string MeasureTableName { get; set; } = "_Measures";
    public string DisplayFolder { get; set; } = "Measures";
    public string MeasurePrefix { get; set; } = "Total ";
    public string DescriptionTemplate { get; set; } = "{Name} in {Table}.";
    public bool AllMeasuresWhenSelectionEmpty { get; set; } = true;
}

public sealed record ObjectChange(string ObjectPath, string Property, string Before, string After, string Reason, TabularNamedObject? Object);

/// <summary>Immutable exact preview, created only by the service. Never contains a remote save or deployment.</summary>
public sealed class ChangePreview
{
    internal ChangePreview(Guid owner, AutomationAction action, string fingerprint, IEnumerable<PlannedEdit> edits, IEnumerable<string> notices, TabularNamedObject? focus)
    {
        Owner = owner; Action = action; Fingerprint = fingerprint;
        Edits = edits.ToArray(); Changes = Array.AsReadOnly(Edits.Select(e => e.Change).ToArray());
        Notices = Array.AsReadOnly(notices.ToArray()); FocusObject = focus;
    }
    internal Guid Owner { get; }
    internal string Fingerprint { get; }
    internal PlannedEdit[] Edits { get; }
    internal bool Consumed { get; set; }
    public Guid Id { get; } = Guid.NewGuid();
    public AutomationAction Action { get; }
    public IReadOnlyList<ObjectChange> Changes { get; }
    public IReadOnlyList<string> Notices { get; }
    public TabularNamedObject? FocusObject { get; }
    public bool CanApply => Changes.Count > 0 && !Consumed;
}

internal sealed record PlannedEdit(ObjectChange Change, Action Apply, Func<bool> Validate);
public sealed record ApplyResult(int ChangedObjects, string Message);
public enum FindingSeverity { Information, Warning, Error }
public sealed record BpaFinding(string RuleId, string Rule, FindingSeverity Severity, TabularNamedObject? Object,
    string ObjectPath, string Reason, string ProposedChange, string Before, string After, string Source, ChangePreview? FixPreview)
{
    public string Risk => BpaRulePacks.Get(RuleId).Risk;
    public string Category => BpaRulePacks.Get(RuleId).Category;
    public string Pack => BpaRulePacks.PackFor(RuleId).Name;
    public string Version => BpaRulePacks.PackFor(RuleId).Version;
}
