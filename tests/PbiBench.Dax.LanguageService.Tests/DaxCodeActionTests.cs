using PbiBench.Dax.LanguageService;
using Xunit;

namespace PbiBench.Dax.LanguageService.Tests;

public sealed class DaxCodeActionTests
{
    private readonly DaxLanguageService language = new();
    private static DaxMetadataSnapshot Model(params DaxSymbol[] extra) => new(new[] {
        new DaxSymbol("table", "Sales", DaxSymbolKind.Table),
        new DaxSymbol("amount", "Amount", DaxSymbolKind.Column, "Sales"),
        new DaxSymbol("revenue", "Revenue", DaxSymbolKind.Measure, "Sales", "SUM('Sales'[Amount])") }.Concat(extra));
    private IReadOnlyList<DaxCodeAction> Actions(string text, string selected, DaxDocumentKind kind = DaxDocumentKind.Expression, DaxMetadataSnapshot? model = null) => language.GetCodeActions(language.Analyze(new DaxDocument("doc", text, 7, kind, "Sales"), model ?? Model()), new TextSpan(text.IndexOf(selected, StringComparison.Ordinal), selected.Length));
    private static DaxCodeAction Extract(IReadOnlyList<DaxCodeAction> actions) => Assert.Single(actions, action => action.Title == "Extract selection to local VAR");

    [Fact]
    public void ExtractIteratorExpressionStaysInsideItsRowContextAndKeepsComments()
    {
        const string selected = "'Sales'[Amount] * 2 /* same row */"; var text = "SUMX('Sales', " + selected + ")"; var action = Extract(Actions(text, selected));
        var result = action.Apply(new DaxDocument("doc", text, 7));
        Assert.StartsWith("SUMX('Sales', ( VAR __pbExtracted =\n", result); Assert.Contains(selected, result); Assert.EndsWith("RETURN __pbExtracted ))", result);
        Assert.DoesNotContain(language.Analyze(new DaxDocument("after", result, Kind: DaxDocumentKind.Expression), Model()).Diagnostics, d => d.Severity == DaxDiagnosticSeverity.Error);
    }
    [Fact]
    public void ExtractUniqueNameAvoidsVariablesParametersAndModelTables()
    {
        const string text = "VAR __pbExtracted = 4 RETURN [Revenue] + __pbExtracted";
        var action = Extract(Actions(text, "[Revenue]", model: Model(new DaxSymbol("collision", "__pbExtracted2", DaxSymbolKind.Table))));
        Assert.Contains("VAR __pbExtracted3", action.Edits.Single().NewText); Assert.Throws<InvalidOperationException>(() => action.Apply(new DaxDocument("doc", text, 8)));
    }
    [Theory]
    [InlineData("1 + 2 * 3", "1 + 2")]
    [InlineData("SUM('Sales'[Amount])", "'Sales'[Amount]")]
    [InlineData("CALCULATE([Revenue], 'Sales'[Amount] > 2)", "'Sales'[Amount] > 2")]
    [InlineData("CALCULATE([Revenue], FILTER('Sales', 'Sales'[Amount] > 2))", "'Sales'[Amount] > 2")]
    [InlineData("ROW(\"Name\", 1)", "\"Name\"")]
    [InlineData("SUMX(KEEPFILTERS('Sales'), [Revenue])", "KEEPFILTERS('Sales')")]
    [InlineData("'Sales'[Amount] + 1", "'Sales'")]
    [InlineData("'Sales'[Amount] + 1", "[Amount]")]
    [InlineData("\"a string\"", "string")]
    [InlineData("// Revenue\n1", "Revenue")]
    [InlineData("UnknownFunction([Revenue])", "[Revenue]")]
    public void ExtractRejectsNonSubtreesReferenceOnlyPositionsAndContextModifiers(string text, string selected) => Assert.DoesNotContain(Actions(text, selected), action => action.Title == "Extract selection to local VAR");

    [Theory]
    [InlineData("1 + 2 * 3", "2")]
    [InlineData("(1 + 2) * 3", "(1 + 2)")]
    [InlineData("CALCULATE([Revenue], 'Sales'[Amount] > 2)", "[Revenue]")]
    [InlineData("VAR a = 1 + 2 RETURN a * 3", "1 + 2")]
    [InlineData("IF(TRUE(), [Revenue], 0)", "[Revenue]")]
    public void ExtractAcceptsOnlyCompleteValuePositions(string text, string selected) => Extract(Actions(text, selected));

    [Fact]
    public void ExtractDoesNotRewriteObjectDeclarationsOrUdfParameterDefaults()
    {
        Assert.DoesNotContain(Actions("DEFINE MEASURE 'Sales'[Other] = 1 EVALUATE ROW(\"x\", [Other])", "'Sales'[Other]", DaxDocumentKind.Query), action => action.Title.StartsWith("Extract"));
        Assert.DoesNotContain(Actions("(x = 1) => x + 2", "1", DaxDocumentKind.Function), action => action.Title.StartsWith("Extract"));
    }
    [Fact]
    public void InvalidSpansAndBrokenSyntaxOfferNoExtraction()
    {
        var analysis = language.Analyze(new DaxDocument("doc", "1 + 2", 7, DaxDocumentKind.Expression), Model());
        Assert.Empty(language.GetCodeActions(analysis, new TextSpan(-1, 2))); Assert.Empty(language.GetCodeActions(analysis, new TextSpan(int.MaxValue, 1)));
        Assert.DoesNotContain(Actions("SUM((1)", "1"), action => action.Title.StartsWith("Extract"));
    }
    [Fact]
    public void InlineConstantReplacesOnlyResolvedUsesAndPreservesDeclarationAndComments()
    {
        const string text = "VAR rate = -2 /* keep declaration */ RETURN rate + SUMX('Sales', VAR rate = 3 RETURN rate) + LEN(\"rate\") // rate";
        var action = Assert.Single(Actions(text, "rate"), item => item.Title == "Inline constant variable uses"); var after = action.Apply(new DaxDocument("doc", text, 7));
        Assert.Contains("VAR rate = -2 /* keep declaration */", after); Assert.Contains("RETURN (- 2) + SUMX", after); Assert.Contains("VAR rate = 3 RETURN rate", after); Assert.Contains("LEN(\"rate\") // rate", after);
    }
    [Theory]
    [InlineData("VAR total = [Revenue] RETURN CALCULATE(total, 'Sales'[Amount] > 2)")]
    [InlineData("VAR total = RAND() RETURN total + total")]
    [InlineData("VAR total = TODAY() RETURN total")]
    [InlineData("VAR total = 1 / 0 RETURN IF(FALSE(), total, 2)")]
    public void InlineRefusesModelContextVolatileAndPotentiallyFailingInitializers(string text) => Assert.DoesNotContain(Actions(text, "total"), action => action.Title.StartsWith("Inline"));

    [Fact]
    public void UdfDependenciesIncludeMeasuresInDependencyOrderAndKeepQueryComments()
    {
        var model = Model(new DaxSymbol("tax", "Finance.Tax", DaxSymbolKind.Function, Expression: "(x : NUMERIC) => x * 0.2"),
            new DaxSymbol("net", "Finance.Net", DaxSymbolKind.Function, Expression: "() => [Revenue] - Finance.Tax([Revenue]) /* Finance.Ignore() */"));
        const string text = "// user heading\nEVALUATE ROW(\"Net\", Finance.Net())"; var action = Assert.Single(Actions(text, "Finance.Net", DaxDocumentKind.Query, model), item => item.Title == "Define UDF with dependencies");
        var after = action.Apply(new DaxDocument("doc", text, 7)); Assert.StartsWith("DEFINE\n", after); Assert.Contains("// user heading", after);
        Assert.Throws<InvalidOperationException>(() => action.Apply(new DaxDocument("doc", text, 8)));
        Assert.True(after.IndexOf("FUNCTION Finance.Tax", StringComparison.Ordinal) < after.IndexOf("FUNCTION Finance.Net", StringComparison.Ordinal)); Assert.True(after.IndexOf("MEASURE 'Sales'[Revenue]", StringComparison.Ordinal) < after.IndexOf("FUNCTION Finance.Net", StringComparison.Ordinal));
        Assert.DoesNotContain(language.Analyze(new DaxDocument("new", after), model).Diagnostics, d => d.Severity == DaxDiagnosticSeverity.Error);
    }
    [Fact]
    public void UdfDependencyExpansionRejectsCyclesAndQueryOverrides()
    {
        var cyclic = Model(new DaxSymbol("a", "Fns.A", DaxSymbolKind.Function, Expression: "() => Fns.B()"), new DaxSymbol("b", "Fns.B", DaxSymbolKind.Function, Expression: "() => Fns.A()"));
        Assert.DoesNotContain(Actions("EVALUATE ROW(\"v\", Fns.A())", "Fns.A", DaxDocumentKind.Query, cyclic), action => action.Title == "Define UDF with dependencies");
        var valid = Model(new DaxSymbol("a", "Fns.A", DaxSymbolKind.Function, Expression: "() => Fns.B()"), new DaxSymbol("b", "Fns.B", DaxSymbolKind.Function, Expression: "() => 2"));
        Assert.DoesNotContain(Actions("DEFINE FUNCTION Fns.B = () => 7 EVALUATE ROW(\"v\", Fns.A())", "Fns.A", DaxDocumentKind.Query, valid), action => action.Title == "Define UDF with dependencies");
    }
    [Fact]
    public void UdfDependencyExpansionRejectsInvalidDefinitionsAndBoundedDeepGraphs()
    {
        var invalid = Model(new DaxSymbol("a", "Fns.A", DaxSymbolKind.Function, Expression: "() => Fns.B()"), new DaxSymbol("b", "Fns.B", DaxSymbolKind.Function, Expression: "(x,x) => x"));
        Assert.DoesNotContain(Actions("EVALUATE ROW(\"v\", Fns.A())", "Fns.A", DaxDocumentKind.Query, invalid), action => action.Title == "Define UDF with dependencies");
        var chain = Enumerable.Range(0, 20).Select(i => new DaxSymbol("f" + i, "Fns.F" + i, DaxSymbolKind.Function, Expression: i == 19 ? "() => 1" : "() => Fns.F" + (i + 1) + "()")).ToArray();
        Assert.DoesNotContain(Actions("EVALUATE ROW(\"v\", Fns.F0())", "Fns.F0", DaxDocumentKind.Query, Model(chain)), action => action.Title == "Define UDF with dependencies");
    }
}
