using System.Text;
using System.Text.Json.Nodes;
using PbiBench.Pbir;
using Xunit;

namespace PbiBench.V2.Tests;

public sealed class ReportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PbiBench-V2-" + Guid.NewGuid().ToString("N"));
    private readonly ReportValidator validator = new();
    private string ReportRoot => Path.Combine(root, "Sales.Report");
    private string VisualFile => "definition/pages/overview/visuals/revenue/visual.json";
    public ReportTests()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "fixture");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { var target = Path.Combine(root, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
    }
    public void Dispose() => Directory.Delete(root, true);
    private Task<ReportIndex> Open() => ReportIndex.OpenAsync(ReportRoot, default);
    [Fact] public async Task OpensAllSupportedRoutesAndValidatesOffline()
    {
        foreach (var route in new[] { root, ReportRoot, Path.Combine(root, "Sales.pbip"), Path.Combine(ReportRoot, "definition.pbir") })
        { var report = await ReportIndex.OpenAsync(route, default); Assert.Empty(validator.Validate(report)); Assert.Single(report.Pages); Assert.Single(report.Pages[0].Visuals); Assert.Equal("4.0", report.Version); }
    }
    [Theory] [InlineData("title")] [InlineData("annotation")] [InlineData("page")] [InlineData("visual")] [InlineData("mapping")]
    public async Task TypedActionsPreviewExactFilesApplyAndRestoreOriginalBytes(string action)
    {
        var visualPath = Path.Combine(ReportRoot, VisualFile); var original = File.ReadAllText(visualPath);
        // Prove BOM and CRLF survive a complete backup/restore, not just equivalent JSON.
        File.WriteAllText(visualPath, original.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(true));
        var report = await Open(); var engine = new ReportChangeEngine(validator); var actions = new ReportActions(engine);
        var plan = action switch {
            "title" => actions.SetTitle(report, VisualFile, "Revenue 'reviewed'"), "annotation" => actions.Annotate(report, VisualFile, "Review", "Ready"),
            "page" => actions.DuplicatePage(report, "overview", "Copy"), "visual" => actions.DuplicateVisual(report, VisualFile, "overview"),
            _ => actions.ReplaceReference(report, new("Sales", "Revenue", "Measure"), new("Sales", "Revenue adjusted", "Measure")) };
        Assert.True(plan.CanApply, string.Join("\n", plan.Validation)); Assert.NotEmpty(plan.Changes);
        Assert.Equal(report.Files[VisualFile].Hash, (await Open()).Files[VisualFile].Hash);
        foreach (var change in plan.Changes) { Assert.Contains(change.Path, change.ExactDiff); Assert.Contains(change.AfterHash!, change.ExactDiff); }
        var approved = plan.Approve(plan.Id); var applied = await engine.ApplyAsync(approved, default);
        Assert.Empty(applied.Validation); await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(approved, default));
        var after = await Open(); Assert.Contains(after.Files.Values, f => !report.Files.TryGetValue(f.Path, out var old) || f.Hash != old.Hash);
        if (action == "mapping") { var refs = ReportLineage.Build(after); Assert.All(refs, r => Assert.Equal("Revenue adjusted", r.Name)); Assert.Contains("Sales.Revenue adjusted", after.Files[VisualFile].Text); }
        var restore = await engine.PreviewRestoreAsync(ReportRoot, applied.BackupManifest, default); Assert.True(restore.CanApply);
        await engine.ApplyAsync(restore.Approve(restore.Id), default);
        var restored = await Open(); Assert.Equal(report.Files.Count, restored.Files.Count);
        foreach (var file in report.Files) Assert.Equal(file.Value.Bytes(), restored.Files[file.Key].Bytes());
    }
    [Fact] public async Task RejectsStaleUntouchedFileBeforeWriting()
    {
        var report = await Open(); var engine = new ReportChangeEngine(validator); var plan = new ReportActions(engine).SetTitle(report, VisualFile, "New title");
        File.AppendAllText(Path.Combine(ReportRoot, "definition/report.json"), "\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(plan.Approve(plan.Id), default));
        Assert.Equal(report.Files[VisualFile].Hash, (await Open()).Files[VisualFile].Hash);
        Assert.False(Directory.Exists(Path.Combine(ReportRoot, ".pbibench/report-backups")));
    }
    [Fact] public async Task RestoreRefusesLaterEditsAndTamperedBackups()
    {
        var report = await Open(); var engine = new ReportChangeEngine(validator); var plan = new ReportActions(engine).SetTitle(report, VisualFile, "New title");
        var result = await engine.ApplyAsync(plan.Approve(plan.Id), default); var current = File.ReadAllText(Path.Combine(ReportRoot, VisualFile));
        File.AppendAllText(Path.Combine(ReportRoot, VisualFile), "\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PreviewRestoreAsync(ReportRoot, result.BackupManifest, default));
        File.WriteAllText(Path.Combine(ReportRoot, VisualFile), current); var manifest = JsonNode.Parse(File.ReadAllText(result.BackupManifest))!;
        manifest["Entries"]![0]!["Before"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")); File.WriteAllText(result.BackupManifest, manifest.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.PreviewRestoreAsync(ReportRoot, result.BackupManifest, default));
    }
    [Fact] public async Task UnknownSchemasAndPropertiesArePreservedAndBlockWrites()
    {
        var path = Path.Combine(ReportRoot, VisualFile); var json = JsonNode.Parse(File.ReadAllText(path))!; json["futureProperty"] = new JsonObject { ["valuable"] = 42 };
        File.WriteAllText(path, json.ToJsonString()); var report = await Open(); var actions = new ReportActions(new(validator)); var plan = actions.SetTitle(report, VisualFile, "New");
        Assert.False(plan.CanApply); Assert.Contains("futureProperty", plan.Changes[0].AfterText);
        json["$schema"] = "https://example.invalid/future/schema.json"; File.WriteAllText(path, json.ToJsonString());
        Assert.Contains(validator.Validate(await Open()), i => i.Message.Contains("Unknown or missing schema"));
    }
    [Fact] public async Task NestedSchemaViolationIsDetected()
    {
        var path = Path.Combine(ReportRoot, VisualFile); var json = JsonNode.Parse(File.ReadAllText(path))!; json["position"]!["width"] = "invalid"; File.WriteAllText(path, json.ToJsonString());
        Assert.Contains(validator.Validate(await Open()), i => i.Severity == "Error" && i.File == VisualFile);
    }
    [Fact] public async Task LineageDistinguishesBrokenUnverifiedAndLiteralValues()
    {
        var report = await Open(); var local = await ReportLineage.ReadLocalModelAsync(report.SemanticModelPath, default);
        Assert.True(local.Complete); Assert.Contains(ReportLineage.Build(report, local.Fields, local.Complete), u => u.Status == "Resolved" && u.Kind == "Measure" && u.Name == "Revenue");
        Assert.All(ReportLineage.Build(report), u => Assert.StartsWith("Unverified", u.Status));
        Assert.All(ReportLineage.Build(report, Array.Empty<SemanticField>(), true), u => Assert.Equal("Broken reference", u.Status));
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(ReportRoot, VisualFile)))!;
        json["annotations"] = new JsonArray(new JsonObject { ["name"] = "Measure", ["value"] = "Missing measure" }); File.WriteAllText(Path.Combine(ReportRoot, VisualFile), json.ToJsonString());
        Assert.Single(ReportLineage.Build(await Open()));
    }
    [Fact] public async Task RejectsTraversalAndExcludesLocalCaches()
    {
        Directory.CreateDirectory(Path.Combine(ReportRoot, ".pbi")); File.WriteAllText(Path.Combine(ReportRoot, ".pbi/localSettings.json"), "private-value");
        var report = await Open(); Assert.DoesNotContain(report.Files.Keys, f => f.Contains(".pbi/")); Assert.DoesNotContain("private-value", ReportActions.Inventory(report));
        var project = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "Sales.pbip")))!; project["artifacts"]![0]!["report"]!["path"] = "../../escape.Report";
        File.WriteAllText(Path.Combine(root, "Sales.pbip"), project.ToJsonString()); await Assert.ThrowsAsync<InvalidDataException>(() => ReportIndex.OpenAsync(Path.Combine(root, "Sales.pbip"), default));
    }
    [Fact] public async Task DuplicateJsonPropertiesAreExplicitValidationErrors()
    {
        File.WriteAllText(Path.Combine(ReportRoot, VisualFile), "{\"name\":\"a\",\"name\":\"b\"}"); var report = await Open(); Assert.Contains(validator.Validate(report), i => i.Message.Contains("Duplicate"));
    }
    [Fact] public async Task CancellationAndWrongApprovalCannotWrite()
    {
        var report = await Open(); var engine = new ReportChangeEngine(validator); var plan = new ReportActions(engine).SetTitle(report, VisualFile, "New");
        Assert.Throws<InvalidOperationException>(() => plan.Approve(Guid.NewGuid()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ApplyAsync(plan.Approve(plan.Id), new CancellationToken(true)));
        Assert.Equal(report.Files[VisualFile].Hash, (await Open()).Files[VisualFile].Hash);
    }
    [Fact] public async Task AFileLockMidApplyRollsBackEarlierFiles()
    {
        var report = await Open(); var engine = new ReportChangeEngine(validator); var plan = new ReportActions(engine).DuplicatePage(report, "overview", "Copy");
        using var locked = new FileStream(Path.Combine(ReportRoot, "definition/pages/pages.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        await Assert.ThrowsAnyAsync<IOException>(() => engine.ApplyAsync(plan.Approve(plan.Id), default));
        var restored = await Open(); Assert.Equal(report.Files.Count, restored.Files.Count); foreach (var file in report.Files) Assert.Equal(file.Value.Hash, restored.Files[file.Key].Hash);
        Assert.True(File.Exists(Path.Combine(ReportRoot, ".pbibench/report-backups", plan.Id.ToString("N"), "manifest.json")));
    }
    [Fact] public async Task CrossReportCopyChecksSourceHashAndRestoresTarget()
    {
        var targetRoot = Path.Combine(root, "Other.Report");
        foreach (var file in Directory.EnumerateFiles(ReportRoot, "*", SearchOption.AllDirectories)) { var target = Path.Combine(targetRoot, Path.GetRelativePath(ReportRoot, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
        var source = await Open(); var targetReport = await ReportIndex.OpenAsync(targetRoot, default); var engine = new ReportChangeEngine(validator); var actions = new ReportActions(engine);
        var stale = actions.CopyVisual(source, VisualFile, targetReport, "overview"); File.AppendAllText(Path.Combine(ReportRoot, VisualFile), "\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(stale.Approve(stale.Id), default));
        var plan = actions.CopyVisual(await Open(), VisualFile, targetReport, "overview"); var result = await engine.ApplyAsync(plan.Approve(plan.Id), default);
        Assert.Equal(2, (await ReportIndex.OpenAsync(targetRoot, default)).Pages[0].Visuals.Count);
        var restore = await engine.PreviewRestoreAsync(targetRoot, result.BackupManifest, default); await engine.ApplyAsync(restore.Approve(restore.Id), default);
        Assert.Single((await ReportIndex.OpenAsync(targetRoot, default)).Pages[0].Visuals);
    }
    [Fact] public async Task MalformedVisualShapeCanStillBeInspectedReadOnly()
    {
        var path = Path.Combine(ReportRoot, VisualFile); var json = JsonNode.Parse(File.ReadAllText(path))!; json["visual"] = 3; File.WriteAllText(path, json.ToJsonString());
        var report = await Open(); Assert.Contains(validator.Validate(report), i => i.File == VisualFile && i.Severity == "Error"); Assert.Contains("3", report.Files[VisualFile].Text);
    }
    [Fact] public async Task InventoryCannotOverwriteDefinitionsOrExistingFiles()
    {
        var report = await Open(); var path = Path.Combine(root, "inventory.json"); File.WriteAllText(path, "precious");
        await Assert.ThrowsAsync<IOException>(() => ReportActions.ExportInventoryAsync(report, path, default)); Assert.Equal("precious", File.ReadAllText(path));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReportActions.ExportInventoryAsync(report, Path.Combine(ReportRoot, VisualFile), default));
        await ReportActions.ExportInventoryAsync(report, Path.Combine(root, "new-inventory.json"), default);
        Assert.Equal(report.Files[VisualFile].Hash, (await Open()).Files[VisualFile].Hash);
    }
    [Fact] public async Task AliasResolutionIsScopedAndMissingAliasesAreUnresolved()
    {
        var query = "{\"From\":[{\"Name\":\"s\",\"Entity\":\"Sales\"}],\"Select\":[{\"Measure\":{\"Expression\":{\"SourceRef\":{\"Source\":\"s\"}},\"Property\":\"Revenue\"}},{\"Column\":{\"Expression\":{\"SourceRef\":{\"Source\":\"missing\"}},\"Property\":\"Amount\"}}]}";
        File.WriteAllText(Path.Combine(ReportRoot, "definition/query.json"), query);
        var references = ReportLineage.Build(await Open()).Where(r => r.File.EndsWith("query.json")).ToArray();
        Assert.Contains(references, r => r.Name == "Revenue" && r.Table == "Sales"); Assert.Contains(references, r => r.Name == "Amount" && r.Status == "Unresolved source alias");
    }
}
