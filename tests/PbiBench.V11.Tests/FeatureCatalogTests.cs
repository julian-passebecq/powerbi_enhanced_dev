using System.Globalization;
using System.Text.Json;
using PbiBench.Core.Platform;
using Xunit;

namespace PbiBench.V11.Tests;

public sealed class FeatureCatalogTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string Serialize(FeatureCatalog value) => JsonSerializer.Serialize(value, Json);
    private static FeatureCatalog WithFeature(Func<CatalogFeature, CatalogFeature> change)
    { var catalog = FeatureCatalog.Bundled(); return catalog with { Features = new[] { change(catalog.Features[0]) } }; }

    [Fact] public void BundledCatalogHasTheAuditedVersionAndConservativePublicBaseline()
    {
        var provenance = ProvenanceCatalog.Bundled(); var catalog = FeatureCatalog.Bundled(provenance);
        Assert.Equal("11.2.0", catalog.ProductVersion); Assert.Equal("675a5026749094206339c312b7f190964953ce57", catalog.BaselineCommit);
        Assert.Equal("Tabular Editor 3", catalog.Comparison.Product); Assert.Equal("3.26.3", catalog.Comparison.VerifiedVersion); Assert.Equal("2026-09-05", catalog.Comparison.VerifiedDate);
        Assert.Equal(21, catalog.Features.Count); Assert.Equal(catalog.Features.Count, catalog.Features.Select(f => f.Id).Distinct().Count());
        Assert.All(catalog.Features, f => { Assert.Contains(f.Status, FeatureCatalog.Statuses); Assert.Contains(f.Focus, FeatureCatalog.Focuses); Assert.Contains(f.Te3.Comparison, FeatureCatalog.Comparisons); });
        Assert.All(catalog.Features.SelectMany(f => f.ProvenanceIds), id => Assert.Contains(provenance.Components, c => c.Id == id));
    }
    [Fact] public void ProductFocusPreservesFrozenAreasAndDistinguishesUnimplementedGaps()
    {
        var catalog = FeatureCatalog.Bundled(); var features = catalog.Features.ToDictionary(f => f.Id);
        foreach (var id in new[] { "semantic-ide", "dax-ide", "workspace" }) Assert.Equal("Heavy focus", features[id].Focus);
        Assert.Equal("Improve selectively", features["csharp-automation"].Focus); Assert.Equal("Improve", features["fabric-semantic"].Focus);
        Assert.Equal("Develop independently", features["fabric-toolbox"].Focus); Assert.Equal("Polish only", features["ai-context-export"].Focus);
        foreach (var id in new[] { "daxstudio", "cli-ci" }) Assert.Equal("Keep", features[id].Focus);
        foreach (var id in new[] { "dataforge", "embedded-agent", "semantic-compiler", "dax-packages", "pbir", "knowledge" }) Assert.Equal("Freeze", features[id].Focus);
        Assert.Equal("Gap", features["dax-debugger"].Status); Assert.Equal("Later", features["dax-debugger"].Focus); Assert.Equal("Gap", features["dax-debugger"].Te3.Comparison);
        Assert.All(catalog.Rows(ProvenanceCatalog.Bundled()).Where(r => r.Status is "Future" or "Gap"), row => { Assert.Equal("Not implemented", row.Origin); Assert.Empty(row.Components); });
    }
    [Fact] public void FiltersHaveExplicitSemanticsWithoutChangingTheCatalog()
    {
        var provenance = ProvenanceCatalog.Bundled(); var catalog = FeatureCatalog.Bundled();
        Assert.Equal(10, catalog.Rows(provenance, FeatureMapFilter.Core).Count);
        Assert.Equal(new[] { "fabric-toolbox", "daxstudio", "dataforge" }, catalog.Rows(provenance, FeatureMapFilter.Companions).Select(r => r.Feature.Id));
        Assert.Equal(6, catalog.Rows(provenance, FeatureMapFilter.Labs).Count);
        Assert.All(catalog.Rows(provenance, FeatureMapFilter.Te3Gaps), r => Assert.Contains(r.Feature.Te3.Comparison, new[] { "Partial", "Gap" }));
        Assert.Contains(catalog.Rows(provenance, FeatureMapFilter.Te3Gaps), r => r.Feature.Id == "dax-debugger");
        Assert.Equal(21, catalog.Rows(provenance).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.Rows(provenance, (FeatureMapFilter)999));
    }
    [Fact] public void OriginAndDetailFollowProvenanceRatherThanADuplicatedLedger()
    {
        var provenance = ProvenanceCatalog.Bundled(); var catalog = FeatureCatalog.Bundled();
        var before = catalog.Rows(provenance).Single(r => r.Feature.Id == "semantic-ide"); Assert.Contains("2.28.0", before.Origin);
        var changed = provenance with { Components = provenance.Components.Select(c => c.Id == "semantic.model-editor.te2" ? c with { Pin = "test-pin", UpdateLane = "test-lane", License = "test-license" } : c).ToArray() };
        var after = catalog.Rows(changed).Single(r => r.Feature.Id == "semantic-ide");
        Assert.Contains("test-pin", after.Origin); Assert.Contains("test-lane", after.Detail); Assert.Contains("test-license", catalog.ToMarkdown(changed));
        Assert.Equal("External: DAX Studio", catalog.Rows(provenance).Single(r => r.Feature.Id == "daxstudio").Origin);
        Assert.Equal("Microsoft APIs + PbiBench", catalog.Rows(provenance).Single(r => r.Feature.Id == "fabric-toolbox").Origin);
    }
    [Fact] public void CatalogProvenanceEmbeddedResourcesAndGeneratedDocumentationCannotDrift()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PbiBench.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory); var architecture = Path.Combine(directory!.FullName, "docs", "architecture");
        var provenance = ProvenanceCatalog.Parse(File.ReadAllText(Path.Combine(architecture, "provenance.json")));
        var catalog = FeatureCatalog.Parse(File.ReadAllText(Path.Combine(architecture, "feature_catalog.json")), provenance);
        Assert.Equal(JsonSerializer.Serialize(provenance), JsonSerializer.Serialize(ProvenanceCatalog.Bundled()));
        Assert.Equal(Serialize(catalog), Serialize(FeatureCatalog.Bundled()));
        Assert.Equal(catalog.ToMarkdown(provenance), File.ReadAllText(Path.Combine(architecture, "FEATURE_CATALOG.md")).Replace("\r\n", "\n"));
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo("tr-TR"); Assert.Equal(catalog.ToMarkdown(provenance), FeatureCatalog.Bundled().ToMarkdown(provenance)); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Theory] [InlineData("core", "Heavy focus", "Comparable")] [InlineData("Everything", "Heavy focus", "Comparable")]
    [InlineData("Core", "Expand all", "Comparable")] [InlineData("Core", "Heavy focus", "Full parity")]
    public void RejectsUnboundedEnumLabels(string status, string focus, string comparison)
    { Assert.Throws<InvalidDataException>(() => FeatureCatalog.Parse(Serialize(WithFeature(f => f with { Status = status, Focus = focus, Te3 = f.Te3 with { Comparison = comparison } })), ProvenanceCatalog.Bundled())); }
    [Theory] [InlineData("http://docs.tabulareditor.com/en/page.html")]
    [InlineData("https://docs.tabulareditor.com.evil.example/en/page.html")] [InlineData("https://docs.tabulareditor.com@evil.example/en/page.html")]
    [InlineData("https://cdn.tabulareditor.com/TabularEditor.exe")] [InlineData("file:///C:/TE3/private.html")]
    [InlineData("C:\\TE3\\private.html")] [InlineData("https://docs.tabulareditor.com/en/private.exe")]
    [InlineData("https://docs.tabulareditor.com/en/page.html?download=private")]
    [InlineData(null)]
    public void RejectsNonOfficialOrMissingComparisonEvidence(string? url)
    { Assert.Throws<InvalidDataException>(() => FeatureCatalog.Parse(Serialize(WithFeature(f => f with { Te3 = f.Te3 with { SourceUrl = url } })), ProvenanceCatalog.Bundled())); }
    [Fact] public void RejectsDuplicatesUnknownFieldsBrokenReferencesAndInvalidHeaders()
    {
        var provenance = ProvenanceCatalog.Bundled(); var catalog = FeatureCatalog.Bundled(); var json = Serialize(catalog);
        foreach (var invalid in new[] {
            catalog with { SchemaVersion = 2 }, catalog with { ProductVersion = "11.1.1" }, catalog with { BaselineCommit = new string('a', 40) },
            catalog with { Comparison = catalog.Comparison with { VerifiedDate = "2026-02-30" } },
            catalog with { Comparison = catalog.Comparison with { Product = "Other product" } },
            catalog with { Features = Array.Empty<CatalogFeature>() }, catalog with { Features = new[] { catalog.Features[0], catalog.Features[0] } },
            WithFeature(f => f with { ProvenanceIds = new[] { "missing.component" } }), WithFeature(f => f with { ProvenanceIds = Array.Empty<string>() }),
            WithFeature(f => f with { ProvenanceIds = new[] { "semantic.model-editor.te2", "semantic.model-editor.te2" } }),
            WithFeature(f => f with { Implementation = new string('x', 121) }), WithFeature(f => f with { Name = "Name\nsecond line" }),
            WithFeature(f => f with { Limitations = Array.Empty<string>() }) })
            Assert.Throws<InvalidDataException>(() => FeatureCatalog.Parse(Serialize(invalid), provenance));
        Assert.Throws<InvalidDataException>(() => FeatureCatalog.Parse(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), provenance));
        Assert.Throws<InvalidDataException>(() => FeatureCatalog.Parse(json.Replace("\"status\":\"Core\"", "\"status\":\"Core\",\"license\":\"forged second ledger\""), provenance));
        Assert.Throws<InvalidDataException>(() => FeatureCatalog.Parse(new string('日', 90000), provenance));
        Assert.Throws<InvalidDataException>(() => FeatureCatalog.Parse("{}", provenance));
        Assert.Throws<InvalidDataException>(() => FeatureCatalog.Parse("null", provenance));
    }
}
