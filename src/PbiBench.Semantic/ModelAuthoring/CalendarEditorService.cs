using PbiBench.Core.DataExploration;
using TabularEditor.TOMWrapper;
using TabularEditor.TOMWrapper.Utils;
using Calendar = TabularEditor.TOMWrapper.Calendar;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed record CalendarMapping(string TimeUnit, string PrimaryColumn, IReadOnlyList<string> AssociatedColumns);
public sealed record CalendarSortChange(string Column, string? SortByColumn);
public sealed record CalendarDraft(string Table, string? OriginalName, string Name, string Description, IReadOnlyList<CalendarMapping> Mappings, IReadOnlyList<string> TimeRelatedColumns)
{
    public IReadOnlyList<CalendarSortChange> SortChanges { get; init; } = Array.Empty<CalendarSortChange>();
}
public sealed record CalendarColumn(string Name, string DataType, string? SortByColumn);
public sealed record CalendarTable(string Name, IReadOnlyList<CalendarColumn> Columns);
public sealed record CalendarSnapshot(int CompatibilityLevel, IReadOnlyList<CalendarTable> Tables, IReadOnlyList<CalendarDraft> Calendars);

public sealed class CalendarEditorService(TabularModelHandler handler)
{
    public static IReadOnlyList<string> TimeUnits { get; } = Array.AsReadOnly(Enum.GetValues(typeof(TimeUnit)).Cast<TimeUnit>().Where(unit => unit != TimeUnit.Unknown).Select(unit => unit.ToString()).ToArray());
    public CalendarSnapshot Capture() => new(handler.CompatibilityLevel,
        Array.AsReadOnly(handler.Model.Tables.Select(table => new CalendarTable(table.Name, Array.AsReadOnly(table.Columns.Select(column => new CalendarColumn(column.Name, column.DataType.ToString(), column.SortByColumn?.Name)).ToArray()))).ToArray()),
        Array.AsReadOnly(handler.Model.AllCalendars.Select(Read).ToArray()));
    public IReadOnlyList<AuthoringIssue> Validate(CalendarDraft draft)
    {
        var issues = new List<AuthoringIssue>();
        void Error(string code, string message) => issues.Add(new(code, message, AuthoringIssueSeverity.Error, draft.Name));
        if (handler.CompatibilityLevel < 1701) Error("CALENDAR_COMPATIBILITY", "Calendars require compatibility level 1701 or later. This editor does not upgrade the model.");
        if (string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Length > 512 || draft.Name.Any(char.IsControl)) Error("CALENDAR_NAME", "Enter a nonblank calendar name without control characters (at most 512 characters).");
        var table = handler.Model.Tables.FirstOrDefault(item => item.Name == draft.Table);
        if (table == null) { Error("CALENDAR_TABLE", "The calendar table no longer exists."); return issues.AsReadOnly(); }
        var original = draft.OriginalName == null ? null : table.Calendars.FirstOrDefault(calendar => calendar.Name == draft.OriginalName);
        if (draft.OriginalName != null && original == null) Error("CALENDAR_STALE", "The original calendar no longer exists. Reload the draft.");
        if (handler.Model.Tables.Any(item => item.Name.Equals(draft.Name, StringComparison.OrdinalIgnoreCase)) || handler.Model.AllCalendars.Any(calendar => calendar != original && calendar.Name.Equals(draft.Name, StringComparison.OrdinalIgnoreCase))) Error("CALENDAR_NAME_COLLISION", "A calendar name must be unique across all calendars and tables.");
        if (draft.Mappings.Count == 0) Error("CALENDAR_PRIMARY", "Assign at least one time category and primary column.");
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Assign(string name, string unit)
        {
            if (!table.Columns.Any(column => column.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Error("CALENDAR_COLUMN", "Unknown column in this table: " + name); return; }
            if (assignments.ContainsKey(name)) Error("CALENDAR_DUPLICATE_COLUMN", "A column can be assigned only once per calendar: " + name);
            else assignments[name] = unit;
        }
        var units = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in draft.Mappings)
        {
            if (!TimeUnits.Contains(mapping.TimeUnit)) Error("CALENDAR_TIME_UNIT", "Unsupported time category: " + mapping.TimeUnit);
            if (!units.Add(mapping.TimeUnit)) Error("CALENDAR_DUPLICATE_UNIT", "A calendar can map each time category only once: " + mapping.TimeUnit);
            Assign(mapping.PrimaryColumn, mapping.TimeUnit);
            foreach (var name in mapping.AssociatedColumns) Assign(name, mapping.TimeUnit);
        }
        foreach (var name in draft.TimeRelatedColumns) Assign(name, "TimeRelated");
        foreach (var calendar in table.Calendars.Where(item => item != original))
        {
            var other = Read(calendar);
            foreach (var mapping in other.Mappings)
                foreach (var name in new[] { mapping.PrimaryColumn }.Concat(mapping.AssociatedColumns))
                    if (assignments.TryGetValue(name, out var unit) && unit != mapping.TimeUnit) Error("CALENDAR_CROSS_CATEGORY", name + " is already categorized as " + mapping.TimeUnit + " in " + calendar.Name + ".");
            foreach (var name in other.TimeRelatedColumns)
                if (assignments.TryGetValue(name, out var unit) && unit != "TimeRelated") Error("CALENDAR_CROSS_CATEGORY", name + " is time-related in " + calendar.Name + ".");
        }
        foreach (var unit in units.Where(unit => unit.Contains("Of")))
        {
            var parts = unit.Split(new[] { "Of" }, StringSplitOptions.None); var complete = parts[0] == "Day" ? "Date" : parts[0];
            bool Complete(string period, HashSet<string> visited)
            {
                if (!visited.Add(period)) return false;
                if (units.Contains(period)) return true;
                return units.Where(item => item.StartsWith(period + "Of", StringComparison.Ordinal)).Any(item => Complete(item.Substring(period.Length + 2), new HashSet<string>(visited)));
            }
            if (!units.Contains(complete) && !Complete(parts[1], new HashSet<string>()))
                issues.Add(new("CALENDAR_PERIOD_PATH", unit + " needs a complete " + parts[1] + " category or a matching chain of partial categories to identify the period.", AuthoringIssueSeverity.Warning, draft.Name));
        }
        var sorts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in draft.SortChanges)
        {
            if (sorts.ContainsKey(change.Column)) { Error("CALENDAR_SORT_DUPLICATE", "A column has more than one sort edit: " + change.Column); continue; }
            sorts[change.Column] = change.SortByColumn;
            if (!table.Columns.Any(column => column.Name == change.Column) || (change.SortByColumn != null && !table.Columns.Any(column => column.Name == change.SortByColumn))) Error("CALENDAR_SORT_COLUMN", "Sort columns must exist in the same table.");
        }
        foreach (var start in sorts.Keys)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string? current = start;
            while (current != null)
            {
                if (!visited.Add(current)) { Error("CALENDAR_SORT_CYCLE", "Sort-by columns cannot form a cycle: " + start); break; }
                current = sorts.TryGetValue(current, out var target) ? target : table.Columns.FirstOrDefault(column => column.Name == current)?.SortByColumn?.Name;
            }
        }
        issues.Add(new("CALENDAR_ENGINE", "Calendar metadata requires a supporting engine and Power BI's enhanced time intelligence feature. Validate generated DAX against the intended target before deployment.", AuthoringIssueSeverity.Information));
        if (table.Columns.Count >= 200) issues.Add(new("CALENDAR_DESKTOP_LIMIT", "Power BI documents a calendar table limit of fewer than 200 columns. Check the intended target before deployment.", AuthoringIssueSeverity.Warning));
        return issues.AsReadOnly();
    }
    public AuthoringPreview Preview(CalendarDraft request)
    {
        // Freeze caller-owned collections before closures are created.
        var draft = request with { Mappings = Array.AsReadOnly(request.Mappings.Select(mapping => mapping with { AssociatedColumns = Array.AsReadOnly(mapping.AssociatedColumns.ToArray()) }).ToArray()), TimeRelatedColumns = Array.AsReadOnly(request.TimeRelatedColumns.ToArray()), SortChanges = Array.AsReadOnly(request.SortChanges.ToArray()) };
        var issues = Validate(draft); var edits = new List<AuthoringEdit>();
        if (issues.Any(issue => issue.Severity == AuthoringIssueSeverity.Error)) return AuthoringPreview.Create(handler, "Calendar authoring", edits, issues);
        var table = handler.Model.Tables[draft.Table]; var existing = draft.OriginalName == null ? null : table.Calendars[draft.OriginalName];
        var before = existing == null ? "(absent)" : Describe(Read(existing)); var after = Describe(draft);
        if (before != after)
        {
            Calendar? applied = existing;
            edits.Add(new(new(draft.Table + " / " + (draft.OriginalName ?? draft.Name), "Calendar definition", before, after, "Keep the calendar identity; edit mappings through the TE2 undo framework."), () =>
            {
                applied ??= table.AddCalendar(draft.Name);
                if (applied.Name != draft.Name)
                {
                    var fixup = handler.Settings.AutoFixup;
                    try { handler.Settings.AutoFixup = false; applied.Name = draft.Name; }
                    finally { handler.Settings.AutoFixup = fixup; }
                }
                applied.Description = draft.Description;
                foreach (var mapping in applied.GetTimeUnits().Where(mapping => !draft.Mappings.Any(wanted => wanted.TimeUnit == mapping.TimeUnit.ToString())).ToArray()) RemoveMapping(mapping);
                foreach (var mapping in draft.Mappings)
                {
                    var unit = (TimeUnit)Enum.Parse(typeof(TimeUnit), mapping.TimeUnit); var association = applied.FindTimeUnit(unit);
                    if (association == null) applied.AddTimeUnit(unit, table.Columns[mapping.PrimaryColumn], mapping.AssociatedColumns.Select(name => table.Columns[name]).ToArray());
                    else
                    {
                        association.PrimaryColumn = table.Columns[mapping.PrimaryColumn];
                        SyncColumns(association.AssociatedColumns, mapping.AssociatedColumns.Select(name => table.Columns[name]).ToArray());
                    }
                }
                var groups = applied.CalendarColumnGroups.OfType<TimeRelatedColumnGroup>().ToArray();
                var related = groups.FirstOrDefault();
                foreach (var extra in groups.Skip(1)) RemoveMapping(extra);
                if (draft.TimeRelatedColumns.Count == 0) { if (related != null) RemoveMapping(related); }
                else { related ??= TimeRelatedColumnGroup.CreateNew(applied); SyncColumns(related.Columns, draft.TimeRelatedColumns.Select(name => table.Columns[name]).ToArray()); }
            }, () => applied != null && Describe(Read(applied)) == after));
        }
        if (existing != null && existing.Name != draft.Name)
        {
            foreach (var caller in existing.ReferencedBy.ToArray())
            {
                if (!caller.DependsOn.TryGetValue(existing, out var references)) continue;
                foreach (var group in references.GroupBy(reference => reference.property))
                {
                    var property = group.Key; var expression = caller.GetDAX(property) ?? ""; var replacement = expression;
                    foreach (var reference in group.OrderByDescending(reference => reference.from))
                    {
                        if (reference.from < 0 || reference.to < reference.from || reference.to >= expression.Length) throw new InvalidOperationException("Calendar references changed. Rebuild dependencies and preview again.");
                        replacement = replacement.Remove(reference.from, reference.to - reference.from + 1).Insert(reference.from, DaxDataSyntax.Table(draft.Name));
                    }
                    var expected = replacement;
                    edits.Add(new(new((caller as TabularObject)?.GetObjectPath() ?? caller.ToString(), property.ToString(), expression, expected, "Update a bound calendar reference as part of this rename."),
                        () => caller.SetDAX(property, expected), () => caller.GetDAX(property) == expected));
                }
            }
        }
        foreach (var sort in draft.SortChanges)
        {
            var column = table.Columns[sort.Column]; var old = column.SortByColumn?.Name; var target = sort.SortByColumn == null ? null : table.Columns[sort.SortByColumn];
            if (old == target?.Name) continue;
            edits.Add(new(new(AuthoringObjects.Id(column), "SortByColumn", old ?? "(none)", target?.Name ?? "(none)", "This column-level sort affects every calendar and visual using the column."), () => column.SortByColumn = target!, () => column.SortByColumn == target));
        }
        return AuthoringPreview.Create(handler, "Calendar authoring", edits, issues);
    }
    public AuthoringPreview PreviewDelete(string tableName, string name)
    {
        var calendar = handler.Model.Tables[tableName].Calendars[name];
        var callers = calendar.ReferencedBy.Select(obj => obj.ToString()).ToArray();
        var issues = callers.Length == 0 ? Array.Empty<AuthoringIssue>() : new[] { new AuthoringIssue("CALENDAR_REFERENCED", "Expressions reference this calendar: " + string.Join(", ", callers) + ". Update them before deletion.", AuthoringIssueSeverity.Error) };
        return AuthoringPreview.Create(handler, "Delete calendar", new[] { new AuthoringEdit(new(tableName + " / " + name, "Calendar", Describe(Read(calendar)), "(removed)", "Remove only the calendar metadata; table columns remain."), () => { foreach (var mapping in calendar.CalendarColumnGroups.ToArray()) RemoveMapping(mapping); calendar.Delete(); }, () => !handler.Model.Tables[tableName].Calendars.Contains(name)) }, issues);
    }
    public string GenerateSample(CalendarDraft draft, string measureTable, string measureName)
    {
        var measure = handler.Model.Tables[measureTable].Measures[measureName];
        return "EVALUATE\nROW(\"Calendar YTD\", TOTALYTD(" + DaxDataSyntax.Column(measure.Table.Name, measure.Name) + ", " + DaxDataSyntax.Table(draft.Name) + "))";
    }
    public string GenerateValidationQuery(CalendarDraft draft)
    {
        if (Validate(draft).Any(issue => issue.Severity == AuthoringIssueSeverity.Error)) throw new ArgumentException("Fix calendar metadata errors before generating validation queries.");
        var relation = DaxDataSyntax.Table(draft.Table); var rows = new List<string>();
        foreach (var mapping in draft.Mappings)
        {
            foreach (var name in new[] { mapping.PrimaryColumn }.Concat(mapping.AssociatedColumns))
                rows.Add("ROW(\"Check\", " + DaxDataSyntax.String("Blank values: " + name) + ", \"Violations\", COALESCE(COUNTROWS(FILTER(" + relation + ", ISBLANK(" + DaxDataSyntax.Column(draft.Table, name) + "))), 0))");
            foreach (var name in mapping.AssociatedColumns)
            {
                var primary = DaxDataSyntax.Column(draft.Table, mapping.PrimaryColumn); var associated = DaxDataSyntax.Column(draft.Table, name);
                var pairs = "COUNTROWS(SUMMARIZE(" + relation + ", " + primary + ", " + associated + "))";
                rows.Add("ROW(\"Check\", " + DaxDataSyntax.String("One-to-one: " + mapping.PrimaryColumn + " / " + name) + ", \"Violations\", " + pairs + " - MIN(COUNTROWS(SUMMARIZE(" + relation + ", " + primary + ")), COUNTROWS(SUMMARIZE(" + relation + ", " + associated + "))))");
            }
        }
        return "// Explicit data scan: blank values and primary/associated one-to-one checks.\n// Period cardinality and target-specific time behavior also require testing.\nEVALUATE\n" + (rows.Count == 1 ? rows[0] : "UNION(\n" + string.Join(",\n", rows) + "\n)");
    }
    private static CalendarDraft Read(Calendar calendar) => new(calendar.Table.Name, calendar.Name, calendar.Name, calendar.Description ?? "",
        Array.AsReadOnly(calendar.GetTimeUnits().Select(mapping => new CalendarMapping(mapping.TimeUnit.ToString(), mapping.PrimaryColumn?.Name ?? "", Array.AsReadOnly(mapping.AssociatedColumns.Select(column => column.Name).ToArray()))).ToArray()),
        Array.AsReadOnly(calendar.CalendarColumnGroups.OfType<TimeRelatedColumnGroup>().SelectMany(group => group.Columns).Select(column => column.Name).ToArray()));
    private static void SyncColumns(AssociatedColumnCollection collection, IReadOnlyList<Column> desired)
    {
        foreach (var column in collection.Where(column => !desired.Contains(column)).ToArray()) collection.Remove(column);
        foreach (var column in desired.Where(column => !collection.Contains(column))) collection.Add(column);
    }
    private static void RemoveMapping(CalendarColumnGroup mapping)
    {
        // Clear references through undo-aware setters first. TE2 2.28 cannot restore named
        // column references from a detached CalendarColumnGroup's serialized undo payload.
        if (mapping is TimeUnitColumnAssociation time) { foreach (var column in time.AssociatedColumns.ToArray()) time.AssociatedColumns.Remove(column); time.PrimaryColumn = null!; }
        if (mapping is TimeRelatedColumnGroup related) foreach (var column in related.Columns.ToArray()) related.Columns.Remove(column);
        mapping.Delete();
    }
    private static string Describe(CalendarDraft draft) => draft.Name + "\n" + draft.Description + "\n" + string.Join("\n", draft.Mappings.OrderBy(mapping => mapping.TimeUnit).Select(mapping => mapping.TimeUnit + ": " + mapping.PrimaryColumn + "; associated: " + string.Join(", ", mapping.AssociatedColumns.OrderBy(name => name)))) + "\nTime related: " + string.Join(", ", draft.TimeRelatedColumns.OrderBy(name => name));
}
