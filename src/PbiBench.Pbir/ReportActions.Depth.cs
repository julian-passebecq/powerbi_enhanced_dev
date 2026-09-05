using System.Text.Json.Nodes;

namespace PbiBench.Pbir;

public sealed partial class ReportActions
{
    public ReportChangePlan BatchVisualProperties(ReportIndex report, IEnumerable<string> visualFiles, bool? hidden, bool? titleShow, string? title)
    {
        var files = visualFiles.ToArray();
        if (files.Length is < 1 or > 200 || files.Distinct(StringComparer.Ordinal).Count() != files.Length)
            throw new ArgumentException("Select 1–200 distinct visuals for a batch.");
        if (hidden == null && titleShow == null && title == null) throw new ArgumentException("Choose a visibility or title change.");
        if (title != null) Text(title, 512, "Title");
        var rows = new List<ReportFileChange>();
        foreach (var file in files)
        {
            if (!report.Pages.SelectMany(p => p.Visuals).Any(v => v.File == file)) throw new ArgumentException("Batch target is not a visual.");
            var before = report.Files[file]; var json = before.Json();
            if (hidden != null) json["isHidden"] = hidden.Value;
            if (titleShow != null || title != null)
            {
                var visual = json["visual"] as JsonObject ?? throw new InvalidOperationException("Title changes require chart visuals.");
                var objects = visual["visualContainerObjects"] as JsonObject; if (objects == null) visual["visualContainerObjects"] = objects = new();
                var titles = objects["title"] as JsonArray; if (titles == null) objects["title"] = titles = new();
                if (titles.Count > 1 || titles.OfType<JsonObject>().Any(t => t["selector"] != null)) throw new InvalidOperationException("Only a common unscoped title is supported.");
                if (titles.Count == 0) titles.Add(new JsonObject());
                var first = titles[0]!.AsObject(); var properties = first["properties"] as JsonObject; if (properties == null) first["properties"] = properties = new();
                if (title != null) properties["text"] = Literal("'" + title.Replace("'", "''") + "'");
                if (titleShow != null) properties["show"] = Literal(titleShow.Value ? "true" : "false");
            }
            rows.Add(new(file, before.Bytes(), Bytes(json)));
        }
        return engine.Prepare(report, hidden == null && titleShow == true && files.Length == 1 ? "Set visual title" : "Batch visual visibility / common title · " + files.Length + " visuals", rows);
    }
    private const string BookmarkSchema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/bookmark/1.0.0/schema.json";
    private const string BookmarksSchema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/bookmarksMetadata/1.0.0/schema.json";
    public ReportChangePlan EditBookmark(ReportIndex report, string file, string displayName, bool duplicate)
    {
        Text(displayName, 256, "Bookmark display name");
        var source = report.Files[file];
        if (!file.StartsWith("definition/bookmarks/", StringComparison.Ordinal) || !file.EndsWith(".bookmark.json", StringComparison.Ordinal) || source.Schema != BookmarkSchema)
            throw new InvalidOperationException("Bookmark edits support the pinned, tested bookmark/1.0.0 schema only. Other versions remain browse-only for this action.");
        var json = source.Json(); json["displayName"] = displayName;
        if (!duplicate) return engine.Prepare(report, "Rename bookmark display name", new[] { new ReportFileChange(file, source.Bytes(), Bytes(json)) });
        var id = Id(); json["name"] = id;
        const string metadataPath = "definition/bookmarks/bookmarks.json";
        report.Files.TryGetValue(metadataPath, out var metadata);
        if (metadata != null && metadata.Schema != BookmarksSchema) throw new InvalidOperationException("Bookmark ordering requires the pinned bookmarksMetadata/1.0.0 schema.");
        var order = metadata?.Json() ?? new JsonObject { ["$schema"] = BookmarksSchema, ["items"] = new JsonArray() };
        order["items"]!.AsArray().Add(new JsonObject { ["name"] = id });
        return engine.Prepare(report, "Duplicate bookmark · " + displayName, new[] {
            new ReportFileChange("definition/bookmarks/" + id + ".bookmark.json", null, Bytes(json)),
            new ReportFileChange(metadataPath, metadata?.Bytes(), Bytes(order)) });
    }
    public static string FormattingEvidence(ReportIndex report, string visualFile)
    {
        var visual = report.Pages.SelectMany(p => p.Visuals).Single(v => v.File == visualFile);
        if (visual.Type is not ("tableEx" or "pivotTable")) return "Select a table or matrix to inspect formatting.";
        var json = report.Files[visualFile].Json();
        return "Detector / preview only. Field selectors and conditional expressions need compatible field-level fixtures before copying. No write plan is produced.\n\n" +
            ReportIndex.At(json, "visual", "objects")?.ToJsonString();
    }
}
