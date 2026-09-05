using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PbiBench.Core.Automation;
using PbiBench.Core.Workspaces;

namespace PbiBench.Core.Commands;

public static class CommandJson
{
    public static JsonSerializerOptions Options { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 48, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, value.GetType(), Options);
    public static CommandRequest ParseRequest(string json)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024) throw new InvalidDataException("Command requests are limited to 8 MB.");
        CommandRequest request;
        try { RejectDuplicateFields(json); request = JsonSerializer.Deserialize<CommandRequest>(json, Options) ?? throw new InvalidDataException("A typed command request is required."); }
        catch (JsonException) { throw new InvalidDataException("Invalid command JSON, unknown fields, or unsupported enum value."); }
        Validate(request); return request;
    }
    public static void Validate(CommandRequest request)
    {
        if (request == null || request.Version != 1 || !Enum.IsDefined(typeof(CommandKind), request.Kind) || request.Target == null || request.Selection == null || request.Selection.Count > 10000 || request.Selection.Any(item => item == null || string.IsNullOrWhiteSpace(item.Kind) || string.IsNullOrWhiteSpace(item.Name))) throw new ArgumentException("Invalid command version, kind, target or selection.");
        if (request.Target.Server != null && (string.IsNullOrWhiteSpace(request.Target.Server) || request.Target.Server.Contains(';') || request.Target.Server.Any(char.IsControl))) throw new ArgumentException("The endpoint must not contain connection-string options.");
        if (request.Target.Database != null && (string.IsNullOrWhiteSpace(request.Target.Database) || request.Target.Database.Any(char.IsControl))) throw new ArgumentException("A valid database identity is required.");
        if (request.Target.ModelPath != null && (request.Target.Server != null || request.Target.Database != null) && request.Kind != CommandKind.Deploy) throw new ArgumentException("Choose a local model or a connected target; only Deploy accepts both.");
        if (request.Kind == CommandKind.Refresh && request.Target.ModelPath != null) throw new ArgumentException("Refresh requires a connected target without a local model path.");
        if ((request.Target.Server == null) != (request.Target.Database == null)) throw new ArgumentException("A connected target requires both server and database.");
        if (request.RowLimit < 1 || request.RowLimit > 1000000 || request.TimeoutSeconds < 1 || request.TimeoutSeconds > 3600) throw new ArgumentException("Invalid row limit or timeout.");
        if (request.FailOn != "Error" && request.FailOn != "Warning" && request.FailOn != "Information" && request.FailOn != "None") throw new ArgumentException("FailOn must be Error, Warning, Information or None.");
        if (request.ScriptLanguage != "SafeCSharp" && request.ScriptLanguage != "Dax") throw new ArgumentException("Only SafeCSharp and Dax scripts are accepted. Trusted execution is not an agent/CLI command.");
        if (request.Script?.Length > 1024 * 1024 || request.Query?.Length > 1024 * 1024 || request.Value?.Length > 1024 * 1024) throw new ArgumentException("Command text is limited to one million characters.");
        if (request.Property != null && request.Kind != CommandKind.Set && request.Kind != CommandKind.Get || request.Value != null && request.Kind != CommandKind.Set || request.Script != null && request.Kind != CommandKind.Script || request.Recipe != null && request.Kind != CommandKind.Action || request.Action != null && request.Kind != CommandKind.Action || request.ActionOptions != null && request.Kind != CommandKind.Action || request.Query != null && request.Kind != CommandKind.Query || request.Tests != null && request.Kind != CommandKind.Test || request.Refresh != null && request.Kind != CommandKind.Refresh || request.ComparePath != null && request.Kind != CommandKind.Diff || request.OutputPath != null && request.Kind != CommandKind.Set && request.Kind != CommandKind.Script && request.Kind != CommandKind.Action || request.BpaProfilePath != null && request.Kind != CommandKind.Bpa && request.Kind != CommandKind.Validate || request.ResolveConflictsUsingSource && request.Kind != CommandKind.Deploy) throw new ArgumentException("The request contains fields that do not belong to its command.");
        if (request.Recipe != null) ActionRecipeRules.Validate(request.Recipe);
        if (request.Recipe != null && request.Action != null) throw new ArgumentException("Choose a recipe or a gallery action, not both.");
    }
    public static string Hash(object value)
    {
        var element = Element(value); return WorkspaceSemanticSnapshot.HashText(Canonical(element));
    }
    public static void RejectDuplicateFields(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 }); Walk(document.RootElement);
        void Walk(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal); foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property: " + property.Name); Walk(property.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) Walk(child);
        }
    }
    private static string Canonical(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? "{" + string.Join(",", value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).Select(property => JsonSerializer.Serialize(property.Name) + ":" + Canonical(property.Value))) + "}"
        : value.ValueKind == JsonValueKind.Array ? "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]" : value.GetRawText();
    public static CommandReview Review(CommandRequest request, string target, string beforeHash, bool remote, bool canApply,
        IEnumerable<CommandChange> changes, IEnumerable<CommandDiagnostic>? issues = null, string? commandText = null)
    {
        var frozenChanges = Array.AsReadOnly(changes.ToArray()); var frozenIssues = Array.AsReadOnly((issues ?? Array.Empty<CommandDiagnostic>()).ToArray());
        var hash = Hash(new { policy = "pbibench-commands-v1", request, target, beforeHash, remote, canApply, changes = frozenChanges, issues = frozenIssues, commandText });
        return new(1, hash, request.Kind, target, beforeHash, remote, canApply, frozenChanges, frozenIssues, commandText);
    }
}
