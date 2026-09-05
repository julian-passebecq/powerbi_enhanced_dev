using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PbiBench.Core.Compiler;
using PbiBench.Core.Packages;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class SemanticPrototypeTests
{
    private const string Basic = "version: 1.1\nsource: company.sales.orders\nfields:\n  - name: Amount\n    expr: source.Amount\nmeasures:\n  - name: Gross\n    expr: SUM(Amount)\n";
    [Fact] public void CompilerProducesOriginalIntentAndBoundedAggregateCandidates()
    {
        var source = Basic + "  - name: Average\n    expr: AVG(source.Amount)\n  - name: Orders\n    expr: COUNT(*)\n"; var result = new MetricViewCompiler().Compile(source, "Orders");
        Assert.True(result.CanProposeMetadata); Assert.Equal("Amount", result.Intent.Dimensions[0].SourceColumn); Assert.Equal(new[] { "SUM", "AVG", "COUNTROWS" }, result.Intent.Measures.Select(item => item.Aggregate)); Assert.Equal(source, result.Intent.OriginalYaml); Assert.Contains("prototype", result.Prototype); Assert.Contains("OriginalYaml", result.ToJson());
    }
    [Theory]
    [InlineData("filter: Amount > 10")]
    [InlineData("parameters:\n  - name: cutoff\n    data_type: int")]
    [InlineData("materialization:\n  schedule: every 5 minutes")]
    [InlineData("joins:\n  - name: customer\n    source: company.sales.customers\n    on: source.CustomerId = customer.Id")]
    public void UnsupportedGlobalSemanticsBlockAllMetadataProposals(string unsupported)
    { var source = Basic + unsupported + "\n"; var result = new MetricViewCompiler().Compile(source); Assert.False(result.CanProposeMetadata); Assert.Contains(result.Diagnostics, item => item.Severity == CompilerSeverity.Error); Assert.Equal(source, result.Intent.OriginalYaml); }
    [Theory]
    [InlineData("SUM(Amount) FILTER (WHERE Active = TRUE)")]
    [InlineData("COUNT(DISTINCT CustomerId)")]
    [InlineData("SUM(Amount * Quantity)")]
    [InlineData("SUM(Amount); DROP TABLE orders")]
    [InlineData("SUM(customer.Amount)")]
    public void SqlBeyondTheExplicitAggregateSubsetIsNeverInventedAsDax(string expression)
    { var result = new MetricViewCompiler().Compile(Basic.Replace("SUM(Amount)", expression)); Assert.False(result.CanProposeMetadata); Assert.Null(result.Intent.Measures[0].Aggregate); }
    [Theory]
    [InlineData("source: *reference")]
    [InlineData("source: &anchor company.sales.orders")]
    [InlineData("source: !external company.sales.orders")]
    [InlineData("source: [company, sales, orders]")]
    [InlineData("source: SELECT * FROM company.sales.orders")]
    [InlineData("source: \"unterminated")]
    public void AliasTagFlowAndUnsupportedSourceSyntaxAreRejected(string source)
    { var result = new MetricViewCompiler().Compile(Basic.Replace("source: company.sales.orders", source)); Assert.False(result.CanProposeMetadata); Assert.Contains(result.Diagnostics, item => item.Severity == CompilerSeverity.Error); }
    [Fact] public void CommentsQuotedScalarsAndLiteralBlocksRetainTheirExactIntent()
    {
        var result = new MetricViewCompiler().Compile(Basic.Replace("version: 1.1", "version: '1.1' # version").Replace("name: Gross", "name: 'Gross # amount'").Replace("expr: SUM(Amount)", "expr: |-\n      SUM(Amount)").Replace("fields:", "comment: \"A: B # retained\"\nfields:"));
        Assert.True(result.CanProposeMetadata); Assert.Equal("Gross # amount", result.Intent.Measures[0].Name); Assert.Equal("A: B # retained", result.Intent.Comment); Assert.Equal("SUM(Amount)", result.Intent.Measures[0].SqlExpression);
    }
    [Fact] public void DuplicateKeysNamesAndFieldAliasLineageAreBlocking()
    {
        Assert.False(new MetricViewCompiler().Compile(Basic + "version: 1.1\n").CanProposeMetadata);
        Assert.False(new MetricViewCompiler().Compile(Basic + "  - name: Gross\n    expr: SUM(Amount)\n").CanProposeMetadata);
        Assert.False(new MetricViewCompiler().Compile(Basic.Replace("expr: source.Amount", "expr: Quantity")).CanProposeMetadata);
        Assert.False(new MetricViewCompiler().Compile(Basic.Replace("    expr: source.Amount", "\texpr: source.Amount")).CanProposeMetadata);
        Assert.Throws<ArgumentException>(() => new MetricViewCompiler().Compile(new string('x', 1024 * 1024 + 1)));
    }
    [Fact] public async Task PackageHashesExactBytesAndRetainsAnImmutableCapture()
    {
        using var temp = new PackageTemp(); temp.Write(); var package = await new LocalDaxPackageReader().ReadAsync(temp.Root); Assert.Equal("contoso.math", package.Manifest.Id); Assert.Equal("1.0.0", package.Manifest.Version); Assert.Equal(64, package.ContentHash.Length);
        File.WriteAllText(Path.Combine(temp.Root, "Double.dax"), "changed", new UTF8Encoding(false)); Assert.Equal(PackageTemp.Body, package.Functions["contoso.math.Double"]); await Assert.ThrowsAsync<InvalidDataException>(() => new LocalDaxPackageReader().ReadAsync(temp.Root));
    }
    [Theory]
    [InlineData("../escape.dax")]
    [InlineData("C:/escape.dax")]
    [InlineData(".git/hook.dax")]
    [InlineData("CON.dax")]
    [InlineData("body.cs")]
    [InlineData("nested\\body.dax")]
    [InlineData("body.dax:stream")]
    public void PackageRejectsExecutableTraversalAndWindowsDevicePaths(string path)
    { Assert.Throws<ArgumentException>(() => LocalDaxPackageReader.ParseManifest(PackageTemp.Manifest(path: path))); }
    [Fact] public void ManifestRejectsUnknownInstallHooksMissingLicenseDuplicatePropertiesAndNonExactVersions()
    {
        var manifest = PackageTemp.Manifest(); Assert.Throws<ArgumentException>(() => LocalDaxPackageReader.ParseManifest(manifest.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"install\":\"run.exe\"")));
        Assert.Throws<ArgumentException>(() => LocalDaxPackageReader.ParseManifest(manifest.Replace("\"license\":\"MIT\"", "\"license\":\"\"")));
        Assert.Throws<ArgumentException>(() => LocalDaxPackageReader.ParseManifest(manifest.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")));
        Assert.Throws<ArgumentException>(() => LocalDaxPackageReader.ParseManifest(manifest.Replace("1.0.0", "^1.0")));
    }
    [Fact] public async Task PackageCancellationAndFileBoundsAreEnforced()
    {
        using var temp = new PackageTemp(); temp.Write(); using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalDaxPackageReader().ReadAsync(temp.Root, canceled.Token));
        File.WriteAllText(Path.Combine(temp.Root, "Double.dax"), new string('x', 262145)); await Assert.ThrowsAsync<InvalidDataException>(() => new LocalDaxPackageReader().ReadAsync(temp.Root));
    }
    [Fact] public void LockRoundTripsExactPinsAndDetectsMissingChangedAndCyclicDependencies()
    {
        var hash = new string('a', 64); var bodyHash = DaxPackageLock.FunctionHash(PackageTemp.Body, "", false);
        var math = new DaxLockedPackage("contoso.math", "1.0.0", "MIT", hash, Array.Empty<DaxPackageDependency>(), new[] { new DaxLockedFunction("contoso.math.Double", bodyHash) });
        var consumer = new DaxLockedPackage("contoso.consumer", "1.0.0", "MIT", new string('b', 64), new[] { new DaxPackageDependency(math.Id, math.Version, hash) }, Array.Empty<DaxLockedFunction>());
        var state = DaxPackageLock.Parse(new DaxPackageLock(new[] { math, consumer }).ToJson()); Assert.Empty(state.ValidateGraph()); Assert.Equal(hash, state.Packages[0].ContentHash);
        Assert.NotEmpty(new DaxPackageLock(new[] { consumer }).ValidateGraph()); Assert.NotEmpty(new DaxPackageLock(new[] { math with { ContentHash = new string('c', 64) }, consumer }).ValidateGraph());
        var cyclic = math with { Dependencies = new[] { new DaxPackageDependency(consumer.Id, consumer.Version, consumer.ContentHash) } }; Assert.Contains(new DaxPackageLock(new[] { cyclic, consumer }).ValidateGraph(), item => item.Contains("cycle"));
        Assert.NotEqual(bodyHash, DaxPackageLock.FunctionHash(PackageTemp.Body, "user change", false));
    }
    private sealed class PackageTemp : IDisposable
    {
        internal const string Body = "(value: SCALAR INT64) => value * 2";
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "pbibench-package-test-" + Guid.NewGuid().ToString("N"));
        internal PackageTemp() => Directory.CreateDirectory(Root);
        internal void Write() { File.WriteAllText(Path.Combine(Root, "Double.dax"), Body, new UTF8Encoding(false)); File.WriteAllText(Path.Combine(Root, "pbibench.package.json"), Manifest(), new UTF8Encoding(false)); }
        internal static string Manifest(string path = "Double.dax") { using var sha = SHA256.Create(); var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Body))).Replace("-", "").ToLowerInvariant(); return JsonSerializer.Serialize(new { schemaVersion = 1, id = "contoso.math", version = "1.0.0", license = "MIT", description = "Original fixture", dependencies = Array.Empty<object>(), functions = new[] { new { name = "contoso.math.Double", path, sha256 = hash, description = "", isHidden = false } } }); }
        public void Dispose() { var full = Path.GetFullPath(Root); if (!string.Equals(Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("pbibench-package-test-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected package test cleanup path."); if (Directory.Exists(full)) Directory.Delete(full, true); }
    }
}
