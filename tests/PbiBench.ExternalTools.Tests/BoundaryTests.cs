using PbiBench.DaxStudio;
using PbiBench.ExternalTools;
using Xunit;

namespace PbiBench.ExternalTools.Tests;

public sealed class BoundaryTests
{
    [Theory] [InlineData("", "\"\"")] [InlineData("two words", "\"two words\"")] [InlineData("a\"b", "\"a\\\"b\"")] [InlineData("C:\\folder\\", "\"C:\\folder\\\\\"")]
    public void WindowsQuotingStaysStable(string input, string output) => Assert.Equal(output, WindowsCommandLine.Quote(input));
    [Fact] public void NulArgumentsFail() => Assert.Throws<ArgumentException>(() => WindowsCommandLine.Quote("bad\0argument"));
    [Fact] public void NeutralAssemblyHasNoRuntimeOrAuthDependency()
    { var names = typeof(CompanionTools).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray(); Assert.DoesNotContain(names, n => n!.StartsWith("PbiBench.") || n.Contains("AnalysisServices") || n.Contains("Identity")); }
    [Fact] public void HandoffRejectsUnknownDuplicateVersionAndMissingContext()
    {
        foreach (var args in new[] { new[] { "--contract-version", "2" }, new[] { "--contract-version", "1", "--token", "secret" }, new[] { "--contract-version", "1", "--contract-version", "1" }, new[] { "--theme", "theme.json" }, new[] { "--contract-version", "1", "--theme", Path.GetFullPath("theme.json") } })
            Assert.Throws<InvalidDataException>(() => ModuleHandoff.Parse(args, true));
    }
    [Fact] public void ProjectContextRejectsCredentialsAndUnsupportedVersion()
    {
        Assert.Throws<InvalidDataException>(() => ContractJson.Parse<ProjectContext>("{\"accessToken\":\"secret\"}"));
        Assert.Throws<InvalidDataException>(() => new ProjectContext(ContractVersion: 2).Validate());
        Assert.Throws<InvalidDataException>(() => new ProjectContext(FabricWorkspaceId: "secret").Validate());
        Assert.Throws<InvalidDataException>(() => new ProjectContext(PbipRoot: "../relative").Validate());
    }
    [Fact] public void ManifestDiscoveryFromChildFolderAndFocusUseTheNeutralAdapter()
    {
        using var fixture = new Fixture(); var exe = fixture.File("report-studio/module.exe"); var child = Path.GetDirectoryName(exe)!;
        var manifest = new ComponentsManifest(1, "2.3.0", 1, 1, new[] { new ProductComponent("semantic-ide", "2.3.0", "PbiBench.exe"), new ProductComponent("report-studio", "2.3.0", "report-studio/module.exe"), new ProductComponent("fabric-toolbox", "0.4.0", "fabric-toolbox/module.exe") });
        var path = fixture.File("components.json", ContractJson.Serialize(manifest)); var fake = new Capture(); var tools = new CompanionTools(fake);
        var report = tools.Discover(CompanionTools.Catalog.Single(t => t.Id == "report-studio"), null, child); Assert.Equal(exe, report.Path); Assert.Equal(path, ComponentsManifest.Find(child));
        var context = new ToolContext(ReportFile: fixture.File("definition.pbir")); tools.Launch(report, context); tools.Launch(report, context); Assert.Equal(2, fake.FocusCalls); Assert.Equal(0, fake.StartCalls);
        var invalid = tools.Discover(report.Tool, Path.Combine(fixture.Root, "absent.exe"), child); Assert.Null(invalid.Path);
        Assert.Throws<InvalidDataException>(() => ComponentsManifest.Parse(ContractJson.Serialize(manifest with { Components = manifest.Components.Select(c => c with { Path = "../escape.exe" }).ToArray() })));
    }
    [Fact] public void ExistingSpecialistAndDesktopArgumentsAreUnchanged()
    {
        using var fixture = new Fixture(); var exe = fixture.File("tool.exe"); var query = fixture.File("file with spaces.dax"); var bridge = new DaxStudioBridge(exe);
        Assert.Equal(new[] { "--server", "localhost:51234", "--database", "Model \"name\"", "--file", query }, bridge.CreateLaunchRequest("localhost:51234", "Model \"name\"", query).Arguments);
        var context = new ToolContext(ProjectDirectory: fixture.Root, ProjectFile: fixture.File("project.pbip"), ReportFile: fixture.File("definition.pbir"));
        ExternalToolStatus Status(string id) => new(CompanionTools.Catalog.Single(t => t.Id == id), exe, "1");
        Assert.Equal(new[] { context.ProjectFile! }, ExternalToolContext.Evaluate(Status("powerbi"), context).Arguments);
        Assert.Equal(new[] { fixture.Root }, ExternalToolContext.Evaluate(Status("vscode"), context).Arguments);
        Assert.Equal(new[] { "--contract-version", "1", "--report", context.ReportFile! }, ExternalToolContext.Evaluate(Status("report-studio"), context).Arguments);
    }
    [Fact] public void BrokenManifestFailsDiscoveryClosedAndRelativeConfiguredPathIsRejected()
    {
        using var fixture = new Fixture(); fixture.File("components.json", "{\"contractVersion\":2}"); fixture.File("report-studio/PbiBench.ReportStudio.exe");
        var tool = CompanionTools.Catalog.Single(t => t.Id == "report-studio"); var tools = new CompanionTools();
        var result = tools.Discover(tool, null, fixture.Root); Assert.Null(result.Path); Assert.NotNull(result.Diagnostic);
        Assert.Null(tools.Discover(tool, "relative.exe", fixture.Root).Path);
    }
    [Fact] public void DesignAndProjectFilesRoundtripThroughTheVersionedHandoff()
    {
        using var fixture = new Fixture(); var exe = fixture.File("tool.exe"); string model = fixture.File("model context.json"), spec = fixture.File("spec.json"), theme = fixture.File("theme.json"), project = fixture.File("project.json");
        var state = ExternalToolContext.Evaluate(new(CompanionTools.Catalog.Single(t => t.Id == "report-studio"), exe, "1"), new(ModelContextFile: model, DashboardSpecFile: spec, ThemeFile: theme, ProjectContextFile: project));
        Assert.True(state.Enabled); var handoff = ModuleHandoff.Parse(state.Arguments, true); Assert.Null(handoff.Report); Assert.Equal(model, handoff.ModelContext); Assert.Equal(spec, handoff.DashboardSpec); Assert.Equal(theme, handoff.Theme); Assert.Equal(project, handoff.ProjectContext);
    }
    private sealed class Capture : IProcessAdapter, IFocusProcessAdapter
    { public int FocusCalls, StartCalls; public void Start(ProcessLaunchRequest request) => StartCalls++; public void StartOrFocus(ProcessLaunchRequest request) => FocusCalls++; public Task<ProcessResult> RunAsync(ProcessLaunchRequest request, CancellationToken ct) => throw new NotSupportedException(); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string File(string relative, string content = "") { var path = Path.GetFullPath(Path.Combine(Root, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); System.IO.File.WriteAllText(path, content); return path; }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
