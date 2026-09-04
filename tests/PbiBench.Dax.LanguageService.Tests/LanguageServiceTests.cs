using System.Diagnostics;
using PbiBench.Dax.LanguageService;
using Xunit;
using Xunit.Abstractions;

namespace PbiBench.Dax.LanguageService.Tests;

public sealed class LanguageServiceTests(ITestOutputHelper output)
{
    private readonly DaxLanguageService service = new();
    private static readonly DaxMetadataSnapshot Model = new(new[]
    {
        new DaxSymbol("table:sales", "Sales", DaxSymbolKind.Table),
        new DaxSymbol("column:amount", "Amount", DaxSymbolKind.Column, "Sales", DataType: "Decimal"),
        new DaxSymbol("measure:revenue", "Revenue", DaxSymbolKind.Measure, "Sales", "SUM ( Sales[Amount] )", "Sales revenue."),
        new DaxSymbol("table:escape", "Owner's Sales", DaxSymbolKind.Table),
        new DaxSymbol("column:escape", "Amount] EUR", DaxSymbolKind.Column, "Owner's Sales"),
        new DaxSymbol("function:tax", "Finance.Tax", DaxSymbolKind.Function, Expression: "(value : SCALAR NUMERIC, rate : SCALAR DOUBLE VAL = 0.2) => value * rate"),
        new DaxSymbol("function:invalid", "Finance.Broken", DaxSymbolKind.Function, Expression: "(x) =>")
    });
    private DaxAnalysis Analyze(string text, DaxDocumentKind kind = DaxDocumentKind.Query) => service.Analyze(new DaxDocument("query", text, 1, kind), Model);

    [Fact]
    public void LexerRetainsEverySourceCharacterAndModernSyntax()
    {
        const string source = "/* outer /* inner */ end */ DEFINE FUNCTION Finance.Tax = (x : SCALAR NUMERIC EXPR = 1e-3) => x\r\nEVALUATE ROW(\"d\",dt\"2026-01-01\",\"m\",'Owner''s Sales'[Amount]] EUR]) //comment";
        var tokens = DaxTokenizer.Tokenize(source);
        Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
        Assert.All(tokens, token => Assert.Equal(token.Text, source.Substring(token.Span.Start, token.Span.Length)));
        Assert.Contains(tokens, token => token.Text == "Finance.Tax" && token.Kind == DaxTokenKind.Identifier);
        Assert.Contains(tokens, token => token.Kind == DaxTokenKind.Date);
        Assert.Contains(tokens, token => token.Value == "Amount] EUR");
        Assert.Contains(tokens, token => token.Kind == DaxTokenKind.Number && token.Text == "1e-3");
        Assert.Single(tokens, token => token.Text.StartsWith("/*"));
        Assert.Empty(Analyze(source).Diagnostics);
    }

    [Theory]
    [InlineData("(x) => x")]
    [InlineData("() => 42")]
    [InlineData("(x : SCALAR NUMERIC EXPR, y : SCALAR DOUBLE VAL = DIVIDE(1,2)) => x+y")]
    [InlineData("(t : TABLE, c : COLUMNREF, m : MEASUREREF, cal : CALENDARREF) => COUNTROWS(t)")]
    [InlineData("(x : NUMERIC, y : EXPR) => x+y")]
    [InlineData("FUNCTION Finance.Tax = (x : ANYVAL = dt\"2026-01-01\") => x")]
    [InlineData("DEFINE FUNCTION F = (x : ANYREF, y : TABLEREF) => 1")]
    public void ValidUdfSyntaxHasSignatureAndNoFalseDiagnostics(string expression)
    {
        Assert.True(DaxLanguageService.TryFunctionSignature("Finance.Tax", expression, out var signature));
        Assert.NotNull(signature);
        var body = expression.StartsWith("DEFINE") ? expression : expression.StartsWith("FUNCTION") ? "DEFINE " + expression : "DEFINE FUNCTION Finance.Tax = " + expression;
        var analysis = Analyze(body + "\n EVALUATE ROW (\"a\", Finance.Tax(1))");
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("(x) =>")]
    [InlineData("(x, x) => x")]
    [InlineData("(measure) => 1")]
    [InlineData("(x : BANANA) => x")]
    [InlineData("(x : TABLE NUMERIC) => x")]
    [InlineData("(x : SCALAR SCALAR) => x")]
    [InlineData("(x = ) => x")]
    [InlineData("(x,) => x")]
    [InlineData("(x) x")]
    public void InvalidFunctionDefinitionsAreNotOffered(string expression)
    {
        Assert.False(DaxLanguageService.TryFunctionSignature("Broken", expression, out _));
        var model = new DaxMetadataSnapshot(new[] { new DaxSymbol("broken", "Broken", DaxSymbolKind.Function, Expression: expression) });
        var analysis = service.Analyze(new DaxDocument("q", "Bro"), model);
        Assert.DoesNotContain(service.Complete(analysis, 3), item => item.Label == "Broken");
    }

    [Fact]
    public void CompletionUsesMetadataAndEscapesQualifiedIdentifiers()
    {
        var analysis = Analyze("EVALUATE ROW(\"x\", 'Owner''s Sales'[Am");
        var completion = Assert.Single(service.Complete(analysis, analysis.Document.Text.Length), item => item.Label == "Amount] EUR");
        Assert.Equal("[Amount]] EUR]", completion.InsertText);
        Assert.Equal("[Am", analysis.Document.Text.Substring(completion.ReplaceSpan.Start, completion.ReplaceSpan.Length));
        Assert.DoesNotContain(service.Complete(analysis, analysis.Document.Text.Length), item => item.Label == "Amount");
        var functions = service.Complete(Analyze("Finance."), 8);
        Assert.Contains(functions, item => item.Label == "Finance.Tax");
        Assert.DoesNotContain(functions, item => item.Label == "Finance.Broken");
        var returnReference = Analyze("VAR x = 1 RETURN [Rev");
        Assert.Contains(service.Complete(returnReference, returnReference.Document.Text.Length), item => item.Label == "Revenue");
    }

    [Fact]
    public void ReferenceTypedFunctionParametersFilterMetadataSuggestions()
    {
        var model = new DaxMetadataSnapshot(Model.Symbols.Concat(new[]
        { new DaxSymbol("function:ref", "ByColumn", DaxSymbolKind.Function, Expression: "(col : COLUMNREF) => COUNT(col)") }));
        var analysis = service.Analyze(new DaxDocument("q", "ByColumn("), model);
        var suggestions = service.Complete(analysis, analysis.Document.Text.Length);
        Assert.Contains(suggestions, item => item.Label == "Amount");
        Assert.DoesNotContain(suggestions, item => item.Label == "Revenue");
        Assert.DoesNotContain(suggestions, item => item.Label == "Sales");
    }

    [Fact]
    public void BrokenQueryFunctionIsNotSuggestedAndFunctionLocalsDoNotLeak()
    {
        var broken = Analyze("DEFINE FUNCTION Broken = (x) => SUM(x\n EVALUATE ROW(\"v\", Bro");
        Assert.DoesNotContain(service.Complete(broken, broken.Document.Text.Length), item => item.Label == "Broken");
        var scoped = Analyze("DEFINE FUNCTION F = (x) => VAR privateValue = x RETURN privateValue\n VAR queryValue = 1\n EVALUATE ROW(\"v\", pri");
        Assert.DoesNotContain(service.Complete(scoped, scoped.Document.Text.Length), item => item.Label == "privateValue");
    }

    [Fact]
    public void CompletionAndSignatureHelpAreQuietInsideStringsAndComments()
    {
        foreach (var text in new[] { "// CALCULATE(", "/* SUM(", "ROW(\"SUM(", "dt\"2026-01" })
        {
            var analysis = Analyze(text);
            Assert.Empty(service.Complete(analysis, text.Length));
            Assert.Null(service.GetSignatureHelp(analysis, text.Length - 1));
        }
    }

    [Fact]
    public void SignatureHelpTracksNestedCallsAndUdfArguments()
    {
        const string text = "EVALUATE ROW(\"v\", Finance.Tax(SUM(Sales[Amount]), ";
        var help = service.GetSignatureHelp(Analyze(text), text.Length);
        Assert.NotNull(help); Assert.Equal("Finance.Tax", help.Signature.Name); Assert.Equal(1, help.ActiveParameter);
        Assert.Contains("rate : SCALAR DOUBLE VAL = 0.2", help.Signature.Parameters);
        Assert.True(DaxFunctionCatalog.BuiltIns.Count >= 352);
    }

    [Fact]
    public void DefinitionsAndReferencesResolveModelObjectsAndEscapes()
    {
        const string text = "EVALUATE ROW(\"x\", [Revenue] + [Revenue], \"y\", SUM('Owner''s Sales'[Amount]] EUR]))";
        var analysis = Analyze(text);
        var definition = service.FindDefinition(analysis, text.IndexOf("[Revenue]", StringComparison.Ordinal) + 2);
        Assert.NotNull(definition); Assert.Equal("measure:revenue", definition.SymbolId); Assert.Null(definition.DocumentId);
        Assert.Equal(2, service.FindReferences(analysis, text.IndexOf("[Revenue]", StringComparison.Ordinal)).Count);
        var escaped = service.FindDefinition(analysis, text.IndexOf("[Amount", StringComparison.Ordinal) + 2);
        Assert.Equal("column:escape", escaped?.SymbolId);
    }

    [Fact]
    public void QueryDefinitionsShadowModelWithoutMutatingSnapshot()
    {
        const string text = "DEFINE MEASURE Sales[Revenue] = 7\n EVALUATE ROW(\"v\",[Revenue])";
        var analysis = Analyze(text);
        var definition = service.FindDefinition(analysis, text.LastIndexOf("[Revenue]", StringComparison.Ordinal) + 2);
        Assert.NotNull(definition); Assert.Equal("query", definition.DocumentId); Assert.Equal("7", definition.Expression);
        Assert.Equal(2, service.FindReferences(analysis, text.LastIndexOf("[Revenue]", StringComparison.Ordinal) + 2).Count);
        Assert.Equal("SUM ( Sales[Amount] )", Model.Symbols.Single(symbol => symbol.Id == "measure:revenue").Expression);
    }

    [Fact]
    public void NestedVariableScopesResolveAndRenameOnlyTheirOwnReferences()
    {
        const string text = "VAR amount = 1 RETURN amount + (VAR amount = 2 RETURN amount) + amount // amount\n + LEN(\"amount\")";
        var analysis = Analyze(text, DaxDocumentKind.Expression);
        var innerCaret = text.IndexOf("RETURN amount)", StringComparison.Ordinal) + 8;
        var definition = service.FindDefinition(analysis, innerCaret);
        Assert.NotNull(definition); Assert.Equal(text.IndexOf("amount = 2", StringComparison.Ordinal), definition.Span!.Value.Start);
        var renamed = service.RenameLocalVariable(analysis, innerCaret, "innerAmount").Apply(analysis.Document);
        Assert.Equal("VAR amount = 1 RETURN amount + (VAR innerAmount = 2 RETURN innerAmount) + amount // amount\n + LEN(\"amount\")", renamed);
        Assert.Equal(3, service.FindReferences(analysis, text.IndexOf("RETURN amount", StringComparison.Ordinal) + 8).Count);
    }

    [Fact]
    public void ParametersAndDefineVariablesResolveAcrossQueryStatements()
    {
        const string text = "DEFINE FUNCTION Finance.Tax = (value : NUMERIC, rate = 0.2) => value * rate\n VAR chosen = 2\n EVALUATE ROW(\"v\", chosen)\n EVALUATE ROW(\"v\", chosen)";
        var analysis = Analyze(text);
        var parameter = service.FindDefinition(analysis, text.IndexOf("value *", StringComparison.Ordinal));
        Assert.Equal(DaxSymbolKind.Parameter, parameter?.Kind);
        Assert.Equal(3, service.FindReferences(analysis, text.LastIndexOf("chosen", StringComparison.Ordinal)).Count);
        // A standalone DEFINE VAR is shared by every EVALUATE in its query.
        var queryVariables = Analyze("DEFINE VAR chosen = 2\n EVALUATE ROW(\"v\", chosen)\n EVALUATE ROW(\"v\", chosen)");
        Assert.Equal(3, service.FindReferences(queryVariables, queryVariables.Document.Text.LastIndexOf("chosen", StringComparison.Ordinal)).Count);
        var standalone = Analyze("(x : NUMERIC) => x * 2", DaxDocumentKind.Function);
        Assert.Equal(DaxSymbolKind.Parameter, service.FindDefinition(standalone, standalone.Document.Text.LastIndexOf("x", StringComparison.Ordinal))?.Kind);
    }

    [Fact]
    public void DiagnosticsFlagClearStructuralAndQualifiedObjectErrorsOnly()
    {
        Assert.Contains(Analyze("SUM (Sales[Amount]").Diagnostics, error => error.Id == "DAX004");
        Assert.Contains(Analyze("SUM(Sales[Missing])").Diagnostics, error => error.Id == "DAX020");
        Assert.Empty(Analyze("EVALUATE FILTER(ADDCOLUMNS(Sales, \"Virtual\", 1), [Virtual] > 0)").Diagnostics);
        Assert.Empty(Analyze("NewFutureFunction ( 1 )").Diagnostics);
        const string virtualShadow = "EVALUATE FILTER(ADDCOLUMNS(Sales, \"Revenue\", 1), [Revenue] > 0)";
        Assert.Null(service.FindDefinition(Analyze(virtualShadow), virtualShadow.IndexOf("[Revenue]", StringComparison.Ordinal) + 2));
    }

    [Fact]
    public void CodeActionsAreReviewableAndRejectStaleTextOrVersions()
    {
        var analysis = Analyze("EVALUATE ROW(\"v\",[Revenue])");
        var caret = analysis.Document.Text.IndexOf("[Revenue]", StringComparison.Ordinal);
        var actions = service.GetCodeActions(analysis, new TextSpan(caret, 0));
        var define = Assert.Single(actions, action => action.Title == "Define measure in query");
        Assert.StartsWith("DEFINE\n    MEASURE 'Sales'[Revenue]", define.Apply(analysis.Document));
        Assert.Throws<InvalidOperationException>(() => define.Apply(analysis.Document with { Version = 2 }));
        Assert.Throws<InvalidOperationException>(() => define.Apply(analysis.Document with { Text = "EVALUATE Sales" }));
        var expression = Analyze("[Revenue]", DaxDocumentKind.Expression);
        Assert.DoesNotContain(service.GetCodeActions(expression, new TextSpan(2, 0)), action => action.Title.StartsWith("Define"));
        var qualified = Assert.Single(service.GetCodeActions(expression, new TextSpan(2, 0)));
        Assert.Equal("'Sales'[Revenue]", qualified.Apply(expression.Document));
        var local = Analyze("VAR x = 1 RETURN x", DaxDocumentKind.Expression);
        Assert.Throws<InvalidOperationException>(() => service.RenameLocalVariable(local, local.Document.Text.Length - 1, "Sales"));
    }

    [Fact]
    public void CancellationIsObservedAndSnapshotIsDetachedFromCallerList()
    {
        var symbols = new List<DaxSymbol> { new("sales", "Sales", DaxSymbolKind.Table) };
        var snapshot = new DaxMetadataSnapshot(symbols); symbols.Clear(); Assert.Single(snapshot.Symbols);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => service.Analyze(new DaxDocument("q", "SUM(Sales[Amount])"), Model, cancel.Token));
    }

    [Fact]
    public void LargeMetadataCompletionRemainsBoundedAndResponsive()
    {
        var model = new DaxMetadataSnapshot(Enumerable.Range(0, 20000).Select(index => new DaxSymbol(index.ToString(), "Measure" + index, DaxSymbolKind.Measure, "Sales", "1")));
        var watch = Stopwatch.StartNew();
        var analysis = service.Analyze(new DaxDocument("q", "EVALUATE ROW(\"v\", [Measure19"), model);
        var completion = service.Complete(analysis, analysis.Document.Text.Length); watch.Stop();
        output.WriteLine($"20,000 model symbols: analysis + completion {watch.ElapsedMilliseconds} ms; {completion.Count} visible suggestions.");
        Assert.InRange(completion.Count, 1, 400); Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }
}

public sealed class QueryAndNavigationTests
{
    private const string Query = "// EVALUATE comment\nDEFINE VAR amount = 3\n FUNCTION Fn = (x) => x\nEVALUATE ROW(\"EVALUATE\",Fn(amount))\nORDER BY [EVALUATE]\n// another\nEVALUATE ROW(\"value\", { 1, 2 })";
    [Fact]
    public void ExecuteAllIsByteForByteAndCurrentPreservesDefinitions()
    {
        var document = new DaxDocument("q", Query);
        Assert.Equal(Query, DaxQueryPlanner.Prepare(document, DaxExecutionMode.All).QueryText);
        var plan = DaxQueryPlanner.Prepare(document, DaxExecutionMode.CurrentStatement, Query.LastIndexOf("ROW", StringComparison.Ordinal));
        Assert.Equal(1, plan.StatementCount); Assert.Contains("DEFINE VAR amount = 3", plan.QueryText);
        Assert.Contains("FUNCTION Fn = (x) => x", plan.QueryText);
        Assert.Contains("EVALUATE ROW(\"value\", { 1, 2 })", plan.QueryText);
        Assert.DoesNotContain("ORDER BY", plan.QueryText);
        Assert.Equal(2, DaxQueryPlanner.StatementSpans(Query).Count);
    }
    [Fact]
    public void ExecuteSelectionRetainsExactSelectedStatementAndPreamble()
    {
        var start = Query.LastIndexOf("EVALUATE ROW", StringComparison.Ordinal);
        var plan = DaxQueryPlanner.Prepare(new DaxDocument("q", Query), DaxExecutionMode.Selection, selection: new TextSpan(start, Query.Length - start));
        Assert.EndsWith(Query.Substring(start), plan.QueryText); Assert.Contains("DEFINE VAR", plan.QueryText);
        Assert.Throws<InvalidOperationException>(() => DaxQueryPlanner.Prepare(new DaxDocument("q", Query), DaxExecutionMode.Selection));
        Assert.Throws<InvalidOperationException>(() => DaxQueryPlanner.Prepare(new DaxDocument("q", "SUM(Sales[Amount])"), DaxExecutionMode.CurrentStatement));
    }
    [Fact]
    public void CurrentStatementFromDefinitionsUsesFirstEvaluate()
    {
        var plan = DaxQueryPlanner.Prepare(new DaxDocument("q", Query), DaxExecutionMode.CurrentStatement, 0);
        Assert.Contains("Fn(amount)", plan.QueryText); Assert.DoesNotContain("\"value\"", plan.QueryText);
    }
    [Fact]
    public void HistoryTruncatesForwardBranchAndBoundsRetainedLocations()
    {
        var history = new DaxNavigationHistory(3);
        for (var index = 0; index < 5; index++) history.Visit(new DaxNavigationPoint("q", index));
        Assert.Equal(3, history.Back()?.Offset); Assert.Equal(2, history.Back()?.Offset); Assert.Null(history.Back());
        Assert.Equal(3, history.Forward()?.Offset); history.Visit(new DaxNavigationPoint("other", 50));
        Assert.False(history.CanGoForward); Assert.Equal("other", history.Current?.DocumentId);
        history.Visit(new DaxNavigationPoint("other", 50)); Assert.Equal(3, history.Back()?.Offset);
    }
}
