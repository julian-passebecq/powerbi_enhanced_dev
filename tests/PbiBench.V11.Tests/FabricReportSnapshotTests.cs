using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PbiBench.Core.Abstractions;
using PbiBench.Core.Fabric;
using PbiBench.Fabric;
using Xunit;

namespace PbiBench.V11.Tests;

public sealed class FabricReportSnapshotTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fabric-report-" + Guid.NewGuid().ToString("N"));
    private static readonly FabricItem Item = new("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "Sales", "Report");
    private const string Operation = "33333333-3333-3333-3333-333333333333";
    public FabricReportSnapshotTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private static object Part(string path, string text = "{}", string type = "InlineBase64") => new { path, payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)), payloadType = type };
    private static string Definition(bool legacy = false) => JsonSerializer.Serialize(new { definition = new { format = legacy ? "PBIR-Legacy" : "PBIR", parts = new[] { Part("definition.pbir", "{\"version\":\"4.0\"}"), Part(legacy ? "report.json" : "definition/report.json"), Part("StaticResources/image.png", "binary") } } });
    private static HttpResponseMessage Response(int status, string text = "{}") => new((HttpStatusCode)status) { Content = new StringContent(text) };
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task DirectDefinitionWritesOnlyANewSnapshotWithHashesAndNoAuth(bool legacy)
    {
        using var fake = new Responses((_, _) => Response(200, Definition(legacy))); using var http = new HttpClient(fake);
        var destination = Path.Combine(root, "snapshot");
        var result = await new FabricReportSnapshotService(http, new Tokens()).GetSnapshotAsync(Item, destination, default);
        Assert.Equal(legacy ? "PBIR-Legacy" : "PBIR", result.Format); Assert.Equal(3, result.PartCount);
        Assert.Equal("/v1/workspaces/" + Item.WorkspaceId + "/reports/" + Item.Id + "/getDefinition", fake.Paths.Single()); Assert.Equal(HttpMethod.Post, fake.Methods.Single());
        var manifest = File.ReadAllText(result.ManifestFile); Assert.Contains(Item.Id, manifest); Assert.DoesNotContain("fixture-secret-token", manifest); Assert.DoesNotContain("Authorization", manifest);
        using var doc = JsonDocument.Parse(manifest); Assert.Equal(3, doc.RootElement.GetProperty("parts").GetArrayLength());
        foreach (var part in doc.RootElement.GetProperty("parts").EnumerateArray())
        {
            using var sha = System.Security.Cryptography.SHA256.Create(); var bytes = File.ReadAllBytes(Path.Combine(destination, part.GetProperty("Path").GetString()!));
            Assert.Equal(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), part.GetProperty("Sha256").GetString());
        }
        await Assert.ThrowsAsync<IOException>(() => new FabricReportSnapshotService(http, new Tokens()).GetSnapshotAsync(Item, destination, default)); Assert.Single(fake.Paths);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task LongOperationAndThrottlingRespectTrustedOperationIdentity(bool headerOnly)
    {
        using var fake = new Responses((attempt, _) =>
        {
            var response = attempt switch { 0 or 2 => Response(429), 1 => Response(202), 3 => Response(200, "{\"status\":\"Succeeded\"}"), _ => Response(200, Definition()) };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            if (attempt == 1) { response.Headers.Add("x-ms-operation-id", Operation); if (!headerOnly) response.Headers.Location = new Uri("https://api.fabric.microsoft.com/v1/operations/" + Operation); }
            return response;
        });
        using var http = new HttpClient(fake); var result = await new FabricReportSnapshotService(http, new Tokens()).GetSnapshotAsync(Item, Path.Combine(root, "snapshot"), default);
        Assert.Equal(5, fake.Paths.Count); Assert.Equal("/v1/operations/" + Operation + "/result", fake.Paths.Last()); Assert.All(fake.Methods.Skip(2), m => Assert.Equal(HttpMethod.Get, m)); Assert.Equal("PBIR", result.Format);
    }
    [Theory] [InlineData("../escape.json")] [InlineData("/absolute")] [InlineData("C:\\target.json")] [InlineData("\\\\server\\share")]
    [InlineData("definition/a/../b.json")] [InlineData("definition/CON.json")] [InlineData("definition/COM¹.json")]
    [InlineData("definition/a.json:stream")] [InlineData("definition/a. /b.json")] [InlineData("definition//b.json")]
    [InlineData("definition/NUL /b.json")] [InlineData(".pbibench/file.json")] [InlineData("pbibench-fabric-snapshot.json")]
    [InlineData("definition/CON .json")]
    public void RejectsUnsafePartPaths(string path) => Assert.Throws<InvalidDataException>(() => FabricReportSnapshotService.NormalizePartPath(path));
    [Theory] [InlineData("duplicate")] [InlineData("type")] [InlineData("base64")] [InlineData("part-bound")]
    [InlineData("count-bound")] [InlineData("total-bound")] [InlineData("credentials")] [InlineData("format")] [InlineData("collision")]
    public async Task MalformedDefinitionsWriteNothing(string kind)
    {
        var parts = new List<object> { Part("definition.pbir"), Part("definition/report.json") };
        switch (kind)
        {
            case "duplicate": parts.Add(Part("definition\\report.json")); break;
            case "type": parts.Add(Part("definition/x.json", "{}", "ExternalUrl")); break;
            case "base64": parts.Add(new { path = "definition/x.json", payload = "%%%", payloadType = "InlineBase64" }); break;
            case "part-bound": parts.Add(Part("StaticResources/big.bin", new string('x', FabricReportSnapshotService.MaximumPartBytes + 1))); break;
            case "count-bound": parts.AddRange(Enumerable.Range(0, 2048).Select(i => Part("definition/a" + i + ".json"))); break;
            case "total-bound": parts.AddRange(Enumerable.Range(0, 9).Select(i => Part("StaticResources/a" + i, new string('x', FabricReportSnapshotService.MaximumPartBytes)))); break;
            case "credentials": parts[0] = Part("definition.pbir", "{\"datasetReference\":{\"byConnection\":{\"connectionString\":\"Data Source=x;Password=secret\"}}}"); break;
            case "collision": parts.Add(Part("definition/report.json/sub.json")); break;
        }
        var body = JsonSerializer.Serialize(new { definition = new { format = kind == "format" ? "PBIR-Legacy" : "PBIR", parts } });
        using var fake = new Responses((_, _) => Response(200, body)); using var http = new HttpClient(fake);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FabricReportSnapshotService(http, new Tokens()).GetSnapshotAsync(Item, Path.Combine(root, "snapshot"), default));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }
    [Fact] public async Task ExistingJunctionIsRejectedBeforeAuthentication()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        var target = Path.Combine(root, "actual"); var link = Path.Combine(root, "linked"); Directory.CreateDirectory(target);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + target + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        try
        {
            using var fake = new Responses((_, _) => Response(200, Definition())); using var http = new HttpClient(fake);
            await Assert.ThrowsAsync<IOException>(() => new FabricReportSnapshotService(http, new Tokens()).GetSnapshotAsync(Item, Path.Combine(link, "snapshot"), default));
            Assert.Empty(fake.Paths); Assert.Empty(Directory.GetFileSystemEntries(target));
        }
        finally { Directory.Delete(link); }
    }
    [Theory] [InlineData("foreign")] [InlineData("mismatch")] [InlineData("failed")] [InlineData("redirect")]
    public async Task UntrustedOrFailedLroNeverWrites(string kind)
    {
        using var fake = new Responses((attempt, _) =>
        {
            if (attempt > 0) return kind == "redirect" ? Response(302) : Response(200, "{\"status\":\"Failed\"}");
            var response = Response(202); response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            response.Headers.Location = new Uri((kind == "foreign" ? "https://evil.invalid" : "https://api.fabric.microsoft.com") + "/v1/operations/" + Operation);
            response.Headers.Add("x-ms-operation-id", kind == "mismatch" ? Item.Id : Operation); return response;
        }); using var http = new HttpClient(fake);
        await Assert.ThrowsAnyAsync<Exception>(() => new FabricReportSnapshotService(http, new Tokens()).GetSnapshotAsync(Item, Path.Combine(root, "snapshot"), default)); Assert.Empty(Directory.GetFileSystemEntries(root));
    }
    [Fact] public async Task CancellationAndExistingDestinationCannotTriggerRequestsOrOverwrite()
    {
        using var fake = new Responses((_, _) => Response(200, Definition())); using var http = new HttpClient(fake); var service = new FabricReportSnapshotService(http, new Tokens());
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetSnapshotAsync(Item, Path.Combine(root, "snapshot"), canceled.Token));
        await Assert.ThrowsAsync<IOException>(() => service.GetSnapshotAsync(Item, root, default)); Assert.Empty(fake.Paths);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetSnapshotAsync(Item with { Kind = "Notebook" }, Path.Combine(root, "snapshot"), default)); Assert.Empty(fake.Paths);
    }
    [Fact] public async Task DestinationAppearingDuringRequestIsNeverMerged()
    {
        var destination = Path.Combine(root, "snapshot");
        using var fake = new Responses((_, _) => { Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "user.txt"), "preserve"); return Response(200, Definition()); });
        using var http = new HttpClient(fake);
        await Assert.ThrowsAsync<IOException>(() => new FabricReportSnapshotService(http, new Tokens()).GetSnapshotAsync(Item, destination, default));
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(destination, "user.txt"))); Assert.Single(Directory.GetFiles(destination)); Assert.Single(Directory.GetDirectories(root));
    }
    private sealed class Tokens : IAccessTokenProvider
    { public Task<string> GetAccessTokenAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default) => Task.FromResult("fixture-secret-token"); }
    private sealed class Responses(Func<int, HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new(); public List<HttpMethod> Methods { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Assert.Equal("fixture-secret-token", request.Headers.Authorization!.Parameter); Paths.Add(request.RequestUri!.AbsolutePath); Methods.Add(request.Method); return Task.FromResult(respond(Paths.Count - 1, request)); }
    }
}
