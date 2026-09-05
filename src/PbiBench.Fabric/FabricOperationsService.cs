using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using PbiBench.Core.Abstractions;
using PbiBench.Core.Fabric;

namespace PbiBench.Fabric;

/// <summary>GET-only job history. Verified public API: docs/FABRIC_TOOLBOX_V02.md. Never follows server-supplied continuation URLs.</summary>
public sealed class FabricOperationsService(HttpClient http, IAccessTokenProvider tokens) : IFabricOperationsService
{
    public async Task<FabricJobInventory> ListRecentAsync(FabricItem item, FabricJobQuery query, CancellationToken cancellationToken)
    {
        query.Validate(); cancellationToken.ThrowIfCancellationRequested();
        var workspace = FabricSchemaRules.Id(item.WorkspaceId); var itemId = FabricSchemaRules.Id(item.Id);
        if (!FabricJobSupport.Supports(item.Kind)) return new(item, Array.Empty<FabricJobInstance>(), false, false, FabricJobSupport.Describe(item.Kind));
        var first = new Uri(FabricApiClient.BaseUri, "workspaces/" + workspace + "/items/" + itemId + "/jobs/instances");
        var next = first; var jobs = new List<FabricJobInstance>(); var seenTokens = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<string>(StringComparer.Ordinal);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(2)); var ct = deadline.Token;
        for (var page = 0; page < query.MaximumPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            using var response = await FabricHttp.SendAsync(http, tokens, HttpMethod.Get, next, FabricAudience.Fabric, null, ct).ConfigureAwait(false);
            if ((int)response.StatusCode != 200) throw new FabricApiException("Fabric job history returned an unexpected status.", (int)response.StatusCode);
            using var doc = await FabricHttp.ReadJsonAsync(response.Content, ct, 2 * 1024 * 1024).ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Fabric omitted its job collection.");
            var captured = DateTimeOffset.UtcNow;
            foreach (var row in values.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (jobs.Count == query.MaximumItems) return Result(true);
                var id = FabricSchemaRules.Id(Required(row, "id"));
                if (FabricSchemaRules.Id(Required(row, "itemId")) != itemId || !ids.Add(id)) throw new InvalidDataException("Job history changed or returned another item's jobs. Refresh again.");
                var start = Date(row, "startTimeUtc"); var end = Date(row, "endTimeUtc");
                if (start != null && end < start) throw new InvalidDataException("Job end precedes its start.");
                var correlation = Optional(row, "rootActivityId"); if (correlation != null) correlation = FabricSchemaRules.Id(correlation);
                string? failure = null;
                if (row.TryGetProperty("failureReason", out var reason) && reason.ValueKind != JsonValueKind.Null)
                    failure = string.Join(" · ", new[] { Optional(reason, "errorCode"), Optional(reason, "message", 2048) }.Where(s => !string.IsNullOrEmpty(s)));
                jobs.Add(new(id, workspace, itemId, item.Name, item.Kind, Required(row, "jobType"), Required(row, "status"), Optional(row, "invokeType") ?? "Not supplied", start, end, correlation, failure) { CapturedAt = captured });
            }
            var token = Optional(root, "continuationToken", 16384);
            if (string.IsNullOrEmpty(token))
            {
                if (!string.IsNullOrEmpty(Optional(root, "continuationUri", 32768))) throw new InvalidDataException("Fabric returned a continuation URL without a token.");
                return Result(false);
            }
            if (!seenTokens.Add(token!)) throw new InvalidDataException("Fabric repeated a job pagination token.");
            if (jobs.Count == query.MaximumItems) return Result(true);
            next = new Uri(first.AbsoluteUri + "?continuationToken=" + Uri.EscapeDataString(token!));
        }
        return Result(true);
        FabricJobInventory Result(bool truncated) => new(item, Array.AsReadOnly(jobs.OrderByDescending(j => j.StartTimeUtc).ToArray()), true, truncated,
            truncated ? "History reached the page/row limit; this result is partial." : "Recent history returned by Fabric. Retention varies by item; this is not a complete audit log.");
    }
    private static string Required(JsonElement row, string key) => Optional(row, key) is { Length: > 0 } value ? value : throw new InvalidDataException("Job metadata omitted " + key + ".");
    private static string? Optional(JsonElement row, string key, int maximum = 512)
    {
        if (row.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid job metadata object.");
        if (!row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > maximum) throw new InvalidDataException("Invalid or oversized job field: " + key);
        return value.GetString();
    }
    private static DateTimeOffset? Date(JsonElement row, string key)
    {
        var text = Optional(row, key, 64); if (string.IsNullOrEmpty(text)) return null;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)) throw new InvalidDataException("Invalid job timestamp.");
        return date;
    }
}
