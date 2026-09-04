using System.Net.Http.Headers;
using System.Text.Json;
using PbiBench.Core.Abstractions;
namespace PbiBench.PowerBI;

public sealed class PowerBiRestClient(HttpClient http, IAccessTokenProvider tokens)
{
    public static readonly Uri BaseUri = new("https://api.powerbi.com/v1.0/myorg/");
    private static readonly string[] Scopes = ["https://analysis.windows.net/powerbi/api/.default"];

    public Task<JsonDocument> ListWorkspacesAsync(CancellationToken ct = default) => GetAsync("groups", ct);
    public Task<JsonDocument> ListReportsAsync(string workspaceId, CancellationToken ct = default) => GetAsync($"groups/{E(workspaceId)}/reports", ct);
    public Task<JsonDocument> ListDatasetsAsync(string workspaceId, CancellationToken ct = default) => GetAsync($"groups/{E(workspaceId)}/datasets", ct);
    public Task<JsonDocument> GetDataSourcesAsync(string workspaceId, string datasetId, CancellationToken ct = default) => GetAsync($"groups/{E(workspaceId)}/datasets/{E(datasetId)}/datasources", ct);

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        http.BaseAddress ??= BaseUri;
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(Scopes, ct));
        using var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Power BI REST failed: {(int)res.StatusCode} {body}");
        return JsonDocument.Parse(body);
    }
    private static string E(string value) => Uri.EscapeDataString(value);
}
