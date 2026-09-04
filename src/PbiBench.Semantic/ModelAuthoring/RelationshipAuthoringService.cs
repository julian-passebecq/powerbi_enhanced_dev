using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed record RelationshipDefinition(Column FromColumn, Column ToColumn,
    RelationshipEndCardinality FromCardinality, RelationshipEndCardinality ToCardinality,
    CrossFilteringBehavior CrossFilteringBehavior, bool IsActive,
    SecurityFilteringBehavior SecurityFilteringBehavior, DateTimeRelationshipBehavior JoinOnDateBehavior,
    bool RelyOnReferentialIntegrity);

/// <summary>Metadata-only relationship edits through TE2's undo-aware public wrappers.</summary>
public sealed class RelationshipAuthoringService
{
    private readonly TabularModelHandler handler;
    public RelationshipAuthoringService(TabularModelHandler handler) => this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
    public static RelationshipDefinition Capture(SingleColumnRelationship relationship) => new(
        relationship.FromColumn, relationship.ToColumn, relationship.FromCardinality, relationship.ToCardinality,
        relationship.CrossFilteringBehavior, relationship.IsActive, relationship.SecurityFilteringBehavior,
        relationship.JoinOnDateBehavior, relationship.RelyOnReferentialIntegrity);

    public AuthoringPreview PreviewInvert(SingleColumnRelationship relationship)
    {
        var current = Capture(relationship);
        return Preview(relationship, current with { FromColumn = current.ToColumn, ToColumn = current.FromColumn,
            FromCardinality = current.ToCardinality, ToCardinality = current.FromCardinality });
    }

    public AuthoringPreview PreviewActive(SingleColumnRelationship relationship, bool active)
        => Preview(relationship, Capture(relationship) with { IsActive = active });

    public AuthoringPreview Preview(SingleColumnRelationship relationship, RelationshipDefinition requested)
    {
        if (relationship == null) throw new ArgumentNullException(nameof(relationship));
        if (requested == null) throw new ArgumentNullException(nameof(requested));
        if (!handler.Model.Relationships.Any(item => ReferenceEquals(item, relationship))) throw new ArgumentException("The relationship must belong to the current model.", nameof(relationship));
        var before = Capture(relationship);
        var path = "Relationship " + relationship.ID;
        var issues = Validate(relationship, requested, path);
        var changes = new List<AuthoringChange>();
        void Change(string property, object? oldValue, object? newValue, string reason)
        {
            var oldText = oldValue?.ToString() ?? "(none)"; var newText = newValue?.ToString() ?? "(none)";
            if (oldText != newText) changes.Add(new(path, property, oldText, newText, reason));
        }
        Change("From column", ColumnPath(before.FromColumn), ColumnPath(requested.FromColumn), "Relationship starting endpoint.");
        Change("To column", ColumnPath(before.ToColumn), ColumnPath(requested.ToColumn), "Relationship destination endpoint.");
        Change("From cardinality", before.FromCardinality, requested.FromCardinality, "Data uniqueness is not verified by metadata editing.");
        Change("To cardinality", before.ToCardinality, requested.ToCardinality, "Data uniqueness is not verified by metadata editing.");
        Change("Cross-filter direction", before.CrossFilteringBehavior, requested.CrossFilteringBehavior, "OneDirection propagates filters from To to From.");
        Change("Active", before.IsActive, requested.IsActive, "Controls default filter propagation.");
        Change("Security filtering", before.SecurityFilteringBehavior, requested.SecurityFilteringBehavior, "Changes row-level security propagation.");
        Change("Date joining", before.JoinOnDateBehavior, requested.JoinOnDateBehavior, "Controls date/time join behavior.");
        Change("Assume referential integrity", before.RelyOnReferentialIntegrity, requested.RelyOnReferentialIntegrity, "DirectQuery join optimization requires proven referential integrity.");
        if (before.FromColumn == requested.ToColumn && before.ToColumn == requested.FromColumn && requested.CrossFilteringBehavior == CrossFilteringBehavior.OneDirection)
            issues.Add(new("INVERT_FILTER", "Inverting endpoints also reverses the one-direction filter flow and security direction. This is an explicit semantic change.", AuthoringIssueSeverity.Warning, path));
        // Endpoint swaps cannot be performed as ordinary sequential assignments: TE2 vetoes assigning the existing opposite endpoint.
        // Clear and replace the endpoints inside one shared undo batch; every displayed property is validated after all mutations.
        var edits = changes.Select((change, index) => new AuthoringEdit(change,
            index == 0 ? () => ApplyDefinition(relationship, requested) : () => { },
            () => Capture(relationship) == requested)).ToArray();
        return AuthoringPreview.Create(handler, "Edit relationship: " + relationship.Name, edits, issues);
    }

    private List<AuthoringIssue> Validate(SingleColumnRelationship relationship, RelationshipDefinition definition, string path)
    {
        var issues = new List<AuthoringIssue>();
        void Error(string code, string message) => issues.Add(new(code, message, AuthoringIssueSeverity.Error, path));
        void Warn(string code, string message) => issues.Add(new(code, message, AuthoringIssueSeverity.Warning, path));
        bool Contains(Column? column) => column != null && handler.Model.Tables.Any(table => table.Columns.Any(item => ReferenceEquals(item, column)));
        if (!Contains(definition.FromColumn) || !Contains(definition.ToColumn)) { Error("ENDPOINT", "Both columns must exist in the current model."); return issues; }
        var from = definition.FromColumn; var to = definition.ToColumn;
        if (ReferenceEquals(from.Table, to.Table)) Error("SAME_TABLE", "A model relationship must connect different tables.");
        if (from.DataType != to.DataType || !Enum.IsDefined(typeof(DataType), from.DataType) || from.DataType == DataType.Binary || from.DataType == DataType.Unknown || from.DataType == DataType.Automatic)
            Error("COLUMN_TYPE", "Relationship columns must use the same supported, explicit data type.");
        if (!Enum.IsDefined(typeof(RelationshipEndCardinality), definition.FromCardinality) || !Enum.IsDefined(typeof(RelationshipEndCardinality), definition.ToCardinality)
            || (int)definition.FromCardinality < 1 || (int)definition.ToCardinality < 1) Error("CARDINALITY", "Choose One or Many for each endpoint.");
        if (!Enum.IsDefined(typeof(CrossFilteringBehavior), definition.CrossFilteringBehavior) || (int)definition.CrossFilteringBehavior < 1) Error("FILTER", "Unsupported cross-filter direction.");
        if (!Enum.IsDefined(typeof(SecurityFilteringBehavior), definition.SecurityFilteringBehavior) || (int)definition.SecurityFilteringBehavior < 1) Error("SECURITY", "Unsupported security filtering behavior.");
        if (!Enum.IsDefined(typeof(DateTimeRelationshipBehavior), definition.JoinOnDateBehavior) || (int)definition.JoinOnDateBehavior < 1) Error("DATE_JOIN", "Unsupported date joining behavior.");
        if (definition.FromCardinality == RelationshipEndCardinality.One && definition.ToCardinality == RelationshipEndCardinality.One && definition.CrossFilteringBehavior != CrossFilteringBehavior.BothDirections)
            Error("ONE_TO_ONE", "A one-to-one relationship requires filtering in both directions.");
        if (definition.FromCardinality == RelationshipEndCardinality.Many && definition.ToCardinality == RelationshipEndCardinality.Many)
        {
            if (handler.Database.CompatibilityLevel < 1400) Error("COMPATIBILITY", "Many-to-many relationships require compatibility level 1400 or later.");
            Warn("LIMITED", "Many-to-many relationships are limited relationships; blank-row and RELATED behavior differs from regular relationships.");
        }
        if (definition.SecurityFilteringBehavior == SecurityFilteringBehavior.BothDirections && definition.CrossFilteringBehavior != CrossFilteringBehavior.BothDirections)
            Error("SECURITY_DIRECTION", "Security filtering in both directions requires cross-filtering in both directions.");
        var others = handler.Model.Relationships.OfType<SingleColumnRelationship>().Where(item => !ReferenceEquals(item, relationship) && item.FromColumn != null && item.ToColumn != null).ToArray();
        if (others.Any(item => (item.FromColumn == from && item.ToColumn == to) || (item.ToColumn == from && item.FromColumn == to)))
            Error("DUPLICATE", "Another relationship already connects these two columns.");
        if (definition.IsActive && others.Any(item => item.IsActive && ((item.FromTable == from.Table && item.ToTable == to.Table) || (item.FromTable == to.Table && item.ToTable == from.Table))))
            Error("ACTIVE_PARALLEL", "Another active relationship already connects these tables. Preview deactivating it before activating this relationship.");
        if (definition.IsActive && HasAlternatePath(others.Where(item => item.IsActive), from.Table, to.Table))
            Warn("FILTER_PATH", "An alternate active path connects these tables. Engine ambiguity rules depend on cardinality, direction and query context; validate representative queries before deploying.");
        if (definition.CrossFilteringBehavior == CrossFilteringBehavior.BothDirections) Warn("BIDIRECTIONAL", "Bidirectional filtering can introduce ambiguous paths and increase query work.");
        if (definition.CrossFilteringBehavior == CrossFilteringBehavior.Automatic) Warn("AUTOMATIC_FILTER", "The server chooses the cross-filter direction; the diagram cannot predict the resolved direction.");
        if (definition.SecurityFilteringBehavior == SecurityFilteringBehavior.BothDirections) Warn("RLS", "Bidirectional security changes RLS propagation. Test every affected role before deploying.");
        if (definition.SecurityFilteringBehavior == SecurityFilteringBehavior.None)
        {
            if (handler.Database.CompatibilityLevel < 1561) Error("SECURITY_COMPATIBILITY", "Disabling security filtering requires compatibility level 1561 or later.");
            Warn("SECURITY_LIMITED", "Security filtering None is only supported for qualifying limited relationships involving a remote semantic-model source. The engine must validate source-group eligibility; test all affected roles.");
        }
        if (definition.JoinOnDateBehavior == DateTimeRelationshipBehavior.DatePartOnly && from.DataType != DataType.DateTime) Error("DATE_TYPE", "Date-part joining requires DateTime columns.");
        bool DirectQuery(Table table) => table.Partitions.Count > 0 && table.Partitions.All(partition => (partition.Mode == ModeType.Default ? table.Model.DefaultMode : partition.Mode) == ModeType.DirectQuery);
        if (definition.RelyOnReferentialIntegrity)
        {
            if (!DirectQuery(from.Table) || !DirectQuery(to.Table)) Error("REFERENTIAL_MODE", "Assume referential integrity is supported here only when both tables use DirectQuery partitions.");
            Warn("REFERENTIAL_DATA", "Assume referential integrity can exclude unmatched rows. This preview does not prove source consistency, absence of nulls, or matching source groups.");
        }
        var modes = new[] { from.Table, to.Table }.SelectMany(table => table.Partitions.Select(partition => (partition.Mode == ModeType.Default ? table.Model.DefaultMode : partition.Mode).ToString())).Distinct().ToArray();
        if (modes.Any(mode => mode != "Import")) Warn("COMPOSITE_SOURCE", "Composite, DirectQuery and Direct Lake relationship behavior also depends on source groups. Local metadata checks do not verify engine/source support or data uniqueness.");
        issues.Add(new("METADATA_ONLY", "Local metadata validation only. Use Data relationship coverage to examine keys; server validation, RLS and refresh checks remain separate. This action does not save, deploy or refresh.", AuthoringIssueSeverity.Information, path));
        return issues;
    }

    private static bool HasAlternatePath(IEnumerable<SingleColumnRelationship> relationships, Table from, Table to)
    {
        var edges = relationships.ToArray(); var visited = new HashSet<Table> { from }; var pending = new Queue<Table>(); pending.Enqueue(from);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var edge in edges.Where(item => item.FromTable == current || item.ToTable == current))
            {
                var next = edge.FromTable == current ? edge.ToTable : edge.FromTable;
                if (next == to) return true;
                if (visited.Add(next)) pending.Enqueue(next);
            }
        }
        return false;
    }

    private static void ApplyDefinition(SingleColumnRelationship relationship, RelationshipDefinition definition)
    {
        if (relationship.FromColumn != definition.FromColumn || relationship.ToColumn != definition.ToColumn)
        {
            relationship.FromColumn = null!;
            relationship.ToColumn = null!;
            relationship.FromColumn = definition.FromColumn;
            relationship.ToColumn = definition.ToColumn;
        }
        relationship.FromCardinality = definition.FromCardinality; relationship.ToCardinality = definition.ToCardinality;
        relationship.CrossFilteringBehavior = definition.CrossFilteringBehavior; relationship.IsActive = definition.IsActive;
        relationship.SecurityFilteringBehavior = definition.SecurityFilteringBehavior; relationship.JoinOnDateBehavior = definition.JoinOnDateBehavior;
        relationship.RelyOnReferentialIntegrity = definition.RelyOnReferentialIntegrity;
    }

    private static string ColumnPath(Column? column) => column == null ? "(none)" : SemanticModelService.ObjectPath(column);
}
