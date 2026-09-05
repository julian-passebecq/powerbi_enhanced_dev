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
    public static Task<LocalSemanticCatalog> ReadLocalModelAsync(string? modelPath, CancellationToken ct) =>
        Task.Run(() => TmdlDeclarationReader.Read(modelPath, ct), ct);
}
