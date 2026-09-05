using System.Globalization;
using PbiBench.Core.Queries;

namespace PbiBench.Core.Quality;

public enum VertiPaqRowset { Model, Tables, Columns, Partitions, Relationships, StorageTables, StorageColumns, StorageSegments }
public sealed record VertiPaqRowsetResult(VertiPaqRowset Rowset, QueryResultSet? Result, string? UnavailableReason = null);

/// <summary>Combines public rowsets by captured table/column identifiers. Truncated rowsets never masquerade as complete totals.</summary>
public static class VertiPaqDmvProjection
{
    public static VertiPaqSnapshot Build(string server, string database, DateTimeOffset capturedAt, IReadOnlyList<VertiPaqRowsetResult> results)
    {
        var warnings = new List<string> { "Live DMV snapshot; no data scans, cache clearing, refresh or writes. Rowsets are captured sequentially, not as an atomic server snapshot.",
            "Relationship missing-key/invalid-row statistics require a separate Data relationship profile. DMV row counts/cardinality and resident sizes may be partial for Direct Lake." };
        var available = new HashSet<VertiPaqRowset>();
        var data = new Dictionary<VertiPaqRowset, Row[]>();
        foreach (var item in results)
        {
            if (item.Result == null || item.Result.IsTruncated)
            {
                warnings.Add(item.Rowset + ": " + (item.Result?.IsTruncated == true ? "retention limit reached; incomplete metrics were discarded." : item.UnavailableReason ?? "unavailable."));
                continue;
            }
            available.Add(item.Rowset); data[item.Rowset] = item.Result.Rows.Select(row => new Row(item.Result.Columns, row)).ToArray();
        }
        Row[] Rows(VertiPaqRowset rowset) => data.TryGetValue(rowset, out var rows) ? rows : Array.Empty<Row>();
        var tableRows = Rows(VertiPaqRowset.Tables);
        var tableIds = tableRows.Where(row => row.Text("ID") != null && row.Text("Name") != null).GroupBy(row => row.Text("ID")!).ToDictionary(group => group.Key, group => group.First().Text("Name")!, StringComparer.Ordinal);
        string? TableFor(Row row) => row.Text("TableID") is string id && tableIds.TryGetValue(id, out var name) ? name : null;
        var metadataColumns = Rows(VertiPaqRowset.Columns).Where(row => row.Text("ID") != null).GroupBy(row => row.Text("ID")!).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var storageColumns = Rows(VertiPaqRowset.StorageColumns).Where(row => row.Text("COLUMN_TYPE") == "BASIC_DATA").ToArray();
        var storageTables = Rows(VertiPaqRowset.StorageTables);
        var storageSegments = Rows(VertiPaqRowset.StorageSegments);
        var columns = new List<VertiPaqColumn>(); var segments = new List<VertiPaqSegment>();
        var columnNames = new Dictionary<string, (string Table, string Column)>(StringComparer.Ordinal);
        foreach (var group in storageColumns.GroupBy(row => (Table: row.Text("DIMENSION_NAME") ?? "", Column: row.Text("COLUMN_ID") ?? "")))
        {
            var first = group.First(); var table = group.Key.Table; var id = group.Key.Column;
            var name = first.Text("ATTRIBUTE_NAME") ?? id; var numericId = TrailingId(id);
            var type = StorageType(first.Text("DATATYPE"));
            if (numericId != null && metadataColumns.TryGetValue(numericId, out var metadata))
            {
                var verifiedTable = TableFor(metadata); var verifiedName = metadata.Text("ExplicitName") ?? metadata.Text("InferredName");
                if (verifiedTable != table || (verifiedName != null && verifiedName != name))
                { warnings.Add("A column identity changed during capture; metrics for " + table + "[" + name + "] were omitted."); continue; }
                type = MetadataType(metadata.Text("ExplicitDataType") ?? metadata.Text("InferredDataType")) ?? type;
                columnNames[numericId] = (table, name);
            }
            var own = storageSegments.Where(row => !Auxiliary(row.Text("TABLE_ID")) && row.Text("DIMENSION_NAME") == table && row.Text("COLUMN_ID") == id).ToArray();
            foreach (var segment in own) segments.Add(new(table, name, segment.Text("PARTITION_NAME"), segment.Number("SEGMENT_NUMBER") ?? 0, segment.Number("RECORDS_COUNT"), segment.Number("USED_SIZE"),
                segment.Boolean("ISRESIDENT"), segment.Boolean("ISPAGEABLE"), segment.Real("TEMPERATURE"), segment.Date("LAST_ACCESSED")));
            var hierarchyRows = storageTables.Where(row => row.Text("DIMENSION_NAME") == table && IsColumnHierarchy(row.Text("TABLE_ID"), id)).ToArray();
            var cardinalities = hierarchyRows.Select(row => row.Number("ROWS_COUNT") is long n && n >= 3 ? n - 3 : (long?)null).Distinct().ToArray();
            var hierarchySegments = storageSegments.Where(row => row.Text("DIMENSION_NAME") == table && IsColumnHierarchy(row.Text("TABLE_ID"), id)).ToArray();
            var dictionaryValues = group.Select(row => row.Number("DICTIONARY_SIZE")).Distinct().ToArray();
            var resident = own.Select(row => row.Boolean("ISRESIDENT")).ToArray();
            columns.Add(new(table, name, type, available.Contains(VertiPaqRowset.StorageTables) && cardinalities.Length == 1 ? cardinalities[0] : null,
                available.Contains(VertiPaqRowset.StorageSegments) ? VertiPaqNumbers.Sum(own.Select(row => row.Number("USED_SIZE"))) : null,
                dictionaryValues.Length == 1 ? dictionaryValues[0] : null,
                available.Contains(VertiPaqRowset.StorageSegments) ? VertiPaqNumbers.Sum(hierarchySegments.Select(row => row.Number("USED_SIZE"))) : null,
                first.Text("COLUMN_ENCODING") switch { "1" => "HASH", "2" => "VALUE", _ => "UNKNOWN" },
                resident.Any(value => value == true) ? true : resident.Length > 0 && resident.All(value => value == false) ? false : null));
        }
        // Preserve metadata-only columns (for example DirectQuery or nonresident Direct Lake) with unavailable storage values.
        foreach (var pair in metadataColumns)
        {
            var table = TableFor(pair.Value); var name = pair.Value.Text("ExplicitName") ?? pair.Value.Text("InferredName");
            if (table == null || name == null) continue;
            columnNames[pair.Key] = (table, name);
            if (!columns.Any(column => column.Table == table && column.Name == name)) columns.Add(new(table, name,
                MetadataType(pair.Value.Text("ExplicitDataType") ?? pair.Value.Text("InferredDataType")) ?? "Unknown", null, null, null, null, null, null));
        }
        var defaultMode = Rows(VertiPaqRowset.Model).FirstOrDefault()?.Text("DefaultMode");
        var partitions = Rows(VertiPaqRowset.Partitions).Select(row => new VertiPaqPartition(TableFor(row) ?? "(unresolved)", row.Text("Name") ?? "Partition", Mode(row.Text("Mode") == "2" ? defaultMode : row.Text("Mode")), row.Text("State"), row.Date("RefreshedTime"))).ToArray();
        var relations = new List<VertiPaqRelationship>();
        foreach (var row in Rows(VertiPaqRowset.Relationships))
        {
            var fromId = row.Text("FromColumnID"); var toId = row.Text("ToColumnID"); var relationId = row.Text("ID");
            var from = fromId != null && columnNames.TryGetValue(fromId, out var f) ? f : ("(unresolved)", "(unresolved)");
            var to = toId != null && columnNames.TryGetValue(toId, out var t) ? t : ("(unresolved)", "(unresolved)");
            long? Size(string table) => !available.Contains(VertiPaqRowset.StorageSegments) ? null : VertiPaqNumbers.Sum(storageSegments.Where(segment => segment.Text("DIMENSION_NAME") == table && segment.Text("TABLE_ID")?.StartsWith("R$", StringComparison.Ordinal) == true && TrailingId(segment.Text("TABLE_ID")) == relationId).Select(segment => segment.Number("USED_SIZE")));
            relations.Add(new(row.Text("Name") ?? relationId ?? "Relationship", from.Item1, from.Item2, to.Item1, to.Item2, null, null, Size(from.Item1), Size(to.Item1)));
        }
        var names = tableIds.Values.Concat(columns.Select(column => column.Table)).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var tables = new List<VertiPaqTable>();
        foreach (var name in names)
        {
            var own = columns.Where(column => column.Table == name).ToArray();
            var ownStorage = storageTables.Where(row => row.Text("DIMENSION_NAME") == name && !Auxiliary(row.Text("TABLE_ID"))).ToArray();
            long? Unique(string property) { var values = ownStorage.Select(row => row.Number(property)).Distinct().ToArray(); return values.Length == 1 ? values[0] : null; }
            var modes = partitions.Where(partition => partition.Table == name).Select(partition => partition.Mode).Distinct().ToArray();
            var relationSize = available.Contains(VertiPaqRowset.StorageSegments) ? VertiPaqNumbers.Sum(storageSegments.Where(row => row.Text("DIMENSION_NAME") == name && row.Text("TABLE_ID")?.StartsWith("R$", StringComparison.Ordinal) == true).Select(row => row.Number("USED_SIZE"))) : null;
            var userSize = available.Contains(VertiPaqRowset.StorageSegments) ? VertiPaqNumbers.Sum(storageSegments.Where(row => row.Text("DIMENSION_NAME") == name && row.Text("TABLE_ID")?.StartsWith("U$", StringComparison.Ordinal) == true).Select(row => row.Number("USED_SIZE"))) : null;
            tables.Add(new(name, Unique("ROWS_COUNT"), own.Length > 0 ? VertiPaqNumbers.Sum(own.Select(column => column.DataBytes)) : null,
                own.Length > 0 ? VertiPaqNumbers.Sum(own.Select(column => column.DictionaryBytes)) : null, own.Length > 0 ? VertiPaqNumbers.Sum(own.Select(column => column.HierarchyBytes)) : null,
                relationSize, userSize, modes.Length == 1 ? modes[0] : modes.Length > 1 ? "Mixed" : "Unknown", Unique("RIVIOLATION_COUNT")));
        }
        if (tables.Count == 0) warnings.Add("No visible table metrics were returned. Check endpoint, catalog and DMV permissions; no model was changed.");
        return new("Live public DMVs", database, server, capturedAt, "Microsoft public rowsets", false, tables, columns, partitions, segments, relations, warnings.Distinct().ToArray());
    }

    private static bool Auxiliary(string? id) => id != null && id.Length > 1 && id[1] == '$';
    private static bool IsColumnHierarchy(string? hierarchy, string column) => hierarchy?.StartsWith("H$", StringComparison.Ordinal) == true && hierarchy.EndsWith("$" + column, StringComparison.Ordinal);
    private static string? TrailingId(string? id)
    {
        if (id == null) return null; var start = id.LastIndexOf('('); var end = id.LastIndexOf(')');
        return start >= 0 && end > start && long.TryParse(id.Substring(start + 1, end - start - 1), out var value) ? value.ToString(CultureInfo.InvariantCulture) : null;
    }
    private static string Mode(string? mode) => mode switch { "0" => "Import", "1" => "DirectQuery", "3" => "Push", "4" => "Dual", "5" => "DirectLake", null or "2" => "Default / unknown", _ => mode };
    private static string? MetadataType(string? type) => type switch { "2" => "String", "6" => "Int64", "8" => "Double", "9" => "DateTime", "10" => "Decimal", "11" => "Boolean", "17" => "Binary", _ => null };
    private static string StorageType(string? type) => type switch { "DBTYPE_WSTR" => "String", "DBTYPE_I8" => "Int64", "DBTYPE_R8" => "Double", "DBTYPE_CY" => "Decimal", "DBTYPE_DATE" => "DateTime", "DBTYPE_BOOL" => "Boolean", _ => type ?? "Unknown" };
    private sealed class Row
    {
        private readonly Dictionary<string, object?> values;
        public Row(IReadOnlyList<QueryColumn> columns, object?[] row) => values = columns.Select((column, index) => (column.Name, Value: index < row.Length ? row[index] : null)).GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        private object? Value(string key) => values.TryGetValue(key, out var value) && value != DBNull.Value ? value : null;
        public string? Text(string key) => Value(key) is object value ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        public long? Number(string key) => decimal.TryParse(Text(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && number >= 0 && number <= long.MaxValue && decimal.Truncate(number) == number ? (long)number : null;
        public double? Real(string key) => double.TryParse(Text(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && !double.IsInfinity(number) && !double.IsNaN(number) ? number : null;
        public bool? Boolean(string key) => bool.TryParse(Text(key), out var value) ? value : null;
        public DateTimeOffset? Date(string key) => DateTimeOffset.TryParse(Text(key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) && value.Year >= 1900 ? value : null;
    }
}
