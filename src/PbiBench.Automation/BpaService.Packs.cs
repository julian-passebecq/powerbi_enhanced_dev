using PbiBench.Dax.LanguageService;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Automation;

public sealed partial class BpaService
{
    private void AddPackFindings(List<BpaFinding> findings, BpaWorkspaceContext? workspace)
    {
        foreach (var obj in new SemanticModelService(handler).Inventory())
            if (obj.Name != obj.Name.Trim()) Add("PBIBENCH006", obj, "The name begins or ends with whitespace, which is hard to distinguish in field lists.", "Review report and external references before renaming through the model editor.");
        foreach (var measure in handler.Model.AllMeasures)
        {
            if (string.IsNullOrEmpty(measure.FormatString) && (handler.CompatibilityLevel < 1601 || string.IsNullOrEmpty(measure.FormatStringExpression)))
                Add("PBIBENCH007", measure, "No static or dynamic format string is defined. The general format may be intentional.", "Choose the format that reflects this measure's units; text measures may retain the general format.");
            if (DaxTokenizer.Tokenize(measure.Expression ?? "").Any(token => token.Kind == DaxTokenKind.Operator && token.Text == "/"))
                Add("PBIBENCH011", measure, "The expression contains a division operator. This scan does not prove the denominator can be zero.", "Test zero/blank denominators. Keep / for a proven nonzero denominator; otherwise review DIVIDE and its result semantics.");
        }
        ModeType EffectiveMode(Partition partition) => partition.Mode == ModeType.Default ? handler.Model.DefaultMode : partition.Mode;
        var directLakeTables = handler.Model.Tables.Where(table => table.Partitions.Any(partition => EffectiveMode(partition).ToString() == "DirectLake")).ToArray();
        var mixed = directLakeTables.Length > 0 && handler.Model.Tables.Any(table => table.Partitions.Any(partition => EffectiveMode(partition).ToString() != "DirectLake"));
        foreach (var table in handler.Model.Tables)
        {
            foreach (var column in table.Columns.OfType<CalculatedColumn>().Where(_ => table.Partitions.Any(partition => EffectiveMode(partition) == ModeType.Import)))
                Add("PBIBENCH008", column, "A calculated column is evaluated for stored rows and may contribute to refresh and memory cost. No cost was measured by this rule.", "Inspect VertiPaq metrics and benchmark before changing source logic or replacing the column. Preserve report dependencies.");
            if (!directLakeTables.Contains(table)) continue;
            if (table.Columns.OfType<CalculatedColumn>().Any()) Add("PBIBENCH012", table, "This table combines Direct Lake partitions and DAX calculated columns. Supported shapes depend on the target engine and source mode.", "Validate the target's supported Direct Lake model shape before deployment; this tool does not silently change modes.");
            if (mixed) Add("PBIBENCH013", table, "The model combines Direct Lake and another partition mode. Mixed storage can be intentional; metadata alone does not prove the effective query path.", "Review OneLake/SQL source mode, authentication, fallback and cross-source relationships; profile the actual engine path.");
        }
        foreach (var relationship in handler.Model.Relationships.OfType<SingleColumnRelationship>())
            if (relationship.SecurityFilteringBehavior == SecurityFilteringBehavior.BothDirections)
                Add("PBIBENCH009", relationship, "The relationship is configured for security filtering in both directions. Its active state and effective role paths need review.", "Test every affected role with representative identities before altering security propagation.");
        foreach (var role in handler.Model.Roles)
            if (!handler.Model.Tables.Any(table => !string.IsNullOrWhiteSpace(role.RowLevelSecurity[table.Name])))
                Add("PBIBENCH010", role, "No nonempty table row filter was found in this role. Unrestricted access may be the intended policy; object permissions and service permissions are not evaluated by this rule.", "Confirm the intended audience and test effective permissions. No role or filter is created automatically.");
        if (workspace != null)
        {
            if (workspace.HasPbip && !workspace.IsGitRepository) Add("PBIBENCH014", null, "A PBIP workspace was detected but Git did not identify a readable repository. Git may be unavailable or access may be limited.", "Check Git installation and access, or initialize/choose the intended repository through your normal Git workflow.");
            if (workspace.HasConflicts) Add("PBIBENCH015", null, "Git reports one or more unmerged paths.", "Resolve and review the conflicts in PBIP / Git before publishing; PbiBench never picks a side automatically.");
            if (workspace.HasSemanticChanges) Add("PBIBENCH016", null, "Git reports changed semantic files. This does not establish whether the loaded live model matches disk.", "Compare semantic disk and live state, run validation, and review the Git diff before committing.");
        }
        void Add(string id, TabularNamedObject? obj, string reason, string proposed)
        {
            var rule = BpaRulePacks.Get(id); var pack = BpaRulePacks.PackFor(id);
            findings.Add(new BpaFinding(id, rule.Title, rule.Severity, obj, obj == null ? "Workspace" : SemanticModelService.ObjectPath(obj),
                reason, proposed, "Observed metadata or workspace state", "Requires reviewed author decision", pack.Name + " / " + pack.Version + " / " + id + " · Original policy · " + rule.Reference, null));
        }
    }
}
