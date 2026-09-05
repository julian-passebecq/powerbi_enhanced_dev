using System.Text.Json.Nodes;
using Json.Schema;

namespace PbiBench.Pbir;

public sealed record ReportIssue(string Severity, string File, string Location, string Message);

/// <summary>Validates against pinned Microsoft schemas entirely offline; unknown versions fail closed for writes.</summary>
public sealed class ReportValidator
{
    private readonly Dictionary<string, JsonSchema> schemas = new(StringComparer.Ordinal);
    private readonly EvaluationOptions options = new() { OutputFormat = OutputFormat.List };
    public ReportValidator()
    {
        options.SchemaRegistry.Fetch = _ => throw new InvalidDataException("Schema reference is not in the pinned offline bundle.");
        var assembly = typeof(ReportValidator).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd(); var document = JsonNode.Parse(text); if (document?["$id"] == null) continue;
            // A few upstream versions repeat a sibling's $id. Use the actual pinned file URI for registry resolution.
            var id = "https://developer.microsoft.com/json-schemas/" + name.Substring("PbiBenchSchemas/".Length).Replace('\\', '/');
            document["$id"] = id; text = document.ToJsonString();
            var schema = JsonSchema.FromText(text); schemas.Add(id, schema); options.SchemaRegistry.Register(new Uri(id), schema);
        }
    }
    public IReadOnlyList<ReportIssue> Validate(ReportIndex report)
    {
        var issues = new List<ReportIssue>();
        if (report.Version != "4.0") issues.Add(new("Error", "definition.pbir", "/version", "Unsupported PBIR format version: " + report.Version + ". Inspect read-only and update the PBIR lane before editing."));
        if (!report.Enhanced) issues.Add(new("Error", "definition.pbir", "", "PBIR-Legacy is read-only. Save enhanced PBIR using Power BI Desktop."));
        foreach (var required in new[] { "definition.pbir", "definition/report.json", "definition/version.json", "definition/pages/pages.json" })
            if (!report.Files.ContainsKey(required)) issues.Add(new("Error", required, "", "Required PBIR file is missing."));
        foreach (var file in report.Files.Values)
        {
            if (file.ParseError != null) { issues.Add(new("Error", file.Path, "", "Invalid JSON: " + file.ParseError)); continue; }
            if (file.Schema == null || !schemas.TryGetValue(file.Schema, out var schema)) { issues.Add(new("Error", file.Path, "$schema", "Unknown or missing schema version. Inspect read-only; update the PBIR schema lane before editing.")); continue; }
            try
            {
                var result = schema.Evaluate(file.Json(), options);
                void Add(EvaluationResults evaluation)
                {
                    if (evaluation.Errors != null) foreach (var error in evaluation.Errors) issues.Add(new("Error", file.Path, evaluation.InstanceLocation.ToString(), error.Key + ": " + error.Value));
                    if (evaluation.Details != null) foreach (var detail in evaluation.Details.Where(d => !d.IsValid)) Add(detail);
                }
                if (!result.IsValid) Add(result);
            }
            catch (Exception error) when (error is InvalidDataException || error is JsonSchemaException || error is InvalidOperationException)
            { issues.Add(new("Error", file.Path, "$schema", "Offline schema evaluation failed: " + error.Message)); }
        }
        var pageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in report.Pages)
        {
            if (!pageIds.Add(page.Id) || page.File.Split('/')[2] != page.Id) issues.Add(new("Error", page.File, "/name", "Page ID is duplicated or differs from its folder."));
            if (page.Width <= 0 || page.Height <= 0) issues.Add(new("Error", page.File, "/width", "Page dimensions must be positive."));
            var visualIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var visual in page.Visuals)
            {
                if (!visualIds.Add(visual.Id) || visual.File.Split('/')[4] != visual.Id) issues.Add(new("Error", visual.File, "/name", "Visual ID is duplicated or differs from its folder."));
                if (visual.Width <= 0 || visual.Height <= 0) issues.Add(new("Error", visual.File, "/position", "Visual dimensions must be positive."));
            }
        }
        if (report.Files.TryGetValue("definition/pages/pages.json", out var pages) && pages.ParseError == null && pages.Json()["pageOrder"] is JsonArray order)
            foreach (var id in order.Select(n => n?.ToString())) if (id == null || !pageIds.Contains(id)) issues.Add(new("Error", pages.Path, "/pageOrder", "Page order refers to a missing page: " + id));
        return issues.AsReadOnly();
    }
}
