using System.Text;

namespace PbiBench.Core.DataExploration;

/// <summary>Original PbiBench profile queries over schema-resolved Microsoft DAX references.</summary>
public static class DataProfileBuilder
{
    private const string NonBlank = "__PbiBenchNonBlank";
    private const string NonBlankCount = "__PbiBenchNonBlankCount";
    private const string Frequency = "__PbiBenchFrequency";
    private const string Value = "[__PbiBenchValue]";
    private const string Occurrences = "[__PbiBenchOccurrences]";

    public static DataProfilePlan Column(DataTableSchema table, string column, DataProfileOptions? options = null)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        options ??= new DataProfileOptions(); options.Validate();
        var schema = ResolveColumn(table, column);
        var tableRef = DaxDataSyntax.Table(table.Name); var columnRef = DaxDataSyntax.Column(table.Name, schema.Name);
        var dataType = schema.DataType.ToLowerInvariant();
        var numeric = dataType is "int64" or "decimal" or "currency" or "double";
        var text = dataType == "string"; var date = dataType is "datetime" or "date"; var boolean = dataType == "boolean";
        if (!numeric && !text && !date && !boolean) throw new ArgumentException($"Column profiles are unavailable for data type '{schema.DataType}'.", nameof(column));

        var query = new StringBuilder("DEFINE\n");
        Variable(query, NonBlank, $"FILTER({tableRef}, NOT(ISBLANK({columnRef})))");
        Variable(query, NonBlankCount, $"COALESCE(COUNTROWS({NonBlank}), 0)");
        var groupCount = UniqueAlias(table, "__PbiBenchGroupCount");
        Variable(query, Frequency,
            $"SELECTCOLUMNS(SUMMARIZE({tableRef}, {columnRef}, {DaxDataSyntax.String(groupCount)}, COUNTROWS({tableRef})), " +
            $"\"__PbiBenchValue\", {columnRef}, \"__PbiBenchOccurrences\", [{groupCount}])");
        var metrics = new List<(string Name, string Expression)>
        {
            ("Rows", $"COALESCE(COUNTROWS({tableRef}), 0)"),
            ("Distinct including blank", $"COALESCE(COUNTROWS(DISTINCT({columnRef})), 0)"),
            ("Blank rows", CountWhere(tableRef, $"ISBLANK({columnRef})")),
            ("Nonblank rows", NonBlankCount),
            ("Distinct nonblank", $"COALESCE(COUNTROWS(SUMMARIZE({NonBlank}, {columnRef})), 0)")
        };
        var warnings = CostWarnings(table.StorageMode).ToList();
        warnings.Add("Counts describe the current identity's visible data. Blank rows are reported separately; aggregate statistics exclude blanks.");
        var names = new List<string> { "Column summary", "Top values" };
        var additionalResults = new List<string>();
        if (boolean)
        {
            metrics.Add(("False rows", CountWhere(NonBlank, $"{columnRef} == FALSE()")));
            metrics.Add(("True rows", CountWhere(NonBlank, $"{columnRef} == TRUE()")));
        }
        else
        {
            metrics.Add(("Minimum", $"MINX({NonBlank}, {columnRef})"));
            metrics.Add(("Maximum", $"MAXX({NonBlank}, {columnRef})"));
        }
        if (numeric)
        {
            metrics.Add(("Mean", $"AVERAGEX({NonBlank}, {columnRef})"));
            metrics.Add(("Median", $"MEDIANX({NonBlank}, {columnRef})"));
            metrics.Add(("StdDev population", $"IF({NonBlankCount} >= 2, STDEVX.P({NonBlank}, {columnRef}), IF({NonBlankCount} = 1, 0, BLANK()))"));
            if (options.IncludeAdvanced) AddNumeric(query, metrics, additionalResults, names, columnRef, options.TopCount);
            warnings.Add("Median scans the full nonblank column. Advanced outliers use the 1.5 × IQR rule; candidates are not automatic data errors.");
        }
        if (text)
        {
            metrics.Add(("Minimum length", $"MINX({NonBlank}, LEN({columnRef}))"));
            metrics.Add(("Maximum length", $"MAXX({NonBlank}, LEN({columnRef}))"));
            metrics.Add(("Mean length", $"AVERAGEX({NonBlank}, LEN({columnRef}))"));
            if (options.IncludeAdvanced) AddText(query, metrics, additionalResults, names, columnRef, options.TopCount);
            warnings.Add("Text ordering uses engine collation. Advanced numeric/date parsing reports candidates using engine locale and date interpretation; it does not infer a required type.");
        }
        if (date && options.IncludeAdvanced)
        {
            AddDates(query, metrics, additionalResults, names, columnRef, options.TopCount);
            warnings.Add("Date gaps compare calendar days between the first and last observed date, ignoring time of day. Largest-gap calculation can be costly on many distinct days; gaps can be legitimate.");
        }
        AppendSummary(query, metrics);
        query.AppendLine(FrequencyResult(Frequency, options.TopCount));
        foreach (var result in additionalResults) query.AppendLine(result);
        return new DataProfilePlan($"Profile · {table.Name}[{schema.Name}]", query.ToString(), names.ToArray(), warnings.ToArray(), true);
    }

    public static DataProfilePlan Relationship(DataModelSchema model, DataRelationshipSchema relationship, DataProfileOptions? options = null)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (relationship == null) throw new ArgumentNullException(nameof(relationship));
        options ??= new DataProfileOptions(); options.Validate();
        if (!model.Relationships.Contains(relationship)) throw new ArgumentException("The relationship is not in the current model schema.", nameof(relationship));
        var reversed = relationship.FromCardinality.Equals("One", StringComparison.OrdinalIgnoreCase) && relationship.ToCardinality.Equals("Many", StringComparison.OrdinalIgnoreCase);
        var conventional = reversed || (relationship.FromCardinality.Equals("Many", StringComparison.OrdinalIgnoreCase) && relationship.ToCardinality.Equals("One", StringComparison.OrdinalIgnoreCase));
        var foreignTable = model.GetTable(reversed ? relationship.ToTable : relationship.FromTable);
        var primaryTable = model.GetTable(reversed ? relationship.FromTable : relationship.ToTable);
        var foreign = ResolveColumn(foreignTable, reversed ? relationship.ToColumn : relationship.FromColumn);
        var primary = ResolveColumn(primaryTable, reversed ? relationship.FromColumn : relationship.ToColumn);
        var fkTable = DaxDataSyntax.Table(foreignTable.Name); var pkTable = DaxDataSyntax.Table(primaryTable.Name);
        var fk = DaxDataSyntax.Column(foreignTable.Name, foreign.Name); var pk = DaxDataSyntax.Column(primaryTable.Name, primary.Name);
        var fkLabel = conventional ? "FK" : "From"; var pkLabel = conventional ? "PK" : "To";
        var query = new StringBuilder("DEFINE\n");
        Variable(query, "__PbiBenchFK", $"FILTER(DISTINCT({fk}), NOT(ISBLANK({fk})))");
        Variable(query, "__PbiBenchPK", $"FILTER(DISTINCT({pk}), NOT(ISBLANK({pk})))");
        Variable(query, "__PbiBenchUnmatchedFK", "EXCEPT(__PbiBenchFK, __PbiBenchPK)");
        Variable(query, "__PbiBenchUnusedPK", "EXCEPT(__PbiBenchPK, __PbiBenchFK)");
        Variable(query, "__PbiBenchFKCount", "COALESCE(COUNTROWS(__PbiBenchFK), 0)");
        Variable(query, "__PbiBenchPKCount", "COALESCE(COUNTROWS(__PbiBenchPK), 0)");
        Variable(query, "__PbiBenchUnmatchedCount", "COALESCE(COUNTROWS(__PbiBenchUnmatchedFK), 0)");
        Variable(query, "__PbiBenchUnusedCount", "COALESCE(COUNTROWS(__PbiBenchUnusedPK), 0)");
        var metrics = new List<(string Name, string Expression)>
        {
            (fkLabel + " rows", $"COALESCE(COUNTROWS({fkTable}), 0)"),
            (fkLabel + " blank rows", CountWhere(fkTable, $"ISBLANK({fk})")),
            (fkLabel + " distinct nonblank", "__PbiBenchFKCount"),
            ("Unmatched " + fkLabel + " values", "__PbiBenchUnmatchedCount"),
            (fkLabel + " coverage fraction", "DIVIDE(__PbiBenchFKCount - __PbiBenchUnmatchedCount, __PbiBenchFKCount)"),
            (pkLabel + " rows", $"COALESCE(COUNTROWS({pkTable}), 0)"),
            (pkLabel + " blank rows", CountWhere(pkTable, $"ISBLANK({pk})")),
            (pkLabel + " distinct nonblank", "__PbiBenchPKCount"),
            ("Unused " + pkLabel + " values", "__PbiBenchUnusedCount"),
            (pkLabel + " coverage fraction", "DIVIDE(__PbiBenchPKCount - __PbiBenchUnusedCount, __PbiBenchPKCount)"),
            (pkLabel + " duplicate nonblank rows", CountWhere(pkTable, $"NOT(ISBLANK({pk}))") + " - __PbiBenchPKCount")
        };
        AppendSummary(query, metrics);
        query.AppendLine($"EVALUATE SELECTCOLUMNS(TOPN({options.TopCount}, __PbiBenchUnmatchedFK, {fk}, ASC), \"Unmatched {fkLabel} value\", {fk})\nORDER BY [Unmatched {fkLabel} value] ASC");
        query.AppendLine($"EVALUATE SELECTCOLUMNS(TOPN({options.TopCount}, __PbiBenchUnusedPK, {pk}, ASC), \"Unused {pkLabel} value\", {pk})\nORDER BY [Unused {pkLabel} value] ASC");
        var warnings = CostWarnings(foreignTable.StorageMode).Concat(CostWarnings(primaryTable.StorageMode)).Distinct().ToList();
        warnings.Add("Coverage compares distinct nonblank keys visible to the current identity. Blanks are separate; coverage is a fraction from 0 to 1 and remains blank when its denominator is zero.");
        if (!conventional) warnings.Add("This is not a many-to-one relationship. From/To labels are used without asserting foreign/primary key semantics.");
        if (!foreign.DataType.Equals(primary.DataType, StringComparison.OrdinalIgnoreCase)) warnings.Add("Endpoint types differ. EXCEPT compares values without type coercion; review the types when interpreting unmatched keys.");
        if (!relationship.IsActive) warnings.Add("This relationship is inactive. The profile compares key values without activating or modifying it.");
        return new DataProfilePlan("Coverage · " + relationship.Name, query.ToString(), new[] { "Relationship coverage", "Unmatched " + fkLabel, "Unused " + pkLabel }, warnings.ToArray(), true);
    }

    private static void AddNumeric(StringBuilder query, List<(string Name, string Expression)> metrics, List<string> results,
        List<string> names, string column, int topCount)
    {
        AddQuartiles(query, column);
        var outside = $"{column} < __PbiBenchLowerFence || {column} > __PbiBenchUpperFence";
        metrics.Add(("First quartile", "__PbiBenchQ1")); metrics.Add(("Third quartile", "__PbiBenchQ3"));
        metrics.Add(("IQR lower fence", "__PbiBenchLowerFence")); metrics.Add(("IQR upper fence", "__PbiBenchUpperFence"));
        metrics.Add(("IQR outlier rows", CountWhere(NonBlank, outside)));
        metrics.Add(("IQR outlier fraction", $"DIVIDE({CountWhere(NonBlank, outside)}, {NonBlankCount})"));
        names.Add("IQR outlier values");
        results.Add(FrequencyResult($"FILTER({Frequency}, NOT(ISBLANK({Value})) && ({Value} < __PbiBenchLowerFence || {Value} > __PbiBenchUpperFence))", topCount));
    }

    private static void AddText(StringBuilder query, List<(string Name, string Expression)> metrics, List<string> results,
        List<string> names, string column, int topCount)
    {
        AddQuartiles(query, $"LEN({column})");
        var trim = $"NOT(EXACT({column}, TRIM({column})))";
        var nbsp = $"CONTAINSSTRING({column}, UNICHAR(160))";
        var numeric = $"LEN(TRIM({column})) > 0 && IFERROR(ISNUMBER(VALUE({column})), FALSE())";
        var date = $"LEN(TRIM({column})) > 0 && IFERROR(NOT(ISBLANK(DATEVALUE({column}))), FALSE())";
        var length = $"LEN({column}) < __PbiBenchLowerFence || LEN({column}) > __PbiBenchUpperFence";
        metrics.Add(("Empty text rows", CountWhere(NonBlank, $"LEN({column}) = 0")));
        metrics.Add(("ASCII whitespace-only rows", CountWhere(NonBlank, $"LEN({column}) > 0 && LEN(TRIM({column})) = 0")));
        metrics.Add(("TRIM changes rows", CountWhere(NonBlank, trim)));
        metrics.Add(("Nonbreaking-space rows", CountWhere(NonBlank, nbsp)));
        metrics.Add(("Numeric text candidates", CountWhere(NonBlank, numeric)));
        metrics.Add(("Date text candidates", CountWhere(NonBlank, date)));
        metrics.Add(("Length IQR lower fence", "__PbiBenchLowerFence")); metrics.Add(("Length IQR upper fence", "__PbiBenchUpperFence"));
        metrics.Add(("Length outlier rows", CountWhere(NonBlank, length)));
        var issues = $"NOT(ISBLANK({Value})) && (NOT(EXACT({Value}, TRIM({Value}))) || CONTAINSSTRING({Value}, UNICHAR(160)) || LEN({Value}) < __PbiBenchLowerFence || LEN({Value}) > __PbiBenchUpperFence)";
        names.Add("Whitespace / length candidates"); results.Add(FrequencyResult($"FILTER({Frequency}, {issues})", topCount));
    }

    private static void AddDates(StringBuilder query, List<(string Name, string Expression)> metrics, List<string> results,
        List<string> names, string column, int topCount)
    {
        Variable(query, "__PbiBenchDays", $"DISTINCT(SELECTCOLUMNS({NonBlank}, \"__PbiBenchDay\", CONVERT(INT({column}), DATETIME)))");
        Variable(query, "__PbiBenchDayCount", "COALESCE(COUNTROWS(__PbiBenchDays), 0)");
        Variable(query, "__PbiBenchFirstDay", "MINX(__PbiBenchDays, [__PbiBenchDay])");
        Variable(query, "__PbiBenchLastDay", "MAXX(__PbiBenchDays, [__PbiBenchDay])");
        Variable(query, "__PbiBenchPredecessors", "ADDCOLUMNS(__PbiBenchDays, \"__PbiBenchPreviousDay\", VAR __PbiBenchCurrentDay = [__PbiBenchDay] RETURN MAXX(FILTER(__PbiBenchDays, [__PbiBenchDay] < __PbiBenchCurrentDay), [__PbiBenchDay]))");
        Variable(query, "__PbiBenchGaps", "FILTER(ADDCOLUMNS(__PbiBenchPredecessors, \"__PbiBenchMissingDays\", IF(NOT(ISBLANK([__PbiBenchPreviousDay])), DATEDIFF([__PbiBenchPreviousDay], [__PbiBenchDay], DAY) - 1, BLANK())), [__PbiBenchMissingDays] > 0)");
        metrics.Add(("Distinct calendar days", "__PbiBenchDayCount"));
        metrics.Add(("Missing days within observed range", "IF(__PbiBenchDayCount > 0, DATEDIFF(__PbiBenchFirstDay, __PbiBenchLastDay, DAY) + 1 - __PbiBenchDayCount, 0)"));
        metrics.Add(("Gap intervals", "COALESCE(COUNTROWS(__PbiBenchGaps), 0)"));
        metrics.Add(("Largest gap in days", "COALESCE(MAXX(__PbiBenchGaps, [__PbiBenchMissingDays]), 0)"));
        names.Add("Largest calendar gaps");
        results.Add($"EVALUATE SELECTCOLUMNS(TOPN({topCount}, __PbiBenchGaps, [__PbiBenchMissingDays], DESC, [__PbiBenchDay], ASC), \"Previous observed day\", [__PbiBenchPreviousDay], \"Next observed day\", [__PbiBenchDay], \"Missing days\", [__PbiBenchMissingDays])\nORDER BY [Missing days] DESC, [Next observed day] ASC");
    }

    private static void AddQuartiles(StringBuilder query, string expression)
    {
        Variable(query, "__PbiBenchQ1", $"IF({NonBlankCount} > 0, PERCENTILEX.INC({NonBlank}, {expression}, 0.25), BLANK())");
        Variable(query, "__PbiBenchQ3", $"IF({NonBlankCount} > 0, PERCENTILEX.INC({NonBlank}, {expression}, 0.75), BLANK())");
        Variable(query, "__PbiBenchLowerFence", "__PbiBenchQ1 - 1.5 * (__PbiBenchQ3 - __PbiBenchQ1)");
        Variable(query, "__PbiBenchUpperFence", "__PbiBenchQ3 + 1.5 * (__PbiBenchQ3 - __PbiBenchQ1)");
    }

    private static IEnumerable<string> CostWarnings(DataStorageMode storageMode)
    {
        yield return "Full-data profile: scans the referenced column/table and can be expensive. Review the generated DAX before Run; sample/display limits do not limit aggregate scan work.";
        if (storageMode is DataStorageMode.DirectQuery or DataStorageMode.Dual or DataStorageMode.Mixed)
            yield return "This storage mode can send profiling work to the source. Run explicitly, use the query timeout, and cancel if source load is unsuitable.";
        if (storageMode is DataStorageMode.DirectLake or DataStorageMode.Mixed)
            yield return "Direct Lake profiling can load referenced columns into capacity memory.";
        if (storageMode == DataStorageMode.Unknown) yield return "Storage mode is unknown; the engine may issue source queries or load model data.";
    }

    private static DataColumnSchema ResolveColumn(DataTableSchema table, string name) => table.Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Column '{name}' is not in table '{table.Name}'.", nameof(name));
    private static string UniqueAlias(DataTableSchema table, string preferred)
    {
        while (table.Columns.Any(c => c.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase))) preferred += "_";
        return preferred;
    }
    private static void Variable(StringBuilder query, string name, string expression) => query.Append("    VAR ").Append(name).Append(" = ").AppendLine(expression);
    private static string CountWhere(string table, string predicate) => $"COALESCE(COUNTROWS(FILTER({table}, {predicate})), 0)";
    private static string FrequencyResult(string relation, int count) => $"EVALUATE SELECTCOLUMNS(TOPN({count}, {relation}, {Occurrences}, DESC, {Value}, ASC), \"Value\", {Value}, \"Rows\", {Occurrences})\nORDER BY [Rows] DESC, [Value] ASC";
    private static void AppendSummary(StringBuilder query, IEnumerable<(string Name, string Expression)> metrics)
        => query.Append("EVALUATE ROW(\n    ").Append(string.Join(",\n    ", metrics.Select(metric => DaxDataSyntax.String(metric.Name) + ", " + metric.Expression))).AppendLine("\n)");
}
