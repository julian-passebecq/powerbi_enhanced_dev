using System.Security.Cryptography;
using System.Text.Json;
using PbiBench.Core.Platform;
using Xunit;

namespace PbiBench.V2.Tests;

public sealed class SchemaProvenanceTests
{
    [Fact] public void BundledSchemasRetainExactPinnedSourceBytesAndLicense()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PbiBench.slnx"))) directory = directory.Parent;
        var root = directory!.FullName; using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schemas/microsoft.lock.json")));
        Assert.Equal("83ce11373faada0d01e76264a5cceb0ba70003e6", manifest.RootElement.GetProperty("commit").GetString());
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray()) Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "schemas/microsoft", entry.GetProperty("path").GetString()!)))).ToLowerInvariant());
        Assert.Contains("MIT License", File.ReadAllText(Path.Combine(root, "schemas/microsoft/LICENSE")));
    }
    [Fact] public void Gen2ModulesHaveIndependentLanesAndNoSemanticRuntimeDependencies()
    {
        var modules = ModuleCatalog.Bundled().Modules;
        foreach (var id in new[] { "report-studio", "pbir", "lineage" }) { var module = modules.Single(m => m.Id == id); Assert.Contains("TOMWrapper", module.ForbiddenDependencies); Assert.DoesNotContain("semantic-ide", module.DependsOnModules); }
        Assert.Equal("SeparateProcess", modules.Single(m => m.Id == "report-studio").Kind);
        Assert.Equal(3, modules.Where(m => m.Id is "bravo-bridge" or "powerbi-bridge" or "vscode-bridge").Select(m => m.UpdateLane).Distinct().Count());
    }
}
