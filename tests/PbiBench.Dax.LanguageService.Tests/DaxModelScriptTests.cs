using PbiBench.Dax.LanguageService;
using Xunit;

namespace PbiBench.Dax.LanguageService.Tests;

public sealed class DaxModelScriptTests
{
    [Fact]
    public void RoundTripPreservesEveryScopeAndExactWhitespaceCommentsAndEscaping()
    {
        var entries = new[] {
            new DaxScriptEntry(DaxScriptObjectKind.Measure, "Sales' ledger", "A]B", "  VAR n = 1\r\nRETURN n -- tail\n "),
            new DaxScriptEntry(DaxScriptObjectKind.Column, "Sales' ledger", "Computed", "\n1 + 2\n"),
            new DaxScriptEntry(DaxScriptObjectKind.Table, null, "New table", "DATATABLE(\"Text\", STRING, {{\"a;b\"}})"),
            new DaxScriptEntry(DaxScriptObjectKind.CalculationItem, "Time", "Current", "SELECTEDMEASURE()"),
            new DaxScriptEntry(DaxScriptObjectKind.Function, null, "Finance.Tax", "(x : NUMERIC) => x * 0.2"),
            new DaxScriptEntry(DaxScriptObjectKind.Measure, "Sales' ledger", "A]B", "\"#,0\"", "FormatStringExpression") };
        var script = DaxModelScript.Serialize(entries); var parsed = DaxModelScript.Parse(script);
        Assert.True(parsed.IsValid, string.Join(";", parsed.Diagnostics.Select(d => d.Message))); Assert.Equal(entries.Length, parsed.Entries.Count);
        for (var i = 0; i < entries.Length; i++) { Assert.Equal(entries[i].Key, parsed.Entries[i].Key); Assert.Equal(entries[i].Expression, parsed.Entries[i].Expression); Assert.Equal(entries[i].Expression, script.Substring(parsed.Entries[i].ExpressionSpan.Start, parsed.Entries[i].ExpressionSpan.Length)); }
    }
    [Theory]
    [InlineData("MEASURE 'T'[M] = 1")]
    [InlineData("MEASURE 'T'[M] = 1; MEASURE 't'[m] = 2;")]
    [InlineData("0 'T'[M] = 1;")]
    [InlineData("FORMATSTRINGEXPRESSION COLUMN 'T'[C] = 1;")]
    [InlineData("MEASURE 'T'[M] = ;")]
    [InlineData("MEASURE 'T'[M] = \"unterminated;")]
    [InlineData("MEASURE [M] = 1;")]
    [InlineData("MEASURE 'T'[M] = SUM((1);")]
    public void MalformedSourceCannotApply(string text) => Assert.False(DaxModelScript.Parse(text).IsValid);

    [Fact]
    public void TopLevelDelimitersIgnoreQuotedCommentsAndNestedSeparators()
    {
        var result = DaxModelScript.Parse("/* ; FUNCTION X = ; */ MEASURE 'T'[M] = IF(TRUE(); \";\"; \"x\"); -- ;\nFUNCTION Finance.Tax = (x : NUMERIC) => x + 1;");
        Assert.True(result.IsValid); Assert.Equal(2, result.Entries.Count); Assert.Equal("Finance.Tax", result.Entries[1].Name);
    }
    [Fact]
    public void EmptyDynamicFormatExpressionIsAnExplicitClear()
    {
        var result = DaxModelScript.Parse(DaxModelScript.Serialize(new[] { new DaxScriptEntry(DaxScriptObjectKind.Measure, "T", "M", "", "FormatStringExpression") }));
        Assert.True(result.IsValid); Assert.Equal("", Assert.Single(result.Entries).Expression);
    }
    [Fact]
    public async Task SourceFilesReplaceAtomicallyAndCanceledSavePreservesOriginal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pbibench-script-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); var path = Path.Combine(directory, "model.daxscript");
        try { await DaxScriptFile.SaveAsync(path, "incomplete draft α", CancellationToken.None); await DaxScriptFile.SaveAsync(path, "replacement β", CancellationToken.None); Assert.Equal("replacement β", await DaxScriptFile.LoadAsync(path, CancellationToken.None)); using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DaxScriptFile.SaveAsync(path, "wrong", canceled.Token)); Assert.Equal("replacement β", await DaxScriptFile.LoadAsync(path, CancellationToken.None)); Assert.Single(Directory.GetFiles(directory)); }
        finally { Directory.Delete(directory, true); }
    }
}
