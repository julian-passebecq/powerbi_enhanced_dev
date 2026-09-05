using PbiBench.Core.Fabric;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed partial class FabricImportService
{
    public IReadOnlyList<FabricSchemaDifference> CompareSchema(string tableName, FabricTableSchema source)
    {
        var table = Table(tableName); var schema = Freeze(source); var differences = new List<FabricSchemaDifference>();
        var mode = CurrentMode(table); var mappings = table.Columns.OfType<DataColumn>().ToArray();
        if (!MatchesSource(table, schema.Source, mode)) differences.Add(new("Mapping mismatch", null, null, "Existing partition source", schema.Source.DisplayName,
            "The table's current partition text/entity mapping does not match this selected source. No source is inferred from matching table names."));
        foreach (var column in mappings)
        {
            var match = schema.Columns.FirstOrDefault(item => item.Name == column.SourceColumn);
            if (match == null)
            {
                differences.Add(new("Removed source column", column.Name, column.SourceColumn, column.DataType.ToString(), "(not in captured schema)", "No semantic object is removed automatically. Check dependencies and source access first."));
                var candidate = schema.Columns.FirstOrDefault(item => string.Equals(item.Name, column.SourceColumn, StringComparison.OrdinalIgnoreCase));
                if (candidate != null) differences.Add(new("Rename candidate", column.Name, candidate.Name, column.SourceColumn ?? "", candidate.Name, "Case-only similarity is a candidate, not proof of source identity. Resolve mapping explicitly in the model editor."));
                continue;
            }
            var issues = new List<AuthoringIssue>(); var mapped = Map(match, schema.Source, mode ?? FabricStorageMode.Import, issues);
            if (issues.Any(issue => issue.Severity == AuthoringIssueSeverity.Error)) differences.Add(new("Unsupported source type", column.Name, match.Name, column.DataType.ToString(), match.SourceType, string.Join(" ", issues.Select(issue => issue.Message))));
            else if (mapped != column.DataType) differences.Add(new("Type change", column.Name, match.Name, column.DataType.ToString(), mapped.ToString(), "Source type " + match.SourceType + ". Select explicitly to preview a semantic type change; relationships and dependants must remain valid."));
        }
        foreach (var column in schema.Columns.Where(column => !mappings.Any(mapped => mapped.SourceColumn == column.Name)))
        {
            var collision = table.Columns.FirstOrDefault(existing => string.Equals(existing.Name, column.Name, StringComparison.OrdinalIgnoreCase));
            differences.Add(new(collision == null ? "New source column" : "Mapping mismatch", collision?.Name, column.Name, collision == null ? "(absent)" : "Existing semantic name with another mapping", column.SourceType,
                collision == null ? "Select this source column to preview adding its metadata." : "This source name collides with existing semantic metadata. Rename or remap explicitly; it will not be replaced."));
        }
        // Name similarity is intentionally weak evidence. No candidate becomes an edit automatically.
        var removed = differences.Where(item => item.Category == "Removed source column").ToArray();
        var added = differences.Where(item => item.Category == "New source column").ToArray();
        if (removed.Length == 1 && added.Length == 1)
            differences.Add(new("Rename candidate", removed[0].SemanticColumn, added[0].SourceColumn, removed[0].SourceColumn ?? "", added[0].SourceColumn ?? "", "One removed and one added column may represent a rename, but identity is unproven. Review it manually."));
        return differences.ToArray();
    }

    public AuthoringPreview PreviewSchemaUpdate(string tableName, FabricTableSchema source, IReadOnlyList<string> selectedSourceColumns)
    {
        var table = Table(tableName); var schema = Freeze(source); var selected = Selected(selectedSourceColumns, schema);
        var mode = CurrentMode(table); var issues = new List<AuthoringIssue>(); var edits = new List<AuthoringEdit>();
        if (mode == null || !MatchesSource(table, schema.Source, mode)) Error(issues, "FABRIC_SOURCE_MISMATCH", "The selected source does not match a recognized current partition mapping. Review and establish that mapping first; this update cannot silently rebind the table.");
        foreach (var change in CompareSchema(tableName, schema).Where(change => change.Category is "Removed source column" or "Rename candidate" or "Mapping mismatch"))
            issues.Add(new("FABRIC_SCHEMA_REVIEW", change.Category + ": " + (change.SemanticColumn ?? tableName) + ". " + change.Reason, AuthoringIssueSeverity.Warning));
        foreach (var name in selected)
        {
            var column = schema.Columns.Single(item => item.Name == name); var type = Map(column, schema.Source, mode ?? FabricStorageMode.Import, issues);
            var existing = table.Columns.OfType<DataColumn>().Where(item => item.SourceColumn == name).ToArray();
            if (existing.Length > 1) { Error(issues, "FABRIC_AMBIGUOUS_MAPPING", "Multiple semantic columns map to " + name + ". Review these mappings individually in the model editor."); continue; }
            if (existing.Length == 1)
            {
                var target = existing[0]; if (target.DataType == type) continue;
                var related = handler.Model.Relationships.OfType<SingleColumnRelationship>().Any(relationship => ReferenceEquals(relationship.FromColumn, target) || ReferenceEquals(relationship.ToColumn, target));
                if (related) { Error(issues, "FABRIC_RELATIONSHIP_TYPE", target.Name + " participates in a relationship. Review both endpoint types together before changing it."); continue; }
                if (target.SortByColumn != null || table.Columns.Any(item => ReferenceEquals(item.SortByColumn, target)))
                { Error(issues, "FABRIC_SORT_TYPE", target.Name + " participates in SortByColumn metadata. Review its ordering before changing type."); continue; }
                issues.Add(new("FABRIC_TYPE_DEPENDANTS", "Type change for " + target.Name + " can affect DAX, formatting and reports. Validate representative queries after the reviewed local edit.", AuthoringIssueSeverity.Warning));
                edits.Add(new(new(SemanticModelService.ObjectPath(target), "DataType", target.DataType.ToString(), type.ToString(), "Explicitly selected schema type update; source " + column.SourceType),
                    () => target.DataType = type, () => target.DataType == type));
            }
            else
            {
                if (table.Columns.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) { Error(issues, "FABRIC_COLUMN_COLLISION", "The source name " + name + " conflicts with another semantic column. Existing objects are not replaced."); continue; }
                DataColumn? created = null;
                edits.Add(new(new(tableName + "/" + name, "Create column", "(absent)", "SourceColumn=" + name + "; DataType=" + type + "; SummarizeBy=None", "Explicitly selected new source column."),
                    () => { created = table.AddDataColumn(name, name, dataType: type); created.SummarizeBy = AggregateFunction.None; },
                    () => created != null && created.SourceColumn == name && created.DataType == type && created.SummarizeBy == AggregateFunction.None));
            }
        }
        if (edits.Count == 0) issues.Add(new("FABRIC_SCHEMA_UNCHANGED", "No selected source column needs an applicable metadata change.", AuthoringIssueSeverity.Information));
        return AuthoringPreview.Create(handler, "Update source schema for " + tableName, edits, issues);
    }

    private FabricStorageMode? CurrentMode(Table table)
    {
        if (table.Partitions.Count != 1 || table is CalculatedTable or CalculationGroupTable) return null;
        var partition = table.Partitions[0];
        return EffectiveMode(partition) switch { ModeType.Import => FabricStorageMode.Import, ModeType.DirectQuery => FabricStorageMode.DirectQuery, ModeType.DirectLake => DirectLakeKind(partition), _ => null };
    }
    private static bool MatchesSource(Table table, FabricSourceRef source, FabricStorageMode? mode)
    {
        if (mode == null || table.Partitions.Count != 1) return false;
        var partition = table.Partitions[0];
        if (mode is FabricStorageMode.DirectLakeOneLake or FabricStorageMode.DirectLakeSql)
            return partition is EntityPartition entity && entity.EntityName == source.Table && entity.SchemaName == source.Schema &&
                CanonicalM(entity.ExpressionSource?.Expression ?? "") == CanonicalM(ConnectionM(source, mode.Value));
        return source.SqlEndpoint != null && partition is MPartition m && CanonicalM(m.Expression ?? "") == CanonicalM(ImportM(source));
    }
}
