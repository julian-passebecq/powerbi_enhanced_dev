using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PbiBench.Core.Automation;
using PbiBench.Core.Queries;

namespace PbiBench.AI.ContextExport;

/// <summary>Detached, provider-neutral export. Preparing never writes a file or sends context to an AI.</summary>
public static class ContextExporter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public const string PrivacyNotice = "Object names, DAX, descriptions and data can be sensitive. Samples are examples, not complete or representative data, and are NOT anonymized. Review every included file before sharing with an external AI. Source/partition expressions, connections, credentials, role members and local recovery paths are excluded.";
    public static async Task<ContextExportPlan> PrepareAsync(ContextModel model, ContextExportOptions options, IContextSampler? sampler, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (options.MaximumBytes < 4096 || options.MaximumBytes > 128L * 1024 * 1024 || options.MaximumRowsPerTable < 1 || options.MaximumRowsPerTable > 1000 || options.MaximumSampleCells < 1 || options.MaximumSampleCells > 1000000)
            throw new ArgumentException("Export limits exceed the supported safe caps (128 MiB, 1,000 rows/table, 1,000,000 cells).");
        if (model.Objects.Count > 50000 || model.Relationships.Count > 50000 || model.Dependencies.Count > 500000 || options.Samples.Count > 1000) throw new ArgumentException("Narrow this model before export.");
        var all = model.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var relationships = model.Relationships.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var excluded = new HashSet<string>(options.ExcludedIds, StringComparer.Ordinal);
        foreach (var id in options.SelectedIds.Concat(excluded)) if (!all.ContainsKey(id) && !relationships.ContainsKey(id)) throw new ArgumentException("An export object no longer exists. Review the scope again.");
        foreach (var table in model.Objects.Where(o => o.Kind == "Table" && excluded.Contains(o.Id)))
            foreach (var child in model.Objects.Where(o => o.Table == table.Name)) excluded.Add(child.Id);
        var included = new HashSet<string>(options.SelectedScope ? options.SelectedIds : all.Keys.Concat(relationships.Keys), StringComparer.Ordinal);
        if (options.SelectedScope && included.Count == 0) throw new ArgumentException("Select at least one export object.");
        foreach (var table in model.Objects.Where(o => o.Kind == "Table" && included.Contains(o.Id)))
            foreach (var child in model.Objects.Where(o => o.Table == table.Name)) included.Add(child.Id);
        var requested = included.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        // Dependencies and relationship endpoints get exact context. Exclusions always win.
        var pending = new Queue<string>(included);
        var dependencies = model.Dependencies.GroupBy(d => d.ObjectId).ToDictionary(g => g.Key, g => g.Select(d => d.DependencyId).ToArray());
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested(); var id = pending.Dequeue(); if (excluded.Contains(id)) continue;
            if (all.TryGetValue(id, out var obj) && obj.Table != null) Add(ContextModel.ObjectId("Table", null, obj.Table));
            if (dependencies.TryGetValue(id, out var refs)) foreach (var dependency in refs) Add(dependency);
            if (relationships.TryGetValue(id, out var relationship)) { Add(relationship.FromColumnId); Add(relationship.ToColumnId); }
        }
        void Add(string id) { if ((all.ContainsKey(id) || relationships.ContainsKey(id)) && !excluded.Contains(id) && included.Add(id)) pending.Enqueue(id); }
        included.ExceptWith(excluded);
        var objects = model.Objects.Where(o => included.Contains(o.Id)).OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
        var rels = model.Relationships.Where(r => !excluded.Contains(r.Id) && included.Contains(r.FromColumnId) && included.Contains(r.ToColumnId)).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal); long total = 4096;
        var redactor = new Redactor(); long recordedReplacements = 0;
        var redactions = new SortedDictionary<string, long>(StringComparer.Ordinal);
        string Clean(string? value) => redactor.Clean(value);
        string Csv(IEnumerable<string> columns, IEnumerable<object?[]> rows, long maximumBytes = 32 * 1024 * 1024, CancellationToken token = default)
            => ContextExporter.Csv(columns, rows, Clean, maximumBytes, token);
        void TrackRedactions(string name)
        {
            var count = redactor.ReplacementCount - recordedReplacements;
            if (count > 0) redactions[name] = count;
            recordedReplacements = redactor.ReplacementCount;
        }
        void Text(string name, string value)
        {
            TrackRedactions(name);
            ct.ThrowIfCancellationRequested(); var bytes = Encoding.UTF8.GetBytes(value); total += bytes.Length + 512;
            if (total > options.MaximumBytes) throw new InvalidDataException("Context exceeds the selected ZIP size cap. Narrow scope or samples."); files.Add(name, bytes);
        }
        void Data(string name, object value, bool manifest = false)
        {
            // Redact string VALUES before writing JSON; regex over serialized JSON can corrupt escaping.
            using var raw = new BoundedMemoryStream(options.MaximumBytes);
            JsonSerializer.Serialize(raw, new { schemaVersion = 1, data = value }, Json);
            raw.Position = 0; using var doc = JsonDocument.Parse(raw);
            using var clean = new BoundedMemoryStream(options.MaximumBytes);
            using (var writer = new Utf8JsonWriter(clean, new JsonWriterOptions { Indented = true })) WriteClean(doc.RootElement, writer, Clean, ct);
            if (manifest)
            {
                TrackRedactions(name);
                using var cleaned = JsonDocument.Parse(clean.ToArray());
                using var final = new BoundedMemoryStream(options.MaximumBytes);
                JsonSerializer.Serialize(final, new { schemaVersion = 2, data = cleaned.RootElement.GetProperty("data"),
                    redaction = new { schemaVersion = 1, applied = redactor.ReplacementCount > 0, replacementCount = redactor.ReplacementCount,
                        files = redactions, unit = "Path/credential pattern replacements across exported string values; repeated occurrences count separately",
                        anonymized = false, notice = "Conservative redaction is not anonymization; sensitive content may remain." } }, Json);
                Text(name, Encoding.UTF8.GetString(final.ToArray()));
            }
            else Text(name, Encoding.UTF8.GetString(clean.ToArray()));
        }
        Data("model/model-summary.json", new { model.Name, model.CompatibilityLevel, objects });
        Text("model/tables.csv", Csv(new[] { "Name", "StorageMode", "Description", "Hidden" }, objects.Where(o => o.Kind == "Table").Select(o => new object?[] { o.Name, o.StorageMode, o.Description, o.Hidden })));
        Text("model/columns.csv", Csv(new[] { "Table", "Name", "DataType", "Hidden", "DisplayFolder", "FormatString", "Description" }, objects.Where(o => o.Kind == "Column").Select(o => new object?[] { o.Table, o.Name, o.DataType, o.Hidden, o.DisplayFolder, o.FormatString, o.Description })));
        Text("model/relationships.csv", Csv(new[] { "Name", "FromColumnId", "ToColumnId", "Active", "FromCardinality", "ToCardinality", "FilterDirection" }, rels.Select(r => new object?[] { r.Name, r.FromColumnId, r.ToColumnId, r.Active, r.FromCardinality, r.ToCardinality, r.FilterDirection })));
        foreach (var category in new[] { ("measures", "Measure"), ("calculated-objects", "Column"), ("functions", "Function") })
            Text("model/" + category.Item1 + ".dax", string.Join("\n\n", objects.Where(o => o.Kind == category.Item2 && o.Expression != null).Select(o => "// " + Clean(o.Id).Replace("\n", " ").Replace("\r", " ") + "\n" + Clean(o.Expression!))));
        Data("model/calculation-groups.json", objects.Where(o => o.Kind is "CalculationGroup" or "CalculationItem").ToArray());
        Data("model/dependencies.json", new { method = "TE2 parsed metadata dependencies; engine validation remains authoritative", edges = model.Dependencies.Where(d => included.Contains(d.ObjectId) && included.Contains(d.DependencyId)).OrderBy(d => d.ObjectId, StringComparer.Ordinal).ThenBy(d => d.DependencyId, StringComparer.Ordinal), unresolved = model.Dependencies.Where(d => included.Contains(d.ObjectId) && !included.Contains(d.DependencyId)).OrderBy(d => d.ObjectId, StringComparer.Ordinal).ThenBy(d => d.DependencyId, StringComparer.Ordinal) });
        Data("model/perspectives.json", model.Perspectives.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new { p.Name, objectIds = p.ObjectIds.Where(included.Contains).OrderBy(x => x, StringComparer.Ordinal).ToArray() }));
        Data("model/translations.json", model.Translations.Where(t => included.Contains(t.ObjectId)).OrderBy(t => t.ObjectId, StringComparer.Ordinal).ThenBy(t => t.Culture, StringComparer.Ordinal).ThenBy(t => t.Property, StringComparer.Ordinal));
        if (options.IncludeRoles) Data("model/roles.json", model.Roles.Where(r => included.Contains(ContextModel.ObjectId("Table", null, r.Table))).OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Table, StringComparer.Ordinal));
        var evidencePaths = new Dictionary<string, string> { ["BPA"] = "quality/bpa.json", ["VertiPaq"] = "quality/vertipaq.json", ["Tests"] = "quality/semantic-tests.json", ["Workspace"] = "workspace/semantic-diff.json" };
        foreach (var group in options.Evidence.GroupBy(e => e.Category))
        { if (!evidencePaths.TryGetValue(group.Key, out var path)) throw new ArgumentException("Unsupported evidence category."); Data(path, group.Where(e => included.Contains(e.ObjectId) || e.ObjectId == "" && !options.SelectedScope).OrderBy(e => e.ObjectId, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.Ordinal)); }
        if (options.IncludeAutomation)
        {
            Text("automation/README.md", AutomationReference.Readme);
            Text("automation/safe-script-capabilities.json", AutomationReference.CapabilitiesJson());
        }
        var sampling = new List<object>(); long cells = 0;
        if (options.IncludeSamples)
        {
            if (sampler == null && options.Samples.Any(s => s.Rows > 0)) throw new InvalidOperationException("Sampling requires an accessible semantic query connection.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sample in options.Samples.OrderBy(s => s.Table, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (!seen.Add(sample.Table) || sample.Rows < 0 || sample.Rows > options.MaximumRowsPerTable) throw new ArgumentException("Duplicate sample table or row count outside configured bounds.");
                if (sample.Rows == 0) continue;
                if (sample.Columns.Count == 0 || sample.Columns.Count > 200 || sample.Columns.Distinct(StringComparer.Ordinal).Count() != sample.Columns.Count) throw new ArgumentException("Select 1–200 distinct sample columns.");
                if (sample.OrderColumn != null && !sample.Columns.Contains(sample.OrderColumn)) throw new ArgumentException("The order column must be among the selected sample columns.");
                foreach (var name in sample.Columns.Concat(sample.OrderColumn == null ? Array.Empty<string>() : new[] { sample.OrderColumn }))
                {
                    var column = objects.FirstOrDefault(o => o.Kind == "Column" && o.Table == sample.Table && o.Name == name);
                    if (column == null || column.Hidden && !sample.IncludeHidden) throw new ArgumentException("A sample column is excluded, hidden or outside the export scope.");
                }
                cells += (long)sample.Rows * sample.Columns.Count; if (cells > options.MaximumSampleCells) throw new ArgumentException("Sample cell cap exceeded.");
            }
            foreach (var sample in options.Samples.Where(s => s.Rows > 0).OrderBy(s => s.Table, StringComparer.Ordinal))
            {
                var result = await sampler!.SampleAsync(sample, ct).ConfigureAwait(false); ct.ThrowIfCancellationRequested();
                if (!result.Columns.SequenceEqual(sample.Columns) || result.Rows.Count > sample.Rows || result.Rows.Any(r => r.Length != sample.Columns.Count)) throw new InvalidDataException("Sampler violated the reviewed projection or row bounds.");
                var path = "samples/" + Hash(Encoding.UTF8.GetBytes(sample.Table)).Substring(0, 24) + ".csv";
                Text(path, Csv(sample.Columns, result.Rows, options.MaximumBytes, ct));
                sampling.Add(new { table = sample.Table, file = path, requestedRows = sample.Rows, actualRows = result.Rows.Count, columns = sample.Columns, method = "FirstN", orderColumn = sample.OrderColumn ?? sample.Columns[0], ties = "Client row cap; order within ties is unspecified", anonymized = false });
            }
        }
        Text("AI_README.md", "# " + Clean(model.Name) + "\n\nCompatibility: " + model.CompatibilityLevel + "\n\n" + PrivacyNotice + "\n\n" +
            "Scope: " + (options.SelectedScope ? "Selected objects plus dependency context" : "Full model") + ". Exact inclusions, exclusions, samples and omissions are in manifest.json. Descriptive text has conservative path/credential redaction; this is not anonymization.\n\n" +
            string.Join("\n", objects.Where(o => o.Kind == "Table").Select(t => "- " + Clean(t.Name) + " (" + Clean(t.StorageMode) + "); row count unknown")) +
            "\n\n## Measure inventory\n" + string.Join("\n", objects.Where(o => o.Kind == "Measure").Select(m => "- " + Clean(m.Table + " / " + m.DisplayFolder + " / " + m.Name))) +
            "\n\nRelationships with cardinality/filter direction: model/relationships.csv. Calculation groups and items: model/calculation-groups.json. UDFs: model/functions.dax. Optional warnings/evidence: quality/ and workspace/.\n\n" +
            "Use the attached semantic metadata as authoritative for object names and relationships. Treat sampled rows as examples only, not complete data. Do not invent columns or measures that are absent from the model. When proposing DAX, state which existing objects it depends on.\n");
        // Last content file so the manifest includes its own redactions and every preceding file's counts.
        Data("manifest.json", new { format = "PbiBench.AI.Context", model.Name, scope = options.SelectedScope ? "Selected" : "Model", requested, included = included.OrderBy(x => x, StringComparer.Ordinal), excluded = excluded.OrderBy(x => x, StringComparer.Ordinal), options.IncludeRoles, options.IncludeAutomation, options.IncludeSamples, samples = sampling, maximumBytes = options.MaximumBytes, omissions = new[] { "Source and partition expressions (including calculated table definitions)", "Connections, credentials, annotations, role membership and machine paths", "Unavailable row counts and evidence are not inferred", "Explicit exclusions can leave unresolved DAX dependencies" } }, manifest: true);
        Text("checksums.sha256", string.Join("\n", files.Select(f => Hash(f.Value) + "  " + f.Key)) + "\n");
        return new(files, options.MaximumBytes);
    }
    public static async Task WriteAsync(ContextExportPlan plan, string path, bool sensitiveDataReviewed, CancellationToken ct)
    {
        if (!sensitiveDataReviewed) throw new InvalidOperationException("Review the exact files and acknowledge sensitive content before exporting.");
        ct.ThrowIfCancellationRequested(); var destination = Path.GetFullPath(path); var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 8192, true))
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) foreach (var file in plan.Files)
                {
                    ct.ThrowIfCancellationRequested(); var entry = zip.CreateEntry(file.Key, CompressionLevel.Optimal); entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var target = entry.Open(); await target.WriteAsync(file.Value, 0, file.Value.Length, ct).ConfigureAwait(false);
                    if (stream.Length > plan.MaximumBytes) throw new InvalidDataException("Actual ZIP exceeds the configured cap.");
                }
                if (stream.Length > plan.MaximumBytes) throw new InvalidDataException("Actual ZIP exceeds the configured cap.");
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            AtomicQueryFile.Commit(temporary, destination, ct);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private sealed class Redactor
    {
        public long ReplacementCount { get; private set; }
        public string Clean(string? text)
        {
            var paths = Regex.Replace(text ?? "", @"(?i)(?:[a-z]:[\\/]|\\\\)[^\s""<>]*", _ => { ReplacementCount++; return "[local-path-redacted]"; }, RegexOptions.None, TimeSpan.FromSeconds(1));
            return Regex.Replace(paths, @"(?i)\b(password|pwd|access[_ -]?token|api[_ -]?key|client[_ -]?secret)\s*[=:]\s*[^;\s""<>]+", m => { ReplacementCount++; return m.Groups[1].Value + "=[redacted]"; }, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
    }
    private static string Csv(IEnumerable<string> columns, IEnumerable<object?[]> rows, Func<string?, string> clean, long maximumBytes, CancellationToken ct)
    {
        string Cell(object? value)
        {
            var text = value switch { null or DBNull => "", DateTime d => d.ToString("O", CultureInfo.InvariantCulture), DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture), bool b => b ? "true" : "false", string s => s, IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => throw new InvalidDataException("Unsupported sample value type.") };
            if (text.Length > 262144) throw new InvalidDataException("A context cell exceeds 256 KiB.");
            return "\"" + clean(text).Replace("\"", "\"\"") + "\"";
        }
        var builder = new StringBuilder(string.Join(",", columns.Select(c => Cell(c))) + "\n");
        foreach (var row in rows) { ct.ThrowIfCancellationRequested(); builder.AppendLine(string.Join(",", row.Select(Cell))); if (builder.Length * 2L > maximumBytes) throw new InvalidDataException("CSV exceeds the conservative export memory cap."); }
        return builder.ToString().Replace("\r\n", "\n");
    }
    private static void WriteClean(JsonElement value, Utf8JsonWriter writer, Func<string?, string> clean, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        switch (value.ValueKind)
        {
            case JsonValueKind.Object: writer.WriteStartObject(); foreach (var property in value.EnumerateObject()) { writer.WritePropertyName(property.Name); WriteClean(property.Value, writer, clean, ct); } writer.WriteEndObject(); break;
            case JsonValueKind.Array: writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) WriteClean(item, writer, clean, ct); writer.WriteEndArray(); break;
            case JsonValueKind.String: writer.WriteStringValue(clean(value.GetString())); break;
            default: value.WriteTo(writer); break;
        }
    }
    private sealed class BoundedMemoryStream(long maximum) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) { if (Position + count > maximum) throw new InvalidDataException("A context section exceeds the size cap."); base.Write(buffer, offset, count); }
#if !NETFRAMEWORK
        public override void Write(ReadOnlySpan<byte> buffer) { if (Position + buffer.Length > maximum) throw new InvalidDataException("A context section exceeds the size cap."); base.Write(buffer); }
#endif
    }
}
