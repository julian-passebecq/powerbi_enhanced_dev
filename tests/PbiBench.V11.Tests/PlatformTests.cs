using System.Net;
using System.Net.Http;
using System.Text.Json;
using PbiBench.Core.Abstractions;
using PbiBench.Core.Fabric;
using PbiBench.Core.Platform;
using PbiBench.DaxStudio;
using PbiBench.Fabric;
using Xunit;

namespace PbiBench.V11.Tests;
public sealed class PlatformTests
{
    [Fact] public void ProjectsRespectProcessAndLanguageBoundariesAndProvenancePathsExist()
    {
        var root = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "PbiBench.slnx"))) root = root.Parent;
        Assert.NotNull(root); var repo = root!.FullName;
        var toolbox = File.ReadAllText(Path.Combine(repo, "src/PbiBench.FabricToolbox/PbiBench.FabricToolbox.csproj"));
        Assert.Contains("net10.0-windows", toolbox); Assert.DoesNotContain("ModelEditor", toolbox); Assert.DoesNotContain("TabularEditor", toolbox); Assert.DoesNotContain("PbiBench.App", toolbox);
        foreach (var module in new[] { "PbiBench.AI.ContextExport", "PbiBench.CSharp.LanguageService" })
        {
            var project = File.ReadAllText(Path.Combine(repo, "src", module, module + ".csproj")); Assert.Contains("net10.0;net48", project); Assert.DoesNotContain("UseWPF", project); Assert.DoesNotContain("TabularEditor", project); Assert.DoesNotContain("ModelEditor", project);
        }
        foreach (var c in ProvenanceCatalog.Bundled().Components)
        { Assert.True(File.Exists(Path.Combine(repo, c.LocalAdapter)), c.Id); foreach (var path in c.LocalPatches.Concat(c.ProtectingTests)) Assert.True(File.Exists(Path.Combine(repo, path)), path); }
    }
    [Fact] public void BundledProvenanceHasPinsPatchesOwnersAndIndependentLanes()
    {
        var catalog = ProvenanceCatalog.Bundled(); Assert.Equal("11.1.0", catalog.ProductVersion); Assert.True(catalog.Components.Count >= 30);
        var te2 = catalog.Components.Single(c => c.Id == "semantic.model-editor.te2"); Assert.Contains("75f10e331b8de0dda5c213180b9b8867b4a38191", te2.Pin); Assert.Equal(2, te2.LocalPatches.Count);
        Assert.Contains(catalog.Components, c => c.UpdateLane == "fabric"); Assert.Contains(catalog.Components, c => c.UpdateLane == "csharp-language"); Assert.DoesNotContain(catalog.Components, c => c.Upstream.Contains("Roslyn"));
        Assert.Throws<InvalidDataException>(() => ProvenanceCatalog.Parse("{}"));
    }
    [Fact] public void HandoffCannotContainApprovalCredentialsCommandsOrUnknownFields()
    {
        var handoff = FabricSelectionHandoff.For(new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Lake", "Lakehouse"));
        var json = JsonSerializer.Serialize(handoff); Assert.Equal(handoff, FabricSelectionHandoff.Parse(json));
        foreach (var property in new[] { "Approval", "AccessToken", "ConnectionString", "Command", "SchemaVersion" })
            Assert.Throws<InvalidDataException>(() => FabricSelectionHandoff.Parse(json.Substring(0, json.Length - 1) + ",\"" + property + "\":\"x\"}"));
        Assert.Throws<InvalidDataException>(() => FabricSelectionHandoff.Parse(JsonSerializer.Serialize(handoff with { RequestedAction = "Execute" })));
    }
    [Fact] public void MissingToolsNeverLaunchAndConfiguredExecutableUsesArgumentArray()
    {
        var process = new FakeProcess(); var launcher = new CompanionTools(process); var tool = CompanionTools.Catalog.Single(t => t.Id == "vscode");
        var missing = launcher.Discover(tool, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"), Path.GetTempPath()); Assert.Null(missing.Path); Assert.Throws<FileNotFoundException>(() => launcher.Launch(missing)); Assert.Null(process.Last);
        var folder = Path.GetTempPath(); var executable = Path.Combine(folder, Guid.NewGuid() + ".exe"); File.WriteAllText(executable, "fixture");
        try { var configured = launcher.Discover(tool, executable, folder); launcher.Launch(configured, folder); Assert.Equal(executable, process.Last!.Executable); Assert.Equal(new[] { Path.GetFullPath(folder) }, process.Last.Arguments); } finally { File.Delete(executable); }
    }
    [Fact] public async Task PlatformInventoryIncludesAllTypesAndPaginatesThroughExistingTransport()
    {
        var handler = new Responses(); using var http = new HttpClient(handler); var catalog = new FabricCatalogService(http, new Tokens());
        var items = await catalog.ListAllItemsAsync(Guid.NewGuid().ToString(), default); Assert.Equal(new[] { "Notebook", "DataPipeline" }, items.Select(i => i.Kind)); Assert.Equal(2, handler.Calls);
    }
    private sealed class FakeProcess : IProcessAdapter
    { public ProcessLaunchRequest? Last; public void Start(ProcessLaunchRequest request) => Last = request; public Task<ProcessResult> RunAsync(ProcessLaunchRequest request, CancellationToken ct = default) => throw new NotSupportedException(); }
    private sealed class Tokens : IAccessTokenProvider
    { public Task<string> GetAccessTokenAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default) => Task.FromResult("fixture-token"); }
    private sealed class Responses : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; if (Calls == 2) Assert.Contains("continuationToken=next", request.RequestUri!.Query);
            var json = Calls == 1 ? "{\"value\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"displayName\":\"Notebook\",\"type\":\"Notebook\"}],\"continuationToken\":\"next\"}" : "{\"value\":[{\"id\":\"22222222-2222-2222-2222-222222222222\",\"displayName\":\"Pipeline\",\"type\":\"DataPipeline\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
