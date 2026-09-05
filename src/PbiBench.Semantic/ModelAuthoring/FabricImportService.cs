using System.Text.Json;
using PbiBench.Core.Fabric;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed record FabricSchemaDifference(string Category, string? SemanticColumn, string? SourceColumn, string Before, string After, string Reason);

/// <summary>Original local authoring plans over captured public Fabric schemas. No source query, remote save or refresh runs here.</summary>
public sealed partial class FabricImportService
{
    private const string SourceAnnotation = "PbiBench.FabricSource";
    private readonly TabularModelHandler handler;
    public FabricImportService(TabularModelHandler handler) => this.handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public AuthoringPreview PreviewImport(FabricImportRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var schema = Freeze(request.Schema); var names = Selected(request.Columns, schema);
        var tableName = request.TargetTableName ?? schema.Source.Table; AuthoringObjects.Name(tableName);
        var issues = ValidateSource(schema, request.Mode); var edits = new List<AuthoringEdit>();
        if (handler.Model.Tables.Any(table => string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase))) Error(issues, "FABRIC_TABLE_EXISTS", "A table with this name already exists. Choose another name or use schema compare.");
        ValidateModes(request.Mode, schema.Source, null, issues);
        var columns = names.Select(name => schema.Columns.Single(column => column.Name == name)).ToArray();
        var mappings = columns.Select(column => (Column: column, Type: Map(column, schema.Source, request.Mode, issues))).ToArray();
        if (issues.Any(issue => issue.Severity == AuthoringIssueSeverity.Error)) return AuthoringPreview.Create(handler, "Import Fabric table " + tableName, Array.Empty<AuthoringEdit>(), issues);
        NamedExpression? expression = null; var expressionName = ExpressionName(schema.Source, request.Mode);
        AddConnectionEdits(edits, issues, schema.Source, request.Mode, expressionName, value => expression = value);
        Table? created = null;
        edits.Add(new(new(tableName, "Create table", "(absent)", tableName, "Creates local metadata only; native defaults and generated lineage identities are retained."), () =>
        {
            // Native AddTable can otherwise synthesize an unrelated legacy provider data source.
            var useM = handler.Settings.UsePowerQueryPartitionsByDefault;
            try { handler.Settings.UsePowerQueryPartitionsByDefault = true; created = handler.Model.AddTable(tableName); }
            finally { handler.Settings.UsePowerQueryPartitionsByDefault = useM; }
        }, () => created != null && created.Name == tableName));
        foreach (var mapping in mappings)
        {
            DataColumn? createdColumn = null;
            edits.Add(new(new(tableName + "/" + mapping.Column.Name, "Create column", "(absent)", "SourceColumn=" + mapping.Column.Name + "; DataType=" + mapping.Type + "; SummarizeBy=None",
                "Source type " + mapping.Column.SourceType + "; schema captured " + schema.CapturedAt.ToString("u")),
                () => { createdColumn = created!.AddDataColumn(mapping.Column.Name, mapping.Column.Name, dataType: mapping.Type); createdColumn.SummarizeBy = AggregateFunction.None; },
                () => createdColumn != null && createdColumn.SourceColumn == mapping.Column.Name && createdColumn.DataType == mapping.Type && createdColumn.SummarizeBy == AggregateFunction.None));
        }
        AddPartitionEdit(edits, () => created!, tableName, schema.Source, request.Mode, expressionName, () => expression, "(absent)");
        var annotation = Annotation(schema.Source, request.Mode);
        edits.Add(new(new(tableName, SourceAnnotation, "(absent)", annotation, "Versioned source identity for later schema comparison; contains no credentials."),
            () => created!.SetAnnotation(SourceAnnotation, annotation), () => created!.GetAnnotation(SourceAnnotation) == annotation));
        return AuthoringPreview.Create(handler, "Import Fabric table " + tableName, edits, issues);
    }

    public AuthoringPreview PreviewConversion(string tableName, FabricTableSchema source, FabricStorageMode mode)
    {
        var table = Table(tableName); var schema = Freeze(source); var issues = ValidateSource(schema, mode); var edits = new List<AuthoringEdit>();
        if (mode is not (FabricStorageMode.DirectLakeOneLake or FabricStorageMode.Import)) Error(issues, "FABRIC_CONVERSION_MODE", "Conversion supports Import → Direct Lake on OneLake and Direct Lake on OneLake → Import. Other modes require a separately authored plan.");
        var currentModes = table.Partitions.Select(EffectiveMode).Distinct().ToArray();
        if (table.Partitions.Count == 0 || table is CalculatedTable or CalculationGroupTable) Error(issues, "FABRIC_CONVERSION_TABLE", "Choose a regular source table with partitions.");
        if (mode == FabricStorageMode.DirectLakeOneLake && (currentModes.Length != 1 || currentModes[0] != ModeType.Import)) Error(issues, "FABRIC_CONVERSION_SOURCE", "The source table must contain only Import partitions.");
        if (mode == FabricStorageMode.Import && (currentModes.Length != 1 || currentModes[0] != ModeType.DirectLake || table.Partitions.Any(partition => DirectLakeKind(partition) != FabricStorageMode.DirectLakeOneLake)))
            Error(issues, "FABRIC_CONVERSION_SOURCE", "The source table must have a recognized Direct Lake on OneLake source expression.");
        if (table.EnableRefreshPolicy) Error(issues, "FABRIC_REFRESH_POLICY", "This table has an incremental refresh policy. Review and remove that policy explicitly before replacing its partitions.");
        if (table.Hierarchies.Count > 0 && mode == FabricStorageMode.DirectLakeOneLake) Error(issues, "FABRIC_HIERARCHY", "Direct Lake tables do not support user-defined hierarchies. Review these objects separately before conversion.");
        foreach (var column in table.Columns.OfType<DataColumn>())
        {
            var match = schema.Columns.FirstOrDefault(item => item.Name == column.SourceColumn);
            if (match == null) Error(issues, "FABRIC_MAPPING", "No exact source match for " + column.Name + " → " + column.SourceColumn + ". Transformations are not inferred or moved to the source.");
            else if (Map(match, schema.Source, mode, issues) != column.DataType) Error(issues, "FABRIC_TYPE", column.Name + " has a different semantic type from the captured source mapping. Review the type explicitly before conversion.");
        }
        if (!table.Columns.OfType<DataColumn>().Any()) Error(issues, "FABRIC_MAPPING", "Conversion requires at least one directly mapped source column.");
        foreach (var calculated in table.Columns.OfType<CalculatedColumn>())
            if (mode == FabricStorageMode.DirectLakeOneLake && (handler.CompatibilityLevel < 1705 || calculated.ExpressionContext != ExpressionContext.UserContext) ||
                mode == FabricStorageMode.Import && handler.CompatibilityLevel >= 1705 && calculated.ExpressionContext == ExpressionContext.UserContext)
                Error(issues, "FABRIC_CALCULATED_COLUMN", "Review calculated column " + calculated.Name + ". OneLake calculated columns require the supported UserContext feature; the conversion does not rewrite its evaluation context.");
        ValidateModes(mode, schema.Source, table, issues);
        if (issues.Any(issue => issue.Severity == AuthoringIssueSeverity.Error)) return AuthoringPreview.Create(handler, "Convert " + tableName, Array.Empty<AuthoringEdit>(), issues);
        issues.Add(new("FABRIC_TRANSFORMATION_LOSS", "All existing partitions listed in the preview are replaced. Import M/SQL transformations, partition filters and processing settings are not carried across. Verify this is the intended 1:1 source mapping.", AuthoringIssueSeverity.Warning, tableName));
        NamedExpression? expression = null; var expressionName = ExpressionName(schema.Source, mode);
        AddConnectionEdits(edits, issues, schema.Source, mode, expressionName, value => expression = value);
        var oldPartitions = table.Partitions.ToArray();
        foreach (var partition in oldPartitions)
        {
            var captured = partition;
            edits.Add(new(new(tableName + "/" + captured.Name, "Remove partition", PartitionDescription(captured), "(removed)", "Transformation/partition loss is explicit. Native Undo restores this partition and its settings."),
                () => { }, () => !table.Partitions.Contains(captured)));
        }
        AddPartitionEdit(edits, () => table, tableName, schema.Source, mode, expressionName, () => expression, "(absent)");
        var annotation = Annotation(schema.Source, mode);
        edits.Add(new(new(tableName, SourceAnnotation, table.GetAnnotation(SourceAnnotation) ?? "(absent)", annotation, "Records the reviewed source mapping; unused shared expressions are retained."),
            () => table.SetAnnotation(SourceAnnotation, annotation), () => table.GetAnnotation(SourceAnnotation) == annotation));
        return AuthoringPreview.Create(handler, "Convert " + tableName + " to " + mode, edits, issues);
    }

    private void AddConnectionEdits(List<AuthoringEdit> edits, List<AuthoringIssue> issues, FabricSourceRef source, FabricStorageMode mode, string name, Action<NamedExpression> assigned)
    {
        if (mode is not (FabricStorageMode.DirectLakeOneLake or FabricStorageMode.DirectLakeSql)) return;
        var text = ConnectionM(source, mode); var existing = handler.Model.Expressions.FirstOrDefault(item => item.Name == name);
        if (existing != null)
        {
            if (existing.Expression != text || existing.Kind != ExpressionKind.M) Error(issues, "FABRIC_EXPRESSION_COLLISION", "The generated source expression name already contains different metadata. Existing expressions are never overwritten.");
            assigned(existing);
        }
        else
        {
            NamedExpression? created = null;
            edits.Add(new(new("Expressions/" + name, "Create M source", "(absent)", text, "Shared source expression for an entity partition."),
                () => { created = handler.Model.AddExpression(name); created.Kind = ExpressionKind.M; created.Expression = text; assigned(created); },
                () => created?.Expression == text && created.Kind == ExpressionKind.M));
        }
        if (mode == FabricStorageMode.DirectLakeOneLake && handler.CompatibilityLevel >= 1604 && handler.Model.DirectLakeBehavior != DirectLakeBehavior.DirectLakeOnly)
            edits.Add(new(new("Model", "DirectLakeBehavior", handler.Model.DirectLakeBehavior.ToString(), "DirectLakeOnly", "OneLake does not support SQL DirectQuery fallback."),
                () => handler.Model.DirectLakeBehavior = DirectLakeBehavior.DirectLakeOnly, () => handler.Model.DirectLakeBehavior == DirectLakeBehavior.DirectLakeOnly));
    }

    private static void AddPartitionEdit(List<AuthoringEdit> edits, Func<Table> table, string tableName, FabricSourceRef source, FabricStorageMode mode, string expressionName, Func<NamedExpression?> expression, string before)
    {
        Partition? created = null; var direct = mode is FabricStorageMode.DirectLakeOneLake or FabricStorageMode.DirectLakeSql;
        var text = direct ? "Mode=DirectLake; EntityName=" + source.Table + "; SchemaName=" + source.Schema + "; ExpressionSource=" + expressionName : "Mode=" + mode + "\n" + ImportM(source);
        edits.Add(new(new(tableName + "/" + tableName, "Create partition", before, text, "No data is loaded by this local metadata change. Configure target credentials and review refresh before using the table."), () =>
        {
            // TE2 intentionally refuses to delete the last partition. Create the replacement
            // with a native unique name first, remove the reviewed old partitions, then rename.
            var target = table(); var previous = target.Partitions.ToArray();
            if (direct) { var entity = target.AddEntityPartition(entityName: source.Table); entity.SchemaName = source.Schema; entity.ExpressionSource = expression()!; entity.Mode = ModeType.DirectLake; created = entity; }
            else { var partition = target.AddMPartition(expression: ImportM(source)); partition.Mode = mode == FabricStorageMode.Import ? ModeType.Import : ModeType.DirectQuery; created = partition; }
            // Native Undo appends restored partitions. Reverse deletion preserves their
            // original ordering as well as their source expressions and processing settings.
            foreach (var old in Enumerable.Reverse(previous)) old.Delete();
            created.Name = tableName;
        }, () => created != null && table().Partitions.Count == 1 && created.Name == tableName && (direct
            ? created is EntityPartition entity && entity.Mode == ModeType.DirectLake && entity.EntityName == source.Table && entity.SchemaName == source.Schema && entity.ExpressionSource?.Name == expressionName
            : created is MPartition partition && partition.Expression == ImportM(source) && partition.Mode == (mode == FabricStorageMode.Import ? ModeType.Import : ModeType.DirectQuery))));
    }
    private Table Table(string name) => handler.Model.Tables.FirstOrDefault(table => table.Name == name) ?? throw new ArgumentException("The target table is not in the current model.");
    private ModeType EffectiveMode(Partition partition) => partition.Mode == ModeType.Default ? handler.Model.DefaultMode : partition.Mode;
    private static void Error(List<AuthoringIssue> issues, string code, string message) => issues.Add(new(code, message, AuthoringIssueSeverity.Error));
    private static string Annotation(FabricSourceRef source, FabricStorageMode mode) => JsonSerializer.Serialize(new { Version = 1, Source = source, Mode = mode });
    private string PartitionDescription(Partition partition) => Microsoft.AnalysisServices.Tabular.JsonSerializer.SerializeObject(handler.Database.Model.Tables[partition.Table.Name].Partitions[partition.Name]);
}
