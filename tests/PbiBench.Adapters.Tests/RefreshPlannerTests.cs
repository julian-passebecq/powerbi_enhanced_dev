using System.Text.Json;
using PbiBench.Core.Domain;
using PbiBench.Core.Refresh;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class RefreshPlannerTests
{
    public static RefreshMetadataSnapshot Metadata(string mode = "Import", RefreshSourceKind source = RefreshSourceKind.M, bool policy = false) => new("server", "database-id", "Model", 1702, "fingerprint", true, false, true,
        new[] { new RefreshTableMetadata("Sales", policy, new[] { new RefreshPartitionMetadata("Current", mode, source, source == RefreshSourceKind.Query ? "Existing source" : null) }), new RefreshTableMetadata("Dates", false, new[] { new RefreshPartitionMetadata("Dates", "Import", RefreshSourceKind.Calculated) }) });
    [Fact]
    public void SequenceReferencesDatabaseNameWhileApprovalBindsIdentityAndExplicitParallelism()
    {
        var model = Metadata() with { DatabaseId = "stable-id", DatabaseName = "name\"quoted" }; var plan = RefreshPlanner.Build(model, new() { Objects = new[] { new RefreshObject("Sales", "Current") }, MaxParallelism = 3 });
        using var json = JsonDocument.Parse(plan.Tmsl); var sequence = json.RootElement.GetProperty("sequence"); Assert.Equal(3, sequence.GetProperty("maxParallelism").GetInt32());
        var operations = sequence.GetProperty("operations"); Assert.Equal(1, operations.GetArrayLength()); var refresh = operations[0].GetProperty("refresh"); Assert.Equal("full", refresh.GetProperty("type").GetString());
        Assert.Equal("name\"quoted", refresh.GetProperty("objects")[0].GetProperty("database").GetString()); Assert.Equal("stable-id", plan.Metadata.DatabaseId); Assert.Equal("Current", refresh.GetProperty("objects")[0].GetProperty("partition").GetString());
        Assert.Equal(ApprovalLevel.RemoteModelWrite, plan.ChangePlan.RequiredApproval); Assert.True(plan.CanExecute); Assert.DoesNotContain("connectionString", plan.Tmsl);
    }
    [Theory]
    [InlineData(RefreshKind.Full, "full")]
    [InlineData(RefreshKind.ClearValues, "clearValues")]
    [InlineData(RefreshKind.Calculate, "calculate")]
    [InlineData(RefreshKind.DataOnly, "dataOnly")]
    [InlineData(RefreshKind.Automatic, "automatic")]
    [InlineData(RefreshKind.Defragment, "defragment")]
    public void DocumentedModelTypesProduceTheirExactTmslName(RefreshKind kind, string expected)
    { var plan = RefreshPlanner.Build(Metadata(), new() { Kind = kind }); Assert.True(plan.CanExecute); using var json = JsonDocument.Parse(plan.Tmsl); Assert.Equal(expected, json.RootElement.GetProperty("sequence").GetProperty("operations")[0].GetProperty("refresh").GetProperty("type").GetString()); }
    [Fact]
    public void AddAndDefragmentRespectTheirDifferentObjectAndSourceScopes()
    {
        var partition = new RefreshRequest { Kind = RefreshKind.Add, Objects = new[] { new RefreshObject("Sales", "Current") } };
        Assert.True(RefreshPlanner.Build(Metadata(), partition).CanExecute); Assert.False(RefreshPlanner.Build(Metadata(), partition with { Objects = new[] { new RefreshObject("Sales") } }).CanExecute);
        Assert.False(RefreshPlanner.Build(Metadata(source: RefreshSourceKind.Calculated), partition).CanExecute); Assert.False(RefreshPlanner.Build(Metadata(source: RefreshSourceKind.None), partition).CanExecute);
        Assert.False(RefreshPlanner.Build(Metadata(), partition with { Kind = RefreshKind.Defragment }).CanExecute); Assert.True(RefreshPlanner.Build(Metadata(), partition with { Kind = RefreshKind.Defragment, Objects = new[] { new RefreshObject() } }).CanExecute);
    }
    [Fact]
    public void OverlappingDuplicateAndStaleScopesNeverBecomeExecutable()
    {
        foreach (var scopes in new[] { new[] { new RefreshObject(), new RefreshObject("Sales") }, new[] { new RefreshObject("Sales"), new RefreshObject("Sales", "Current") },
            new[] { new RefreshObject("Sales"), new RefreshObject("Sales") }, new[] { new RefreshObject("Missing") }, new[] { new RefreshObject("Sales", "Missing") }, new[] { new RefreshObject(null, "Current") } })
            Assert.False(RefreshPlanner.Build(Metadata(), new() { Objects = scopes }).CanExecute);
        Assert.True(RefreshPlanner.Build(Metadata(), new() { Objects = new[] { new RefreshObject("Sales", "Current"), new RefreshObject("Dates") } }).CanExecute);
    }
    [Fact]
    public void UnsavedOfflineOldCompatibilityAndInvalidLimitsBlockExecution()
    {
        foreach (var metadata in new[] { Metadata() with { IsConnected = false }, Metadata() with { HasUnsavedChanges = true }, Metadata() with { CompatibilityLevel = 1100 } }) Assert.False(RefreshPlanner.Build(metadata, new()).CanExecute);
        Assert.False(RefreshPlanner.Build(Metadata(), new() { MaxParallelism = 0 }).CanExecute); Assert.False(RefreshPlanner.Build(Metadata(), new() { TimeoutSeconds = 86401 }).CanExecute);
        Assert.False(RefreshPlanner.Build(Metadata(), new() { Kind = (RefreshKind)999 }).CanExecute);
    }
    [Theory]
    [InlineData("DirectLake", RefreshKind.DataOnly)]
    [InlineData("DirectLake", RefreshKind.ClearValues)]
    [InlineData("DirectQuery", RefreshKind.Add)]
    [InlineData("DirectQuery", RefreshKind.Defragment)]
    public void StorageModeIncompatibleOperationsAreNotPresentedAsSupported(string mode, RefreshKind kind)
    { Assert.False(RefreshPlanner.Build(Metadata(mode), new() { Kind = kind, Objects = new[] { new RefreshObject("Sales", "Current") } }).CanExecute); }
    [Fact]
    public void IncrementalPolicyDefaultsAndEffectiveDateAreVisibleAndRestricted()
    {
        var plan = RefreshPlanner.Build(Metadata(policy: true), new() { ApplyRefreshPolicy = true, EffectiveDate = new DateTime(2026, 9, 1) }); Assert.True(plan.CanExecute); Assert.Contains(plan.Issues, issue => issue.Code == "POLICY"); Assert.Contains("2026-09-01", plan.Tmsl);
        Assert.False(RefreshPlanner.Build(Metadata(policy: true), new() { ApplyRefreshPolicy = false, EffectiveDate = new DateTime(2026, 9, 1) }).CanExecute);
        Assert.False(RefreshPlanner.Build(Metadata(policy: true) with { IsPowerBi = false }, new() { ApplyRefreshPolicy = false }).CanExecute);
        Assert.False(RefreshPlanner.Build(Metadata(), new() { EffectiveDate = new DateTime(2026, 9, 1) }).CanExecute);
        Assert.False(RefreshPlanner.Build(Metadata(policy: true), new() { Kind = RefreshKind.Calculate, EffectiveDate = new DateTime(2026, 9, 1) }).CanExecute);
        Assert.Contains(RefreshPlanner.Build(Metadata(policy: true), new() { ApplyRefreshPolicy = false }).Issues, issue => issue.Code == "POLICY_DISABLED");
    }
    [Theory]
    [InlineData(RefreshSourceKind.M)]
    [InlineData(RefreshSourceKind.Query)]
    public void DevelopmentOverridesRetainSourceTypeAndBindingWithoutEditingMetadata(RefreshSourceKind source)
    {
        var metadata = Metadata(source: source); var expression = source == RefreshSourceKind.M ? "let Source = #table({\"ID\"}, {{1}}) in Source" : "SELECT 1 AS ID";
        var plan = RefreshPlanner.Build(metadata, new() { Objects = new[] { new RefreshObject("Sales", "Current") }, SourceOverrides = new[] { new RefreshSourceOverride("Sales", "Current", source, expression) } });
        Assert.True(plan.CanExecute); using var json = JsonDocument.Parse(plan.Tmsl); var binding = json.RootElement.GetProperty("sequence").GetProperty("operations")[0].GetProperty("refresh").GetProperty("overrides")[0].GetProperty("partitions")[0];
        Assert.Equal("Model", binding.GetProperty("originalObject").GetProperty("database").GetString()); Assert.Equal(source == RefreshSourceKind.M ? "m" : "query", binding.GetProperty("source").GetProperty("type").GetString());
        Assert.Equal(expression, binding.GetProperty("source").GetProperty(source == RefreshSourceKind.M ? "expression" : "query").GetString());
        if (source == RefreshSourceKind.Query) Assert.Equal("Existing source", binding.GetProperty("source").GetProperty("dataSource").GetString());
        Assert.Equal(source, metadata.Tables[0].Partitions[0].SourceKind); Assert.DoesNotContain("dataSources", plan.Tmsl);
    }
    [Fact]
    public void OverridesCannotConvertSourcesEscapeScopesOrSilentlyOverridePolicyCreatedPartitions()
    {
        var request = new RefreshRequest { Objects = new[] { new RefreshObject("Sales", "Current") }, SourceOverrides = new[] { new RefreshSourceOverride("Sales", "Current", RefreshSourceKind.M, "let a = 1 in a") } };
        Assert.False(RefreshPlanner.Build(Metadata(source: RefreshSourceKind.Query), request).CanExecute);
        Assert.False(RefreshPlanner.Build(Metadata(), request with { Objects = new[] { new RefreshObject("Dates") } }).CanExecute);
        Assert.False(RefreshPlanner.Build(Metadata(policy: true), request).CanExecute); Assert.True(RefreshPlanner.Build(Metadata(policy: true), request with { ApplyRefreshPolicy = false }).CanExecute);
        Assert.False(RefreshPlanner.Build(Metadata("DirectLake"), request).CanExecute); Assert.False(RefreshPlanner.Build(Metadata(), request with { Kind = RefreshKind.Calculate }).CanExecute);
    }
    [Fact]
    public void PlansFreezeInputsAndRequireExactUnreplayedTargetBoundApproval()
    {
        var scopes = new[] { new RefreshObject("Sales") }; var metadata = Metadata(); var plan = RefreshPlanner.Build(metadata, new() { Objects = scopes }); scopes[0] = new("Dates"); Assert.Equal("Sales", plan.Request.Objects[0].Table);
        var approval = new ApprovedChangePlan(plan.ChangePlan, DateTimeOffset.UtcNow, "fixture"); var target = new RefreshConnection("server", "database-id");
        Assert.Throws<InvalidOperationException>(() => plan.ValidateApproval(approval with { Plan = plan.ChangePlan with { Changes = Array.Empty<PlannedChange>() } }, target));
        Assert.Throws<InvalidOperationException>(() => plan.ValidateApproval(approval, target with { Server = "different" }));
        Assert.Throws<InvalidOperationException>(() => plan.ValidateApproval(approval with { ApprovalActor = "" }, target));
        plan.ClaimExecution(approval, target); Assert.False(plan.CanExecute); Assert.Throws<InvalidOperationException>(() => plan.ClaimExecution(approval, target));
    }
    [Fact]
    public async Task ProfilesRoundTripOnlyTypedRequestsAndRejectArbitraryCommandsOrTransportFields()
    {
        var profile = new RefreshDevelopmentProfile(1, "Development", new() { Objects = new[] { new RefreshObject("Sales", "Current"), new RefreshObject("Dates") }, ApplyRefreshPolicy = false });
        var json = RefreshProfileStore.Serialize(profile); var loaded = RefreshProfileStore.Deserialize(json); Assert.Equal(profile.Request.Objects, loaded.Request.Objects);
        Assert.DoesNotContain("Server", json); Assert.DoesNotContain("ConnectionString", json);
        Assert.Throws<InvalidDataException>(() => RefreshProfileStore.Deserialize(json.Insert(1, "\"Tmsl\":\"delete\",")));
        Assert.Throws<InvalidDataException>(() => RefreshProfileStore.Deserialize("{\"FormatVersion\":1,\"Name\":\"Unsafe\",\"Request\":{}}"));
        var directory = Path.Combine(Path.GetTempPath(), "PbiBench-refresh-" + Guid.NewGuid().ToString("N")); var path = Path.Combine(directory, "profile.json");
        try { await RefreshProfileStore.SaveAsync(path, profile, CancellationToken.None); await RefreshProfileStore.SaveAsync(path, profile, CancellationToken.None); Assert.Equal(profile.Name, (await RefreshProfileStore.LoadAsync(path, CancellationToken.None)).Name); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
