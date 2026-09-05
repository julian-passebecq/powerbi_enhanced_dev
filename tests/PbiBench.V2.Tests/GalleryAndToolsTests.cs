using PbiBench.CSharp.LanguageService;
using PbiBench.DaxStudio;
using Xunit;

namespace PbiBench.V2.Tests;

public sealed class GalleryAndToolsTests
{
    [Fact] public void CuratedGalleryIsNativeFirstAndEveryRecipeGeneratesBoundedSource()
    {
        Assert.True(PowerBiGallery.All.Count >= 10); Assert.True(PowerBiGallery.All.Count(c => c.Mode == "SAFE RECIPE") >= 6);
        foreach (var card in PowerBiGallery.All)
        {
            Assert.NotEmpty(card.Risk); Assert.NotEmpty(card.License); Assert.StartsWith("https://", card.Source);
            if (card.Mode != "SAFE RECIPE") continue;
            var kind = card.Selection.Split('/')[0];
            var recipe = PowerBiGallery.Generate(card, new[] { new AutomationSymbol(kind, "A_\"B", kind == "Table" ? null : "T'able", true, "Decimal") }, new Dictionary<string, string>());
            Assert.Single(recipe.Steps); Assert.NotEmpty(PbiBench.Core.Automation.RecipeCSharpGenerator.Generate(recipe).Source);
            Assert.Throws<ArgumentException>(() => PowerBiGallery.Generate(card, Array.Empty<AutomationSymbol>(), new Dictionary<string, string>()));
        }
    }
    [Fact] public void GalleryRejectsUnsupportedAggregationAndNumericSelection()
    {
        var card = PowerBiGallery.All.Single(c => c.Id == "explicit");
        Assert.Throws<ArgumentException>(() => PowerBiGallery.Generate(card, new[] { new AutomationSymbol("Column", "Text", "Sales", true, "String") }, new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => PowerBiGallery.Generate(card, new[] { new AutomationSymbol("Column", "Amount", "Sales", true, "Decimal") }, new Dictionary<string, string> { ["Aggregation"] = "SHELL" }));
    }
    [Fact] public void BravoOnlyReceivesSupportedServerAndDatabaseArguments()
    {
        var path = Path.GetTempFileName();
        try
        {
            var status = new CompanionStatus(CompanionTools.Catalog.Single(t => t.Id == "bravo"), path, "test"); var fake = new Capture();
            new CompanionTools(fake).Launch(status, new ToolContext("localhost:51234", "Model \"name\""));
            Assert.Equal(new[] { "--server=localhost:51234", "--database=Model \"name\"" }, fake.Request!.Arguments);
            Assert.False(ExternalToolContext.Evaluate(status, new()).Enabled);
            Assert.False(ExternalToolContext.Evaluate(status, new("Data Source=x;Password=secret", "db")).Enabled);
        }
        finally { File.Delete(path); }
    }
    [Fact] public void ProjectLaunchersUseKnownFilesAndReportHandoffIsVersioned()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "tool.exe"); File.WriteAllText(exe, ""); var pbip = Path.Combine(root, "project.pbip"); File.WriteAllText(pbip, "{}"); var pbir = Path.Combine(root, "definition.pbir"); File.WriteAllText(pbir, "{}");
            CompanionStatus Status(string id) => new(CompanionTools.Catalog.Single(t => t.Id == id), exe, "test");
            var context = new ToolContext(ProjectDirectory: root, ProjectFile: pbip, ReportFile: pbir, PageId: "page1", VisualId: "visual1");
            Assert.Equal(new[] { pbip }, ExternalToolContext.Evaluate(Status("powerbi"), context).Arguments);
            Assert.Equal(new[] { pbir }, ExternalToolContext.Evaluate(Status("powerbi"), context with { ProjectFile = null }).Arguments);
            Assert.False(ExternalToolContext.Evaluate(Status("powerbi"), new()).Enabled);
            Assert.Equal(new[] { root }, ExternalToolContext.Evaluate(Status("vscode"), context).Arguments);
            Assert.Equal(new[] { "--contract-version", "1", "--report", pbir, "--page", "page1", "--visual", "visual1" }, ExternalToolContext.Evaluate(Status("report-studio"), context).Arguments);
            Assert.False(ExternalToolContext.Evaluate(Status("report-studio"), context with { ProjectFile = null, ReportFile = null }).Enabled);
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Capture : IProcessAdapter
    {
        public ProcessLaunchRequest? Request { get; private set; }
        public void Start(ProcessLaunchRequest request) => Request = request;
        public Task<ProcessResult> RunAsync(ProcessLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
}
