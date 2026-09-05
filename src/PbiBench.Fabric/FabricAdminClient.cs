using System.Net.Http;
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
        using var res = await FabricHttp.SendAsync(http, tokens, HttpMethod.Get, new Uri(FabricApiClient.BaseUri, path), FabricAudience.Fabric, null, ct).ConfigureAwait(false);
        return await FabricHttp.ReadJsonAsync(res.Content, ct).ConfigureAwait(false);
    }
    private static string Q(string? token) => string.IsNullOrWhiteSpace(token) ? "" : $"?continuationToken={Uri.EscapeDataString(token)}";
}
