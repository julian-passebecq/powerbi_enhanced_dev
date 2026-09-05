using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PbiBench.Core.Commands;
using PbiBench.Core.Domain;
using PbiBench.Core.Refresh;
using PbiBench.Core.Workspaces;
using PbiBench.Semantic;
using PbiBench.Semantic.Workspaces;
using PbiBench.Workspace;

namespace PbiBench.Automation.Commands;

/// <summary>CLI and host commands reuse the same private refresh/workspace transports as the workbench.</summary>
internal static class ConnectedCommandOperations
{
    internal static Task<PreparedCommand> PrepareAsync(CommandRequest request, Func<CommandTarget, string?> connectionString, CancellationToken ct)
        => PrepareAsync(request, connectionString, new TomRefreshService(), new TomWorkspaceSyncService(), ct);

    internal static async Task<PreparedCommand> PrepareAsync(CommandRequest request, Func<CommandTarget, string?> connectionString,
        TomRefreshService refresh, TomWorkspaceSyncService workspace, CancellationToken ct)
    {
        CommandJson.Validate(request); ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Target.Server) || string.IsNullOrWhiteSpace(request.Target.Database))
            throw new ArgumentException("Refresh and deploy require an explicit server and database.");
        var credentials = connectionString(request.Target);
        if (request.Kind == CommandKind.Refresh)
        {
            if (request.Refresh == null || request.Target.ModelPath != null) throw new ArgumentException("Refresh requires typed refresh options and a connected target without a local model path.");
            if (request.Refresh.SourceOverrides?.Any(source => source?.Expression != null && EmbeddedCredential.IsMatch(source.Expression)) == true)
                throw new ArgumentException("Inline authentication is not accepted in CLI refresh overrides or saved reviews. Configure source credentials on the engine and preview a credential-free source expression.");
            var captureConnection = new RefreshConnection(request.Target.Server!, request.Target.Database!) { ConnectionString = credentials };
            var metadata = await refresh.CaptureAsync(captureConnection, request.TimeoutSeconds, ct).ConfigureAwait(false);
            var connection = captureConnection with { DatabaseId = metadata.DatabaseId };
            var plan = RefreshPlanner.Build(metadata, request.Refresh);
            var identity = CommandJson.Serialize(new { server = connection.Server, databaseId = metadata.DatabaseId, databaseName = metadata.DatabaseName });
            var exactHash = CommandJson.Hash(new { metadata.Fingerprint, plan.Tmsl });
            var review = CommandJson.Review(request, identity, exactHash, true, plan.CanExecute,
                Rows(plan.ChangePlan), plan.Issues.Select(issue => new CommandDiagnostic(issue.Code, RedactText(issue.Message), issue.Severity.ToString())), RedactJson(plan.Tmsl));
            return new(request, review, async (actor, token) =>
            {
                var result = await refresh.ExecuteAsync(plan, new(plan.ChangePlan, DateTimeOffset.UtcNow, actor), connection, null, token).ConfigureAwait(false);
                var status = result.Outcome switch
                {
                    RefreshOutcome.Succeeded or RefreshOutcome.SucceededWithWarnings => CommandStatus.Succeeded,
                    RefreshOutcome.CanceledBeforeExecution => CommandStatus.Canceled,
                    RefreshOutcome.OutcomeUnknown => CommandStatus.OutcomeUnknown,
                    _ => CommandStatus.Failed
                };
                return new(1, request.Kind, status, ExitCode(status), result.Message, CommandJson.Element(result));
            });
        }
        if (request.Kind != CommandKind.Deploy) throw new ArgumentException("Only refresh and deploy are connected mutation commands.");
        if (string.IsNullOrWhiteSpace(request.Target.ModelPath)) throw new ArgumentException("Deploy requires a local BIM, TMDL or PBIP source and an existing explicit live target.");
        var sourcePath = Path.GetFullPath(request.Target.ModelPath!);
        var source = await Task.Run(() => CaptureSource(sourcePath, ct), ct).ConfigureAwait(false);
        var liveConnection = new WorkspaceConnection(request.Target.Server!, request.Target.Database!, credentials, request.TimeoutSeconds);
        var live = await workspace.CaptureAsync(liveConnection, ct).ConfigureAwait(false);
        // Fresh live is the deployment baseline. This is a full replacement with exact deletions;
        // three-way merge remains the workspace UI's separate operation.
        var comparison = WorkspaceSemanticDiff.Compare(live.Snapshot, source.Semantic, live.Snapshot);
        var target = CommandJson.Serialize(new { server = liveConnection.Server, databaseId = live.DatabaseId, databaseName = live.DatabaseName,
            source = sourcePath, backupDirectory = source.BackupDirectory });
        if (comparison.Changes.Count == 0)
        {
            var noChanges = CommandJson.Review(request, target, CommandJson.Hash(new { liveHash = live.Snapshot.Hash, sourceHash = source.Hash }), true, false,
                Array.Empty<CommandChange>(), new[] { new CommandDiagnostic("NO_CHANGES", "Source and live metadata already match.", "Information") });
            return new(request, noChanges, (_, _) => throw new InvalidOperationException("There are no deployment changes."));
        }
        var push = workspace.PreparePush(comparison, live, liveConnection, source.Hash, request.ResolveConflictsUsingSource);
        var binding = CommandJson.Hash(new { liveHash = live.Snapshot.Hash, sourceHash = source.Hash, source.Semantic.Hash, push.Tmsl });
        var deploymentReview = CommandJson.Review(request, target, binding, true, true, Rows(push.Plan),
            new[] { new CommandDiagnostic("FULL_REPLACEMENT", "Replaces existing target metadata, including deletions. Changed partitions may need refresh. The recovery BIM contains metadata, not processed data.", "Warning"),
                new CommandDiagnostic("REDACTED_REVIEW", "Credential fields in displayed metadata are redacted; the approval hash still binds their exact original values.", "Information") }, RedactJson(push.Tmsl));
        return new(request, deploymentReview, async (actor, token) =>
        {
            var submitted = false;
            try
            {
                var result = await workspace.ApplyPushAsync(push, new(push.Plan, DateTimeOffset.UtcNow, actor),
                    currentToken => CaptureSource(sourcePath, currentToken).Hash, source.BackupDirectory, token, () => submitted = true).ConfigureAwait(false);
                return CommandResult.Success(request.Kind, new { result.BackupPath, result.Live.DatabaseId, result.Live.DatabaseName, metadataHash = result.Live.Snapshot.Hash, commandSubmitted = true },
                    "The server acknowledged the metadata deployment. Reload and validate the target; processing may be required.");
            }
            catch (Exception) when (submitted)
            {
                return new(1, request.Kind, CommandStatus.OutcomeUnknown, ExitCode(CommandStatus.OutcomeUnknown),
                    "Deployment was submitted but its final state is not confirmed. Inspect the explicit target and recovery folder before retrying.", CommandJson.Element(new { source.BackupDirectory, commandSubmitted = true }));
            }
            catch (OperationCanceledException)
            {
                return new(1, request.Kind, CommandStatus.Canceled, ExitCode(CommandStatus.Canceled), "Deployment canceled before submission.");
            }
            catch (Exception)
            {
                return new(1, request.Kind, CommandStatus.Failed, ExitCode(CommandStatus.Failed), "Deployment was not submitted. The source or target changed, or the private connection/backup could not be prepared. Compare again.");
            }
        });
    }

    private static IEnumerable<CommandChange> Rows(ChangePlan plan) => plan.Changes.Select(change => new CommandChange(change.Target, change.Operation,
        RedactText(change.BeforeSummary), RedactText(change.AfterSummary), string.Join(" ", change.Validation.Select(RedactText))));
    private static int ExitCode(CommandStatus status) => status switch { CommandStatus.Succeeded => 0, CommandStatus.Canceled => 5, CommandStatus.OutcomeUnknown => 6, _ => 4 };

    private sealed record SourceCapture(WorkspaceSemanticSnapshot Semantic, string Hash, string BackupDirectory);
    private static SourceCapture CaptureSource(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); WorkspaceDiskStore.RejectLinks(path); var codec = new TmdlWorkspaceCodec();
        if (File.Exists(path) && Path.GetExtension(path).Equals(".bim", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("BIM inputs are limited to 16 MiB.");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true); var json = reader.ReadToEnd(); ct.ThrowIfCancellationRequested();
            return new(codec.Normalize(json), WorkspaceSemanticSnapshot.HashText(json), Path.Combine(Path.GetDirectoryName(path)!, ".pbibench", "deployment-backups"));
        }
        var directory = WorkspaceDiskStore.ResolveDefinitionDirectory(path); var disk = new WorkspaceDiskStore().Capture(directory, ct);
        return new(codec.Parse(disk, ct), disk.Hash, Path.Combine(directory, ".pbibench", "deployment-backups"));
    }

    private static string RedactJson(string json)
    {
        var node = JsonNode.Parse(json)!; Visit(node); return node.ToJsonString(CommandJson.Options);
        static void Visit(JsonNode value)
        {
            if (value is JsonObject obj)
                foreach (var item in obj.ToArray())
                {
                    if (SensitiveKey.IsMatch(item.Key)) obj[item.Key] = "[restricted metadata]";
                    else if (item.Value is JsonValue scalar && scalar.TryGetValue<string>(out var text)) obj[item.Key] = RedactText(text);
                    else if (item.Value is JsonArray lines && lines.All(line => line is JsonValue part && part.TryGetValue<string>(out _)) &&
                        EmbeddedCredential.IsMatch(string.Join("\n", lines.Select(line => line!.GetValue<string>())))) obj[item.Key] = "[redacted: credential-bearing metadata]";
                    else if (item.Value != null) Visit(item.Value);
                }
            else if (value is JsonArray array)
                for (var i = 0; i < array.Count; i++)
                    if (array[i] is JsonValue scalar && scalar.TryGetValue<string>(out var text)) array[i] = RedactText(text);
                    else if (array[i] != null) Visit(array[i]!);
        }
    }
    private static readonly Regex SensitiveKey = new(@"password|pwd|token|secret|credential|connectionString|accountkey|authorization|api.?key|access.?key|sharedaccesssignature", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EmbeddedCredential = new(@"(?:password|pwd|access_token|token|secret|accountkey|authorization|api[ _-]?key|access[ _-]?key|sharedaccesssignature|clientsecret|signature|sig)\b[\s""'\\]*(?:=|:)|\bBearer\s+\S+|\bBasic\s+[A-Za-z0-9+/=]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // Source expressions may contain JSON, M header records or escaped connection strings.
    // Suppress the entire credential-bearing value instead of guessing where a secret ends.
    // The exact original bytes still contribute to the independent approval binding.
    private static string RedactText(string text) => EmbeddedCredential.IsMatch(text) ? "[redacted: credential-bearing metadata]" : text;
}
