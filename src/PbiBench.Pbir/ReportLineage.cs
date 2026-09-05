using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PbiBench.Pbir;

public sealed record SemanticField(string Table, string Name, string Kind);
public sealed record ReportUsage(string Report, string Page, string Visual, string File, string Pointer,
    string? Table, string Name, string Kind, string Status, string ReportRoot);
public sealed record LocalSemanticCatalog(IReadOnlyList<SemanticField> Fields, bool Complete, string Notice);

/// <summary>Structural semantic query references only. Literal slicer/filter values are never treated as object names.</summary>
public static class ReportLineage
{
    public static IReadOnlyList<ReportUsage> Build(ReportIndex report, IReadOnlyList<SemanticField>? fields = null, bool complete = false)
    {
        var result = new List<ReportUsage>();
        var extensionFields = report.Files.Values.Where(f => f.ParseError == null && f.Schema?.Contains("/reportExtension/") == true)
            .SelectMany(f => (f.Json()["entities"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            .SelectMany(e => (e["measures"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(m => new SemanticField(e["name"]?.ToString() ?? "", m["name"]?.ToString() ?? "", "Measure"))).ToArray();
        foreach (var file in report.Files.Values.Where(f => f.ParseError == null && f.Path.StartsWith("definition/", StringComparison.Ordinal)))
        {
            var page = report.Pages.FirstOrDefault(p => file.Path.StartsWith(p.File.Substring(0, p.File.Length - "page.json".Length), StringComparison.Ordinal));
            var visual = page?.Visuals.FirstOrDefault(v => v.File == file.Path);
            foreach (var reference in References(file.Json()))
            {
                var found = fields?.Any(f => string.Equals(f.Table, reference.Table, StringComparison.OrdinalIgnoreCase) && string.Equals(f.Name, reference.Name, StringComparison.OrdinalIgnoreCase) && f.Kind == reference.Kind) == true;
                var extension = extensionFields.Any(f => f.Table == reference.Table && f.Name == reference.Name && f.Kind == reference.Kind);
                var status = reference.Table == null ? "Unresolved source alias" : extension ? "Resolved (report measure)" : found ? "Resolved" : complete ? "Broken reference" : "Unverified (model unavailable or partial)";
                result.Add(new(report.Name, page?.Name ?? "Report", visual?.Id ?? "(page/report)", file.Path, reference.Pointer, reference.Table, reference.Name, reference.Kind, status, report.Root));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }
    internal sealed record Reference(JsonObject Node, JsonObject? Source, string Pointer, string? Table, string Name, string Kind);
    internal static IEnumerable<Reference> References(JsonNode root)
    {
        var result = new List<Reference>();
        void Visit(JsonNode? node, string pointer, Dictionary<string, string> inherited)
        {
            if (node is JsonObject obj)
            {
                var aliases = new Dictionary<string, string>(inherited, StringComparer.Ordinal);
                if (obj["From"] is JsonArray from) foreach (var item in from.OfType<JsonObject>())
                    if (item["Name"] is JsonValue name && item["Entity"] is JsonValue entity) aliases[name.ToString()] = entity.ToString();
                foreach (var kind in new[] { "Measure", "Column" }) if (obj[kind] is JsonObject field && field["Property"] is JsonValue property)
                {
                    var source = ReportIndex.At(field, "Expression", "SourceRef") as JsonObject;
                    var table = source?["Entity"]?.ToString();
                    if (table == null && source?["Source"]?.ToString() is { } alias && aliases.TryGetValue(alias, out var resolved)) table = resolved;
                    result.Add(new(field, source, pointer + "/" + kind, table, property.ToString(), kind));
                }
                foreach (var child in obj) Visit(child.Value, pointer + "/" + child.Key.Replace("~", "~0").Replace("/", "~1"), aliases);
            }
            else if (node is JsonArray array) for (var i = 0; i < array.Count; i++) Visit(array[i], pointer + "/" + i, inherited);
        }
        Visit(root, "", new(StringComparer.Ordinal)); return result;
    }
    public static Task<LocalSemanticCatalog> ReadLocalModelAsync(string? modelPath, CancellationToken ct) => Task.Run(() =>
    {
        if (modelPath == null || !Directory.Exists(modelPath)) return new LocalSemanticCatalog(Array.Empty<SemanticField>(), false, "No local semantic model. Remote references remain unverified; authentication belongs to Fabric Toolbox.");
        var fields = new List<SemanticField>(); var complete = true; var tableCount = 0;
        // Read only table declaration files. Expressions and partition/source text are never exported.
        foreach (var path in Disk.Enumerate(modelPath, ct).Where(p => p.EndsWith(".tmdl", StringComparison.OrdinalIgnoreCase) && p.Replace('\\', '/').IndexOf("/tables/", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            string? table = null; var tableIndent = -1;
            foreach (var line in Disk.ReadText(path).Split('\n'))
            {
                ct.ThrowIfCancellationRequested();
                var match = Regex.Match(line.TrimEnd('\r'), @"^(\s*)(table|column|measure)\s+('(?:[^']|'')*'|[^=\r\n]+?)(?:\s*=.*)?\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (!match.Success) continue;
                var indent = match.Groups[1].Value.Length; var kind = match.Groups[2].Value; var name = match.Groups[3].Value.Trim();
                if (name.StartsWith("'", StringComparison.Ordinal) && name.EndsWith("'", StringComparison.Ordinal)) name = name.Substring(1, name.Length - 2).Replace("''", "'");
                if (kind == "table" && indent == 0) { table = name; tableIndent = indent; tableCount++; }
                else if (table != null && indent == tableIndent + 1) fields.Add(new(table, name, kind == "measure" ? "Measure" : "Column"));
                else if (kind is "measure" or "column") complete = false;
            }
        }
        // TMDL indentation can use tabs or spaces. Unsupported layouts are explicitly partial instead of declaring absent fields broken.
        return new LocalSemanticCatalog(Array.AsReadOnly(fields.Distinct().ToArray()), complete && tableCount > 0, tableCount > 0 ? (complete ? "Local TMDL declarations indexed." : "Partial TMDL declaration index; absence is unverified.") : "No supported local TMDL tables found.");
    }, ct);
}
