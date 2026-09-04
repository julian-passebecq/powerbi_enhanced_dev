using System.Net.Http.Headers;
namespace PbiBench.Fabric;

public sealed class LongRunningOperationPoller(HttpClient http, Func<CancellationToken, Task<string>> tokenFactory)
{
    public async Task<HttpResponseMessage> WaitAsync(HttpResponseMessage initial, CancellationToken ct)
    {
        if ((int)initial.StatusCode != 202) return initial;
        var location = initial.Headers.Location ?? throw new FabricApiException("202 response did not include Location header.", 202);
        var delay = RetryDelay(initial.Headers.RetryAfter) ?? TimeSpan.FromSeconds(2);
        initial.Dispose();
        for (var attempt = 0; attempt < 180; attempt++)
        {
            await Task.Delay(delay, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, location);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenFactory(ct));
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode == 202)
            {
                delay = RetryDelay(response.Headers.RetryAfter) ?? TimeSpan.FromSeconds(2);
                response.Dispose();
                continue;
            }
            return response;
        }
        throw new TimeoutException("Fabric long-running operation did not complete within polling budget.");
    }

    private static TimeSpan? RetryDelay(RetryConditionHeaderValue? value)
        => value?.Delta ?? (value?.Date is { } dt ? dt - DateTimeOffset.UtcNow : null);
}
