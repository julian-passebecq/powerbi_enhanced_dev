using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PbiBench.AI.ContextExport;
using Xunit;

namespace PbiBench.V11.Tests;

public sealed class ExportTests
{
    internal static string Id(string kind, string name, string? table = null) => ContextModel.ObjectId(kind, table, name);
    internal static ContextModel Model() => new("Fixture", 1600, new[]
    {
        new ContextObject(Id("Table", "Sales"), "Table", "Sales", Description: "Fact table", StorageMode: "Import"),
        new ContextObject(Id("Column", "Amount", "Sales"), "Column", "Amount", "Sales", DataType: "Decimal"),
        new ContextObject(Id("Column", "Private", "Sales"), "Column", "Private", "Sales", Hidden: true),
        new ContextObject(Id("Measure", "Revenue", "Sales"), "Measure", "Revenue", "Sales", Expression: "SUM('Sales'[Amount])"),
        new ContextObject(Id("Table", "Other"), "Table", "Other"),
        new ContextObject(Id("Column", "Key", "Other"), "Column", "Key", "Other")
    }, new[] { new ContextRelationship(Id("Relationship", "R"), "R", Id("Column", "Amount", "Sales"), Id("Column", "Key", "Other"), true, "Many", "One", "OneDirection") }, new[] { new ContextDependency(Id("Measure", "Revenue", "Sales"), Id("Column", "Amount", "Sales")) });
    [Fact] public async Task DefaultExportIsProviderFreeAndNeverSamples()
    {
        var sampler = new Sampler(); var plan = await ContextExporter.PrepareAsync(Model(), new(), sampler, CancellationToken.None);
        Assert.Equal(0, sampler.Calls); Assert.DoesNotContain(plan.Review, f => f.Path.StartsWith("samples/")); Assert.DoesNotContain(plan.Review, f => f.Path.EndsWith("roles.json"));
        Assert.Contains("SUM('Sales'[Amount])", plan.ReadText("model/measures.dax")); Assert.Contains("NOT anonymized", plan.ReadText("AI_README.md"));
    }
    [Fact] public async Task SelectedMeasureIncludesDependenciesButNotOtherTables()
    {
        var plan = await ContextExporter.PrepareAsync(Model(), new() { SelectedScope = true, SelectedIds = new[] { Id("Measure", "Revenue", "Sales") } }, null, default);
        Assert.Contains("Amount", plan.ReadText("model/columns.csv")); Assert.DoesNotContain("Private", plan.ReadText("model/columns.csv")); Assert.DoesNotContain("Other", plan.ReadText("model/tables.csv"));
    }
    [Fact] public async Task SelectedRelationshipIncludesOnlyEndpointsAndParents()
    {
        var plan = await ContextExporter.PrepareAsync(Model(), new() { SelectedScope = true, SelectedIds = new[] { Id("Relationship", "R") } }, null, default);
        Assert.Contains("Other", plan.ReadText("model/tables.csv")); Assert.Contains("Many", plan.ReadText("model/relationships.csv")); Assert.DoesNotContain("Revenue", plan.ReadText("model/measures.dax"));
    }
    [Fact] public async Task ExclusionsWinOverDependenciesAndTablesExcludeChildren()
    {
        var plan = await ContextExporter.PrepareAsync(Model(), new() { ExcludedIds = new[] { Id("Column", "Amount", "Sales"), Id("Table", "Other") } }, null, default);
        Assert.DoesNotContain("Amount", plan.ReadText("model/columns.csv")); Assert.DoesNotContain("Other", plan.ReadText("model/tables.csv")); Assert.Contains("unresolved", plan.ReadText("model/dependencies.json"));
    }
    [Theory] [InlineData(-1)] [InlineData(1001)] public async Task RejectsRowBoundsBeforeAnyQuery(int rows)
    {
        var sampler = new Sampler(); await Assert.ThrowsAsync<ArgumentException>(() => ContextExporter.PrepareAsync(Model(), new() { IncludeSamples = true, Samples = new[] { new SampleRequest("Sales", new[] { "Amount" }, rows) } }, sampler, default)); Assert.Equal(0, sampler.Calls);
    }
    [Theory] [InlineData("Private")] [InlineData("Missing")] public async Task HiddenAndUnknownColumnsFailClosed(string name)
    {
        var sampler = new Sampler(); await Assert.ThrowsAsync<ArgumentException>(() => ContextExporter.PrepareAsync(Model(), new() { IncludeSamples = true, Samples = new[] { new SampleRequest("Sales", new[] { name }) } }, sampler, default)); Assert.Equal(0, sampler.Calls);
    }
    [Fact] public async Task CellLimitsArePreflightedAcrossAllTables()
    {
        var sampler = new Sampler(); await Assert.ThrowsAsync<ArgumentException>(() => ContextExporter.PrepareAsync(Model(), new() { IncludeSamples = true, MaximumSampleCells = 5, Samples = new[] { new SampleRequest("Sales", new[] { "Amount" }, 5), new SampleRequest("Other", new[] { "Key" }, 5) } }, sampler, default)); Assert.Equal(0, sampler.Calls);
    }
    [Fact] public async Task SamplesUseExactProjectionInvariantCsvAndTransparentManifest()
    {
        var sampler = new Sampler(); var plan = await ContextExporter.PrepareAsync(Model(), new() { IncludeSamples = true, Samples = new[] { new SampleRequest("Sales", new[] { "Amount" }, 5) } }, sampler, default);
        var file = Assert.Single(plan.Review, f => f.Path.StartsWith("samples/")); Assert.Contains("12.5", plan.ReadText(file.Path)); Assert.Contains("FirstN", plan.ReadText("manifest.json")); Assert.Contains("\"anonymized\": false", plan.ReadText("manifest.json"));
    }
    [Fact] public async Task SamplerCannotReturnExtraColumnsOrRows()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => ContextExporter.PrepareAsync(Model(), new() { IncludeSamples = true, Samples = new[] { new SampleRequest("Sales", new[] { "Amount" }) } }, new Sampler { WrongColumns = true }, default));
    }
    [Fact] public async Task JsonRedactionPreservesValidEscapingAndDropsPathsAndObviousSecrets()
    {
        var model = Model(); model = model with { Objects = model.Objects.Select(o => o with { Description = "Path C:\\private\\model and password=topsecret; quoted \"business\"" }).ToArray() };
        var plan = await ContextExporter.PrepareAsync(model, new(), null, default);
        foreach (var file in plan.Review.Where(f => f.Path.EndsWith(".json"))) { using var doc = JsonDocument.Parse(plan.ReadText(file.Path)); Assert.DoesNotContain("topsecret", plan.ReadText(file.Path)); Assert.DoesNotContain("private", plan.ReadText(file.Path)); }
    }
    [Fact] public async Task ManifestCountsRedactionsPerFileIncludingItselfWithoutRetainingOriginals()
    {
        var model = Model() with { Name = "password=fixture-secret" };
        model = model with { Objects = model.Objects.Select(o => o.Kind == "Table" && o.Name == "Sales" ? o with { Description = @"Path C:\private\model and pwd=another-secret;" } : o).ToArray() };
        var plan = await ContextExporter.PrepareAsync(model, new(), null, default);
        using var manifest = JsonDocument.Parse(plan.ReadText("manifest.json"));
        Assert.Equal(2, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        var redaction = manifest.RootElement.GetProperty("redaction"); Assert.Equal(1, redaction.GetProperty("schemaVersion").GetInt32());
        Assert.True(redaction.GetProperty("applied").GetBoolean()); Assert.False(redaction.GetProperty("anonymized").GetBoolean());
        var files = redaction.GetProperty("files");
        Assert.Equal(3, files.GetProperty("model/model-summary.json").GetInt32());
        Assert.Equal(2, files.GetProperty("model/tables.csv").GetInt32());
        Assert.Equal(1, files.GetProperty("AI_README.md").GetInt32()); Assert.Equal(1, files.GetProperty("manifest.json").GetInt32());
        Assert.Equal(7, redaction.GetProperty("replacementCount").GetInt64());
        Assert.Equal(redaction.GetProperty("replacementCount").GetInt64(), files.EnumerateObject().Sum(p => p.Value.GetInt64()));
        foreach (var file in plan.Review)
        { var text = plan.ReadText(file.Path); Assert.DoesNotContain("fixture-secret", text); Assert.DoesNotContain("another-secret", text); Assert.DoesNotContain("private", text); }
        Assert.Equal(plan.Review, (await ContextExporter.PrepareAsync(model, new(), null, default)).Review);
    }
    [Fact] public async Task NoRedactionIsExplicitAndSamplesEvidenceAndDaxAreCountedWhenIncluded()
    {
        var clean = await ContextExporter.PrepareAsync(Model(), new(), null, default);
        using (var manifest = JsonDocument.Parse(clean.ReadText("manifest.json")))
        { var redaction = manifest.RootElement.GetProperty("redaction"); Assert.False(redaction.GetProperty("applied").GetBoolean()); Assert.Equal(0, redaction.GetProperty("replacementCount").GetInt64()); Assert.Empty(redaction.GetProperty("files").EnumerateObject()); }
        var model = Model(); model = model with { Objects = model.Objects.Select(o => o.Kind == "Measure" ? o with { Expression = "1 // api_key=dax-secret" } : o).ToArray() };
        var options = new ContextExportOptions { IncludeSamples = true, Samples = new[] { new SampleRequest("Sales", new[] { "Amount" }, 1) },
            Evidence = new[] { new ContextEvidence("BPA", Id("Table", "Sales"), "Finding", "Warning", "pwd=evidence-secret") } };
        var plan = await ContextExporter.PrepareAsync(model, options, new Sampler { Value = "password=sample-secret" }, default);
        using var doc = JsonDocument.Parse(plan.ReadText("manifest.json")); var counts = doc.RootElement.GetProperty("redaction").GetProperty("files");
        Assert.Equal(1, counts.GetProperty("model/measures.dax").GetInt32()); Assert.Equal(1, counts.GetProperty("quality/bpa.json").GetInt32());
        Assert.Equal(1, counts.GetProperty(Assert.Single(plan.Review, f => f.Path.StartsWith("samples/")).Path).GetInt32());
        Assert.All(plan.Review, file => Assert.DoesNotContain("-secret", plan.ReadText(file.Path)));
    }
    [Fact] public async Task CancellationAndSizeLimitsRejectWithoutCommitting()
    {
        using var ct = new CancellationTokenSource(); ct.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContextExporter.PrepareAsync(Model(), new(), null, ct.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => ContextExporter.PrepareAsync(Model(), new() { MaximumBytes = 4096 }, null, default));
        var plan = await ContextExporter.PrepareAsync(Model(), new(), null, default); var path = Path.GetTempFileName();
        try { File.WriteAllText(path, "existing"); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContextExporter.WriteAsync(plan, path, true, ct.Token)); Assert.Equal("existing", File.ReadAllText(path)); await Assert.ThrowsAsync<InvalidOperationException>(() => ContextExporter.WriteAsync(plan, path, false, default)); } finally { File.Delete(path); }
    }
    [Fact] public async Task ManifestChecksumsAndZipAreDeterministic()
    {
        var model = Model() with { Name = "Fixture password=redacted-value" };
        var a = await ContextExporter.PrepareAsync(model, new() { IncludeAutomation = true }, null, default); var b = await ContextExporter.PrepareAsync(model, new() { IncludeAutomation = true }, null, default);
        Assert.Equal(a.Review, b.Review); Assert.Contains("writableProperties", a.ReadText("automation/safe-script-capabilities.json"));
        var first = Path.GetTempFileName(); var second = Path.GetTempFileName();
        try
        {
            await ContextExporter.WriteAsync(a, first, true, default); await ContextExporter.WriteAsync(b, second, true, default); Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second)); Assert.True(new FileInfo(first).Length <= a.EstimatedBytes);
            using var stream = File.OpenRead(first); using var zip = new ZipArchive(stream);
            foreach (var entry in zip.Entries.Where(e => e.FullName != "checksums.sha256"))
            { using var bytes = new MemoryStream(); using (var source = entry.Open()) source.CopyTo(bytes); using var sha = SHA256.Create(); var hash = BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant(); Assert.Contains(hash + "  " + entry.FullName, a.ReadText("checksums.sha256")); }
        }
        finally { File.Delete(first); File.Delete(second); }
    }
    private sealed class Sampler : IContextSampler
    {
        public int Calls; public bool WrongColumns; public object Value = 12.5m;
        public Task<SampleResult> SampleAsync(SampleRequest request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(new SampleResult(WrongColumns ? new[] { "Other" } : request.Columns, new[] { new object?[] { Value } })); }
    }
}
