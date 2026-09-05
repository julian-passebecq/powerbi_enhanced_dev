using System.Text.Json;
using System.Xml.Linq;
using PbiBench.Core.Platform;
using Xunit;

namespace PbiBench.V11.Tests;

public sealed class ModuleCatalogTests
{
    [Fact] public void CatalogProjectGraphExactlyMatchesSourceAndDeclaredModuleClosure()
    {
        var root = Root(); var catalog = ModuleCatalog.Bundled(); var graph = catalog.ProjectGraph.ToDictionary(p => p.Path, StringComparer.Ordinal);
        var projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)).Select(p => p.Substring(root.Length + 1).Replace('\\', '/')).OrderBy(p => p).ToArray();
        Assert.Equal(projects, graph.Keys.OrderBy(p => p));
        var modules = catalog.Modules.ToDictionary(m => m.Id);
        HashSet<string> ModuleClosure(string id)
        { var seen = new HashSet<string>(); var pending = new Stack<string>(); pending.Push(id); while (pending.Count > 0) { var next = pending.Pop(); if (seen.Add(next)) foreach (var dep in modules[next].DependsOnModules) pending.Push(dep); } return seen; }
        foreach (var project in graph.Values)
        {
            var file = Path.Combine(root, project.Path);
            var actual = XDocument.Load(file).Descendants("ProjectReference").Select(e => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, (string)e.Attribute("Include")!)).Substring(root.Length + 1).Replace('\\', '/')).OrderBy(p => p).ToArray();
            Assert.Equal(actual, project.References.OrderBy(p => p));
            var closure = ModuleClosure(project.ModuleId);
            foreach (var target in actual) Assert.Contains(graph[target].ModuleId, closure);
        }
        Assert.Equal(new[] { "src/PbiBench.ExternalTools/PbiBench.ExternalTools.csproj" }, graph["src/PbiBench.DaxStudio/PbiBench.DaxStudio.csproj"].References);
        foreach (var child in new[] { "ReportStudio", "FabricToolbox" })
        {
            var seen = new HashSet<string>(); var pending = new Stack<string>(); pending.Push("src/PbiBench." + child + "/PbiBench." + child + ".csproj");
            while (pending.Count > 0) { var next = pending.Pop(); if (seen.Add(next)) foreach (var dep in graph[next].References) pending.Push(dep); }
            Assert.DoesNotContain(seen, p => p.Contains("PbiBench.DaxStudio"));
        }
    }
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string Serialize(ModuleCatalog catalog) => JsonSerializer.Serialize(catalog, Json);
    private static string Root()
    {
        var path = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (path != null && !File.Exists(Path.Combine(path.FullName, "PbiBench.slnx"))) path = path.Parent;
        return path?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
    [Fact] public void AllFeaturesResolveVersionedModulesAndEmbeddedMetadataMatchesDisk()
    {
        var catalog = ModuleCatalog.Bundled(); Assert.Equal(22, catalog.Modules.Count); Assert.Equal("2.3.0", catalog.ProductVersion);
        Assert.Equal(Serialize(catalog), Serialize(ModuleCatalog.Parse(File.ReadAllText(Path.Combine(Root(), "docs/architecture/module_catalog.json")))));
        foreach (var module in catalog.Modules)
        {
            Assert.Contains(module.Lifecycle, ModuleCatalog.Lifecycles);
            foreach (var path in module.OwnerProjects.Where(p => p != "planned").Concat(module.ProtectingTests)) Assert.True(File.Exists(Path.Combine(Root(), path)), path);
        }
        foreach (var row in FeatureCatalog.Bundled().Rows(ProvenanceCatalog.Bundled()))
        { Assert.NotEmpty(row.Modules); Assert.Contains("version:", row.Detail); Assert.Contains("runtime:", row.Detail); Assert.DoesNotContain("Freeze", row.Detail); }
    }
    [Fact] public void RejectsInvalidMetadataUnknownLinksCyclesAndTransitiveForbiddenDependencies()
    {
        var catalog = ModuleCatalog.Bundled();
        ModuleCatalog Change(string id, Func<CatalogModule, CatalogModule> change) => catalog with { Modules = catalog.Modules.Select(m => m.Id == id ? change(m) : m).ToArray() };
        foreach (var invalid in new[] {
            catalog with { SchemaVersion = 2 }, catalog with { Modules = new[] { catalog.Modules[0], catalog.Modules[0] } },
            Change("dax", m => m with { Lifecycle = "Freeze" }), Change("dax", m => m with { Kind = "Dynamic" }),
            Change("dax", m => m with { Version = "latest" }), Change("dax", m => m with { TargetFrameworks = new[] { "unknown" } }),
            Change("fabric-services", m => m with { TargetFrameworks = new[] { "net48" } }),
            Change("dax", m => m with { OwnerProjects = Array.Empty<string>() }), Change("dax", m => m with { ProtectingTests = Array.Empty<string>() }),
            Change("dax", m => m with { OwnerProjects = new[] { "src/../../private.csproj" } }),
            Change("dax", m => m with { UpdateLane = "" }), Change("dax", m => m with { DependsOnModules = new[] { "absent" } }),
            Change("dax", m => m with { DependsOnModules = new[] { "semantic-ide" } }),
            Change("fabric-services", m => m with { DependsOnModules = new[] { "csharp-automation" } }),
            Change("fabric-toolbox", m => m with { DependsOnModules = new[] { "semantic-ide" } }) })
            Assert.Throws<InvalidDataException>(() => ModuleCatalog.Parse(Serialize(invalid)));
        Assert.Throws<InvalidDataException>(() => ModuleCatalog.Parse(Serialize(catalog).Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")));
        Assert.Throws<InvalidDataException>(() => ModuleCatalog.Parse(Serialize(catalog).Replace("\"schemaVersion\":1", "\"unrecognized\":1,\"schemaVersion\":1")));
        Assert.Throws<InvalidDataException>(() => ModuleCatalog.Parse(new string('日', 90000)));
        Assert.Throws<InvalidDataException>(() => ModuleCatalog.Parse("{}"));
    }
    [Fact] public void FeatureModuleLinksRejectMissingDuplicateAndMismatchedCatalogs()
    {
        var features = FeatureCatalog.Bundled(); var provenance = ProvenanceCatalog.Bundled();
        foreach (var ids in new[] { new[] { "missing" }, new[] { "dax", "dax" }, Array.Empty<string>() })
        {
            var invalid = features with { Features = new[] { features.Features[0] with { ModuleIds = ids } } };
            Assert.Throws<InvalidDataException>(() => FeatureCatalog.Parse(JsonSerializer.Serialize(invalid, Json), provenance));
        }
        Assert.Throws<InvalidDataException>(() => FeatureCatalog.Parse(JsonSerializer.Serialize(features, Json), provenance, ModuleCatalog.Bundled() with { ProductVersion = "99.0.0" }));
    }
    [Fact] public void ActualProjectReferenceClosuresRespectEveryForbiddenModuleDependency()
    {
        var root = Root();
        foreach (var module in ModuleCatalog.Bundled().Modules.Where(m => m.ForbiddenDependencies.Count > 0))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var pending = new Stack<string>(module.OwnerProjects.Select(p => Path.GetFullPath(Path.Combine(root, p))));
            while (pending.Count > 0)
            {
                var path = pending.Pop(); if (!seen.Add(path)) continue;
                Assert.DoesNotContain(Path.GetFileNameWithoutExtension(path), module.ForbiddenDependencies);
                var project = XDocument.Load(path);
                foreach (var reference in project.Descendants().Where(e => e.Name.LocalName is "Reference" or "PackageReference"))
                    Assert.DoesNotContain(((string?)reference.Attribute("Include") ?? "").Split(',')[0], module.ForbiddenDependencies);
                foreach (var reference in project.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
                {
                    var relative = (string?)reference.Attribute("Include"); if (relative == null || relative.Contains("$(")) continue;
                    pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, relative)));
                }
            }
        }
    }
}
