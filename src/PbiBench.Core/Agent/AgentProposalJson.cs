using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PbiBench.Core.Automation;
using PbiBench.Core.Quality;

namespace PbiBench.Core.Agent;

/// <summary>Strict public proposal envelope. It cannot express approval, shell commands, remote writes, or trusted scripts.</summary>
public static class AgentProposalJson
{
    public const int MaximumBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Json = Options();
    private static JsonSerializerOptions Options()
    {
        var value = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false,
            WriteIndented = true, MaxDepth = 24, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        value.Converters.Add(new JsonStringEnumConverter(null, false)); return value;
    }
    public static string Serialize(AgentProposal proposal)
    { Validate(proposal); var text = JsonSerializer.Serialize(proposal, Json); if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("Agent proposals are limited to 256 KiB."); return text; }
    public static AgentProposal Parse(string text)
    {
        if (text == null || Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("Agent proposals are limited to 256 KiB.");
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 24 }); var root = doc.RootElement;
            Shape(root, "version", "kind", "title", "explanation", "recipe", "query", "test");
            if (root.GetProperty("recipe").ValueKind != JsonValueKind.Null) RecipeShape(root.GetProperty("recipe"));
            if (root.GetProperty("test").ValueKind != JsonValueKind.Null)
            { var test = root.GetProperty("test"); Shape(test, "name", "query", "comparison", "expected"); Shape(test.GetProperty("expected"), "kind", "value"); }
            var proposal = JsonSerializer.Deserialize<AgentProposal>(text, Json) ?? throw new InvalidDataException("The proposal is empty.");
            Validate(proposal); return proposal;
        }
        catch (JsonException) { throw new InvalidDataException("The proposal must match the published JSON schema exactly. Unknown fields, numeric enums and malformed JSON are rejected."); }
    }
    public static void Validate(AgentProposal proposal)
    {
        if (proposal == null || proposal.Version != 1 || !Enum.IsDefined(typeof(AgentProposalKind), proposal.Kind)) throw new InvalidDataException("Unknown agent proposal version or kind.");
        Text(proposal.Title, 128, "title"); Text(proposal.Explanation, 32000, "explanation", true);
        if ((proposal.Kind == AgentProposalKind.Action) != (proposal.Recipe != null) ||
            (proposal.Kind == AgentProposalKind.Query) != (proposal.Query != null) ||
            (proposal.Kind == AgentProposalKind.Test) != (proposal.Test != null)) throw new InvalidDataException("Proposal kind and payload do not match. Exactly its declared payload is allowed.");
        if (proposal.Recipe is { } recipe)
        {
            ActionRecipeRules.Validate(recipe);
            if (recipe.Steps.Count == 0 || recipe.Steps.Count > 100) throw new InvalidDataException("Agent recipes require 1 to 100 explicit steps.");
            foreach (var step in recipe.Steps)
            {
                if (step.Target.Scope is not (RecipeScope.Table or RecipeScope.Column or RecipeScope.Measure)) throw new InvalidDataException("Agent recipe targets must name each table, column or measure explicitly. Implicit selections and all-model scopes are not allowed.");
                if (step.Target.Scope == RecipeScope.Table && step.Target.Table != null) throw new InvalidDataException("Table targets must not contain a hidden containing-table field.");
                foreach (var value in new[] { step.Value, step.Expression, step.DisplayFolder }.Where(value => value != null))
                    if (value!.Parts.Count != 1 || value.Parts[0].Kind != RecipeValueKind.Literal || value.Parts[0].Text.Length > 32000)
                        throw new InvalidDataException("Agent recipe values must each be one literal of at most 32,000 characters.");
            }
        }
        if (proposal.Query != null) Text(proposal.Query, 64000, "query");
        if (proposal.Test is { } test)
        {
            Text(test.Name, 128, "test name"); Text(test.Query, 64000, "test query");
            if (!Enum.IsDefined(typeof(SemanticComparison), test.Comparison) || test.Expected == null) throw new InvalidDataException("Unknown test comparison or missing expected value.");
            test.Expected.Validate(); if (test.Expected.Value?.Length > 8000) throw new InvalidDataException("Expected test text is limited to 8,000 characters.");
        }
    }
    private static void Text(string value, int maximum, string field, bool allowEmpty = false)
    { if (value == null || value.Length > maximum || (!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Contains('\0')) throw new InvalidDataException("Invalid agent " + field + "."); }
    private static void Shape(JsonElement element, params string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("An agent proposal object was expected.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) if (!names.Add(property.Name) || !required.Contains(property.Name)) throw new InvalidDataException("Duplicate or unsupported proposal field: " + property.Name + ".");
        if (names.Count != required.Length) throw new InvalidDataException("The proposal omitted a required field; nullable fields must be explicit nulls.");
    }
    private static void RecipeShape(JsonElement recipe)
    {
        Shape(recipe, "name", "steps", "version"); var steps = recipe.GetProperty("steps");
        if (steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() > 100) throw new InvalidDataException("Agent recipe steps must be a bounded array.");
        foreach (var step in steps.EnumerateArray())
        {
            Shape(step, "target", "operation", "property", "value", "expression", "displayFolder"); Shape(step.GetProperty("target"), "scope", "table", "name");
            foreach (var key in new[] { "value", "expression", "displayFolder" })
            {
                var value = step.GetProperty(key); if (key != "value" && value.ValueKind == JsonValueKind.Null) continue;
                Shape(value, "parts"); var parts = value.GetProperty("parts");
                if (parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() != 1) throw new InvalidDataException("A recipe literal must have exactly one value part.");
                foreach (var part in parts.EnumerateArray()) Shape(part, "kind", "text");
            }
        }
    }

    /// <summary>Responses API strict JSON Schema. The same ActionRecipe contract is consumed by the shared command service.</summary>
    public static string SchemaJson { get; } = BuildSchema();
    private static string BuildSchema()
    {
        object Str(params string[] values) => values.Length == 0 ? new { type = "string" } : (object)new { type = "string", @enum = values };
        object Obj(params (string Key, object Value)[] fields) => new { type = "object", properties = fields.ToDictionary(field => field.Key, field => field.Value), required = fields.Select(field => field.Key).ToArray(), additionalProperties = false };
        object Nullable(object value) => new { anyOf = new[] { value, new { type = "null" } } };
        object Arr(object item) => new { type = "array", items = item };
        var value = Obj(("parts", Arr(Obj(("kind", Str("Literal")), ("text", Str())))));
        var recipe = Obj(("name", Str()), ("steps", Arr(Obj(
            ("target", Obj(("scope", Str("Table", "Column", "Measure")), ("table", Nullable(Str())), ("name", Str()))),
            ("operation", Str("SetProperty", "CreateMeasure", "DeleteMeasure")), ("property", Str()), ("value", value), ("expression", Nullable(value)), ("displayFolder", Nullable(value))))), ("version", new { type = "integer", @enum = new[] { 1 } }));
        var test = Obj(("name", Str()), ("query", Str()), ("comparison", Str(Enum.GetNames(typeof(SemanticComparison)))),
            ("expected", Obj(("kind", Str(Enum.GetNames(typeof(SemanticValueKind)))), ("value", Nullable(Str())))));
        return JsonSerializer.Serialize(Obj(("version", new { type = "integer", @enum = new[] { 1 } }),
            ("kind", Str(Enum.GetNames(typeof(AgentProposalKind)))), ("title", Str()), ("explanation", Str()), ("recipe", Nullable(recipe)), ("query", Nullable(Str())), ("test", Nullable(test))), Json);
    }
}
