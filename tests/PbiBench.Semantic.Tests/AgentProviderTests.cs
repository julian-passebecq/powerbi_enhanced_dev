using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Automation.Agent;
using PbiBench.Core.Agent;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class AgentProviderTests
{
    [TestMethod]
    public async Task OfflineProviderProducesAReadOnlyReviewWithoutSharingApproval()
    {
        var provider = new OfflineAgentProvider(); Assert.IsFalse(provider.IsOnline);
        var result = await provider.ProposeAsync(Request(false), CancellationToken.None);
        Assert.AreEqual(AgentProposalKind.Review, result.Kind); Assert.IsNull(result.Recipe); StringAssert.Contains(result.Explanation, "no provider request");
    }
    [TestMethod]
    public async Task OnlineRequiresExplicitSharingApprovalBeforeKeyAccessOrHttp()
    {
        var keyCalls = 0; using var transport = new Transport(_ => throw new AssertFailedException("Unexpected HTTP.")); using var http = new HttpClient(transport);
        var provider = new OpenAiAgentProvider(http, "fixture-model", _ => { keyCalls++; return Task.FromResult("fixture-secret"); });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.ProposeAsync(Request(false), CancellationToken.None)); Assert.AreEqual(0, keyCalls); Assert.AreEqual(0, transport.Calls);
    }
    [TestMethod]
    public async Task ProviderUsesExactReviewedPayloadStrictSchemaAndStatelessSingleRequest()
    {
        string? sent = null; using var transport = new Transport(async request =>
        {
            Assert.AreEqual(OpenAiAgentProvider.Endpoint, request.RequestUri); Assert.AreEqual("fixture-secret", request.Headers.Authorization!.Parameter);
            sent = await request.Content!.ReadAsStringAsync(); return Response();
        });
        using var http = new HttpClient(transport); var provider = Provider(http); var request = Request(true); var proposal = await provider.ProposeAsync(request, CancellationToken.None);
        Assert.AreEqual(AgentProposalKind.Explanation, proposal.Kind); Assert.AreEqual(1, transport.Calls);
        using var body = JsonDocument.Parse(sent!); var root = body.RootElement;
        Assert.IsFalse(root.GetProperty("store").GetBoolean()); Assert.IsFalse(root.GetProperty("background").GetBoolean());
        Assert.AreEqual("json_schema", root.GetProperty("text").GetProperty("format").GetProperty("type").GetString()); Assert.IsTrue(root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.IsFalse(root.TryGetProperty("tools", out _)); Assert.IsFalse(root.TryGetProperty("previous_response_id", out _));
        StringAssert.Contains(root.GetProperty("input")[0].GetProperty("content").GetString()!, request.Context.SharedJson);
        Assert.IsFalse(sent!.Contains("fixture-secret")); Assert.IsFalse(sent.Contains("LOCAL-HASH-NOT-SHARED"));
    }
    [TestMethod]
    [DataRow("incomplete")]
    [DataRow("failed")]
    [DataRow("cancelled")]
    public async Task UnfinishedResponsesCannotBecomeProposals(string status)
    {
        using var transport = new Transport(_ => Task.FromResult(Response(status))); using var http = new HttpClient(transport);
        await Assert.ThrowsExactlyAsync<AgentProviderException>(() => Provider(http).ProposeAsync(Request(true), CancellationToken.None));
    }
    [TestMethod]
    [DataRow("function_call")]
    [DataRow("web_search_call")]
    [DataRow("computer_call")]
    public async Task ProviderToolCallsAreRejectedWithoutDispatch(string type)
    {
        using var transport = new Transport(_ => Task.FromResult(Json(new { status = "completed", output = new[] { new { type } } })));
        using var http = new HttpClient(transport); await Assert.ThrowsExactlyAsync<AgentProviderException>(() => Provider(http).ProposeAsync(Request(true), CancellationToken.None)); Assert.AreEqual(1, transport.Calls);
    }
    [TestMethod]
    public async Task RefusalAndErrorBodiesNeverExposeProviderSecrets()
    {
        using var refused = new Transport(_ => Task.FromResult(Json(new { status = "completed", output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "refusal", refusal = "fixture-secret" } } } } })));
        using var refusalHttp = new HttpClient(refused); var error = await Assert.ThrowsExactlyAsync<AgentProviderException>(() => Provider(refusalHttp).ProposeAsync(Request(true), CancellationToken.None)); Assert.IsFalse(error.ToString().Contains("fixture-secret"));
        using var failure = new Transport(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("fixture-secret") })); using var http = new HttpClient(failure);
        var denied = await Assert.ThrowsExactlyAsync<AgentProviderException>(() => Provider(http).ProposeAsync(Request(true), CancellationToken.None)); Assert.IsFalse(denied.ToString().Contains("fixture-secret")); Assert.AreEqual(1, failure.Calls);
    }
    [TestMethod]
    public async Task CancellationAndResponseCapsRejectBeforeAcceptingOutput()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var transport = new Transport(_ => Task.FromResult(Response())); using var http = new HttpClient(transport);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Provider(http).ProposeAsync(Request(true), cancel.Token)); Assert.AreEqual(0, transport.Calls);
        using var oversized = new Transport(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 512 * 1024 + 1)) })); using var largeHttp = new HttpClient(oversized);
        await Assert.ThrowsExactlyAsync<AgentProviderException>(() => Provider(largeHttp).ProposeAsync(Request(true), CancellationToken.None));
    }
    [TestMethod]
    public void ContextIsOptInAndExcludesWholeDatabasePartitionsCredentialsAndPaths()
    {
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("PrivateSales"); var measure = table.AddMeasure("Total", "123");
        table.AddMPartition("SensitiveSource", "let Password = \"secret-value\" in Password");
        var extras = new AgentContextExtras("CURRENT-DAX", new[] { new AgentContextFinding("Fixture", "PrivateSales", "Warning", "BPA-DETAIL") },
            new[] { new AgentContextDiff("DataSource", "connectionString", "", "secret-value"), new AgentContextDiff("Model/tables/Sales/measures/Total", "displayFolder", "Old", "New"),
                new AgentContextDiff("Model/tables/Sales/partitions/Import/source", "expression", "", "secret-value"), new AgentContextDiff("Model/expressions/Parameter", "expression", "", "secret-value"),
                new AgentContextDiff("Model/tables/Sales/measures/Total", "expression", "SUM(1)", "SUM(2)") },
            new[] { new AgentContextTest("Test", "Passed", "TEST-EVIDENCE") }, new[] { "ReadMetadata", "server=secret-value" });
        var nothing = AgentContextCapture.Capture(handler, new[] { measure }, new(), extras);
        Assert.IsFalse(nothing.SharedJson.Contains("PrivateSales")); Assert.IsFalse(nothing.SharedJson.Contains("CURRENT-DAX"));
        var selected = AgentContextCapture.Capture(handler, new[] { measure }, new(SelectedObjects: true, WorkspaceDiff: true, Capabilities: true), extras);
        StringAssert.Contains(selected.SharedJson, "PrivateSales"); StringAssert.Contains(selected.SharedJson, "displayFolder"); StringAssert.Contains(selected.SharedJson, "SUM(2)"); StringAssert.Contains(selected.SharedJson, "ReadMetadata");
        Assert.IsFalse(selected.SharedJson.Contains("secret-value")); Assert.IsFalse(selected.SharedJson.Contains("BPA-DETAIL")); Assert.IsFalse(selected.SharedJson.Contains("TEST-EVIDENCE")); Assert.IsFalse(selected.SharedJson.Contains("CURRENT-DAX"));
    }
    [TestMethod]
    public void ContextBudgetsDiscloseOmissionsAndRejectCrossModelSelection()
    {
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("Facts"); var measure = table.AddMeasure("Total", "1");
        using var other = new TabularModelHandler(1600); Assert.ThrowsExactly<InvalidOperationException>(() => AgentContextCapture.Capture(other, new[] { measure }, new(SelectedObjects: true)));
        var findings = Enumerable.Range(0, 101).Select(index => new AgentContextFinding("Rule", "Object", "Warning", new string('x', 10))).ToArray();
        var context = AgentContextCapture.Capture(handler, Array.Empty<TabularNamedObject>(), new(BpaFindings: true), new(Findings: findings)); StringAssert.Contains(context.SharedJson, "Additional items omitted");
    }
    private static AgentRequest Request(bool approval) => new("Explain the explicitly shared context.", new(Guid.NewGuid(), "LOCAL-HASH-NOT-SHARED", "{\"sections\":[]}"), approval);
    private static OpenAiAgentProvider Provider(HttpClient http) => new(http, "fixture-model", _ => Task.FromResult("fixture-secret"));
    private static HttpResponseMessage Response(string status = "completed") => Json(new { status, output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = AgentProposalJson.Serialize(new(1, AgentProposalKind.Explanation, "Fixture explanation", "Synthetic HTTP evidence only.", null, null, null)) } } } } });
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Transport(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; var response = await send(request); response.RequestMessage ??= request; return response; }
    }
}

