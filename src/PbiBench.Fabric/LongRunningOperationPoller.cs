using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
namespace PbiBench.Fabric;

public sealed class LongRunningOperationPoller(HttpClient http, Func<CancellationToken, Task<string>> tokenFactory,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
{
    public async Task<HttpResponseMessage> WaitAsync(HttpResponseMessage initial, CancellationToken ct, bool requiresResult = true)
    {
        if ((int)initial.StatusCode != 202) return initial;
        Uri location; TimeSpan delay;
        try
        {
            location = OperationUri(initial.Headers.Location ?? throw new FabricApiException("Fabric operation omitted its status URL.", 202));
            delay = FabricHttp.RetryDelay(initial.Headers.RetryAfter, TimeSpan.FromSeconds(2));
        }
        finally { initial.Dispose(); }
        var operation = location.AbsolutePath.Split('/')[3];
        Uri BoundUri(Uri value) { OperationUri(value); if (value.AbsolutePath.Split('/')[3] != operation) throw new InvalidDataException("Fabric operation changed identity while polling."); return value; }
        var delayTask = delayAsync ?? Task.Delay;
        for (var attempt = 0; attempt < 180; attempt++)
        {
            await delayTask(delay, ct).ConfigureAwait(false); ct.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, location);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenFactory(ct).ConfigureAwait(false));
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var returned = false;
            try
            {
                if (response.RequestMessage?.RequestUri is { } actual) BoundUri(actual);
                if ((int)response.StatusCode == 429) { delay = FabricHttp.RetryDelay(response.Headers.RetryAfter, delay); continue; }
                if (!response.IsSuccessStatusCode) throw new FabricApiException($"Fabric operation returned HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
                var next = response.Headers.Location == null ? null : BoundUri(response.Headers.Location);
                if ((int)response.StatusCode == 202) { if (next != null) location = next; delay = FabricHttp.RetryDelay(response.Headers.RetryAfter, delay); continue; }
                if (location.AbsolutePath.EndsWith("/result", StringComparison.Ordinal)) { returned = true; return response; }
                using var doc = await FabricHttp.ReadJsonAsync(response.Content, ct, 1048576).ConfigureAwait(false);
                var state = doc.RootElement.TryGetProperty("status", out var status) ? status.GetString() : null;
                if (state is "Failed" or "Cancelled" or "Canceled") throw new FabricApiException("Fabric operation " + state.ToLowerInvariant() + ". Inspect the operation in Fabric for details.", (int)response.StatusCode);
                if (state == "Succeeded")
                {
                    if (next == null && !requiresResult) return new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new StringContent("{}") };
                    next ??= new Uri(location.AbsoluteUri.TrimEnd('/') + "/result");
                    if (!next.AbsolutePath.EndsWith("/result", StringComparison.Ordinal)) throw new FabricApiException("Completed Fabric operation returned an invalid result URL.", 200);
                    location = next; delay = TimeSpan.Zero; continue;
                }
                if (state is not ("Running" or "NotStarted" or "Undefined")) throw new FabricApiException("Fabric operation returned an unknown state.", (int)response.StatusCode);
                if (next != null) location = next;
                delay = FabricHttp.RetryDelay(response.Headers.RetryAfter, delay);
            }
            finally { if (!returned) response.Dispose(); }
        }
        throw new TimeoutException("Fabric long-running operation did not complete within polling budget.");
    }

    public static Uri OperationUri(Uri uri)
    {
        FabricHttp.ValidateUri(uri, FabricAudience.Fabric);
        var segments = uri.AbsolutePath.Split('/');
        if ((segments.Length != 4 && segments.Length != 5) || segments[1] != "v1" || segments[2] != "operations" ||
            !Guid.TryParse(segments[3], out _) || (segments.Length == 5 && segments[4] != "result") || uri.Query.Length != 0)
            throw new ArgumentException("Fabric operation URLs must identify a public operation status or result.");
        return uri;
    }
}
