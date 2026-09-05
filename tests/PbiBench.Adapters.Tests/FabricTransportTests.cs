using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PbiBench.Core.Abstractions;
using PbiBench.Core.Domain;
using PbiBench.Core.Fabric;
using PbiBench.Fabric;
using PbiBench.Workspace;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class FabricTransportTests
{
    private const string Workspace = "11111111-1111-1111-1111-111111111111", Item = "22222222-2222-2222-2222-222222222222", Operation = "33333333-3333-3333-3333-333333333333";
    [Fact]
    public async Task CatalogFollowsEncodedTokensWithoutFollowingUntrustedContinuationUris()
    {
        var tokens = new Tokens(); using var fixture = new HttpFixture(
            Json("{\"value\":[{\"id\":\"" + Workspace + "\",\"displayName\":\"Finance\"}],\"continuationToken\":\"a+/#&\",\"continuationUri\":\"https://evil.invalid/collect\"}"),
            Json("{\"value\":[{\"id\":\"" + Item + "\",\"displayName\":\"Research\"}]}"));
        using var http = new HttpClient(fixture); var service = new FabricCatalogService(http, tokens);
        var rows = await service.ListWorkspacesAsync(CancellationToken.None); Assert.Equal(2, rows.Count);
        Assert.All(fixture.Requests, request => Assert.Equal("api.fabric.microsoft.com", request.Url.Host));
        Assert.Contains("continuationToken=a%2B%2F%23%26", fixture.Requests[1].Url.AbsoluteUri);
        Assert.All(tokens.Requests, scopes => Assert.Equal("https://api.fabric.microsoft.com/.default", Assert.Single(scopes)));
    }
    [Fact]
    public async Task CatalogRejectsRepeatedTokensAndDuplicateObjects()
    {
        using var repeated = new HttpFixture(Json("{\"value\":[],\"continuationToken\":\"same\"}"), Json("{\"value\":[],\"continuationToken\":\"same\"}"));
        using var http = new HttpClient(repeated);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FabricCatalogService(http, new Tokens()).ListWorkspacesAsync(CancellationToken.None));
        using var duplicated = new HttpFixture(Json(JsonSerializer.Serialize(new { value = new[] { new { id = Workspace, displayName = "A" }, new { id = Workspace, displayName = "A" } } })));
        using var other = new HttpClient(duplicated);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FabricCatalogService(other, new Tokens()).ListWorkspacesAsync(CancellationToken.None));
    }
    [Fact]
    public async Task OneLakeSchemaUsesStorageAudienceAndCapturesPublicTypedMetadata()
    {
        using var fixture = new HttpFixture(Json("{\"name\":\"O'Brien\",\"schema_name\":\"a.b\",\"data_source_format\":\"DELTA\",\"columns\":[{\"name\":\"Id\",\"type_name\":\"long\",\"nullable\":false,\"position\":0},{\"name\":\"Amount\",\"type_name\":\"decimal\",\"type_precision\":18,\"type_scale\":4,\"nullable\":true,\"position\":1}]}"));
        using var http = new HttpClient(fixture); var tokens = new Tokens(); var source = new FabricSourceRef(Workspace, Item, "Lakehouse", "a.b", "O'Brien", "DELTA");
        var schema = await new FabricCatalogService(http, tokens).GetSchemaAsync(source, CancellationToken.None);
        FabricSchemaRules.Validate(schema); Assert.Equal("decimal(18,4)", schema.Columns[1].SourceType); Assert.False(schema.Columns[0].IsNullable);
        Assert.Equal("https://storage.azure.com/.default", Assert.Single(Assert.Single(tokens.Requests)));
        Assert.Contains("catalog_name=" + Item + "&schema_name=a.b", fixture.Requests[0].Url.AbsoluteUri);
        Assert.DoesNotContain("secret", schema.ToString());
    }
    [Theory]
    [InlineData("Other", "dbo", false)]
    [InlineData("Sales", "Other", false)]
    [InlineData("Sales", "dbo", true)]
    public async Task MismatchedOrIncompleteSourceSchemaCannotBeImported(string name, string schema, bool missingColumns)
    {
        using var fixture = new HttpFixture(Json(JsonSerializer.Serialize(new { name, schema_name = schema, data_source_format = "DELTA", columns = missingColumns ? null : new[] { new { name = "Id", type_name = "long", position = 0 } } })));
        using var http = new HttpClient(fixture);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FabricCatalogService(http, new Tokens()).GetSchemaAsync(Source(), CancellationToken.None));
    }
    [Fact]
    public async Task SqlEndpointDiscoveryUsesPublicPropertiesAndRejectsConnectionStringOptions()
    {
        using var fixture = new HttpFixture(Json(JsonSerializer.Serialize(new { id = Item, workspaceId = Workspace, type = "SQLDatabase", displayName = "SQL", properties = new { serverFqdn = "fixture.database.fabric.microsoft.com,1433", databaseName = "ActualCatalog" } })));
        using var http = new HttpClient(fixture); var resolved = await new FabricCatalogService(http, new Tokens()).ResolveItemAsync(new(Workspace, Item, "SQL", "SQLDatabase"), CancellationToken.None);
        Assert.Equal(new FabricSqlEndpoint("fixture.database.fabric.microsoft.com", "ActualCatalog"), resolved.SqlEndpoint);
        Assert.Throws<ArgumentException>(() => FabricSchemaRules.ValidateEndpoint(new("fixture.database.fabric.microsoft.com;Password=secret", "db")));
    }
    [Fact]
    public async Task HttpErrorsDoNotExposeResponseBodiesAndCancellationSkipsAuthentication()
    {
        using var fixture = new HttpFixture(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Password=secret; accessToken=hidden") });
        using var http = new HttpClient(fixture); var tokens = new Tokens(); var service = new FabricCatalogService(http, tokens);
        var error = await Assert.ThrowsAsync<FabricApiException>(() => service.ListWorkspacesAsync(CancellationToken.None));
        Assert.Null(error.ResponseBody); Assert.DoesNotContain("secret", error.ToString());
        tokens.Requests.Clear(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ListWorkspacesAsync(cancellation.Token)); Assert.Empty(tokens.Requests);
    }
    [Theory]
    [InlineData("https://evil.invalid/v1/operations/33333333-3333-3333-3333-333333333333")]
    [InlineData("http://api.fabric.microsoft.com/v1/operations/33333333-3333-3333-3333-333333333333")]
    [InlineData("https://api.fabric.microsoft.com:444/v1/operations/33333333-3333-3333-3333-333333333333")]
    [InlineData("https://api.fabric.microsoft.com/v1/workspaces/33333333-3333-3333-3333-333333333333")]
    public async Task LroRejectsUntrustedOriginsAndNonOperationPathsBeforeTokenAcquisition(string location)
    {
        using var fixture = new HttpFixture(); using var http = new HttpClient(fixture); var called = false;
        await Assert.ThrowsAsync<ArgumentException>(() => new LongRunningOperationPoller(http, _ => { called = true; return Task.FromResult("secret"); }).WaitAsync(Accepted(location), CancellationToken.None));
        Assert.False(called); Assert.Empty(fixture.Requests);
    }
    [Fact]
    public async Task LroPollsRunningStateAndFetchesSucceededResultWithoutLocation()
    {
        using var fixture = new HttpFixture(Json("{\"status\":\"Running\"}"), Json("{\"status\":\"Succeeded\"}"), Json("{\"definition\":{\"parts\":[]}}"));
        using var http = new HttpClient(fixture);
        using var result = await new LongRunningOperationPoller(http, _ => Task.FromResult("secret"), (_, _) => Task.CompletedTask).WaitAsync(Accepted(Url()), CancellationToken.None);
        using var json = await FabricHttp.ReadJsonAsync(result.Content, CancellationToken.None);
        Assert.True(json.RootElement.TryGetProperty("definition", out _)); Assert.Equal(3, fixture.Requests.Count); Assert.EndsWith("/result", fixture.Requests[2].Url.AbsoluteUri);
    }
    [Theory]
    [InlineData("Failed")]
    [InlineData("Canceled")]
    [InlineData("Unknown")]
    public async Task LroDoesNotReportFailedOrUnknownStatesAsSuccess(string state)
    {
        using var fixture = new HttpFixture(Json("{\"status\":\"" + state + "\",\"error\":{\"message\":\"secret\"}}")); using var http = new HttpClient(fixture);
        var error = await Assert.ThrowsAsync<FabricApiException>(() => new LongRunningOperationPoller(http, _ => Task.FromResult("token"), (_, _) => Task.CompletedTask).WaitAsync(Accepted(Url()), CancellationToken.None));
        Assert.DoesNotContain("secret", error.ToString());
    }
    [Fact]
    public async Task LroRejectsDifferentOperationIdentity()
    {
        var response = Json("{\"status\":\"Succeeded\"}"); response.Headers.Location = new Uri("https://api.fabric.microsoft.com/v1/operations/" + Item + "/result");
        using var fixture = new HttpFixture(response); using var http = new HttpClient(fixture);
        await Assert.ThrowsAsync<InvalidDataException>(() => new LongRunningOperationPoller(http, _ => Task.FromResult("token"), (_, _) => Task.CompletedTask).WaitAsync(Accepted(Url()), CancellationToken.None));
    }
    [Fact]
    public async Task RemoteDefinitionRequiresExactPayloadApprovalAndConsumesPlanOnce()
    {
        using var fixture = new HttpFixture(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }); using var http = new HttpClient(fixture);
        var service = new FabricApiClient(http, new Tokens()); var definition = new CloudDefinition("TMDL", new[] { new DefinitionPart("definition/model.tmdl", "eA==", "InlineBase64") });
        var now = DateTimeOffset.UtcNow; var plan = new ChangePlan(Guid.NewGuid(), now, ApprovalLevel.RemoteModelWrite, new ResourceRef("Fabric", null, Workspace, Item, "SemanticModel", "Fixture"),
            new[] { new PlannedChange(Item, "Update semantic model definition", "Reviewed baseline", FabricApiClient.DefinitionUpdateFingerprint(definition, false), Array.Empty<string>()) }, "snapshot", "reviewed rollback");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateSemanticModelDefinitionAsync(Workspace, Item, definition, true, new(plan, now, "tester"))); Assert.Empty(fixture.Requests);
        await service.UpdateSemanticModelDefinitionAsync(Workspace, Item, definition, false, new(plan, now, "tester"));
        Assert.Equal(HttpMethod.Post, Assert.Single(fixture.Requests).Method); Assert.Contains("\"path\"", fixture.Requests[0].Body);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateSemanticModelDefinitionAsync(Workspace, Item, definition, false, new(plan, now, "tester"))); Assert.Single(fixture.Requests);
    }
    [Fact]
    public async Task AuthenticationValidationNeverStartsInteractiveSignInImplicitly()
    {
        var auth = new EntraPublicClientTokenProvider();
        await Assert.ThrowsAsync<FabricAuthenticationRequiredException>(() => auth.GetAccessTokenAsync(EntraPublicClientTokenProvider.Scopes(FabricAudience.Fabric)));
        await Assert.ThrowsAsync<ArgumentException>(() => auth.GetAccessTokenAsync(new[] { "https://evil.invalid/.default" }));
        await Assert.ThrowsAsync<ArgumentException>(() => auth.SignInAsync(new("organizations", Item), FabricAudience.Fabric, CancellationToken.None));
        Assert.Null(auth.AccountLabel);
    }
    [Fact]
    public async Task JsonAndSchemaLimitsFailWithoutPartialMetadata()
    {
        using var content = new StringContent("{\"padding\":\"" + new string('x', 1000) + "\"}");
        await Assert.ThrowsAsync<InvalidDataException>(() => FabricHttp.ReadJsonAsync(content, CancellationToken.None, 100));
        var schema = Schema(); Assert.Throws<ArgumentException>(() => FabricSchemaRules.Validate(schema with { Warnings = null! }));
        Assert.Throws<ArgumentException>(() => FabricSchemaRules.Validate(schema with { Columns = new[] { schema.Columns[0] with { SourceType = "string" } } }));
        Assert.Throws<ArgumentException>(() => FabricSchemaRules.Validate(schema with { Source = schema.Source with { Table = "Other" } }));
    }
    internal static FabricSourceRef Source() => new(Workspace, Item, "Lakehouse", "dbo", "Sales", "DELTA", new("fixture.datawarehouse.fabric.microsoft.com", Item));
    internal static FabricTableSchema Schema()
    { var source = Source(); var columns = new[] { new FabricColumnSchema("Id", "long", false) }; return new(source, columns, FabricSchemaRules.Fingerprint(source, columns), DateTimeOffset.UtcNow, Array.Empty<string>()); }
    private static string Url() => "https://api.fabric.microsoft.com/v1/operations/" + Operation;
    private static HttpResponseMessage Accepted(string url) { var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("") }; response.Headers.Location = new Uri(url); return response; }
    internal static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    internal sealed class Tokens : IAccessTokenProvider
    {
        public List<string[]> Requests { get; } = new();
        public Task<string> GetAccessTokenAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Requests.Add(scopes.ToArray()); return Task.FromResult("secret-token"); }
    }
    private sealed record Sent(Uri Url, HttpMethod Method, string Body);
    private sealed class HttpFixture(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<Sent> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests.Add(new(request.RequestUri!, request.Method, request.Content == null ? "" : await request.Content.ReadAsStringAsync()));
            var response = responses.Dequeue(); response.RequestMessage = request; return response;
        }
    }
}
