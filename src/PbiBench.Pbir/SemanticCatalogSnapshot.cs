using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PbiBench.Pbir;

/// <summary>Versioned metadata-only handoff. No connection, partition, source expression, credentials or values.</summary>
public sealed class SemanticCatalogSnapshot
{
    public int Version => 1;
    public IReadOnlyList<SemanticField> Fields { get; }
    public bool Complete { get; }
    public DateTimeOffset CapturedAt { get; }
    public SemanticCatalogSnapshot(IEnumerable<SemanticField> fields, bool complete, DateTimeOffset capturedAt)
    {
        var copy = fields.Take(100001).ToArray();
        if (copy.Length > 100000 || copy.Any(f => f.Kind is not ("Measure" or "Column") ||
            string.IsNullOrWhiteSpace(f.Table) || string.IsNullOrWhiteSpace(f.Name) || f.Table.Length > 512 || f.Name.Length > 512 ||
            f.Table.Any(char.IsControl) || f.Name.Any(char.IsControl))) throw new InvalidDataException("Invalid semantic catalog fields.");
        if (copy.Select(f => f.Kind + "\0" + f.Table + "\0" + f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Length)
            throw new InvalidDataException("Duplicate semantic catalog fields.");
        Fields = Array.AsReadOnly(copy); Complete = complete; CapturedAt = capturedAt;
    }
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public static SemanticCatalogSnapshot Parse(string text)
    {
        var json = Disk.Parse(text);
        if (json["Version"]?.GetValue<int>() != 1 || json.Any(p => p.Key is not ("Version" or "Fields" or "Complete" or "CapturedAt")))
            throw new InvalidDataException("Unsupported semantic catalog contract.");
        var fields = json["Fields"]!.AsArray().Select(n =>
        {
            var f = n!.AsObject();
            if (f.Any(p => p.Key is not ("Table" or "Name" or "Kind"))) throw new InvalidDataException("Unexpected semantic catalog data.");
            return new SemanticField(f["Table"]!.GetValue<string>(), f["Name"]!.GetValue<string>(), f["Kind"]!.GetValue<string>());
        });
        return new(fields, json["Complete"]!.GetValue<bool>(), json["CapturedAt"]!.GetValue<DateTimeOffset>());
    }
    public Task SaveAsync(string destination, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested(); Disk.CheckLinks(destination);
        var bytes = Encoding.UTF8.GetBytes(ToJson());
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
    }, ct);
    public static Task<SemanticCatalogSnapshot> ReadAsync(string path, CancellationToken ct) => Task.Run(() =>
    { ct.ThrowIfCancellationRequested(); return Parse(Disk.ReadText(path)); }, ct);
}

/// <summary>Declaration reader, not a TMDL expression parser. Only direct table members count; ambiguous layouts stay partial.</summary>
internal static class TmdlDeclarationReader
{
    private static readonly Regex Declaration = new(@"^(table|column|measure)\s+('(?:[^']|'')*'|[^'=:\r\n]+?)(?:\s*=.*)?\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex TableRef = new(@"^ref\s+table\s+('(?:[^']|'')*'|[^'\r\n]+)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string Name(string value) => value.Trim().StartsWith("'", StringComparison.Ordinal) ? value.Trim().Substring(1, value.Trim().Length - 2).Replace("''", "'") : value.Trim();
    internal static LocalSemanticCatalog Read(string? modelPath, CancellationToken ct)
    {
        if (modelPath == null || !Directory.Exists(modelPath)) return new(Array.Empty<SemanticField>(), false, "No local semantic model. References remain unverified; authentication belongs to Fabric Toolbox.");
        var fields = new List<SemanticField>(); var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var complete = true;
        foreach (var path in Disk.Enumerate(modelPath, ct).Where(p => p.EndsWith(".tmdl", StringComparison.OrdinalIgnoreCase)))
        {
            var tableFile = path.Replace('\\', '/').IndexOf("/tables/", StringComparison.OrdinalIgnoreCase) >= 0;
            string? table = null, childIndent = null; var inFence = false; var fileTables = 0; var directBlock = "";
            foreach (var raw in Disk.ReadText(path).Split('\n'))
            {
                ct.ThrowIfCancellationRequested(); var line = raw.TrimEnd('\r'); var text = line.TrimStart(' ', '\t');
                var indent = line.Substring(0, line.Length - text.Length);
                if (text.StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
                if (inFence || text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith("///", StringComparison.Ordinal)) continue;
                if (!tableFile)
                {
                    var reference = TableRef.Match(text); if (reference.Success) refs.Add(Name(reference.Groups[1].Value));
                    // A declaration outside supported table files means the inventory is incomplete.
                    if (indent.Length == 0 && Declaration.IsMatch(text)) complete = false;
                    continue;
                }
                if (indent.Length == 0)
                {
                    table = null; childIndent = null; directBlock = "";
                    var declaration = Declaration.Match(text);
                    if (declaration.Success && declaration.Groups[1].Value == "table")
                    { table = Name(declaration.Groups[2].Value); fileTables++; if (!tables.Add(table)) complete = false; }
                    else complete = false;
                    continue;
                }
                if (table == null) { complete = false; continue; }
                childIndent ??= indent;
                if (!indent.StartsWith(childIndent, StringComparison.Ordinal)) { complete = false; continue; }
                // Nested metadata/expressions may contain declaration-like text. Never promote it to a field.
                if (indent != childIndent)
                {
                    if (Declaration.IsMatch(text) && !Regex.IsMatch(directBlock, @"^(measure|column|partition|annotation|calculationGroup)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) complete = false;
                    continue;
                }
                directBlock = text;
                var member = Declaration.Match(text);
                if (member.Success && member.Groups[1].Value is "column" or "measure")
                    fields.Add(new(table, Name(member.Groups[2].Value), member.Groups[1].Value == "measure" ? "Measure" : "Column"));
                else if (Regex.IsMatch(text, @"^(column|measure|table)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ||
                    !Regex.IsMatch(text, @"^(?:[a-zA-Z][a-zA-Z0-9]*\s*:|(?:hierarchy|partition|annotation|calculationGroup|variation|changedProperty|extendedProperty)\b)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) complete = false;
            }
            if (inFence || tableFile && fileTables == 0) complete = false;
        }
        if (refs.Except(tables, StringComparer.OrdinalIgnoreCase).Any()) complete = false;
        var unique = fields.GroupBy(f => f.Kind + "\0" + f.Table + "\0" + f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (unique.Any(g => g.Count() != 1)) complete = false;
        complete &= tables.Count > 0;
        return new(Array.AsReadOnly(unique.Select(g => g.First()).ToArray()), complete,
            complete ? "Local TMDL declarations indexed (relative indentation)." : "Partial or unsupported TMDL inventory; missing fields remain unverified.");
    }
}
