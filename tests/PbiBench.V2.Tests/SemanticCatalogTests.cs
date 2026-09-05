using PbiBench.Pbir;
using Xunit;

namespace PbiBench.V2.Tests;

public sealed class SemanticCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "catalog-" + Guid.NewGuid().ToString("N"));
    private void Write(string text, string name = "tables/table.tmdl")
    { var path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    [Theory] [InlineData(" ")] [InlineData("  ")] [InlineData("    ")] [InlineData("\t")]
    public async Task DirectMembersUseRelativeIndentationAndQuotedNames(string indent)
    {
        Write($"table 'O''Brien Sales'\n{indent}lineageTag: abc\n{indent}column 'Net Amount'\n{indent}{indent}dataType: decimal\n{indent}measure 'Net Revenue' = 1\n{indent}{indent}annotation Nested = value\n{indent}{indent}{indent}column Fake\ntable Next\n{indent}column Id\n");
        var catalog = await ReportLineage.ReadLocalModelAsync(root, default);
        Assert.True(catalog.Complete); Assert.Equal(3, catalog.Fields.Count);
        Assert.Contains(new SemanticField("O'Brien Sales", "Net Revenue", "Measure"), catalog.Fields);
        Assert.Contains(new SemanticField("Next", "Id", "Column"), catalog.Fields);
        Assert.DoesNotContain(catalog.Fields, f => f.Name == "Fake");
    }
    [Theory]
    [InlineData("  table Nested\n    column X")]
    [InlineData("table T\n    column X\n  measure Y = 1")]
    [InlineData("table T\n  column 'unterminated")]
    [InlineData("table T\n  lineageTag: abc\n    column Skipped")]
    [InlineData("table T\n  Measure Unsupported = 1")]
    [InlineData("table T\n  unknownDeclaration Missing")]
    [InlineData("table T\n  measure M =\n    ```\n    column Fake")]
    public async Task UnsupportedLayoutsNeverClaimComplete(string text)
    { Write(text); Assert.False((await ReportLineage.ReadLocalModelAsync(root, default)).Complete); }
    [Fact] public async Task MissingReferencedTableAndDuplicateMembersArePartial()
    {
        Write("table T\n  column X\n  column X"); Write("model Model\n  ref table Missing", "model.tmdl");
        var catalog = await ReportLineage.ReadLocalModelAsync(root, default); Assert.False(catalog.Complete); Assert.Single(catalog.Fields);
    }
    [Fact] public void SnapshotIsImmutableVersionedAndRejectsExtraSensitiveData()
    {
        var fields = new[] { new SemanticField("T", "M", "Measure") }; var snapshot = new SemanticCatalogSnapshot(fields, true, DateTimeOffset.UtcNow);
        fields[0] = new("Other", "Other", "Column"); Assert.Equal("T", snapshot.Fields[0].Table);
        var roundtrip = SemanticCatalogSnapshot.Parse(snapshot.ToJson()); Assert.True(roundtrip.Complete); Assert.Equal(snapshot.Fields, roundtrip.Fields);
        Assert.Throws<InvalidDataException>(() => SemanticCatalogSnapshot.Parse(snapshot.ToJson().Replace("\"Version\": 1", "\"Version\": 2")));
        Assert.Throws<InvalidDataException>(() => SemanticCatalogSnapshot.Parse(snapshot.ToJson().Replace("\"Version\": 1", "\"ConnectionString\": \"secret\", \"Version\": 1")));
    }
}
