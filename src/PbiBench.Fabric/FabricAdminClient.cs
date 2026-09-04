using System.Net.Http.Headers;
using System.Text.Json;
using PbiBench.Core.Abstractions;
namespace PbiBench.Fabric;

public sealed class FabricAdminClient(HttpClient http, IAccessTokenProvider tokens)
{
    private static readonly string[] Scopes = ["https://api.fabric.microsoft.com/.default"];

    public Task<JsonDocument> ListWorkspacesAsync(string? continuationToken = null, CancellationToken ct = default)
        => GetAsync("admin/workspaces" + Q(continuationToken), ct);

    public Task<JsonDocument> ListItemsAsync(string? continuationToken = null, CancellationToken ct = default)
        => GetAsync("admin/items" + Q(continuationToken), ct);

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        http.BaseAddress ??= FabricApiClient.BaseUri;
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(Scopes, ct));
        using var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new FabricApiException($"Fabric Admin API failed: {(int)res.StatusCode}", (int)res.StatusCode, body);
        return JsonDocument.Parse(body);
    }
    private static string Q(string? token) => string.IsNullOrWhiteSpace(token) ? "" : $"?continuationToken={Uri.EscapeDataString(token)}";
}
