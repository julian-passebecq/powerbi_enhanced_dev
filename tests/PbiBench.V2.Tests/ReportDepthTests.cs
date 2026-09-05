using System.Text.Json;
using System.Text.Json.Nodes;
using PbiBench.Pbir;
using Xunit;

namespace PbiBench.V2.Tests;

public sealed class ReportDepthTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "report-depth-" + Guid.NewGuid().ToString("N"));
    private const string Visual = "definition/pages/overview/visuals/revenue/visual.json";
    private const string Bookmark = "definition/bookmarks/first.bookmark.json";
    private string Folder => Path.Combine(root, "Sales.Report");
    private readonly ReportValidator validator = new();
    public ReportDepthTests()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "fixture");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        { var target = Path.Combine(root, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
    }
    public void Dispose() => Directory.Delete(root, true);
    private Task<ReportIndex> Open() => ReportIndex.OpenAsync(Folder, default);
    private void Edit(string file, Action<JsonObject> change)
    { var path = Path.Combine(Folder, file); var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); change(json); File.WriteAllText(path, json.ToJsonString()); }
    private void AddBookmark()
    {
        Directory.CreateDirectory(Path.Combine(Folder, "definition/bookmarks"));
        File.WriteAllText(Path.Combine(Folder, Bookmark), """
            {"$schema":"https://developer.microsoft.com/json-schemas/fabric/item/report/definition/bookmark/1.0.0/schema.json","name":"first","displayName":"First","explorationState":{"version":"1.0","activeSection":"overview","sections":{}}}
            """);
    }
    [Theory] [InlineData("4.0", true)] [InlineData("4.1", false)] [InlineData("5.0", false)] [InlineData("1.0", false)]
    public async Task OnlyExplicitlyPinnedVersionSchemaContractsEnableWrites(string version, bool allowed)
    {
        Edit("definition.pbir", json => json["version"] = version); var report = await Open();
        var plan = new ReportActions(new(validator)).SetTitle(report, Visual, "Version test"); Assert.Equal(allowed, plan.CanApply);
        if (!allowed) Assert.Contains(plan.Validation, i => i.Message.Contains("pbir-write-policy.json"));
        Edit("definition/version.json", json => json["version"] = "99.0.0"); Assert.Contains(validator.Validate(await Open()), i => i.Message.Contains("version/schema contract"));
    }
    [Theory] [InlineData("1.0.0")] [InlineData("2.0.0")]
    public async Task EachListedDefinitionPropertiesSchemaIsRegressionTested(string schema)
    {
        Edit("definition.pbir", json => json["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/" + schema + "/schema.json");
        Assert.Empty(validator.Validate(await Open()));
    }
    [Theory] [InlineData("offset")] [InlineData("batch")] [InlineData("bookmark-rename")] [InlineData("bookmark-copy")] [InlineData("display")]
    public async Task DepthActionsRoundTripExactBytesThroughReviewedRecovery(string action)
    {
        AddBookmark(); Edit(Visual, json => json["visual"]!["query"]!["queryState"]!["Values"]!["projections"]![0]!["displayName"] = "Revenue label");
        var report = await Open(); var engine = new ReportChangeEngine(validator); var actions = new ReportActions(engine);
        var manifest = new DisplayNameManifest(new[] { new DisplayNameMapping(new("Sales", "Revenue", "Measure"), "New label", report.Name, "overview", "revenue") });
        var plan = action switch
        {
            "offset" => actions.DuplicateVisual(report, Visual, "overview", 15, 20),
            "batch" => actions.BatchVisualProperties(report, new[] { Visual }, true, false, "Reviewed 'batch'"),
            "bookmark-rename" => actions.EditBookmark(report, Bookmark, "Reviewed bookmark", false),
            "bookmark-copy" => actions.EditBookmark(report, Bookmark, "Copy", true),
            _ => actions.ApplyDisplayNames(report, manifest)
        };
        Assert.True(plan.CanApply, string.Join("\n", plan.Validation)); Assert.NotEmpty(plan.Changes);
        Assert.Throws<InvalidOperationException>(() => plan.Approve(Guid.NewGuid()));
        var result = await engine.ApplyAsync(plan.Approve(plan.Id), default); var after = await Open();
        if (action == "batch") { Assert.True(after.Pages[0].Visuals[0].Hidden); Assert.Equal("Reviewed 'batch'", after.Pages[0].Visuals[0].Title); }
        if (action == "offset") Assert.Contains(after.Pages[0].Visuals, v => v.X == report.Pages[0].Visuals[0].X + 15 && v.Y == report.Pages[0].Visuals[0].Y + 20);
        if (action == "display") Assert.Equal("New label", DisplayNameManifest.Extract(after).Mappings.Single().DisplayName);
        var restore = await engine.PreviewRestoreAsync(Folder, result.BackupManifest, default); await engine.ApplyAsync(restore.Approve(restore.Id), default);
        var restored = await Open(); Assert.Equal(report.Files.Count, restored.Files.Count); foreach (var file in report.Files) Assert.Equal(file.Value.Bytes(), restored.Files[file.Key].Bytes());
    }
    [Fact] public async Task ActionsRejectUnboundedSelectionOffsetsAndUnknownBookmarkSchemas()
    {
        AddBookmark(); var report = await Open(); var actions = new ReportActions(new(validator));
        Assert.Throws<ArgumentException>(() => actions.BatchVisualProperties(report, Array.Empty<string>(), true, null, null));
        Assert.Throws<ArgumentException>(() => actions.BatchVisualProperties(report, Enumerable.Repeat(Visual, 201), true, null, null));
        Assert.Throws<ArgumentException>(() => actions.BatchVisualProperties(report, new[] { Visual, Visual }, true, null, null));
        Assert.Throws<ArgumentException>(() => actions.BatchVisualProperties(report, new[] { "definition/report.json" }, true, null, null));
        Assert.Throws<ArgumentException>(() => actions.DuplicateVisual(report, Visual, "overview", double.NaN));
        Assert.Throws<ArgumentException>(() => actions.DuplicateVisual(report, Visual, "overview", 4097));
        Assert.Throws<ArgumentException>(() => actions.DuplicateVisual(report, Visual, "overview", -4000));
        Edit(Bookmark, json => json["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/bookmark/1.5.0/schema.json");
        report = await Open(); Assert.Throws<InvalidOperationException>(() => actions.EditBookmark(report, Bookmark, "Unknown", true));
    }
    [Fact] public async Task ViewCachesLineageSearchAndOccurrenceCountsForSnapshot()
    {
        var report = await Open(); var catalog = await ReportLineage.ReadLocalModelAsync(report.SemanticModelPath, default); var view = new ReportViewSnapshot(report, catalog, validator.Validate(report));
        var page = report.Pages[0]; var visual = page.Visuals[0]; var cached = view.ForFile(visual.File);
        foreach (var term in new[] { page.Name, page.Id, visual.Type, visual.Title, visual.Id, "Sales[Revenue]" }) Assert.True(view.Matches(page, visual, term));
        Assert.False(view.Matches(page, visual, "not present")); Assert.Same(cached, view.ForFile(visual.File));
        Assert.Equal(new ReportOccurrenceImpact(1, 1, 1, 1), view.Impact(new("Sales", "Revenue", "Measure")));
        Assert.Contains("unverified", new ReportViewSnapshot(report, new(Array.Empty<SemanticField>(), false, "partial"), Array.Empty<ReportIssue>()).Badges(Visual));
        Assert.Contains("broken", new ReportViewSnapshot(report, new(Array.Empty<SemanticField>(), true, "complete"), Array.Empty<ReportIssue>()).Badges(Visual));
        Edit(Visual, json => json["isHidden"] = true); Assert.DoesNotContain("hidden", view.Badges(Visual));
        Assert.Contains("hidden", new ReportViewSnapshot(await Open(), catalog, Array.Empty<ReportIssue>()).Badges(Visual));
    }
    [Fact] public async Task BookmarkIdentityAndPageReferencesAreValidatedBeforeAnyWrite()
    {
        AddBookmark(); Edit(Bookmark, json => { json["name"] = "mismatch"; json["explorationState"]!["activeSection"] = "absent"; });
        var report = await Open(); var plan = new ReportActions(new(validator)).EditBookmark(report, Bookmark, "Review", false);
        Assert.False(plan.CanApply); Assert.Contains(plan.Validation, i => i.Message.Contains("Bookmark ID")); Assert.Contains(plan.Validation, i => i.Message.Contains("missing page"));
    }
    [Fact] public async Task MultiReportCandidatesAndImpactHandoffNeverWriteReports()
    {
        var other = Path.Combine(root, "Other.Report"); Directory.CreateDirectory(other); File.Copy(Path.Combine(Folder, "definition.pbir"), Path.Combine(other, "definition.pbir"));
        var project = Path.Combine(root, "multi.pbip"); File.WriteAllText(project, "{\"artifacts\":[{\"report\":{\"path\":\"Sales.Report\"}},{\"report\":{\"path\":\"Other.Report\"}}]}");
        Assert.Equal(2, (await ReportIndex.CandidatesAsync(project, default)).Count); Assert.Equal(2, (await ReportIndex.CandidatesAsync(root, default)).Count);
        var report = await Open(); var handoff = new ReportImpactHandoff("Rename", new("Sales", "Revenue", "Measure"), new("Sales", "Revenue2", "Measure"), new[] { report });
        Assert.Single(handoff.Usages); Assert.Equal(report.Files[Visual].Hash, handoff.Files.Single().Hash); Assert.Contains("not an atomic", handoff.Recovery);
        var path = Path.Combine(root, "impact.json"); await handoff.SaveAsync(path, default); Assert.Contains("\"Version\": 1", File.ReadAllText(path));
        Assert.Equal(report.Files[Visual].Hash, (await Open()).Files[Visual].Hash);
    }
    [Fact] public async Task DisplayNameBridgeRejectsConflictsStaleRowsAndUnknownContracts()
    {
        var row = new DisplayNameMapping(new("Sales", "Revenue", "Measure"), "Friendly", "Sales.Report", "overview", "revenue");
        Assert.Throws<InvalidDataException>(() => new DisplayNameManifest(new[] { row, row with { DisplayName = "Different" } }));
        var manifest = new DisplayNameManifest(new[] { row }); Assert.Equal(manifest.Mappings, DisplayNameManifest.Parse(manifest.ToJson()).Mappings);
        Assert.Throws<InvalidDataException>(() => DisplayNameManifest.Parse(manifest.ToJson().Replace("\"Version\": 1", "\"Version\": 2")));
        Assert.Throws<InvalidDataException>(() => DisplayNameManifest.Parse(manifest.ToJson().Replace("\"Version\": 1", "\"token\":\"secret\",\"Version\":1")));
        var report = await Open(); Assert.Throws<InvalidOperationException>(() => new ReportActions(new(validator)).ApplyDisplayNames(report, new(new[] { row with { Visual = "absent" } })));
    }
    [Fact] public async Task LegacyDefinitionIsBrowsableAndCannotProduceAnApplicablePlan()
    {
        var folder = Path.Combine(root, "Legacy.Report"); Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(Folder, "definition.pbir"), Path.Combine(folder, "definition.pbir")); File.WriteAllText(Path.Combine(folder, "report.json"), "{\"sections\":[]}");
        var report = await ReportIndex.OpenAsync(folder, default); Assert.False(report.Enhanced); Assert.Contains("sections", report.Files["report.json"].Text);
        Assert.Contains(validator.Validate(report), i => i.Message.Contains("PBIR-Legacy is read-only"));
    }
    [Fact] public void PolicyNamesTheExactPinnedSchemaBundle()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PbiBench.slnx"))) dir = dir.Parent;
        using var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "schemas/pbir-write-policy.json")));
        using var bundle = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, "schemas/microsoft.lock.json")));
        Assert.Equal(bundle.RootElement.GetProperty("commit").GetString(), policy.RootElement.GetProperty("schemaBundleCommit").GetString());
    }
}
