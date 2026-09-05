using System.Text.Json;
using PbiBench.Core.Automation;
using PbiBench.Core.Commands;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class CommandContractTests
{
    private static CommandRequest Request => new() { Kind = CommandKind.Set, Target = new("source.bim"), Selection = new[] { new CommandObject("Measure", "Revenue", "Sales") }, Property = "Description", Value = "Reviewed", OutputPath = "output.bim" };
    private static CommandReview Review(CommandRequest request, string before = "before") => CommandJson.Review(request, "output.bim", before, false, true, new[] { new CommandChange("Sales/Revenue", "Description", "", request.Value!, "Explicit edit") });

    [Theory]
    [InlineData("{\"kind\":\"Inspect\",\"kind\":\"Set\"}")]
    [InlineData("{\"kind\":\"Inspect\",\"target\":{\"modelPath\":\"a\",\"modelPath\":\"b\"}}")]
    [InlineData("{\"kind\":\"Inspect\",\"password\":\"never accepted\"}")]
    [InlineData("{\"kind\":0}")]
    [InlineData("{}")]
    public void MalformedUnknownAndDuplicateCommandFieldsAreRejected(string json) => Assert.Throws<InvalidDataException>(() => CommandJson.ParseRequest(json));

    [Fact]
    public void TargetAndPayloadAmbiguityAreRejectedBeforeServicesRun()
    {
        Assert.Throws<ArgumentException>(() => CommandJson.Validate(Request with { Target = new("a.bim", "server", "database") }));
        Assert.Throws<ArgumentException>(() => CommandJson.Validate(new() { Kind = CommandKind.Inspect, Query = "EVALUATE {1}" }));
        Assert.Throws<ArgumentException>(() => CommandJson.Validate(new() { Kind = CommandKind.Query, Target = new(Server: "s;Password=secret", Database: "db") }));
        Assert.Throws<ArgumentException>(() => CommandJson.Validate(new() { Kind = CommandKind.Query, Target = new(Server: "s") }));
        Assert.Throws<ArgumentException>(() => CommandJson.Validate(new() { Kind = CommandKind.Script, ScriptLanguage = "TrustedCSharp" }));
        CommandJson.Validate(new() { Kind = CommandKind.Deploy, Target = new("a.bim", "server", "database") });
    }
    [Fact]
    public void ReviewHashIsCanonicalButChangesWithTargetInputAndExactSource()
    {
        using var a = JsonDocument.Parse("{\"b\":2,\"a\":1}"); using var b = JsonDocument.Parse("{\"a\":1,\"b\":2}");
        Assert.Equal(CommandJson.Hash(a.RootElement), CommandJson.Hash(b.RootElement));
        Assert.Equal(Review(Request).Hash, Review(CommandJson.ParseRequest(CommandJson.Serialize(Request))).Hash);
        Assert.NotEqual(Review(Request).Hash, Review(Request with { Value = "Different" }).Hash);
        Assert.NotEqual(Review(Request).Hash, Review(Request with { Target = new("other.bim") }).Hash);
        Assert.NotEqual(Review(Request).Hash, Review(Request, "externally edited").Hash);
    }
    [Fact]
    public async Task SavedApprovalBindsNonceLifetimeContentAndRejectsReplay()
    {
        using var folder = new Temp(); var request = Request; var review = Review(request); var envelope = CommandReviewStore.Create(request, review);
        var path = Path.Combine(folder.Root, "review.json"); await CommandReviewStore.SaveAsync(path, envelope, CancellationToken.None); var loaded = CommandReviewStore.Load(path);
        Assert.Equal(envelope.ApprovalHash, loaded.ApprovalHash); Assert.NotEqual(review.Hash, envelope.ApprovalHash);
        CommandReviewStore.Claim(loaded, request, review, envelope.ApprovalHash, folder.Root, CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() => CommandReviewStore.Claim(loaded, request, review, envelope.ApprovalHash, folder.Root, CancellationToken.None));
        foreach (var tampered in new[] { loaded with { ReviewId = Guid.NewGuid() }, loaded with { CreatedAt = loaded.CreatedAt.AddSeconds(1), ExpiresAt = loaded.ExpiresAt.AddSeconds(1) }, loaded with { Review = review with { Changes = Array.Empty<CommandChange>() } } })
            Assert.Throws<InvalidOperationException>(() => CommandReviewStore.Claim(tampered, request, review, envelope.ApprovalHash, folder.Root, CancellationToken.None));
        var another = CommandReviewStore.Create(request, review);
        Assert.Throws<InvalidOperationException>(() => CommandReviewStore.Claim(another, request, review, envelope.ApprovalHash, folder.Root, CancellationToken.None));
        Assert.Single(Directory.GetFiles(folder.Root, "*.claimed.json"));
    }
    [Fact]
    public void StaleReviewAndCancellationNeverClaimAnApproval()
    {
        using var folder = new Temp(); var request = Request; var review = Review(request); var envelope = CommandReviewStore.Create(request, review);
        Assert.Throws<InvalidOperationException>(() => CommandReviewStore.Claim(envelope, request, Review(request, "new source"), envelope.ApprovalHash, folder.Root, CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => CommandReviewStore.Claim(envelope, request with { Value = "different" }, review, envelope.ApprovalHash, folder.Root, CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => CommandReviewStore.Validate(envelope with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }));
        Assert.Throws<OperationCanceledException>(() => CommandReviewStore.Claim(envelope, request, review, envelope.ApprovalHash, folder.Root, new CancellationToken(true)));
        Assert.Empty(Directory.GetFiles(folder.Root));
    }
    [Fact]
    public void SavedReviewRejectsDuplicateNestedFields()
    {
        using var folder = new Temp(); var envelope = CommandReviewStore.Create(Request, Review(Request));
        var text = CommandJson.Serialize(envelope).Replace("\"canApply\": true", "\"canApply\": false, \"canApply\": true");
        var path = Path.Combine(folder.Root, "review.json"); File.WriteAllText(path, text);
        Assert.Throws<InvalidDataException>(() => CommandReviewStore.Load(path));
    }
    [Fact]
    public void ModelCatalogPublishesClosedSchemasAndAnExplicitWorkingExample()
    {
        using var catalog = JsonDocument.Parse(CommandSchema.Export(true)); var operations = catalog.RootElement.GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(9, operations.Length);
        foreach (var operation in operations)
        {
            Assert.False(operation.GetProperty("remoteWrite").GetBoolean()); var schema = operation.GetProperty("inputSchema");
            Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.GetProperty("$schema").GetString());
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean()); Assert.False(schema.GetProperty("properties").TryGetProperty("target", out _));
        }
        var request = CommandSchema.ParseModelRequest(catalog.RootElement.GetProperty("proposalExample").GetRawText());
        Assert.Equal(RecipeScope.Measure, Assert.Single(request.Recipe!.Steps).Target.Scope);
    }
    [Theory]
    [InlineData("{\"version\":1,\"kind\":\"Inspect\",\"target\":{\"modelPath\":\"secret.bim\"}}")]
    [InlineData("{\"version\":1,\"kind\":\"Query\",\"query\":\"EVALUATE {1}\",\"approve\":\"hash\"}")]
    [InlineData("{\"version\":1,\"kind\":\"Query\",\"query\":\"\"}")]
    [InlineData("{\"version\":1,\"kind\":\"Get\",\"selection\":[]}")]
    [InlineData("{\"version\":1,\"kind\":\"Script\"}")]
    public void ModelProposalsCannotRouteApplyOrBypassRequiredBounds(string json) => Assert.Throws<InvalidDataException>(() => CommandSchema.ParseModelRequest(json));
    [Fact]
    public void ModelRecipeCannotExpandAnExplicitProposalToAllObjects()
    {
        using var catalog = JsonDocument.Parse(CommandSchema.Export(true)); var json = catalog.RootElement.GetProperty("proposalExample").GetRawText();
        Assert.Throws<InvalidDataException>(() => CommandSchema.ParseModelRequest(json.Replace("\"Measure\"", "\"AllMeasures\"")));
        Assert.Throws<InvalidDataException>(() => CommandSchema.ParseModelRequest(json.Replace("Reviewed revenue measure", new string('x', 32001))));
    }
    private sealed class Temp : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "PbiBench-command-contract-" + Guid.NewGuid().ToString("N"));
        internal Temp() => Directory.CreateDirectory(Root);
        public void Dispose() { var full = Path.GetFullPath(Root); if (Path.GetDirectoryName(full) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(full).StartsWith("PbiBench-command-contract-", StringComparison.Ordinal)) throw new InvalidOperationException(); PbiBench.Workspace.WorkspaceDiskStore.RejectLinks(full); Directory.Delete(full, true); }
    }
}
