using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Automation;

/// <summary>Original, deterministic BPA companion. Native TE2 BPA remains available in the editor.</summary>
public sealed class BpaService
{
    private readonly TabularModelHandler handler;
    private readonly AutomationService automation;
    public BpaService(TabularModelHandler handler, AutomationService automation)
    {
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
    }

    public IReadOnlyList<BpaFinding> Scan()
    {
        var findings = new List<BpaFinding>();
        var fingerprint = new SemanticModelService(handler).Fingerprint();
        foreach (var measure in handler.Model.AllMeasures)
        {
            if (string.IsNullOrWhiteSpace(measure.Expression))
                findings.Add(Finding("PBIBENCH001", "Measure has no expression", FindingSeverity.Error, measure,
                    "An empty measure cannot provide a useful calculation.", "Enter and validate the intended DAX in the Model editor.", measure.Expression ?? "", "Requires author decision", null));
            if (string.IsNullOrWhiteSpace(measure.Description))
            {
                var fix = automation.PreviewAtSnapshot(AutomationActionId.AddDescriptions, new[] { measure }, fingerprint);
                findings.Add(Finding("PBIBENCH002", "Measure needs a description", FindingSeverity.Information, measure,
                    "A description makes the measure easier to discover and interpret. The template is a starting point for author documentation.", "Add a description from the displayed template.", "", fix.Changes.FirstOrDefault()?.After ?? "", fix));
            }
            if (string.IsNullOrWhiteSpace(measure.DisplayFolder))
            {
                var fix = automation.PreviewAtSnapshot(AutomationActionId.OrganizeMeasures, new[] { measure }, fingerprint);
                findings.Add(Finding("PBIBENCH003", "Measure has no display folder", FindingSeverity.Information, measure,
                    "Folders help organize the field list without changing the calculation.", "Move the measure to the Measures display folder.", "", "Measures", fix));
            }
        }

        var keys = new HashSet<Column>(handler.Model.Tables.SelectMany(t => t.Columns).Where(c => c.IsKey));
        foreach (var relation in handler.Model.Relationships.OfType<SingleColumnRelationship>())
        {
            if (relation.ToColumn != null && relation.ToCardinality == RelationshipEndCardinality.One) keys.Add(relation.ToColumn);
            if (relation.FromColumn != null && relation.FromCardinality == RelationshipEndCardinality.One) keys.Add(relation.FromColumn);
            if (relation.IsActive && relation.CrossFilteringBehavior == CrossFilteringBehavior.BothDirections)
                findings.Add(Finding("PBIBENCH005", "Review bidirectional filtering", FindingSeverity.Warning, relation,
                    "Active bidirectional filtering can create ambiguous propagation paths. Intent depends on model semantics, so no automatic fix is offered.",
                    "Inspect the relationship and report requirements.", "BothDirections", "Requires author decision", null));
        }
        foreach (var column in keys.Where(c => c.SummarizeBy != AggregateFunction.None))
        {
            var fix = automation.PreviewAtSnapshot(AutomationActionId.SetSummarizeByNone, new[] { column }, fingerprint);
            findings.Add(Finding("PBIBENCH004", "Key column allows implicit aggregation", FindingSeverity.Warning, column,
                "This is an explicit key or the one side of a relationship; its implicit sum is usually not meaningful. Review existing report defaults before applying.",
                "Set SummarizeBy to None.", column.SummarizeBy.ToString(), "None", fix));
        }
        return findings.OrderByDescending(f => f.Severity).ThenBy(f => f.ObjectPath, StringComparer.Ordinal).ThenBy(f => f.RuleId, StringComparer.Ordinal).ToArray();
    }

    private static BpaFinding Finding(string id, string rule, FindingSeverity severity, TabularNamedObject obj, string reason, string proposed, string before, string after, ChangePreview? fix)
        => new(id, rule, severity, obj, SemanticModelService.ObjectPath(obj), reason, proposed, before, after,
            "PbiBench Pass 1 policy / " + id + " (original rule; safe metadata subset)", fix);
}
