using System.Net;
using System.Net.Http;
using System.Text.Json;
using PbiBench.Core.Abstractions;
using PbiBench.Core.Fabric;
using PbiBench.Fabric;
using Xunit;

namespace PbiBench.V11.Tests;

public sealed class FabricOperationsTests
{
    private static readonly FabricItem Item = new("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "Daily pipeline", "DataPipeline");
    private static object Job(int id = 3, string? itemId = null) => new { id = id.ToString("x8") + "-3333-3333-3333-333333333333", itemId = itemId ?? Item.Id, jobType = "Pipeline", status = "Failed", invokeType = "Scheduled", rootActivityId = "44444444-4444-4444-4444-444444444444", startTimeUtc = "2026-09-05T08:00:00", endTimeUtc = "2026-09-05T08:02:00Z", failureReason = new { errorCode = "FixtureFailure", message = "Synthetic failure details", arbitrarySecret = "DO_NOT_EXPORT" } };
    private static string Page(int id, string? token = null) => JsonSerializer.Serialize(new { value = new[] { Job(id) }, continuationToken = token, continuationUri = token == null ? null : "https://untrusted.invalid/credential-sink" });
    [Fact] public async Task RecentHistoryUsesGetAndFixedOriginTokenPaginationWithUtcDetails()
    {
        using var handler = new Responses((n, _) => Page(n + 3, n == 0 ? "next +/&" : null)); using var http = new HttpClient(handler);
        var result = await new FabricOperationsService(http, new Tokens()).ListRecentAsync(Item, new(), default);
        Assert.True(result.Supported); Assert.False(result.Truncated); Assert.Equal(2, result.Jobs.Count);
        Assert.All(handler.Requests, r => { Assert.Equal(HttpMethod.Get, r.Method); Assert.Equal("api.fabric.microsoft.com", r.Uri.Host); Assert.EndsWith("/items/" + Item.Id + "/jobs/instances", r.Uri.AbsolutePath); });
        Assert.Equal("?continuationToken=next%20%2B%2F%26", handler.Requests[1].Uri.Query);
        var job = result.Jobs[0]; Assert.Equal(TimeSpan.FromMinutes(2), job.Duration); Assert.Equal(TimeSpan.Zero, job.StartTimeUtc!.Value.Offset);
        Assert.Contains("Synthetic failure details", job.Detail); Assert.DoesNotContain("DO_NOT_EXPORT", job.Detail); Assert.Contains(Item.Id, job.Detail);
    }
    [Fact] public async Task UnsupportedTypesNeverAcquireTokensOrSendRequests()
    {
        using var handler = new Responses((_, _) => throw new InvalidOperationException()); using var http = new HttpClient(handler);
        var tokens = new Tokens(); var result = await new FabricOperationsService(http, tokens).ListRecentAsync(Item with { Kind = "Report" }, new(), default);
        Assert.False(result.Supported); Assert.Contains("not supported", result.Notice); Assert.Empty(handler.Requests); Assert.Equal(0, tokens.Calls);
    }
    [Fact] public async Task LimitsReturnExplicitPartialResultsAndCyclesAreRejected()
    {
        using var handler = new Responses((n, _) => Page(n + 3, "next")); using var http = new HttpClient(handler); var service = new FabricOperationsService(http, new Tokens());
        var pageLimited = await service.ListRecentAsync(Item, new(1, 100), default); Assert.True(pageLimited.Truncated); Assert.Single(pageLimited.Jobs);
        handler.Requests.Clear(); var rowLimited = await service.ListRecentAsync(Item, new(10, 1), default); Assert.True(rowLimited.Truncated); Assert.Single(handler.Requests);
        handler.Requests.Clear(); await Assert.ThrowsAsync<InvalidDataException>(() => service.ListRecentAsync(Item, new(), default)); Assert.Equal(2, handler.Requests.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListRecentAsync(Item, new(21, 1), default));
    }
    [Theory] [InlineData("{}")] [InlineData("{\"value\":{},\"continuationToken\":null}")] [InlineData("{\"value\":[],\"continuationToken\":123}")]
    [InlineData("{\"value\":[],\"continuationUri\":\"https://untrusted.invalid\"}")]
    public async Task MalformedResponsesCannotLookLikeCompleteHistory(string json)
    {
        using var handler = new Responses((_, _) => json); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FabricOperationsService(http, new Tokens()).ListRecentAsync(Item, new(), default));
    }
    [Fact] public async Task RejectsWrongItemDuplicateJobsOversizedResponsesAndPreservesHttpFailures()
    {
        foreach (var json in new[] { JsonSerializer.Serialize(new { value = new[] { Job(itemId: Guid.NewGuid().ToString()) } }), JsonSerializer.Serialize(new { value = new[] { Job(), Job() } }), new string(' ', 2 * 1024 * 1024 + 1) })
        {
            using var handler = new Responses((_, _) => json); using var http = new HttpClient(handler);
            await Assert.ThrowsAsync<InvalidDataException>(() => new FabricOperationsService(http, new Tokens()).ListRecentAsync(Item, new(), default));
        }
        using var denied = new Responses((_, _) => "private error containing token") { Status = HttpStatusCode.Forbidden }; using var client = new HttpClient(denied);
        var error = await Assert.ThrowsAsync<FabricApiException>(() => new FabricOperationsService(client, new Tokens()).ListRecentAsync(Item, new(), default)); Assert.Equal(403, error.StatusCode); Assert.DoesNotContain("token", error.Message);
    }
    [Fact] public async Task CancellationBeforeAndBetweenPagesStopsReads()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); using var handler = new Responses((_, _) => Page(3)); using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FabricOperationsService(http, new Tokens()).ListRecentAsync(Item, new(), canceled.Token)); Assert.Empty(handler.Requests);
        using var mid = new CancellationTokenSource(); using var between = new Responses((_, ct) => { mid.Cancel(); return Page(3, "next"); }); using var client = new HttpClient(between);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FabricOperationsService(client, new Tokens()).ListRecentAsync(Item, new(), mid.Token)); Assert.Single(between.Requests);
    }
    [Fact] public async Task InventoryExportIsAllowlistedBoundedFormulaSafeAndCancellable()
    {
        var rows = new[] { Item with { Name = " =HYPERLINK(\"evil\")", SqlEndpoint = new("secret-token", "password") }, Item with { Id = Guid.NewGuid().ToString(), Name = "Notebook", Kind = "Notebook" } };
        Assert.Single(FabricInventoryExport.Filter(rows, "PIPELINE", "DataPipeline"));
        var json = FabricInventoryExport.Serialize(rows, false); var csv = FabricInventoryExport.Serialize(rows, true);
        Assert.DoesNotContain("secret-token", json + csv); Assert.DoesNotContain("password", json + csv); Assert.Contains("' =HYPERLINK", csv);
        using var doc = JsonDocument.Parse(json); Assert.Equal(new[] { "workspaceId", "itemId", "name", "type" }, doc.RootElement.GetProperty("items")[0].EnumerateObject().Select(p => p.Name));
        Assert.Throws<InvalidDataException>(() => FabricInventoryExport.Serialize(Enumerable.Repeat(Item, 10001).ToArray(), false));
        var path = Path.GetTempFileName(); File.WriteAllText(path, "existing");
        try { using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FabricInventoryExport.SaveAsync(path, rows, false, cancel.Token)); Assert.Equal("existing", File.ReadAllText(path)); }
        finally { File.Delete(path); }
    }
    private sealed class Tokens : IAccessTokenProvider
    { public int Calls; public Task<string> GetAccessTokenAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult("fixture-access-token"); } }
    private sealed class Responses(Func<int, CancellationToken, string> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri)> Requests { get; } = new(); public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { var n = Requests.Count; Requests.Add((request.Method, request.RequestUri!)); return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(respond(n, cancellationToken)) }); }
    }
}
