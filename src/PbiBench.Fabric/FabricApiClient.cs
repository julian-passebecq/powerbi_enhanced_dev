using System.Net.Http;
using System.Collections.Concurrent;
using System.Text.Json;
using PbiBench.Core.Abstractions;
using PbiBench.Core.Domain;
using PbiBench.Core.Fabric;
using PbiBench.Core.Quality;
using PbiBench.Workspace;

namespace PbiBench.Fabric;

public sealed class FabricApiClient(HttpClient http, IAccessTokenProvider tokens)
{
    public static readonly Uri BaseUri = new("https://api.fabric.microsoft.com/v1/");
    private static readonly string[] DefaultScopes = ["https://api.fabric.microsoft.com/.default"];
    private readonly ConcurrentDictionary<Guid, byte> submittedPlans = new();

    public async Task<JsonDocument> ListWorkspacesAsync(CancellationToken ct = default)
        => await SendJsonAsync(HttpMethod.Get, "workspaces", null, DefaultScopes, ct);

    public async Task<JsonDocument> ListItemsAsync(string workspaceId, CancellationToken ct = default)
        => await SendJsonAsync(HttpMethod.Get, $"workspaces/{Uri.EscapeDataString(workspaceId)}/items", null, DefaultScopes, ct);

    public async Task<JsonDocument> GetItemAsync(string workspaceId, string itemId, CancellationToken ct = default)
        => await SendJsonAsync(HttpMethod.Get, $"workspaces/{E(workspaceId)}/items/{E(itemId)}", null, DefaultScopes, ct);

    public async Task<CloudDefinition> GetItemDefinitionAsync(string workspaceId, string itemId, string? format = null, CancellationToken ct = default)
    {
        var path = $"workspaces/{E(workspaceId)}/items/{E(itemId)}/getDefinition" + (string.IsNullOrWhiteSpace(format) ? "" : $"?format={E(format!)}");
        using var doc = await SendJsonAsync(HttpMethod.Post, path, new { }, DefaultScopes, ct);
        return ParseDefinition(doc.RootElement);
    }

    public async Task<CloudDefinition> GetSemanticModelDefinitionAsync(string workspaceId, string semanticModelId, string format = "TMDL", CancellationToken ct = default)
    {
        var path = $"workspaces/{E(workspaceId)}/semanticModels/{E(semanticModelId)}/getDefinition?format={E(format)}";
        using var doc = await SendJsonAsync(HttpMethod.Post, path, new { }, DefaultScopes, ct);
        return ParseDefinition(doc.RootElement);
    }

    public async Task UpdateSemanticModelDefinitionAsync(string workspaceId, string semanticModelId, CloudDefinition definition, bool updateMetadata, ApprovedChangePlan approval, CancellationToken ct = default)
    {
        FabricSchemaRules.Id(workspaceId); FabricSchemaRules.Id(semanticModelId); ct.ThrowIfCancellationRequested();
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(new { definition = new { format = definition.Format, parts = definition.Parts } }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var fingerprint = QueryBenchmarkService.Hash(snapshot.RootElement.GetRawText() + "|updateMetadata=" + updateMetadata);
        if (approval?.Plan == null || approval.Plan.Id == Guid.Empty || approval.Plan.RequiredApproval != ApprovalLevel.RemoteModelWrite ||
            approval.Plan.Target.WorkspaceId != workspaceId || approval.Plan.Target.ItemId != semanticModelId ||
            approval.ApprovedAt < approval.Plan.CreatedAt || approval.ApprovedAt > DateTimeOffset.UtcNow.AddMinutes(1) ||
            string.IsNullOrWhiteSpace(approval.ApprovalActor) || !approval.Plan.Changes.Any(change => change.Operation == "Update semantic model definition" && change.AfterSummary == fingerprint))
            throw new InvalidOperationException("Remote definition update requires approval of this exact target and DefinitionUpdateFingerprint payload.");
        if (!submittedPlans.TryAdd(approval.Plan.Id, 0)) throw new InvalidOperationException("This remote definition plan was already submitted. Inspect the remote state before preparing another plan.");
        var path = $"workspaces/{E(workspaceId)}/semanticModels/{E(semanticModelId)}/updateDefinition?updateMetadata={updateMetadata.ToString().ToLowerInvariant()}";
        await SendNoContentAsync(HttpMethod.Post, path, snapshot.RootElement, DefaultScopes, ct);
    }
    public static string DefinitionUpdateFingerprint(CloudDefinition definition, bool updateMetadata) => QueryBenchmarkService.Hash(
        JsonSerializer.Serialize(new { definition = new { format = definition.Format, parts = definition.Parts } }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "|updateMetadata=" + updateMetadata);

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string relative, object? body, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        using var response = await SendAsync(method, relative, body, scopes, ct);
        return await FabricHttp.ReadJsonAsync(response.Content, ct).ConfigureAwait(false);
    }

    private async Task SendNoContentAsync(HttpMethod method, string relative, object? body, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        using var response = await SendAsync(method, relative, body, scopes, ct, false);
        _ = response.StatusCode;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relative, object? body, IReadOnlyCollection<string> scopes, CancellationToken ct, bool requiresResult = true)
    {
        var response = await FabricHttp.SendAsync(http, tokens, method, new Uri(BaseUri, relative), FabricAudience.Fabric, body, ct).ConfigureAwait(false);
        return (int)response.StatusCode == 202
            ? await new LongRunningOperationPoller(http, c => tokens.GetAccessTokenAsync(scopes, c)).WaitAsync(response, ct, requiresResult).ConfigureAwait(false)
            : response;
    }

    private static CloudDefinition ParseDefinition(JsonElement root)
    {
        var def = root.TryGetProperty("definition", out var d) ? d : root;
        var format = def.TryGetProperty("format", out var f) ? f.GetString() : null;
        var parts = new List<DefinitionPart>();
        if (def.TryGetProperty("parts", out var p))
        {
            foreach (var part in p.EnumerateArray())
                parts.Add(new DefinitionPart(part.GetProperty("path").GetString()!, part.GetProperty("payload").GetString()!, part.GetProperty("payloadType").GetString()!));
        }
        return new CloudDefinition(format, parts);
    }

    private static string E(string value) => Uri.EscapeDataString(value);
}
