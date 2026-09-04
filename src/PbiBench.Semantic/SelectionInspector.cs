using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic;

public enum InspectorAction
{
    EditDax, FormatDax, Dependencies, BestPractices, PreviewSafeFixes,
    AnalyzeInDaxStudio, ShowDiagram, GoToFromTable, GoToToTable
}

public sealed record InspectorField(string Label, string Value);

public sealed record SelectionInspectorSnapshot(string Kind, string Title, string Path, string Expression,
    IReadOnlyList<InspectorField> Fields, int DependencyCount, int ReferenceCount, int? BpaFindingCount,
    IReadOnlyList<InspectorAction> Actions, IReadOnlyList<string> Dependencies, IReadOnlyList<string> References);

/// <summary>A focused read-only projection. The native property grid remains the full metadata editor.</summary>
public static class SelectionInspector
{
    public static SelectionInspectorSnapshot Create(IEnumerable<TabularNamedObject> selection,
        Func<TabularNamedObject, int>? findingCount = null)
    {
        if (selection == null) throw new ArgumentNullException(nameof(selection));
        var objects = selection.Distinct().ToArray();
        if (objects.Length == 0)
            return new("No selection", "Select a model object", "", "",
                new[] { new InspectorField("Tip", "Select a table, column, measure or relationship in the model tree.") },
                0, 0, null, Array.Empty<InspectorAction>(), Array.Empty<string>(), Array.Empty<string>());

        var dependencies = objects.OfType<IDaxDependantObject>().SelectMany(o => o.DependsOn.Keys)
            .Select(o => o.DaxObjectFullName).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        var references = objects.OfType<IDaxObject>().SelectMany(o => o.ReferencedBy)
            .OfType<TabularNamedObject>().Select(SemanticModelService.ObjectPath).Distinct()
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        var findings = findingCount == null ? (int?)null : objects.Sum(findingCount);
        if (objects.Length > 1)
            return new("Multiple selection", $"{objects.Length} objects selected", "", "",
                objects.Select(o => new InspectorField(o.ObjectTypeName, SemanticModelService.ObjectPath(o))).ToArray(),
                dependencies.Length, references.Length, findings,
                new[] { InspectorAction.BestPractices, InspectorAction.PreviewSafeFixes }, dependencies, references);

        var item = objects[0];
        var fields = new List<InspectorField>();
        var actions = new List<InspectorAction>();
        void Field(string label, object? value) => fields.Add(new(label, value?.ToString() is { Length: > 0 } text ? text : "—"));
        if (item is ITabularTableObject child) Field("Table", child.Table.Name);
        var kind = item.ObjectTypeName;
        switch (item)
        {
            case Measure measure:
                kind = "Measure";
                Field("Format", measure.FormatString);
                Field("Display folder", measure.DisplayFolder);
                Field("Hidden", measure.IsHidden);
                Field("Test status", "No model test results available");
                actions.AddRange(new[] { InspectorAction.EditDax, InspectorAction.FormatDax,
                    InspectorAction.Dependencies, InspectorAction.AnalyzeInDaxStudio });
                break;
            case Column column:
                kind = "Column";
                Field("Data type", column.DataType);
                Field("Hidden", column.IsHidden);
                Field("Summarize by", column.SummarizeBy);
                Field("Key", column.IsKey);
                Field("Source column", (column as DataColumn)?.SourceColumn);
                Field("Display folder", column.DisplayFolder);
                actions.AddRange(new[] { InspectorAction.Dependencies, InspectorAction.PreviewSafeFixes });
                if (column is IExpressionObject) actions.Add(InspectorAction.EditDax);
                break;
            case SingleColumnRelationship relationship:
                kind = "Relationship";
                Field("From", relationship.FromColumn == null ? null : SemanticModelService.ObjectPath(relationship.FromColumn));
                Field("To", relationship.ToColumn == null ? null : SemanticModelService.ObjectPath(relationship.ToColumn));
                Field("Cardinality", $"{relationship.FromCardinality} → {relationship.ToCardinality}");
                Field("State", relationship.IsActive ? "Active" : "Inactive");
                Field("Cross-filter direction", relationship.CrossFilteringBehavior);
                Field("Security filtering", relationship.SecurityFilteringBehavior);
                actions.AddRange(new[] { InspectorAction.GoToFromTable, InspectorAction.GoToToTable, InspectorAction.ShowDiagram });
                break;
            case CalculationItem calculation:
                kind = "Calculation item";
                Field("Ordinal", calculation.Ordinal);
                Field("Format expression", calculation.FormatStringExpression);
                actions.Add(InspectorAction.EditDax);
                break;
            case Table table:
                kind = table is CalculationGroupTable ? "Calculation group" : "Table";
                Field("Columns", table.Columns.Count);
                Field("Measures", table.Measures.Count);
                Field("Partitions", table.Partitions.Count);
                Field("Hidden", table.IsHidden);
                if (table is CalculationGroupTable group)
                {
                    Field("Calculation items", group.CalculationItems.Count);
                    Field("Precedence", group.CalculationGroupPrecedence);
                }
                actions.AddRange(new[] { InspectorAction.Dependencies, InspectorAction.ShowDiagram });
                break;
        }
        if (item is IDescriptionObject described) Field("Description", described.Description);
        actions.Add(InspectorAction.BestPractices);
        return new(kind, item.Name, SemanticModelService.ObjectPath(item),
            (item as IExpressionObject)?.Expression ?? "", fields, dependencies.Length, references.Length,
            findings, actions, dependencies, references);
    }
}
