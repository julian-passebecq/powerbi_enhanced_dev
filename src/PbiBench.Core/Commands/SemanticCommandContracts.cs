using System.Text.Json;
using System.Text.Json.Serialization;
using PbiBench.Core.Automation;
using PbiBench.Core.Quality;
using PbiBench.Core.Refresh;

namespace PbiBench.Core.Commands;

public enum CommandKind { Inspect, List, Get, Set, Script, Action, Bpa, Query, Test, Refresh, Validate, Diff, Deploy }
public enum CommandStatus { Succeeded, Preview, Rejected, Failed, Canceled, OutcomeUnknown }
public sealed record CommandTarget(string? ModelPath = null, string? Server = null, string? Database = null);
public sealed record CommandObject(string Kind, string Name, string? Table = null);
/// <summary>Only typed inputs are accepted; authentication is supplied separately by the host.</summary>
public sealed record CommandRequest
{
    public int Version { get; init; } = 1;
    [JsonRequired] public CommandKind Kind { get; init; }
    public CommandTarget Target { get; init; } = new();
    public IReadOnlyList<CommandObject> Selection { get; init; } = Array.Empty<CommandObject>();
    public string? ObjectKind { get; init; }
    public string? Property { get; init; }
    public string? Value { get; init; }
    public string? Script { get; init; }
    public string ScriptLanguage { get; init; } = "SafeCSharp";
    public ActionRecipe? Recipe { get; init; }
    public string? Action { get; init; }
    public IReadOnlyDictionary<string, string>? ActionOptions { get; init; }
    public string? Query { get; init; }
    public int RowLimit { get; init; } = 10000;
    public int TimeoutSeconds { get; init; } = 60;
    public SemanticTestArtifact? Tests { get; init; }
    public RefreshRequest? Refresh { get; init; }
    public string? ComparePath { get; init; }
    public string? OutputPath { get; init; }
    public string? BpaProfilePath { get; init; }
    public string FailOn { get; init; } = "Error";
    public bool ResolveConflictsUsingSource { get; init; }
}
public sealed record CommandChange(string ObjectPath, string Property, string Before, string After, string Reason);
public sealed record CommandDiagnostic(string Code, string Message, string Severity = "Error", string? ObjectPath = null);
public sealed record CommandReview(int Version, string Hash, CommandKind Kind, string TargetIdentity, string BeforeHash,
    bool IsRemote, bool CanApply, IReadOnlyList<CommandChange> Changes, IReadOnlyList<CommandDiagnostic> Issues, string? CommandText = null);
public sealed record CommandResult(int Version, CommandKind Kind, CommandStatus Status, int ExitCode, string Message,
    JsonElement? Data = null, CommandReview? Review = null, IReadOnlyList<CommandDiagnostic>? Diagnostics = null)
{
    public static CommandResult Success(CommandKind kind, object? data, string message = "Completed.") => new(1, kind, CommandStatus.Succeeded, 0, message, data == null ? null : CommandJson.Element(data));
    public static CommandResult Preview(CommandReview review) => new(1, review.Kind, CommandStatus.Preview, review.CanApply ? 0 : 3, review.CanApply ? "Review the exact changes before applying." : "This preview cannot be applied.", Review: review, Diagnostics: review.Issues);
}
