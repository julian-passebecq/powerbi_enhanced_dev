using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PbiBench.Pbir;

public sealed record DisplayNameMapping(SemanticField Field, string DisplayName, string Report, string Page, string Visual);
public sealed class DisplayNameManifest
{
    public int Version => 1;
    public IReadOnlyList<DisplayNameMapping> Mappings { get; }
    public DisplayNameManifest(IEnumerable<DisplayNameMapping> mappings)
    {
        var rows = mappings.Take(10001).ToArray();
        if (rows.Length > 10000 || rows.Any(r => new[] { r.DisplayName, r.Report, r.Page, r.Visual, r.Field.Table, r.Field.Name }
            .Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 512 || v.Any(char.IsControl)) || r.Field.Kind is not ("Measure" or "Column"))) throw new InvalidDataException("Invalid display-name mapping or payload bound.");
        if (rows.GroupBy(r => (r.Field, r.Report, r.Page, r.Visual)).Any(g => g.Select(r => r.DisplayName).Distinct().Count() > 1))
            throw new InvalidDataException("Conflicting display names in a visual. Resolve the mapping explicitly.");
        Mappings = Array.AsReadOnly(rows.Distinct().ToArray());
    }
    public static DisplayNameManifest Extract(ReportIndex report) => new(report.Pages.SelectMany(p => p.Visuals.SelectMany(v =>
        Projections(report.Files[v.File].Json()).Where(item => item.Projection["displayName"] is JsonValue).Select(item =>
            new DisplayNameMapping(item.Field, item.Projection["displayName"]!.GetValue<string>(), report.Name, p.Id, v.Id)))));
    internal static IEnumerable<(JsonObject Projection, SemanticField Field)> Projections(JsonObject visual)
    {
        if (ReportIndex.At(visual, "visual", "query", "queryState") is not JsonObject state) yield break;
        foreach (var role in state.Select(p => p.Value).OfType<JsonObject>())
            foreach (var projection in (role["projections"] as JsonArray ?? new()).OfType<JsonObject>())
            {
                if (projection["field"] is not JsonObject field) continue;
                var references = ReportLineage.References(field).ToArray();
                if (references.Length == 1 && references[0].Table is { } table)
                    yield return (projection, new(table, references[0].Name, references[0].Kind));
            }
    }
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public static DisplayNameManifest Parse(string text)
    {
        var json = Disk.Parse(text);
        if (json["Version"]?.GetValue<int>() != 1 || json.Any(p => p.Key is not ("Version" or "Mappings"))) throw new InvalidDataException("Unsupported display-name contract.");
        foreach (var item in json["Mappings"]!.AsArray().OfType<JsonObject>())
        {
            if (item.Any(p => p.Key is not ("Field" or "DisplayName" or "Report" or "Page" or "Visual")) ||
                item["Field"]!.AsObject().Any(p => p.Key is not ("Table" or "Name" or "Kind"))) throw new InvalidDataException("Unexpected display-name data.");
        }
        return new(JsonSerializer.Deserialize<DisplayNameMapping[]>(json["Mappings"]!.ToJsonString()) ?? throw new InvalidDataException("Missing mappings."));
    }
    public static Task<DisplayNameManifest> ReadAsync(string path, CancellationToken ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); return Parse(Disk.ReadText(path)); }, ct);
    public Task SaveAsync(string destination, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested(); Disk.CheckLinks(destination); var bytes = Encoding.UTF8.GetBytes(ToJson());
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.Write(bytes, 0, bytes.Length);
    }, ct);
}
