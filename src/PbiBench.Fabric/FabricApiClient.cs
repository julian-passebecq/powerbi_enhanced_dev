using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PbiBench.Core.Abstractions;
using PbiBench.Workspace;

namespace PbiBench.Fabric;

public sealed class FabricApiClient(HttpClient http, IAccessTokenProvider tokens)
{
    public static readonly Uri BaseUri = new("https://api.fabric.microsoft.com/v1/");
    private static readonly string[] DefaultScopes = ["https://api.fabric.microsoft.com/.default"];

    public async Task<JsonDocument> ListWorkspacesAsync(CancellationToken ct = default)
        => await SendJsonAsync(HttpMethod.Get, "workspaces", null, DefaultScopes, ct);

    public async Task<JsonDocument> ListItemsAsync(string workspaceId, CancellationToken ct = default)
        => await SendJsonAsync(HttpMethod.Get, $"workspaces/{Uri.EscapeDataString(workspaceId)}/items", null, DefaultScopes, ct);

    public async Task<JsonDocument> GetItemAsync(string workspaceId, string itemId, CancellationToken ct = default)
        => await SendJsonAsync(HttpMethod.Get, $"workspaces/{E(workspaceId)}/items/{E(itemId)}", null, DefaultScopes, ct);

    public async Task<CloudDefinition> GetItemDefinitionAsync(string workspaceId, string itemId, string? format = null, CancellationToken ct = default)
    {
        var path = $"workspaces/{E(workspaceId)}/items/{E(itemId)}/getDefinition" + (string.IsNullOrWhiteSpace(format) ? "" : $"?format={E(format)}");
        using var doc = await SendJsonAsync(HttpMethod.Post, path, new { }, DefaultScopes, ct);
        return ParseDefinition(doc.RootElement);
    }

    public async Task<CloudDefinition> GetSemanticModelDefinitionAsync(string workspaceId, string semanticModelId, string format = "TMDL", CancellationToken ct = default)
    {
        var path = $"workspaces/{E(workspaceId)}/semanticModels/{E(semanticModelId)}/getDefinition?format={E(format)}";
        using var doc = await SendJsonAsync(HttpMethod.Post, path, new { }, DefaultScopes, ct);
        return ParseDefinition(doc.RootElement);
    }

    public async Task UpdateSemanticModelDefinitionAsync(string workspaceId, string semanticModelId, CloudDefinition definition, bool updateMetadata, CancellationToken ct = default)
    {
        var path = $"workspaces/{E(workspaceId)}/semanticModels/{E(semanticModelId)}/updateDefinition?updateMetadata={updateMetadata.ToString().ToLowerInvariant()}";
        await SendNoContentAsync(HttpMethod.Post, path, new { definition = new { format = definition.Format, parts = definition.Parts } }, DefaultScopes, ct);
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string relative, object? body, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        using var response = await SendAsync(method, relative, body, scopes, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return JsonDocument.Parse("{}");
        return JsonDocument.Parse(text);
    }

    private async Task SendNoContentAsync(HttpMethod method, string relative, object? body, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        using var response = await SendAsync(method, relative, body, scopes, ct);
        _ = response.StatusCode;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relative, object? body, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        http.BaseAddress ??= BaseUri;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var request = new HttpRequestMessage(method, relative);
            var token = await tokens.GetAccessTokenAsync(scopes, ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode == 429 && attempt < 4)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                response.Dispose();
                await Task.Delay(delay, ct);
                continue;
            }
            if ((int)response.StatusCode == 202)
            {
                response = await new LongRunningOperationPoller(http, c => tokens.GetAccessTokenAsync(scopes, c)).WaitAsync(response, ct);
            }
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                var code = (int)response.StatusCode;
                response.Dispose();
                throw new FabricApiException($"Fabric API request failed with HTTP {code}.", code, content);
            }
            return response;
        }
        throw new FabricApiException("Fabric API retry budget exhausted.", 429);
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
