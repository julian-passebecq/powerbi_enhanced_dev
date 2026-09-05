using PbiBench.AI.ContextExport;
using PbiBench.Core.Queries;
using PbiBench.Core.DataExploration;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic;

/// <summary>Call on the model-owning thread. Explicit presentation/DAX projection; never serialize TOM or source objects.</summary>
public static class AIContextCapture
{
    public static string Id(TabularNamedObject obj) => ContextModel.ObjectId(obj is Table ? "Table" : obj is Column ? "Column" : obj is Measure ? "Measure" : obj is SingleColumnRelationship ? "Relationship" : obj.ObjectType.ToString(), (obj as ITabularTableObject)?.Table.Name, obj.Name);
    public static ContextModel Capture(TabularModelHandler handler, bool includeRoles = false, CancellationToken ct = default)
    {
        var objects = new List<ContextObject>(); var native = new List<TabularNamedObject>();
        foreach (var table in handler.Model.Tables)
        {
            ct.ThrowIfCancellationRequested(); native.Add(table); native.AddRange(table.Columns); native.AddRange(table.Measures);
            if (table is CalculationGroupTable group) native.AddRange(group.CalculationItems);
        }
        native.AddRange(handler.Model.Functions);
        if (native.Count > 50000) throw new InvalidOperationException("Context capture is limited to 50,000 semantic objects.");
        foreach (var obj in native)
        {
            ct.ThrowIfCancellationRequested();
            var table = obj as Table;
            // Even calculated table partition definitions are omitted: there is no general source-expression escape hatch.
            var expression = obj is Measure or CalculatedColumn or CalculationItem or Function ? (obj as IExpressionObject)?.Expression : null;
            objects.Add(new(Id(obj), obj is Table ? "Table" : obj is Column ? "Column" : obj is Measure ? "Measure" : obj.ObjectType.ToString(), obj.Name,
                (obj as ITabularTableObject)?.Table.Name, (obj as IDescriptionObject)?.Description, expression,
                (obj as Column)?.DataType.ToString(), (obj as IHideableObject)?.IsHidden ?? false,
                (obj as IFolderObject)?.DisplayFolder, obj is Measure m ? m.FormatString : (obj as Column)?.FormatString,
                obj is Measure measure ? measure.FormatStringExpression : (obj as CalculationItem)?.FormatStringExpression,
                table == null ? null : string.Join(",", table.Partitions.Select(p => (p.Mode == ModeType.Default ? table.Model.DefaultMode : p.Mode).ToString()).Distinct().OrderBy(s => s, StringComparer.Ordinal)),
                (obj as CalculationItem)?.Ordinal));
            if (table is CalculationGroupTable calc) objects.Add(new(ContextModel.ObjectId("CalculationGroup", table.Name, table.Name), "CalculationGroup", table.Name, table.Name, Ordinal: calc.CalculationGroup.Precedence));
        }
        var rels = handler.Model.Relationships.OfType<SingleColumnRelationship>().Where(r => r.FromColumn != null && r.ToColumn != null)
            .Select(r => new ContextRelationship(Id(r), r.Name, Id(r.FromColumn), Id(r.ToColumn), r.IsActive, r.FromCardinality.ToString(), r.ToCardinality.ToString(), r.CrossFilteringBehavior.ToString())).ToArray();
        var dependencies = native.OfType<IDaxDependantObject>().SelectMany(obj => obj.DependsOn.Keys.OfType<TabularNamedObject>().Where(native.Contains).Select(dep => new ContextDependency(Id((TabularNamedObject)obj), Id(dep)))).Distinct().ToArray();
        var translations = new List<ContextTranslation>();
        foreach (var obj in native.OfType<ITranslatableObject>()) foreach (var culture in handler.Model.Cultures)
        {
            ct.ThrowIfCancellationRequested(); var id = Id((TabularNamedObject)obj);
            if (obj.TranslatedNames.Contains(culture)) translations.Add(new(id, culture.Name, "Name", obj.TranslatedNames[culture]));
            if (obj.TranslatedDescriptions.Contains(culture)) translations.Add(new(id, culture.Name, "Description", obj.TranslatedDescriptions[culture]));
            if (obj is IFolderObject folder && folder.TranslatedDisplayFolders.Contains(culture)) translations.Add(new(id, culture.Name, "DisplayFolder", folder.TranslatedDisplayFolders[culture]));
        }
        return new(handler.Database.Name, handler.CompatibilityLevel, objects.ToArray(), rels, dependencies)
        {
            Perspectives = handler.Model.Perspectives.Select(p => new ContextPerspective(p.Name, native.Where(o => o is ITabularPerspectiveObject member && member.InPerspective[p]).Select(Id).ToArray())).ToArray(),
            Translations = translations.ToArray(),
            Roles = includeRoles ? handler.Database.Model.Roles.SelectMany(r => r.TablePermissions.Select(p => new ContextRole(r.Name, p.Table.Name, p.FilterExpression ?? ""))).ToArray() : Array.Empty<ContextRole>()
        };
    }
}

/// <summary>Transient connection lives only in this adapter. Query result endpoint and timing context are never exported.</summary>
public sealed class SemanticContextSampler(IDaxQueryService queries, string server, string database, string? connectionString) : IContextSampler
{
    public async Task<SampleResult> SampleAsync(SampleRequest request, CancellationToken cancellationToken)
    {
        var table = DaxDataSyntax.Table(request.Table); var order = DaxDataSyntax.Column(request.Table, request.OrderColumn ?? request.Columns[0]);
        var projection = string.Join(", ", request.Columns.Select((c, i) => "\"C" + i + "\", " + DaxDataSyntax.Column(request.Table, c)));
        var orderIndex = request.Columns.ToList().IndexOf(request.OrderColumn ?? request.Columns[0]);
        var dax = $"EVALUATE\nSELECTCOLUMNS(TOPN({request.Rows}, {table}, {order}, ASC), {projection})\nORDER BY [C{orderIndex}] ASC";
        var result = await queries.ExecuteAsync(new QueryRequest(server, database, dax, request.Rows, 30)
        { ConnectionString = connectionString, MaximumResultSets = 1, MaximumCells = request.Rows * request.Columns.Count }, cancellationToken).ConfigureAwait(false);
        var set = result.Results.Single();
        if (set.Columns.Count != request.Columns.Count) throw new InvalidOperationException("Query returned an unexpected sample projection.");
        return new(request.Columns.ToArray(), set.Rows.Take(request.Rows).Select(r => r.ToArray()).ToArray());
    }
}
