using System.Text.Json;
using PbiBench.Core.Agent;
using PbiBench.Core.Automation;
using PbiBench.Core.Quality;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class AgentProposalTests
{
    [Fact]
    public void ExplicitRecipeRoundTripsWithoutApprovalOrExecutionFields()
    {
        var json = AgentProposalJson.Serialize(Action()); var parsed = AgentProposalJson.Parse(json);
        Assert.Equal("DisplayFolder", Assert.Single(parsed.Recipe!.Steps).Property); Assert.DoesNotContain("approved", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RecipeScope.Measure, parsed.Recipe.Steps[0].Target.Scope);
    }
    [Theory]
    [InlineData("\"version\": 1", "\"version\": 1, \"approved\": true")]
    [InlineData("\"version\": 1", "\"version\": 1, \"version\": 1")]
    [InlineData("\"kind\": \"Action\"", "\"kind\": 2")]
    [InlineData("\"kind\": \"Action\"", "\"kind\": \"ExecuteShell\"")]
    [InlineData("\"operation\": \"SetProperty\"", "\"operation\": \"TrustedLegacy\"")]
    [InlineData("\"property\": \"DisplayFolder\"", "\"property\": \"ConnectionString\"")]
    [InlineData("\"scope\": \"Measure\"", "\"scope\": \"AllMeasures\"")]
    [InlineData("\"scope\": \"Measure\"", "\"scope\": \"SelectedMeasures\"")]
    [InlineData("\"kind\": \"Literal\"", "\"kind\": \"ObjectName\"")]
    [InlineData("\"query\": null,", "")]
    public void UnsupportedHiddenAndAmbiguousInputsAreRejected(string before, string after)
    { Assert.ThrowsAny<Exception>(() => AgentProposalJson.Parse(AgentProposalJson.Serialize(Action()).Replace(before, after))); }
    [Fact]
    public void ADeclaredReviewCannotHideARecipeOrQuery()
    {
        Assert.Throws<InvalidDataException>(() => AgentProposalJson.Validate(Action() with { Kind = AgentProposalKind.Review }));
        Assert.Throws<InvalidDataException>(() => AgentProposalJson.Validate(Action() with { Query = "EVALUATE ROW(\"x\",1)" }));
    }
    [Fact]
    public void TestProposalStagesVersionedTypedScalarArtifact()
    {
        var proposal = new AgentProposal(1, AgentProposalKind.Test, "Total remains positive", "Draft expected-value assertion; no result is claimed.", null, null,
            new("Total", "EVALUATE ROW(\"Value\",[Total])", SemanticComparison.GreaterThan, SemanticValue.From(0)));
        var parsed = AgentProposalJson.Parse(AgentProposalJson.Serialize(proposal)); var artifact = parsed.ToTestArtifact(); var test = Assert.Single(artifact.Tests);
        Assert.Equal(SemanticTestKind.Scalar, test.Kind); Assert.Equal(1000, test.RowLimit); Assert.Equal(SemanticComparison.GreaterThan, test.Comparison); Assert.Null(test.Snapshot);
    }
    [Fact]
    public void ProposalAndContextHaveWholeDocumentBudgets()
    {
        Assert.Throws<InvalidDataException>(() => AgentProposalJson.Parse(new string(' ', AgentProposalJson.MaximumBytes + 1)));
        var many = Action().Recipe! with { Steps = Enumerable.Repeat(Action().Recipe!.Steps[0], 101).ToArray() };
        Assert.Throws<InvalidDataException>(() => AgentProposalJson.Validate(Action() with { Recipe = many }));
        Assert.Throws<ArgumentException>(() => new AgentContextDocument(Guid.NewGuid(), "fingerprint", "{\"value\":\"" + new string('x', AgentContextDocument.MaximumBytes) + "\"}"));
    }
    [Fact]
    public void ContextHashCoversExactlyTheImmutableSharingText()
    {
        var one = new AgentContextDocument(Guid.NewGuid(), "local fingerprint", "{\"sections\":[]}");
        var two = new AgentContextDocument(Guid.NewGuid(), "other fingerprint", one.SharedJson);
        Assert.Equal(one.Hash, two.Hash); Assert.DoesNotContain("fingerprint", one.SharedJson);
        Assert.NotEqual(one.Hash, new AgentContextDocument(Guid.NewGuid(), "local fingerprint", "{\"sections\":[1]}").Hash);
    }
    [Fact]
    public void PublicStructuredOutputSchemaMakesEveryObjectStrictAndEveryPropertyRequired()
    {
        using var schema = JsonDocument.Parse(AgentProposalJson.SchemaJson); var objects = 0;
        void Walk(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("type", out var type) && type.GetString() == "object")
                {
                    objects++; Assert.False(value.GetProperty("additionalProperties").GetBoolean());
                    Assert.Equal(value.GetProperty("properties").EnumerateObject().Select(property => property.Name).OrderBy(name => name), value.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).OrderBy(name => name));
                }
                foreach (var property in value.EnumerateObject()) Walk(property.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Walk(item);
        }
        Walk(schema.RootElement); Assert.True(objects >= 8);
    }
    private static AgentProposal Action() => new(1, AgentProposalKind.Action, "Folder proposal", "Set a reviewed literal folder.",
        new ActionRecipe("Organize", new[] { new RecipeStep(new(RecipeScope.Measure, "Sales", "Total"), RecipeOperation.SetProperty, "DisplayFolder", RecipeValue.Literal("Measures")) }), null, null);
}
