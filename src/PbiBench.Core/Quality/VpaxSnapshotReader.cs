using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace PbiBench.Core.Quality;

/// <summary>Reads the documented VPAX statistics part. Embedded model metadata is never applied or extracted.</summary>
public sealed class VpaxSnapshotReader : IVpaxSnapshotReader
{
    public const int MaximumJsonBytes = 64 * 1024 * 1024;
    public const int MaximumObjects = 500000;
    public async Task<VertiPaqSnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        cancellationToken.ThrowIfCancellationRequested();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (file.Length > 256L * 1024 * 1024) throw new InvalidDataException("The VPAX file exceeds the 256 MB import limit.");
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        if (archive.Entries.Count > 64) throw new InvalidDataException("The VPAX archive contains too many entries.");
        var entries = archive.Entries.Where(entry => entry.FullName.Equals("DaxModel.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length != 1) throw new InvalidDataException("The VPAX archive must contain exactly one root DaxModel.json statistics part.");
        var entry = entries[0];
        if (entry.Length > MaximumJsonBytes) throw new InvalidDataException("The VPAX statistics exceed the 64 MB import limit.");
        using var stream = entry.Open(); using var buffer = new MemoryStream();
        var chunk = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaximumJsonBytes) throw new InvalidDataException("The expanded VPAX statistics exceed the import limit.");
            buffer.Write(chunk, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() => Parse(buffer.ToArray(), Path.GetFileName(path), cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public static VertiPaqSnapshot Parse(byte[] utf8Json, string source, CancellationToken cancellationToken = default)
    {
        if (utf8Json == null) throw new ArgumentNullException(nameof(utf8Json));
        if (utf8Json.Length > MaximumJsonBytes) throw new InvalidDataException("The VPAX statistics exceed the import limit.");
        var offset = utf8Json.Length >= 3 && utf8Json[0] == 0xef && utf8Json[1] == 0xbb && utf8Json[2] == 0xbf ? 3 : 0;
        using var document = JsonDocument.Parse(new ReadOnlyMemory<byte>(utf8Json, offset, utf8Json.Length - offset), new JsonDocumentOptions { MaxDepth = 128 });
        var json = new VpaxJson(document.RootElement, cancellationToken);
        return json.Project(source);
    }

    private sealed class VpaxJson
    {
        private readonly JsonElement root;
        private readonly CancellationToken token;
        private readonly Dictionary<string, JsonElement> references = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> columnTables = new(StringComparer.Ordinal);
        private int count;
        public VpaxJson(JsonElement root, CancellationToken token) { this.root = root; this.token = token; Index(root); }
        private void Index(JsonElement value)
        {
            token.ThrowIfCancellationRequested();
            if (++count > MaximumObjects) throw new InvalidDataException("The VPAX statistics exceed the object limit.");
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("$id", out var id))
                {
                    var key = id.GetString();
                    if (key == null || references.ContainsKey(key)) throw new InvalidDataException("Invalid or duplicate VPAX reference identifier.");
                    references.Add(key, value);
                }
                foreach (var property in value.EnumerateObject()) Index(property.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Index(item);
        }
        private JsonElement Resolve(JsonElement value)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$ref", out var reference))
            {
                var key = reference.GetString();
                if (key == null || !visited.Add(key) || !references.TryGetValue(key, out value)) throw new InvalidDataException("The VPAX statistics contain an unresolved or cyclic reference.");
            }
            return value;
        }
        private JsonElement Property(JsonElement value, string name)
        {
            value = Resolve(value); return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) ? Resolve(result) : default;
        }
        private IEnumerable<JsonElement> Array(JsonElement value, string name)
        {
            var array = Property(value, name);
            if (array.ValueKind == JsonValueKind.Object) array = Property(array, "$values");
            if (array.ValueKind == JsonValueKind.Undefined || array.ValueKind == JsonValueKind.Null) return Enumerable.Empty<JsonElement>();
            if (array.ValueKind != JsonValueKind.Array) throw new InvalidDataException("VPAX " + name + " must be an array.");
            return array.EnumerateArray().Select(Resolve);
        }
        private string? Text(JsonElement value, string name)
        {
            var item = Property(value, name);
            if (item.ValueKind == JsonValueKind.Object) item = Property(item, "Name");
            if (item.ValueKind == JsonValueKind.Undefined || item.ValueKind == JsonValueKind.Null) return null;
            if (item.ValueKind == JsonValueKind.Number) return item.GetRawText();
            if (item.ValueKind != JsonValueKind.String) throw new InvalidDataException("VPAX " + name + " must be text.");
            return item.GetString();
        }
        private string Name(JsonElement value, string name) => Text(value, name) is { Length: > 0 } result ? result : throw new InvalidDataException("The VPAX object has no " + name + ".");
        private long? Number(JsonElement value, string name)
        {
            var item = Property(value, name);
            if (item.ValueKind == JsonValueKind.Undefined || item.ValueKind == JsonValueKind.Null) return null;
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out var result) || result < 0) throw new InvalidDataException("VPAX " + name + " must be a nonnegative integer.");
            return result;
        }
        private bool? Boolean(JsonElement value, string name)
        {
            var item = Property(value, name);
            return item.ValueKind == JsonValueKind.True ? true : item.ValueKind == JsonValueKind.False ? false : item.ValueKind == JsonValueKind.Undefined || item.ValueKind == JsonValueKind.Null ? null : throw new InvalidDataException("VPAX " + name + " must be boolean.");
        }
        private DateTimeOffset? Date(JsonElement value, string name)
        {
            var text = Text(value, name);
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) && date.Year >= 1900 ? date : null;
        }
        private double? Temperature(JsonElement value)
        {
            var item = Property(value, "Temperature");
            if (item.ValueKind == JsonValueKind.Null || item.ValueKind == JsonValueKind.Undefined) return null;
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var number) || double.IsInfinity(number) || double.IsNaN(number)) throw new InvalidDataException("Invalid segment temperature.");
            return number;
        }
        private long? Aggregate(JsonElement value, string property, string members, string field)
        {
            var direct = Number(value, property);
            if (direct.HasValue) return direct;
            if (Property(value, members).ValueKind == JsonValueKind.Undefined) return null;
            return VertiPaqNumbers.Sum(Array(value, members).Select(item => Number(item, field)));
        }
        private string Mode(JsonElement partition, string defaultMode)
        {
            var value = Text(partition, "Mode");
            if (value == "2" || value == "Default") value = defaultMode;
            return value switch { "0" => "Import", "1" => "DirectQuery", "3" => "Push", "4" => "Dual", "5" => "DirectLake", null or "2" => "Unknown", _ => value };
        }

        public VertiPaqSnapshot Project(string source)
        {
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The VPAX statistics root must be an object.");
            var version = Text(root, "DaxModelVersion") ?? "unknown";
            if (!Version.TryParse(version, out var parsed) || parsed.Major != 1 || parsed > new Version(1, 9, 0)) throw new InvalidDataException("Unsupported VPAX model schema " + version + "; supported schemas are 1.0 through 1.9.");
            var warnings = new List<string> { "Imported snapshot. Metrics describe the captured model and may differ from the current model. Missing metrics are unavailable, not zero. Embedded Model.bim was not opened." };
            var statistics = Boolean(Property(root, "ExtractorProperties"), "StatisticsEnabled");
            if (statistics != true) warnings.Add("Data-statistics collection was disabled or not recorded. Relationship missing-key/invalid-row counts are shown as unavailable.");
            var defaultMode = Text(root, "DefaultMode") ?? "Unknown";
            if (Property(root, "Tables").ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("The VPAX statistics have no Tables collection.");
            var tableObjects = Array(root, "Tables").ToArray();
            var columns = new List<VertiPaqColumn>(); var segments = new List<VertiPaqSegment>(); var partitions = new List<VertiPaqPartition>();
            var tables = new List<VertiPaqTable>(); var relationships = new List<VertiPaqRelationship>();
            var tableNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var table in tableObjects)
            {
                token.ThrowIfCancellationRequested();
                var tableName = Name(table, "TableName");
                if (!tableNames.Add(tableName)) throw new InvalidDataException("Duplicate table name in VPAX statistics.");
                if (Property(table, "Columns").ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("The VPAX table has no Columns collection.");
                foreach (var column in Array(table, "Columns"))
                {
                    var id = Text(column, "$id"); if (id != null) columnTables[id] = tableName;
                    var name = Name(column, "ColumnName");
                    var columnSegments = Array(column, "ColumnSegments").ToArray();
                    foreach (var segment in columnSegments) segments.Add(new(tableName, name, Text(Property(segment, "Partition"), "PartitionName"),
                        Number(segment, "SegmentNumber") ?? 0, Number(segment, "SegmentRows"), Number(segment, "UsedSize"), Boolean(segment, "IsResident"), Boolean(segment, "IsPageable"), Temperature(segment), Date(segment, "LastAccessed")));
                    var resident = columnSegments.Select(segment => Boolean(segment, "IsResident")).ToArray();
                    columns.Add(new(tableName, name, Text(column, "DataType") ?? "Unknown", Number(column, "ColumnCardinality"),
                        Aggregate(column, "DataSize", "ColumnSegments", "UsedSize"), Number(column, "DictionarySize"), Aggregate(column, "HierarchiesSize", "ColumnHierarchies", "UsedSize"),
                        Text(column, "Encoding"), resident.Any(value => value == true) ? true : resident.Length > 0 && resident.All(value => value == false) ? false : null));
                }
                foreach (var partition in Array(table, "Partitions")) partitions.Add(new(tableName, Name(partition, "PartitionName"), Mode(partition, defaultMode), Text(partition, "State"), Date(partition, "RefreshedTime")));
            }
            string ColumnTable(JsonElement column)
            {
                var id = Text(column, "$id");
                if (id != null && columnTables.TryGetValue(id, out var table)) return table;
                return Text(Property(column, "Table"), "TableName") ?? "(unresolved)";
            }
            bool CompleteStatistics(string table)
            {
                if (statistics != true) return false;
                var modes = partitions.Where(partition => partition.Table == table).Select(partition => partition.Mode).ToArray();
                var extraction = Property(root, "ExtractorProperties");
                var directQuery = Text(extraction, "DirectQueryMode"); var directLake = Text(extraction, "DirectLakeMode");
                return modes.Length > 0 && modes.All(mode => mode == "Import" || ((mode == "DirectQuery" || mode == "Dual") && (directQuery == "1" || directQuery == "Full")) || (mode == "DirectLake" && (directLake == "2" || directLake == "Full")));
            }
            foreach (var relation in Array(root, "Relationships"))
            {
                var from = Property(relation, "FromColumn"); var to = Property(relation, "ToColumn");
                var collected = CompleteStatistics(ColumnTable(from)) && CompleteStatistics(ColumnTable(to));
                relationships.Add(new(Text(relation, "Name") ?? "Relationship", ColumnTable(from), Text(from, "ColumnName") ?? "(unresolved)", ColumnTable(to), Text(to, "ColumnName") ?? "(unresolved)",
                    collected ? Number(relation, "MissingKeys") : null, collected ? Number(relation, "InvalidRows") : null, Number(relation, "UsedSizeFrom"), Number(relation, "UsedSizeTo")));
            }
            if (statistics == true && relationships.Any(relationship => relationship.MissingKeys == null)) warnings.Add("Some relationship statistics lack full collection coverage for their storage/extraction mode; missing-key/invalid-row counts remain unavailable.");
            foreach (var table in tableObjects)
            {
                var name = Name(table, "TableName"); var own = columns.Where(column => column.Table == name).ToArray();
                var modes = partitions.Where(partition => partition.Table == name).Select(partition => partition.Mode).Distinct().ToArray();
                var relationSize = Property(root, "Relationships").ValueKind == JsonValueKind.Undefined ? null : VertiPaqNumbers.Sum(relationships.Where(r => r.FromTable == name).Select(r => r.FromBytes).Concat(relationships.Where(r => r.ToTable == name).Select(r => r.ToBytes)));
                var userSize = Property(table, "UserHierarchies").ValueKind == JsonValueKind.Undefined ? null : VertiPaqNumbers.Sum(Array(table, "UserHierarchies").Select(item => Number(item, "UsedSize")));
                tables.Add(new(name, Number(table, "RowsCount"), VertiPaqNumbers.Sum(own.Select(c => c.DataBytes)), VertiPaqNumbers.Sum(own.Select(c => c.DictionaryBytes)),
                    VertiPaqNumbers.Sum(own.Select(c => c.HierarchyBytes)), relationSize, userSize, modes.Length == 1 ? modes[0] : modes.Length > 1 ? "Mixed" : "Unknown", Number(table, "ReferentialIntegrityViolationCount")));
            }
            if (tables.Any(table => table.StorageMode == "DirectLake" || table.StorageMode == "Mixed")) warnings.Add("Direct Lake values depend on resident segments and extraction mode. Size is captured storage, not a full capacity or unused-data assessment.");
            return new(source, Text(root, "ModelName") ?? "Unknown model", Text(root, "ServerName"), Date(root, "ExtractionDate"), version, statistics,
                tables.ToArray(), columns.ToArray(), partitions.ToArray(), segments.ToArray(), relationships.ToArray(), warnings.ToArray());
        }
    }
}
