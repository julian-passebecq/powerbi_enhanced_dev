using System.Collections.ObjectModel;

namespace PbiBench.Core.DataExploration;

/// <summary>Builds read-only model queries from schema-checked fields. Totals are evaluated by the engine.</summary>
public static class PivotQueryBuilder
{
    public static PivotQueryPlan Build(PivotLayout layout, DataModelSchema model)
    {
        ValidateShape(layout);
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (layout.Values.Count == 0) throw new ArgumentException("Add a measure or an explicit aggregation to Values before running the pivot.", nameof(layout));
        var axes = layout.Rows.Concat(layout.Columns).ToArray();
        if (axes.GroupBy(field => field.Table + "\0" + field.Column, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new ArgumentException("A model column can appear only once across Rows and Columns.", nameof(layout));
        foreach (var field in axes) GetColumn(model, field.Table, field.Column);
        var prefix = "__pbp";
        var names = new HashSet<string>(model.Tables.SelectMany(table => table.Columns.Select(column => column.Name).Concat(table.Measures.Select(measure => measure.Name)).Concat(new[] { table.Name })), StringComparer.OrdinalIgnoreCase);
        while (names.Any(name => name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))) prefix += "_";
        var rowFlag = prefix + "_row_total"; var columnFlag = prefix + "_column_total";
        var baseVariable = prefix + "_base"; var projectedVariable = prefix + "_projected";
        var groups = new List<string>(); var projection = new List<string>(); var ordering = new List<string>();
        var columns = new List<PivotResultColumn>();
        var rowTotals = layout.IncludeRowTotals && layout.Rows.Count > 0;
        var columnTotals = layout.IncludeColumnTotals && layout.Columns.Count > 0;
        AddAxis(layout.Rows, PivotResultRole.Row, "row", rowTotals, rowFlag);
        AddAxis(layout.Columns, PivotResultRole.Column, "column", columnTotals, columnFlag);
        foreach (var filter in layout.Filters)
        {
            var table = model.GetTable(filter.Table); var column = GetColumn(model, table.Name, filter.Column);
            var canonical = filter with { Table = table.Name, Column = column.Name };
            var reference = DaxDataSyntax.Column(table.Name, column.Name);
            string expression;
            if (filter.Operator is DataFilterOperator.Equals or DataFilterOperator.In)
            {
                var values = filter.Operator == DataFilterOperator.In ? filter.Values ?? new[] { filter.Value } : new[] { filter.Value };
                if (values.Count == 0 || values.Count > 500) throw new ArgumentException("Choose between 1 and 500 included values for a pivot filter.");
                expression = "TREATAS({ " + string.Join(", ", values.Select(value => DaxDataSyntax.Literal(value, column.DataType))) + " }, " + reference + ")";
            }
            else expression = "FILTER(ALL(" + reference + "), " + DaxDataSyntax.Predicate(canonical, column) + ")";
            groups.Add("KEEPFILTERS(" + expression + ")");
        }
        var valueCaptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < layout.Values.Count; index++)
        {
            var value = layout.Values[index]; var table = model.GetTable(value.Table);
            string expression;
            if (value.Aggregation == PivotAggregation.Measure)
            {
                var measure = table.Measures.FirstOrDefault(item => Same(item.Name, value.Name)) ?? throw new ArgumentException($"Measure '{value.Name}' is not present in table '{table.Name}'.");
                expression = DaxDataSyntax.Column(table.Name, measure.Name);
            }
            else
            {
                var column = GetColumn(model, table.Name, value.Name);
                if (value.Aggregation is PivotAggregation.Sum or PivotAggregation.Average && !IsNumeric(column.DataType))
                    throw new ArgumentException($"{value.Aggregation} requires a numeric column; '{table.Name}[{column.Name}]' is {column.DataType}.");
                if (value.Aggregation is PivotAggregation.Min or PivotAggregation.Max && !IsNumeric(column.DataType) && !Same(column.DataType, "DateTime") && !Same(column.DataType, "Date"))
                    throw new ArgumentException($"{value.Aggregation} requires a numeric or date column.");
                var function = value.Aggregation switch
                {
                    PivotAggregation.Sum => "SUM", PivotAggregation.Average => "AVERAGE", PivotAggregation.Min => "MIN",
                    PivotAggregation.Max => "MAX", PivotAggregation.Count => "COUNTA", PivotAggregation.DistinctCount => "DISTINCTCOUNT",
                    _ => throw new ArgumentOutOfRangeException(nameof(value.Aggregation))
                };
                expression = function + "(" + DaxDataSyntax.Column(table.Name, column.Name) + ")";
            }
            var key = prefix + "_value_" + index;
            var caption = string.IsNullOrWhiteSpace(value.Caption) ? value.Aggregation == PivotAggregation.Measure ? value.Name : value.Aggregation + " of " + value.Name : value.Caption!;
            var uniqueCaption = caption;
            for (var suffix = 2; !valueCaptions.Add(uniqueCaption); suffix++) uniqueCaption = caption + " (" + suffix + ")";
            groups.Add(DaxDataSyntax.String(key) + ", " + expression);
            AddProjection(key, uniqueCaption, PivotResultRole.Value, Member(key));
        }
        AddProjection(rowFlag, "Row total", PivotResultRole.RowTotalFlag, rowTotals ? Member(rowFlag) : "FALSE()");
        AddProjection(columnFlag, "Column total", PivotResultRole.ColumnTotalFlag, columnTotals ? Member(columnFlag) : "FALSE()");
        // All group keys plus rollup flags form the unique ordering tuple. Flags keep a real blank
        // member distinct from a total. TOPN caps transferred rows, not underlying evaluation cost.
        var sort = new List<string> { Member(rowFlag) + ", DESC", Member(columnFlag) + ", DESC" };
        sort.AddRange(ordering);
        var dax = "DEFINE\n    VAR " + baseVariable + " = SUMMARIZECOLUMNS(\n        " + string.Join(",\n        ", groups) +
            "\n    )\n    VAR " + projectedVariable + " = SELECTCOLUMNS(" + baseVariable + ",\n        " + string.Join(",\n        ", projection) +
            "\n    )\nEVALUATE\n    TOPN(" + (layout.RowLimit + 1) + ", " + projectedVariable + ", " + string.Join(", ", sort) +
            ")\nORDER BY " + string.Join(", ", sort.Select(item => item.Replace(", DESC", " DESC").Replace(", ASC", " ASC")));
        var warnings = new List<string> { "Totals are evaluated in their own filter context. Empty combinations with all measures blank are omitted.",
            "The row limit bounds returned results. Grouping, filters and totals may still scan substantial model data." };
        var modes = axes.Select(field => model.GetTable(field.Table).StorageMode)
            .Concat(layout.Values.Select(value => model.GetTable(value.Table).StorageMode))
            .Concat(layout.Filters.Select(filter => model.GetTable(filter.Table).StorageMode)).Distinct().ToArray();
        if (modes.Contains(DataStorageMode.DirectLake)) warnings.Add("Direct Lake pivot evaluation can load referenced columns into capacity memory.");
        if (modes.Any(mode => mode is DataStorageMode.DirectQuery or DataStorageMode.Dual or DataStorageMode.Mixed)) warnings.Add("This pivot may execute source queries. Auto refresh can generate repeated source work.");
        return new PivotQueryPlan(dax, new ReadOnlyCollection<PivotResultColumn>(columns), layout.RowLimit, warnings.AsReadOnly()) { Layout = Freeze(layout) };

        void AddAxis(IReadOnlyList<PivotAxisField> fields, PivotResultRole role, string axis, bool totals, string flag)
        {
            var references = new List<string>();
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i]; var table = model.GetTable(field.Table); var column = GetColumn(model, table.Name, field.Column);
                var reference = DaxDataSyntax.Column(table.Name, column.Name); references.Add(reference);
                var key = prefix + "_" + axis + "_" + i;
                AddProjection(key, table.Name + "[" + column.Name + "]", role, reference);
                ordering.Add(Member(key) + (field.Descending ? ", DESC" : ", ASC"));
            }
            if (references.Count > 0)
            {
                if (totals) groups.Add("ROLLUPADDISSUBTOTAL(ROLLUPGROUP(" + string.Join(", ", references) + "), " + DaxDataSyntax.String(flag) + ")");
                else groups.AddRange(references);
            }
        }
        void AddProjection(string key, string caption, PivotResultRole role, string expression)
        {
            projection.Add(DaxDataSyntax.String(key) + ", " + expression);
            columns.Add(new PivotResultColumn(key, caption, role, columns.Count));
        }
    }

    internal static void ValidateShape(PivotLayout layout)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (layout.Version != 1) throw new InvalidDataException("This pivot layout version is not supported.");
        if (string.IsNullOrWhiteSpace(layout.Name) || layout.Name.Length > 256) throw new ArgumentException("Choose a pivot name between 1 and 256 characters.");
        if (layout.Rows == null || layout.Columns == null || layout.Values == null || layout.Filters == null) throw new InvalidDataException("The pivot layout has missing field collections.");
        if (layout.Rows.Count > 32 || layout.Columns.Count > 32 || layout.Values.Count > 32 || layout.Filters.Count > 64)
            throw new ArgumentException("Pivot layouts support up to 32 fields per area and 64 filters.");
        if (layout.RowLimit < 1 || layout.RowLimit > 100000) throw new ArgumentOutOfRangeException(nameof(layout.RowLimit), "Choose 1 to 100,000 result rows.");
        if (layout.Rows.Concat(layout.Columns).Any(field => field == null || string.IsNullOrWhiteSpace(field.Table) || string.IsNullOrWhiteSpace(field.Column)))
            throw new ArgumentException("Each pivot axis field needs a model table and column.");
        if (layout.Values.Any(value => value == null || string.IsNullOrWhiteSpace(value.Table) || string.IsNullOrWhiteSpace(value.Name) || !Enum.IsDefined(typeof(PivotAggregation), value.Aggregation)))
            throw new ArgumentException("Each pivot value needs a model field and a supported aggregation.");
        if (layout.Filters.Any(filter => filter == null || string.IsNullOrWhiteSpace(filter.Table) || string.IsNullOrWhiteSpace(filter.Column) ||
            !Enum.IsDefined(typeof(DataFilterOperator), filter.Operator) || filter.Value?.Length > 16384 || filter.Values?.Count > 500 || filter.Values?.Any(value => value?.Length > 16384) == true))
            throw new ArgumentException("The pivot contains an invalid or oversized filter.");
    }
    internal static PivotLayout Freeze(PivotLayout layout) => layout with
    {
        Rows = Array.AsReadOnly(layout.Rows.ToArray()), Columns = Array.AsReadOnly(layout.Columns.ToArray()),
        Values = Array.AsReadOnly(layout.Values.ToArray()), Filters = Array.AsReadOnly(layout.Filters.Select(filter => filter with
        { Values = filter.Values == null ? null : Array.AsReadOnly(filter.Values.ToArray()) }).ToArray())
    };
    private static DataColumnSchema GetColumn(DataModelSchema model, string table, string name) => model.GetTable(table).Columns.FirstOrDefault(column => Same(column.Name, name))
        ?? throw new ArgumentException($"Column '{table}[{name}]' is not present in the current model schema.");
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool IsNumeric(string dataType) => new[] { "Int64", "Double", "Decimal", "Currency" }.Any(type => Same(type, dataType));
    private static string Member(string name) => "[" + name.Replace("]", "]]") + "]";
}
