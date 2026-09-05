using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using PbiBench.ExternalTools;

namespace PbiBench.DesignExchange;

public sealed record ThemeValidation(bool IsValid, string SchemaVersion, string? Name, IReadOnlyList<string> DataColors,
    IReadOnlyList<string> VisualStyleFamilies, IReadOnlyList<DesignDiagnostic> Diagnostics);
public sealed class ThemeValidator
{
    public const string SchemaVersion = "Power BI Desktop 2.156 / theme 5.75";
    public const string SchemaUri = "https://raw.githubusercontent.com/microsoft/powerbi-desktop-samples/6ccd62e9d79c4b1b0662ba8955598492c35cc8c4/Report%20Theme%20JSON%20Schema/reportThemeSchema-2.156.json";
    private readonly JsonSchema schema;
    private readonly HashSet<string> knownFamilies;
    private readonly HashSet<string> knownProperties = new(StringComparer.Ordinal);
    public ThemeValidator()
    {
        using var stream = typeof(ThemeValidator).Assembly.GetManifestResourceStream("PbiBench.ReportTheme.2.156")!;
        using var reader = new StreamReader(stream); var text = reader.ReadToEnd();
        schema = JsonSchema.FromText(text);
        using var doc = JsonDocument.Parse(text);
        knownFamilies = new(doc.RootElement.GetProperty("properties").GetProperty("visualStyles").GetProperty("properties").EnumerateObject().Select(p => p.Name), StringComparer.Ordinal);
        void Vocabulary(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "properties" && property.Value.ValueKind == JsonValueKind.Object) foreach (var name in property.Value.EnumerateObject()) knownProperties.Add(name.Name);
                    Vocabulary(property.Value);
                }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Vocabulary(item);
        }
        Vocabulary(doc.RootElement);
    }
    public ThemeValidation Validate(string json)
    {
        var diagnostics = new List<DesignDiagnostic>(); var colors = new List<string>(); var families = new List<string>(); string? name = null;
        try
        {
            using var doc = ContractJson.Document(json, 1024 * 1024);
            var options = new EvaluationOptions { OutputFormat = OutputFormat.List };
            options.SchemaRegistry.Fetch = _ => throw new InvalidDataException("Only the pinned offline theme schema is supported.");
            var result = schema.Evaluate(JsonNode.Parse(doc.RootElement.GetRawText()), options);
            void Collect(EvaluationResults item)
            {
                if (diagnostics.Count >= 100) return;
                if (item.Errors != null) foreach (var error in item.Errors.Take(100 - diagnostics.Count)) diagnostics.Add(new("Error", item.InstanceLocation.ToString(), error.Key + ": " + error.Value));
                if (item.Details != null) foreach (var detail in item.Details.Where(d => !d.IsValid)) Collect(detail);
            }
            if (!result.IsValid) { Collect(result); if (diagnostics.Count == 0) diagnostics.Add(new("Error", "/", "Theme does not match the pinned schema.")); }
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("$schema", out var declared) && declared.ValueKind == JsonValueKind.String && declared.GetString() != SchemaUri)
                    diagnostics.Add(new("Warning", "/$schema", "The declared schema is not the pinned version. Validation used " + SchemaVersion + " offline; no URL was fetched."));
                if (root.TryGetProperty("name", out var title) && title.ValueKind == JsonValueKind.String) name = title.GetString();
                if (root.TryGetProperty("dataColors", out var data) && data.ValueKind == JsonValueKind.Array) colors.AddRange(data.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Take(64).Select(c => c.GetString()!));
                if (root.TryGetProperty("visualStyles", out var styles) && styles.ValueKind == JsonValueKind.Object)
                    foreach (var family in styles.EnumerateObject())
                    {
                        if (knownFamilies.Contains(family.Name)) families.Add(family.Name); else diagnostics.Add(new("Warning", "/visualStyles/" + family.Name, "Unsupported/new visual style family; no preview renderer."));
                        // Upstream permits some open formatting objects. Flag unfamiliar vocabulary without claiming the schema rejected it.
                        if (family.Value.ValueKind == JsonValueKind.Object) foreach (var selector in family.Value.EnumerateObject()) WarnUnknown(selector.Value, "/visualStyles/" + family.Name + "/" + selector.Name);
                    }
                void WarnUnknown(JsonElement element, string location)
                {
                    if (diagnostics.Count >= 100) return;
                    if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject())
                    {
                        if (diagnostics.Count >= 100) break;
                        if (!knownProperties.Contains(property.Name)) diagnostics.Add(new("Warning", location + "/" + property.Name, "Property is not in the pinned theme vocabulary; custom/new formatting is not previewed."));
                        WarnUnknown(property.Value, location + "/" + property.Name);
                    }
                    else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) WarnUnknown(item, location);
                }
            }
        }
        catch (Exception error) when (error is InvalidDataException || error is JsonException || error is JsonSchemaException || error is InvalidOperationException)
        { diagnostics.Add(new("Error", "/", "Theme validation failed: " + error.Message)); }
        return new(diagnostics.All(d => d.Severity != "Error"), SchemaVersion, name, colors.AsReadOnly(), families.AsReadOnly(), diagnostics.AsReadOnly());
    }
}
