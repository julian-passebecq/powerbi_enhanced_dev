using System.Text.Json.Nodes;
using PbiBench.AI.ContextExport;
using PbiBench.DesignExchange;
using PbiBench.ExternalTools;
using Xunit;

namespace PbiBench.DesignExchange.Tests;

public sealed class ContractTests
{
    [Fact] public void ThemeSchemaAndLicenseMatchTheirPinnedHashes()
    {
        var root = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); while (root != null && !File.Exists(Path.Combine(root.FullName, "PbiBench.slnx"))) root = root.Parent;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "schemas/report-theme.lock.json")));
        foreach (var entry in doc.RootElement.GetProperty("files").EnumerateArray())
        {
            using var sha = System.Security.Cryptography.SHA256.Create(); var bytes = File.ReadAllBytes(Path.Combine(root.FullName, "schemas/report-theme", entry.GetProperty("path").GetString()!));
            Assert.Equal(entry.GetProperty("sha256").GetString(), string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2"))));
        }
        Assert.Contains("MIT", File.ReadAllText(Path.Combine(root.FullName, "schemas/report-theme/LICENSE")));
    }
    public static ModelContext Model() => ModelContext.Create(new("Sales", 1600, new[] {
        new ContextObject(ContextModel.ObjectId("Table", null, "Sales"), "Table", "Sales", Expression: "SECRET PARTITION"),
        new ContextObject(ContextModel.ObjectId("Column", "Sales", "Amount"), "Column", "Amount", "Sales", DataType: "Decimal"),
        new ContextObject(ContextModel.ObjectId("Measure", "Sales", "Revenue"), "Measure", "Revenue", "Sales", Expression: "SUM(Sales[Amount])", FormatString: "$0")
    }, Array.Empty<ContextRelationship>(), Array.Empty<ContextDependency>()) { Roles = new[] { new ContextRole("Sensitive", "Sales", "SECRET ROLE") } });
    public static string Spec(ModelContext? context = null) => ContractJson.Serialize(new DashboardSpec(1, new("Revenue overview", "Executive"),
        new[] { new DesignPage("summary", "Executive Summary", new(1280, 720), new[] { new DesignVisual("revenue", "card", new Dictionary<string, DesignBinding> { ["value"] = new("Measure", "Sales", "Revenue") }, Region: "top") }) }, (context ?? Model()).ModelFingerprint));
    [Fact] public void MetadataRoundTripOmitsSensitiveChannelsAndHasStableFingerprint()
    {
        var model = Model(); var text = model.ToJson(); var roundtrip = ModelContext.Parse(text);
        Assert.Equal(model.ModelFingerprint, roundtrip.ModelFingerprint); Assert.Contains("SUM(Sales[Amount])", text);
        foreach (var excluded in new[] { "SECRET", "roles", "samples", "credentials", "connectionString", "accessToken", "partitions", "gateway", "pbipRoot" }) Assert.DoesNotContain(excluded, text);
        var unordered = new ContextModel("Sales", 1600, model.Model.Objects.Reverse().ToArray(), model.Model.Relationships, Array.Empty<ContextDependency>());
        Assert.Equal(model.ModelFingerprint, ModelContext.Create(unordered).ModelFingerprint);
        var changed = JsonNode.Parse(text)!; changed["model"]!["name"] = "Changed"; Assert.Throws<InvalidDataException>(() => ModelContext.Parse(changed.ToJsonString()));
    }
    [Theory] [InlineData("credentials")] [InlineData("accessToken")] [InlineData("partitionSource")] [InlineData("rows")]
    public void UnknownModelFieldsFailClosed(string key)
    { var json = JsonNode.Parse(Model().ToJson())!; json["model"]![key] = "secret"; Assert.Throws<InvalidDataException>(() => ModelContext.Parse(json.ToJsonString())); }
    [Fact] public void ValidDashboardHasExactBindingEvidence()
    { var result = DashboardValidator.Validate(Spec(), Model()); Assert.True(result.IsValid); Assert.Equal("Valid", Assert.Single(result.Bindings).Status); }
    [Theory] [InlineData("script")] [InlineData("expression")] [InlineData("dax")] [InlineData("url")] [InlineData("command")]
    public void ExecutableAndUnknownFieldsAreNeverAccepted(string field)
    { var json = JsonNode.Parse(Spec())!; json["pages"]![0]!["visuals"]![0]![field] = "RunSomething()"; Assert.False(DashboardValidator.Validate(json.ToJsonString(), Model()).IsValid); }
    [Theory] [InlineData("Revenue", "Column")] [InlineData("Invented", "Measure")]
    public void MissingObjectsAndWrongKindsAreExplicit(string name, string kind)
    { var json = JsonNode.Parse(Spec())!; var binding = json["pages"]![0]!["visuals"]![0]!["bindings"]!["value"]!; binding["name"] = name; binding["kind"] = kind;
        var result = DashboardValidator.Validate(json.ToJsonString(), Model()); Assert.False(result.IsValid); Assert.Contains("Invalid", Assert.Single(result.Bindings).Status); }
    [Fact] public void FingerprintMismatchAndExplicitUnboundRemainDistinct()
    {
        var json = JsonNode.Parse(Spec())!; json["modelFingerprint"] = "sha256:wrong"; Assert.False(DashboardValidator.Validate(json.ToJsonString(), Model()).IsValid);
        json["unbound"] = true; Assert.False(DashboardValidator.Validate(json.ToJsonString(), Model()).IsValid);
        json.AsObject().Remove("modelFingerprint"); var result = DashboardValidator.Validate(json.ToJsonString(), Model()); Assert.True(result.IsValid); Assert.Contains("Unverified", result.Bindings[0].Status);
    }
    [Fact] public void DuplicateVisualsEvenAcrossPagesFail()
    {
        var json = JsonNode.Parse(Spec())!; var page = json["pages"]![0]!.DeepClone(); page["id"] = "second"; json["pages"]!.AsArray().Add(page);
        Assert.False(DashboardValidator.Validate(json.ToJsonString(), Model()).IsValid);
    }
    [Fact] public void UnsupportedKindsAreVisibleAsWarningsWithoutInventingPbir()
    { var json = JsonNode.Parse(Spec())!; json["pages"]![0]!["visuals"]![0]!["kind"] = "customThing"; var result = DashboardValidator.Validate(json.ToJsonString(), Model()); Assert.True(result.IsValid); Assert.Contains(result.Diagnostics, d => d.Severity == "Warning" && d.Message.Contains("Unsupported")); }
    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(8193)]
    public void CanvasBoundsFail(double width)
    { var json = JsonNode.Parse(Spec())!; json["pages"]![0]!["canvas"]!["width"] = width; Assert.False(DashboardValidator.Validate(json.ToJsonString(), Model()).IsValid); }
    [Fact] public void CountsCoordinatesRequiredFieldsAndNullsFailClosed()
    {
        foreach (var mutation in new Action<JsonNode>[] {
            j => j["pages"] = null,
            j => j["pages"]![0]!["canvas"] = null,
            j => j["pages"]![0]!["canvas"]!.AsObject().Remove("width"),
            j => j["pages"]![0]!["visuals"]![0]!["bindings"] = null,
            j => j["pages"]![0]!["visuals"]![0]!["region"] = null,
            j => j["pages"]![0]!["visuals"]![0]!["position"] = JsonNode.Parse("{\"x\":1280,\"y\":0,\"width\":100,\"height\":100}"),
            j => { for (int i=0;i<32;i++) j["pages"]!.AsArray().Add(j["pages"]![0]!.DeepClone()); }
        }) { var json = JsonNode.Parse(Spec())!; mutation(json); Assert.False(DashboardValidator.Validate(json.ToJsonString(), Model()).IsValid); }
        Assert.False(DashboardValidator.Validate(Spec().Replace("1280", "1e9999"), Model()).IsValid);
    }
    [Fact] public void DuplicateKeysSizeLimitsAndDeepJsonFailClosed()
    {
        Assert.False(DashboardValidator.Validate(Spec().Replace("\"contractVersion\": 1", "\"contractVersion\": 1, \"ContractVersion\": 1"), Model()).IsValid);
        Assert.Throws<InvalidDataException>(() => ContractJson.Document(new string('x', ContractJson.MaximumBytes + 1)));
        Assert.Throws<InvalidDataException>(() => ContractJson.Document(new string('[', 40) + "0" + new string(']', 40)));
    }
    [Theory] [InlineData("{\"name\":\"Test\",\"dataColors\":[\"#315DA8\",\"#555555\"]}", true)]
    [InlineData("{\"dataColors\":[\"#315DA8\"]}", false)]
    [InlineData("{\"name\":\"Test\",\"unknown\":true}", false)]
    [InlineData("{\"name\":\"Test\",\"dataColors\":42}", false)]
    [InlineData("{\"name\":\"Test\",\"dataColors\":[\"not-a-color\"]}", false)]
    [InlineData("{\"name\":\"Test\",\"name\":\"Duplicate\"}", false)]
    public void ThemeUsesPinnedSchema(string json, bool valid)
    { var result = new ThemeValidator().Validate(json); Assert.Equal(valid, result.IsValid); Assert.Contains("2.156", result.SchemaVersion); }
    [Fact] public void ThemeSchemaUrlNeverFetchesAndReportsVersionWarning()
    { var result = new ThemeValidator().Validate("{\"name\":\"Offline\",\"$schema\":\"https://127.0.0.1/must-not-fetch\"}"); Assert.True(result.IsValid); Assert.Contains(result.Diagnostics, d => d.Severity == "Warning"); }
    [Fact] public void OpenThemeFormattingObjectsStillDiagnoseNewProperties()
    {
        var result = new ThemeValidator().Validate("{\"name\":\"Custom\",\"visualStyles\":{\"card\":{\"*\":{\"labels\":[{\"futureProperty\":true}]}}}}");
        Assert.Contains(result.Diagnostics, d => d.Location.Contains("futureProperty") && d.Severity == "Warning");
    }
    [Fact] public async Task FileHandoffRevalidatesChangedBytesAndNeverMutatesInput()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var model = Path.Combine(root, "model.json"); var spec = Path.Combine(root, "spec.json"); await Model().SaveAsync(model, default); File.WriteAllText(spec, Spec());
            var before = File.ReadAllText(spec); var package = await DesignPackage.LoadAsync(model, spec, null, default); Assert.True(package.IsValid); Assert.Equal(before, File.ReadAllText(spec));
            File.WriteAllText(spec, "{}"); Assert.False((await DesignPackage.LoadAsync(model, spec, null, default)).IsValid);
            using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DesignPackage.LoadAsync(model, spec, null, canceled.Token));
        }
        finally { Directory.Delete(root, true); }
    }
}
