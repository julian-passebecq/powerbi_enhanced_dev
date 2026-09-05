using System.Text.Json;
using PbiBench.Core.Agent;
using PbiBench.Core.Automation;

namespace PbiBench.Core.Commands;

/// <summary>Closed, bounded JSON Schema for model proposals. The host supplies routing and performs review.</summary>
public static class CommandSchema
{
    private static readonly CommandKind[] ModelKinds = { CommandKind.Inspect, CommandKind.List, CommandKind.Get, CommandKind.Bpa, CommandKind.Query, CommandKind.Test, CommandKind.Validate, CommandKind.Diff, CommandKind.Action };
    public static string Export(bool modelFacing = false)
    {
        var commands = modelFacing ? ModelKinds : Enum.GetValues(typeof(CommandKind)).Cast<CommandKind>().ToArray();
        return CommandJson.Serialize(new
        {
            version = 1, name = "PbiBench semantic command contract",
            authentication = "Supplied by the host; never an input field or output value.",
            operations = commands.Select(kind => new { kind, mode = kind is CommandKind.Set or CommandKind.Script or CommandKind.Action or CommandKind.Refresh or CommandKind.Deploy ? "preview-only until explicit host approval" : "read",
                nativeThread = "Capture and apply on the single model owner's STA; detached computation and private I/O may run in background.",
                requiresModel = kind is not (CommandKind.Query or CommandKind.Test or CommandKind.Diff or CommandKind.Refresh or CommandKind.Deploy),
                remoteWrite = kind is CommandKind.Refresh or CommandKind.Deploy,
                inputSchema = ModelKinds.Contains(kind) ? Input(kind) : null,
                hostBinding = "Schemas describe model proposal inputs. The host supplies target, file paths and transport credentials separately; CLI requests use the documented CommandRequest contract." }).ToArray(),
            proposalExample = new { version = 1, kind = "Action", recipe = new ActionRecipe("Describe Revenue", new[] { new RecipeStep(new(RecipeScope.Measure, "Sales", "Revenue"), RecipeOperation.SetProperty, "Description", RecipeValue.Literal("Reviewed revenue measure")) }) },
            apply = modelFacing ? "Not exposed to model proposals. A user reviews the exact changes; only the trusted host submits the matching hash." : "--apply --approve HASH; remote operations also require the unexpired, single-use --review FILE envelope.",
            typedRequest = "CommandRequest version 1; unknown fields, irrelevant fields and arbitrary script/TMSL execution are rejected."
        });
    }

    /// <summary>Defense in depth for direct model-generated proposals, before the host binds routing.</summary>
    public static CommandRequest ParseModelRequest(string json)
    {
        var request = CommandJson.ParseRequest(json);
        if (!ModelKinds.Contains(request.Kind)) throw new InvalidDataException("This command is not exposed to model proposals.");
        using var document = JsonDocument.Parse(json);
        var schema = CommandJson.Element(Input(request.Kind));
        if (!Matches(document.RootElement, schema)) throw new InvalidDataException("The proposal must match its published bounded input schema; routing and approval fields belong to the host.");
        if (request.Kind == CommandKind.Action) AgentProposalJson.Validate(new AgentProposal(1, AgentProposalKind.Action, "Command proposal", "", request.Recipe, null, null));
        if (request.Kind == CommandKind.Test)
        {
            var artifact = request.Tests ?? throw new InvalidDataException("A scalar test artifact is required.");
            if (artifact.FormatVersion != 1 || artifact.Tests == null || artifact.Tests.Count < 1 || artifact.Tests.Count > 100) throw new InvalidDataException("Model test proposals require 1 to 100 scalar assertions.");
            foreach (var test in artifact.Tests)
                if (test == null || test.Kind != Quality.SemanticTestKind.Scalar || test.Query.Length > 64000 || test.Name.Length > 128 || test.Id.Length > 128 || test.Snapshot != null || test.ComparisonQuery != null) throw new InvalidDataException("Model test proposals support bounded scalar assertions only.");
            Quality.SemanticTestArtifactStore.Deserialize(Quality.SemanticTestArtifactStore.Serialize(artifact));
        }
        return request;
    }
    private static object Input(CommandKind kind)
    {
        var fields = new Dictionary<string, object> { ["version"] = new { type = "integer", @const = 1 }, ["kind"] = new { type = "string", @const = kind.ToString() } };
        var required = new List<string> { "version", "kind" };
        if (kind == CommandKind.List) fields["objectKind"] = Str(64);
        if (kind == CommandKind.Get) { fields["selection"] = Arr(Obj(new[] { "kind", "name" }, ("kind", Str(64)), ("name", Str(512)), ("table", Str(512))), 1, 100); required.Add("selection"); fields["property"] = Str(64, "Name", "Description", "Expression", "IsHidden", "DisplayFolder", "FormatString", "DataType", "SummarizeBy"); }
        if (kind is CommandKind.Bpa or CommandKind.Validate) fields["failOn"] = Str(16, "Error", "Warning", "Information", "None");
        if (kind is CommandKind.Query or CommandKind.Test) { fields["rowLimit"] = Integer(1, 1000000); fields["timeoutSeconds"] = Integer(1, 3600); }
        if (kind == CommandKind.Query) { fields["query"] = Str(64000); required.Add("query"); }
        if (kind == CommandKind.Action) { fields["recipe"] = Recipe(); required.Add("recipe"); }
        if (kind == CommandKind.Test)
        {
            var scalar = Obj(new[] { "id", "name", "query", "kind", "expected" }, ("id", Str(128)), ("name", Str(128)), ("query", Str(64000)), ("kind", Str(16, "Scalar")),
                ("comparison", Str(32, Enum.GetNames(typeof(Quality.SemanticComparison)))), ("expected", Obj(new[] { "kind", "value" }, ("kind", Str(16, Enum.GetNames(typeof(Quality.SemanticValueKind)))), ("value", Nullable(Str(8000, minimum: 0))))),
                ("columnIndex", Integer(0, 10000)), ("absoluteTolerance", new { type = "number", minimum = 0 }), ("relativeTolerance", new { type = "number", minimum = 0 }), ("rowLimit", Integer(1, 1000000)), ("timeoutSeconds", Integer(1, 3600)));
            fields["tests"] = Obj(new[] { "formatVersion", "tests" }, ("formatVersion", new { type = "integer", @const = 1 }), ("tests", Arr(scalar, 1, 100))); required.Add("tests");
        }
        return new Dictionary<string, object> { ["$schema"] = "https://json-schema.org/draft/2020-12/schema", ["type"] = "object", ["properties"] = fields, ["required"] = required, ["additionalProperties"] = false };
    }
    private static object Recipe()
    {
        var literal = Obj(new[] { "parts" }, ("parts", Arr(Obj(new[] { "kind", "text" }, ("kind", Str(16, "Literal")), ("text", Str(32000, minimum: 0))), 1, 1)));
        object Step(string operation, object property, object expression, object folder) => Obj(new[] { "target", "operation", "property", "value", "expression", "displayFolder" },
            ("target", new { oneOf = new[] { Obj(new[] { "scope", "name", "table" }, ("scope", Str(16, "Table")), ("name", Str(512)), ("table", new { type = "null" })), Obj(new[] { "scope", "name", "table" }, ("scope", Str(16, "Column", "Measure")), ("name", Str(512)), ("table", Str(512))) } }),
            ("operation", Str(32, operation)), ("property", property), ("value", literal), ("expression", expression), ("displayFolder", folder));
        var nullValue = new { type = "null" };
        return Obj(new[] { "version", "name", "steps" }, ("version", new { type = "integer", @const = 1 }), ("name", Str(128)),
            ("steps", Arr(new { oneOf = new[] { Step("SetProperty", Str(64, ActionRecipeRules.Properties.ToArray()), nullValue, nullValue), Step("CreateMeasure", new { @const = "" }, literal, Nullable(literal)), Step("DeleteMeasure", new { @const = "" }, nullValue, nullValue) } }, 1, 100)));
    }
    private static object Str(int maximum, params string[] values) => Str(maximum, 1, values);
    private static object Str(int maximum, int minimum, params string[] values) { var result = new Dictionary<string, object> { ["type"] = "string", ["minLength"] = minimum, ["maxLength"] = maximum }; if (values.Length > 0) result["enum"] = values; return result; }
    private static object Integer(int minimum, int maximum) => new { type = "integer", minimum, maximum };
    private static object Nullable(object value) => new { anyOf = new[] { value, new { type = "null" } } };
    private static object Arr(object value, int minimum, int maximum) => new { type = "array", items = value, minItems = minimum, maxItems = maximum };
    private static object Obj(string[] required, params (string Key, object Value)[] fields) => new { type = "object", properties = fields.ToDictionary(item => item.Key, item => item.Value), required, additionalProperties = false };

    // This validator implements only the keywords emitted above; it never loads remote schemas.
    private static bool Matches(JsonElement value, JsonElement schema)
    {
        if (schema.TryGetProperty("oneOf", out var one) && one.EnumerateArray().Count(item => Matches(value, item)) != 1) return false;
        if (schema.TryGetProperty("anyOf", out var any) && !any.EnumerateArray().Any(item => Matches(value, item))) return false;
        if (schema.TryGetProperty("const", out var constant) && (constant.ValueKind != value.ValueKind || constant.ToString() != value.ToString())) return false;
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(choice => choice.ValueKind == value.ValueKind && choice.ToString() == value.ToString())) return false;
        if (!schema.TryGetProperty("type", out var type)) return true;
        switch (type.GetString())
        {
            case "null": return value.ValueKind == JsonValueKind.Null;
            case "string": return value.ValueKind == JsonValueKind.String && (!schema.TryGetProperty("minLength", out var minLength) || value.GetString()!.Length >= minLength.GetInt32()) && (!schema.TryGetProperty("maxLength", out var maxLength) || value.GetString()!.Length <= maxLength.GetInt32());
            case "integer": case "number":
                return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && !double.IsInfinity(number) && (type.GetString() != "integer" || Math.Truncate(number) == number) && (!schema.TryGetProperty("minimum", out var minimum) || number >= minimum.GetDouble()) && (!schema.TryGetProperty("maximum", out var maximum) || number <= maximum.GetDouble());
            case "array": return value.ValueKind == JsonValueKind.Array && value.GetArrayLength() >= schema.GetProperty("minItems").GetInt32() && value.GetArrayLength() <= schema.GetProperty("maxItems").GetInt32() && value.EnumerateArray().All(item => Matches(item, schema.GetProperty("items")));
            case "object":
                if (value.ValueKind != JsonValueKind.Object) return false;
                var properties = schema.GetProperty("properties");
                return schema.GetProperty("required").EnumerateArray().All(item => value.TryGetProperty(item.GetString()!, out _)) && value.EnumerateObject().All(property => properties.TryGetProperty(property.Name, out var child) && Matches(property.Value, child));
            default: return false;
        }
    }
}
