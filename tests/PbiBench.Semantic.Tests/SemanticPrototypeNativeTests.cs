using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Core.Compiler;
using PbiBench.Core.Packages;
using PbiBench.Semantic.Compiler;
using PbiBench.Semantic.Packages;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class SemanticPrototypeNativeTests
{
    private const string Yaml = "version: 1.1\nsource: company.sales.orders\nfields:\n  - name: Amount\n    expr: Amount\nmeasures:\n  - name: Gross prototype\n    expr: SUM(Amount)\n  - name: Row prototype\n    expr: COUNT(*)\n";
    private static TabularModelHandler Model(int compatibility = 1702)
    { var handler = new TabularModelHandler(compatibility); var table = handler.Model.AddTable("Sales"); table.AddDataColumn("Amount", "Amount", dataType: DataType.Decimal); table.AddDataColumn("Category", "Category", dataType: DataType.String); handler.UndoManager.Clear(); return handler; }
    [TestMethod] public void CompilerPreviewUsesExplicitMappingAndOneUndoWithoutCreatingSourceObjects()
    {
        using var handler = Model(); var before = new SemanticModelService(handler).Fingerprint(); var preview = new SemanticCompilerService(handler).Preview(new MetricViewCompiler().Compile(Yaml), "Sales");
        Assert.IsTrue(preview.CanApply); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint()); Assert.AreEqual(2, preview.Changes.Count); preview.Apply(handler);
        Assert.AreEqual("SUM('Sales'[Amount])", handler.Model.Tables["Sales"].Measures["Gross prototype"].Expression); Assert.AreEqual("COALESCE(COUNTROWS('Sales'), 0)", handler.Model.Tables["Sales"].Measures["Row prototype"].Expression); Assert.AreEqual(1, handler.Model.Tables.Count);
        handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }
    [TestMethod] public void CompilerRejectsUnsupportedSemanticsMappingTypesCollisionsAndStalePlans()
    {
        using var handler = Model(); var service = new SemanticCompilerService(handler); var parsed = new MetricViewCompiler().Compile(Yaml);
        Assert.IsFalse(service.Preview(parsed, "Absent").CanApply); Assert.IsFalse(service.Preview(new MetricViewCompiler().Compile(Yaml + "filter: Amount > 0"), "Sales").CanApply);
        Assert.IsFalse(service.Preview(new MetricViewCompiler().Compile(Yaml.Replace("SUM(Amount)", "SUM(Category)")), "Sales").CanApply);
        var preview = service.Preview(parsed, "Sales"); handler.Model.Description = "newer model edit"; Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(handler));
        handler.Model.Tables["Sales"].AddMeasure("Gross prototype", "42"); Assert.IsFalse(service.Preview(parsed, "Sales").CanApply);
    }
    [TestMethod] public void PackageInstallUpdatesOwnedFunctionsAndLockTogetherWithNativeUndo()
    {
        using var handler = Model(); using var temp = new PackageFiles(); var package = temp.Write("1.0.0", 2); var service = new DaxPackageService(handler); var before = new SemanticModelService(handler).Fingerprint();
        var install = service.PreviewInstall(package); Assert.IsTrue(install.CanApply, string.Join("\n", install.Issues)); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint()); install.Apply(handler);
        Assert.AreEqual(2, handler.Model.Functions.Count); Assert.AreEqual(package.ContentHash, service.CaptureLock().Packages[0].ContentHash); var installed = new SemanticModelService(handler).Fingerprint();
        handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint()); handler.UndoManager.Redo(); Assert.AreEqual(installed, new SemanticModelService(handler).Fingerprint());
        var update = service.PreviewInstall(temp.Write("1.1.0", 2, "value * 3")); Assert.IsTrue(update.CanApply); update.Apply(handler); Assert.AreEqual("1.1.0", service.CaptureLock().Packages[0].Version); Assert.IsTrue(handler.Model.Functions["contoso.math.F0"].Expression.Contains("* 3"));
        handler.UndoManager.Undo(); Assert.AreEqual(installed, new SemanticModelService(handler).Fingerprint());
    }
    [TestMethod] public void PackageRemovalRejectsCallersAndLocalChangesButIgnoresCommentsAndStrings()
    {
        using var handler = Model(); using var temp = new PackageFiles(); var service = new DaxPackageService(handler); service.PreviewInstall(temp.Write("1.0.0", 1)).Apply(handler);
        var measure = handler.Model.Tables["Sales"].AddMeasure("Caller", "contoso.math.F0(2)"); Assert.IsFalse(service.PreviewRemove("contoso.math").CanApply);
        measure.Expression = "\"contoso.math.F0(2)\" // contoso.math.F0(2)"; Assert.IsTrue(service.PreviewRemove("contoso.math").CanApply);
        handler.Model.Functions["contoso.math.F0"].Description = "user edit"; Assert.IsFalse(service.PreviewRemove("contoso.math").CanApply); Assert.IsFalse(service.PreviewInstall(temp.Write("1.1.0", 1)).CanApply);
    }
    [TestMethod] public void PackageRemovalAndOneUndoRestoreFunctionsAndLockWithOtherFunctionsPresent()
    {
        using var handler = Model(); using var temp = new PackageFiles(); var unrelated = handler.Model.AddFunction("User.Before"); unrelated.Expression = "() => 1"; var service = new DaxPackageService(handler); service.PreviewInstall(temp.Write("1.0.0", 2)).Apply(handler);
        var later = handler.Model.AddFunction("User.After"); later.Expression = "() => 2"; later.Description = "Preserve unrelated metadata and wrapper identity";
        var beforeUpdate = new SemanticModelService(handler).Fingerprint(); service.PreviewInstall(temp.Write("1.1.0", 3)).Apply(handler); var afterUpdate = new SemanticModelService(handler).Fingerprint();
        handler.UndoManager.Undo(); Assert.AreEqual(beforeUpdate, new SemanticModelService(handler).Fingerprint()); handler.UndoManager.Redo(); Assert.AreEqual(afterUpdate, new SemanticModelService(handler).Fingerprint());
        handler.UndoManager.Clear(); var before = new SemanticModelService(handler).Fingerprint();
        var removal = service.PreviewRemove("contoso.math"); Assert.IsTrue(removal.CanApply); removal.Apply(handler); Assert.AreEqual(2, handler.Model.Functions.Count); Assert.AreEqual(0, service.CaptureLock().Packages.Count);
        var removed = new SemanticModelService(handler).Fingerprint(); handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint()); Assert.AreSame(later, handler.Model.Functions["User.After"]); Assert.AreSame(unrelated, handler.Model.Functions["User.Before"]);
        handler.UndoManager.Redo(); Assert.AreEqual(removed, new SemanticModelService(handler).Fingerprint()); handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }
    [TestMethod] public void PackageBlocksUnownedCollisionUnsupportedCompatibilityRepublishAndConsumedPreview()
    {
        using var temp = new PackageFiles(); var package = temp.Write("1.0.0", 1);
        using (var handler = Model(1600)) Assert.IsFalse(new DaxPackageService(handler).PreviewInstall(package).CanApply);
        using (var handler = Model())
        {
            var collision = handler.Model.AddFunction("contoso.math.F0"); collision.Expression = "() => 1"; Assert.IsFalse(new DaxPackageService(handler).PreviewInstall(package).CanApply); collision.Delete();
            var service = new DaxPackageService(handler); var preview = service.PreviewInstall(package); preview.Apply(handler); Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(handler)); Assert.IsFalse(service.PreviewInstall(temp.Write("1.0.0", 1, "value * 3")).CanApply);
        }
    }
    [TestMethod] public void PackageDependencyPinsRequireInstalledUnmodifiedContentAndBlockDependentRemoval()
    {
        using var handler = Model(); using var mathFiles = new PackageFiles(); using var consumerFiles = new PackageFiles(); var math = mathFiles.Write("1.0.0", 1); var service = new DaxPackageService(handler);
        var consumer = consumerFiles.Write("1.0.0", 1, "contoso.math.F0(value)", "contoso.consumer", new[] { new DaxPackageDependency(math.Manifest.Id, math.Manifest.Version, math.ContentHash) });
        Assert.IsFalse(service.PreviewInstall(consumer).CanApply); service.PreviewInstall(math).Apply(handler); Assert.IsTrue(service.PreviewInstall(consumer).CanApply);
        handler.Model.Functions["contoso.math.F0"].Description = "newer edit"; Assert.IsFalse(service.PreviewInstall(consumer).CanApply); handler.UndoManager.Undo(); service.PreviewInstall(consumer).Apply(handler);
        Assert.IsFalse(service.PreviewRemove(math.Manifest.Id).CanApply); Assert.IsFalse(service.PreviewInstall(mathFiles.Write("1.1.0", 1)).CanApply);
    }
    [TestMethod] public void PackageUnknownDependencyAndRemovedFunctionCallersAreBlocking()
    {
        using var handler = Model(); using var mathFiles = new PackageFiles(); using var consumerFiles = new PackageFiles(); var service = new DaxPackageService(handler); service.PreviewInstall(mathFiles.Write("1.0.0", 2)).Apply(handler);
        Assert.IsFalse(service.PreviewInstall(consumerFiles.Write("1.0.0", 1, "contoso.math.F0(value)", "contoso.consumer")).CanApply);
        handler.Model.Tables["Sales"].AddMeasure("Calls removed", "contoso.math.F1(1)"); Assert.IsFalse(service.PreviewInstall(mathFiles.Write("1.1.0", 1)).CanApply);
    }
    [TestMethod] public void PackageRejectsUnknownCodeLikeCallsAndStatementListsWithoutRunningThem()
    {
        using var handler = Model(); using var temp = new PackageFiles(); var service = new DaxPackageService(handler); var before = new SemanticModelService(handler).Fingerprint();
        Assert.IsFalse(service.PreviewInstall(temp.Write("1.0.0", 1, "System.IO.File.WriteAllText(\"never-created\", \"payload\")")).CanApply);
        Assert.IsFalse(service.PreviewInstall(temp.Write("1.0.0", 1, "value; EVALUATE {1}")).CanApply);
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint()); Assert.AreEqual(0, handler.Model.Functions.Count);
    }
    private sealed class PackageFiles : IDisposable
    {
        private string Root { get; } = Path.Combine(Path.GetTempPath(), "pbibench-package-native-" + Guid.NewGuid().ToString("N")); public PackageFiles() => Directory.CreateDirectory(Root);
        public LocalDaxPackage Write(string version, int count, string expression = "value * 2", string id = "contoso.math", IReadOnlyList<DaxPackageDependency>? dependencies = null)
        {
            var files = Enumerable.Range(0, count).Select(index => { var path = "Function" + index + ".dax"; var body = "(value: SCALAR INT64) => " + expression; var bytes = Encoding.UTF8.GetBytes(body); File.WriteAllBytes(Path.Combine(Root, path), bytes); using var sha = SHA256.Create(); return new { name = id + ".F" + index, path, sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), description = "Original test function", isHidden = false }; }).ToArray();
            var manifest = new { schemaVersion = 1, id, version, license = "MIT", description = "Original test package", dependencies = (dependencies ?? Array.Empty<DaxPackageDependency>()).Select(item => new { id = item.Id, version = item.Version, sha256 = item.Sha256 }), functions = files };
            File.WriteAllText(Path.Combine(Root, "pbibench.package.json"), JsonSerializer.Serialize(manifest), new UTF8Encoding(false)); return new LocalDaxPackageReader().ReadAsync(Root).GetAwaiter().GetResult();
        }
        public void Dispose() { var full = Path.GetFullPath(Root); if (!string.Equals(Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("pbibench-package-native-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected cleanup path."); if (Directory.Exists(full)) Directory.Delete(full, true); }
    }
}
