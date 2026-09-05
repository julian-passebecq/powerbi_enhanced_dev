using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PbiBench.Core.Abstractions;

namespace PbiBench.Fabric;

public static class FabricHttp
{
    public static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) };
    public static Uri ValidateUri(Uri uri, FabricAudience audience)
    {
        var host = audience == FabricAudience.Fabric ? "api.fabric.microsoft.com" : audience == FabricAudience.OneLake ? "onelake.table.fabric.microsoft.com" : null;
        if (host == null || !uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Host != host || !string.IsNullOrEmpty(uri.Fragment)) throw new ArgumentException("The request URL is outside the trusted Fabric resource origin.");
        var prefix = audience == FabricAudience.Fabric ? "/v1/" : "/delta/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("The request URL is outside the supported Fabric API path.");
        return uri;
    }
    public static async Task<JsonDocument> ReadJsonAsync(HttpContent content, CancellationToken ct, int maximumBytes = 8388608)
    {
        if (content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("Fabric response exceeds the bounded JSON size.");
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false); using var output = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false); if (count == 0) break;
            if (output.Length + count > maximumBytes) throw new InvalidDataException("Fabric response exceeds the bounded JSON size.");
            output.Write(buffer, 0, count);
        }
        ct.ThrowIfCancellationRequested();
        return output.Length == 0 ? JsonDocument.Parse("{}") : JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
    }
    public static TimeSpan RetryDelay(RetryConditionHeaderValue? retry, TimeSpan fallback)
    {
        var delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : fallback);
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : delay;
    }
    internal static async Task<HttpResponseMessage> SendAsync(HttpClient http, IAccessTokenProvider tokens, HttpMethod method,
        Uri uri, FabricAudience audience, object? body, CancellationToken ct)
    {
        ValidateUri(uri, audience); ct.ThrowIfCancellationRequested();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(EntraPublicClientTokenProvider.Scopes(audience), ct).ConfigureAwait(false));
            if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
            catch (HttpRequestException) { throw new FabricApiException("Fabric transport could not reach the selected service.", 0); }
            try { if (response.RequestMessage?.RequestUri is { } actual) ValidateUri(actual, audience); }
            catch { response.Dispose(); throw; }
            if ((int)response.StatusCode == 429 && attempt < 4)
            {
                var delay = RetryDelay(response.Headers.RetryAfter, TimeSpan.FromSeconds(Math.Pow(2, attempt))); response.Dispose();
                await Task.Delay(delay, ct).ConfigureAwait(false); continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode; response.Dispose();
                throw new FabricApiException($"Fabric request failed with HTTP {code}. Check resource permissions and availability.", code);
            }
            return response;
        }
        throw new FabricApiException("Fabric retry budget exhausted.", 429);
    }
}
