using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PbiBench.Pbir;

public sealed record ReportActionCard(string Id, string Title, string Selection, string Purpose);
public static class ReportActionGallery
{
    public static IReadOnlyList<ReportActionCard> All { get; } = Array.AsReadOnly(new[] {
        new ReportActionCard("duplicate-page", "Duplicate page", "Page", "Copy page definition and visuals; append the new page to page order."),
        new ReportActionCard("duplicate-visual", "Duplicate visual", "Visual", "Copy the visual with a new ID on the selected page."),
        new ReportActionCard("copy-visual", "Copy visual to local report", "Visual + target page", "Copy a resource-independent visual between reports sharing a local model."),
        new ReportActionCard("map-field", "Replace semantic reference", "Report + explicit mapping", "Replace structured field references and their query labels."),
        new ReportActionCard("title", "Edit visual title", "Visual", "Set a literal title using the public visual configuration contract."),
        new ReportActionCard("annotation", "Add / update annotation", "Report, page or visual", "Store a bounded name/value annotation."),
        new ReportActionCard("inventory", "Export inventory", "Report", "Export paths, IDs and layout; exclude persisted filter values and .pbi files."),
        new ReportActionCard("validate", "Validate PBIR / broken references", "Report", "Check pinned schemas and local semantic references."),
        new ReportActionCard("restore", "Restore reviewed backup", "Backup manifest", "Preview original bytes; refuse later disk edits.") });
}
public sealed class ReportActions(ReportChangeEngine engine)
{
    private static byte[] Bytes(JsonObject json) => Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    private static string Text(string value, int max, string name)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(c => c == '\0' || char.IsControl(c) && c != '\n')) throw new ArgumentException(name + " must contain 1–" + max + " characters."); return value; }
    private static string Id() => Guid.NewGuid().ToString("N").Substring(0, 20);
    private ReportChangePlan Edit(ReportIndex report, string file, string title, Action<JsonObject> mutate)
    {
        var before = report.Files[file]; var json = before.Json(); mutate(json);
        return engine.Prepare(report, title, new[] { new ReportFileChange(file, before.Bytes(), Bytes(json)) });
    }
    public ReportChangePlan SetTitle(ReportIndex report, string visualFile, string title) => Edit(report, visualFile, "Set visual title", json =>
    {
        Text(title, 512, "Title"); var visual = json["visual"] as JsonObject ?? throw new InvalidOperationException("Select a chart visual.");
        var objects = visual["visualContainerObjects"] as JsonObject; if (objects == null) visual["visualContainerObjects"] = objects = new();
        var titles = objects["title"] as JsonArray; if (titles == null) objects["title"] = titles = new();
        if (titles.Count == 0) titles.Add(new JsonObject());
        var first = titles[0]!.AsObject(); var properties = first["properties"] as JsonObject; if (properties == null) first["properties"] = properties = new();
        properties["text"] = Literal("'" + title.Replace("'", "''") + "'"); properties["show"] = Literal("true");
    });
    private static JsonObject Literal(string value) => new() { ["expr"] = new JsonObject { ["Literal"] = new JsonObject { ["Value"] = value } } };
    public ReportChangePlan Annotate(ReportIndex report, string file, string name, string value) => Edit(report, file, "Set annotation · " + Text(name, 128, "Annotation name"), json =>
    {
        Text(value, 2048, "Annotation value"); var annotations = json["annotations"] as JsonArray; if (annotations == null) json["annotations"] = annotations = new();
        var existing = annotations.OfType<JsonObject>().Where(a => a["name"]?.ToString() == name).ToArray();
        if (existing.Length > 1) throw new InvalidDataException("Annotation name is duplicated; inspect the definition.");
        if (existing.Length == 1) existing[0]["value"] = value; else annotations.Add(new JsonObject { ["name"] = name, ["value"] = value });
    });
    public ReportChangePlan DuplicatePage(ReportIndex report, string pageId, string displayName)
    {
        Text(displayName, 256, "Page name"); var page = report.Pages.Single(p => p.Id == pageId); var id = Id();
        var prefix = page.File.Substring(0, page.File.Length - "page.json".Length); var rows = new List<ReportFileChange>();
        foreach (var file in report.Files.Values.Where(f => f.Path.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var bytes = file.Bytes(); if (file.Path == page.File) { var json = file.Json(); json["name"] = id; json["displayName"] = displayName; bytes = Bytes(json); }
            rows.Add(new("definition/pages/" + id + "/" + file.Path.Substring(prefix.Length), null, bytes));
        }
        var metadata = report.Files["definition/pages/pages.json"]; var order = metadata.Json();
        var pageOrder = order["pageOrder"] as JsonArray; if (pageOrder == null) order["pageOrder"] = pageOrder = new(report.Pages.Select(p => (JsonNode?)JsonValue.Create(p.Id)).ToArray());
        pageOrder.Add(id); rows.Add(new(metadata.Path, metadata.Bytes(), Bytes(order)));
        return engine.Prepare(report, "Duplicate page · " + page.Name, rows);
    }
    public ReportChangePlan DuplicateVisual(ReportIndex report, string visualFile, string targetPageId) => CopyVisual(report, visualFile, report, targetPageId);
    public ReportChangePlan CopyVisual(ReportIndex source, string visualFile, ReportIndex target, string targetPageId)
    {
        var page = target.Pages.Single(p => p.Id == targetPageId); var original = source.Files[visualFile]; var json = original.Json();
        var crossReport = !source.Root.Equals(target.Root, StringComparison.OrdinalIgnoreCase);
        if (crossReport && (source.SemanticModelPath == null || !string.Equals(source.SemanticModelPath, target.SemanticModelPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Cross-report copy requires the same local semantic model; map fields explicitly in a separate reviewed plan.");
        if (json["visualGroup"] != null || json["parentGroupName"] != null) throw new InvalidOperationException("Grouped visuals require their group context; duplicate the page instead.");
        if (crossReport && (source.Resources.Count > 0 || !source.Files.ContainsKey(visualFile))) throw new InvalidOperationException("Cross-report copy currently supports resource-independent reports only.");
        if (crossReport && (source.Files.Values.Any(f => f.Schema?.Contains("/reportExtension/") == true) || source.Files["definition/report.json"].Json()["publicCustomVisuals"] != null || source.Files["definition/report.json"].Json()["organizationCustomVisuals"] != null))
            throw new InvalidOperationException("Custom visuals and report measures require additional target definitions; cross-report copy is not yet supported for these reports.");
        var prefix = visualFile.Substring(0, visualFile.Length - "visual.json".Length);
        if (source.Files.Values.Any(f => f.Path.StartsWith(prefix, StringComparison.Ordinal) && f.Path != visualFile)) throw new InvalidOperationException("This visual has companion definitions. Duplicate its page to preserve them.");
        var id = Id(); json["name"] = id;
        return engine.Prepare(target, "Copy visual · " + original.Json()["name"], new[] { new ReportFileChange("definition/pages/" + page.Id + "/visuals/" + id + "/visual.json", null, Bytes(json)) }, crossReport ? new[] { source } : null);
    }
    public ReportChangePlan ReplaceReference(ReportIndex report, SemanticField before, SemanticField after)
    {
        if (before.Kind != after.Kind || before.Kind is not ("Measure" or "Column")) throw new ArgumentException("Map a column to a column or a measure to a measure.");
        Text(after.Table, 512, "Table"); Text(after.Name, 512, "Field"); var rows = new List<ReportFileChange>();
        foreach (var file in report.Files.Values.Where(f => f.Path.StartsWith("definition/", StringComparison.Ordinal) && f.ParseError == null))
        {
            var json = file.Json(); var references = ReportLineage.References(json).Where(r => r.Kind == before.Kind && r.Table == before.Table && r.Name == before.Name).ToArray();
            if (references.Length == 0) continue;
            foreach (var reference in references)
            {
                reference.Node["Property"] = after.Name;
                // Replace only the captured field's source. Other fields using the original alias are preserved.
                reference.Node["Expression"] = new JsonObject { ["SourceRef"] = new JsonObject { ["Entity"] = after.Table } };
            }
            void Labels(JsonNode? node)
            {
                if (node is JsonObject obj) foreach (var pair in obj.ToArray())
                {
                    if (pair.Key is "queryRef" or "nativeQueryRef" && pair.Value?.ToString() == before.Table + "." + before.Name) obj[pair.Key] = after.Table + "." + after.Name;
                    else Labels(pair.Value);
                }
                else if (node is JsonArray array) foreach (var item in array) Labels(item);
            }
            Labels(json); rows.Add(new(file.Path, file.Bytes(), Bytes(json)));
        }
        return engine.Prepare(report, "Map " + before.Table + "[" + before.Name + "] → " + after.Table + "[" + after.Name + "]", rows);
    }
    public static string Inventory(ReportIndex report) => JsonSerializer.Serialize(new {
        version = 1, report = report.Name, pages = report.Pages.Select(p => new { p.Id, p.Name, p.Width, p.Height, visuals = p.Visuals }), resources = report.Resources
    }, new JsonSerializerOptions { WriteIndented = true });
    public static async Task ExportInventoryAsync(ReportIndex report, string destination, CancellationToken ct)
    {
        var path = Path.GetFullPath(destination); Disk.CheckLinks(path);
        if (path.StartsWith(report.Root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Export inventory outside the report folder so it cannot become a PBIR definition.");
        var bytes = Encoding.UTF8.GetBytes(Inventory(report)); ct.ThrowIfCancellationRequested();
        // Exports cannot overwrite any report, backup, or user file through this alternate write route.
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }
}
