using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PbiBench.Core.Automation;
using PbiBench.Core.Quality;

namespace PbiBench.Core.Agent;

public enum AgentProposalKind { Explanation, Review, Action, Query, Test }
public sealed record AgentTestProposal(string Name, string Query, SemanticComparison Comparison, SemanticValue Expected);
public sealed record AgentProposal(int Version, AgentProposalKind Kind, string Title, string Explanation,
    ActionRecipe? Recipe, string? Query, AgentTestProposal? Test)
{
    public SemanticTestArtifact ToTestArtifact()
    {
        AgentProposalJson.Validate(this);
        var test = Test ?? throw new InvalidOperationException("This proposal is not a semantic test.");
        return new(SemanticTestArtifact.CurrentVersion, new[] { new SemanticTestDefinition
        { Id = Guid.NewGuid().ToString("N"), Name = test.Name, Query = test.Query, Kind = SemanticTestKind.Scalar,
            Comparison = test.Comparison, Expected = test.Expected, RowLimit = 1000, TimeoutSeconds = 60 } });
    }
}
public sealed record AgentContextOptions(bool SelectedObjects = false, bool Inventory = false, bool CurrentDax = false,
    bool BpaFindings = false, bool WorkspaceDiff = false, bool TestResults = false, bool Capabilities = false);
public sealed record AgentContextFinding(string Rule, string ObjectPath, string Severity, string Reason);
public sealed record AgentContextDiff(string ObjectPath, string Property, string Before, string After);
public sealed record AgentContextTest(string Name, string Outcome, string Evidence);
public sealed record AgentContextExtras(string? CurrentDax = null, IReadOnlyList<AgentContextFinding>? Findings = null,
    IReadOnlyList<AgentContextDiff>? WorkspaceDiff = null, IReadOnlyList<AgentContextTest>? TestResults = null,
    IReadOnlyList<string>? Capabilities = null);

/// <summary>The exact displayed sharing payload is immutable. The local model fingerprint is never part of the provider payload.</summary>
public sealed class AgentContextDocument
{
    public const int MaximumBytes = 128 * 1024;
    public AgentContextDocument(Guid captureId, string modelFingerprint, string sharedJson)
    {
        if (captureId == Guid.Empty || modelFingerprint == null || sharedJson == null || Encoding.UTF8.GetByteCount(sharedJson) > MaximumBytes)
            throw new ArgumentException("Agent context is invalid or exceeds 128 KiB. Narrow the shared sections.");
        using var json = JsonDocument.Parse(sharedJson, new JsonDocumentOptions { MaxDepth = 16 });
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Agent context must be a JSON object.");
        CaptureId = captureId; ModelFingerprint = modelFingerprint; SharedJson = sharedJson;
        using var sha = SHA256.Create(); Hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(sharedJson)));
    }
    public Guid CaptureId { get; }
    public string ModelFingerprint { get; }
    public string SharedJson { get; }
    public string Hash { get; }
}
public sealed record AgentRequest(string Prompt, AgentContextDocument Context, bool ContextSharingApproved)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Prompt) || Prompt.Length > 16000 || Context == null)
            throw new ArgumentException("Enter a request of 1 to 16,000 characters and capture its context first.");
    }
}
public interface IAgentProvider
{
    string DisplayName { get; }
    bool IsOnline { get; }
    Task<AgentProposal> ProposeAsync(AgentRequest request, CancellationToken cancellationToken);
}
