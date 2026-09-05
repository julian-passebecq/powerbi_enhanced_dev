namespace PbiBench.Dax.LanguageService;

public sealed partial class DaxLanguageService
{
    private void AddSafeExpressionActions(DaxAnalysis analysis, TextSpan selection, List<DaxCodeAction> actions)
    {
        if (analysis.Document.Text.Length > 2 * 1024 * 1024 || analysis.Diagnostics.Any(d => d.Severity == DaxDiagnosticSeverity.Error)) return;
        if (TryExtractExpression(analysis, selection) is { } extract) actions.Add(extract);
        var reference = ReferenceAt(analysis, selection.Start);
        if (reference?.Declaration is not { Valid: true } declaration || reference.Symbol.Kind != DaxSymbolKind.Variable || !ConstantLiteral(reference.Symbol.Expression ?? "")) return;
        var uses = analysis.BoundReferences.Where(item => item.Symbol.Id == reference.Symbol.Id && !item.IsDefinition).ToArray();
        if (uses.Length == 0 || uses.Length > 200 || uses.Any(use => use.Span.Start < declaration.ScopeStart || use.Span.End > declaration.ScopeEnd)) return;
        var literal = string.Join(" ", DaxTokenizer.Tokenize(reference.Symbol.Expression!).Where(token => !DaxTokenizer.IsTrivia(token)).Select(token => token.Text));
        actions.Add(Action(analysis, "Inline constant variable uses", "Replace resolved uses of this context-independent literal. The declaration and its comments remain, preserving the surrounding VAR/RETURN structure. Model references, volatile calls and other initializers are not eligible.",
            uses.Select(use => new DaxTextEdit(use.Span, "(" + literal + ")")).ToArray()));
    }

    private DaxCodeAction? TryExtractExpression(DaxAnalysis analysis, TextSpan requested)
    {
        if (requested.Length == 0 || requested.Length > 128 * 1024) return null;
        var all = analysis.Tokens.Where(token => !DaxTokenizer.IsTrivia(token)).ToArray();
        var chosen = all.Where(token => token.Span.Start >= requested.Start && token.Span.End <= requested.End).ToArray();
        if (chosen.Length == 0 || analysis.Tokens.Any(token => token.Span.Start < requested.Start && token.Span.End > requested.Start || token.Span.Start < requested.End && token.Span.End > requested.End)) return null;
        var start = Array.IndexOf(all, chosen[0]); var end = Array.IndexOf(all, chosen[chosen.Length - 1]);
        if (start > 0 && chosen[0].Kind == DaxTokenKind.BracketIdentifier && all[start - 1].Kind is DaxTokenKind.Identifier or DaxTokenKind.QuotedIdentifier) return null;
        if (end + 1 < all.Length && all[end + 1].Kind == DaxTokenKind.BracketIdentifier && chosen[chosen.Length - 1].Kind is DaxTokenKind.Identifier or DaxTokenKind.QuotedIdentifier) return null;
        if (chosen.Any(token => Is(token, "DEFINE") || Is(token, "EVALUATE") || Is(token, "FUNCTION") || token.Text == ":" || token.Text == "=>")) return null;
        var errors = new List<DaxDiagnostic>(); CheckDelimiters(chosen, errors); if (errors.Count != 0) return null;
        if (chosen.Any(token => Is(token, "KEEPFILTERS") || Is(token, "REMOVEFILTERS") || Is(token, "USERELATIONSHIP") || Is(token, "CROSSFILTER"))) return null;
        if (analysis.Document.Kind == DaxDocumentKind.Function && all.FirstOrDefault(token => token.Text == "=>") is { } body && requested.Start < body.Span.End) return null;
        foreach (var function in analysis.Declarations.Where(item => item.Symbol.Kind == DaxSymbolKind.Function && item.ExpressionSpan != null))
            if (function.ExpressionSpan!.Value.Contains(requested.Start) && all.FirstOrDefault(token => token.Text == "=>" && function.ExpressionSpan.Value.Contains(token.Span.Start)) is { } arrow && requested.Start < arrow.Span.End) return null;
        // Only whole arguments, whole initializers/bodies, atoms and already-parenthesized subtrees
        // are eligible. A lexical substring such as 1+2 inside 1+2*3 is not an expression subtree.
        var frames = new Stack<(int Open, int Argument, int Start)>();
        for (var index = 0; index < start; index++)
        {
            if (all[index].Text is "(" or "{") frames.Push((index, 0, index + 1));
            else if (all[index].Text is ")" or "}") { if (frames.Count > 0) frames.Pop(); }
            else if (all[index].Text is "," or ";" && frames.Count > 0) { var frame = frames.Pop(); frames.Push((frame.Open, frame.Argument + 1, index + 1)); }
        }
        foreach (var frame in frames)
        {
            if (all[frame.Open].Text == "{" || frame.Open == 0) continue;
            var before = all[frame.Open - 1];
            if (before.Kind is DaxTokenKind.Identifier || before.Kind == DaxTokenKind.Keyword && analysis.Functions.ContainsKey(before.Value))
                if (!ValueArgument(before.Value, frame.Argument)) return null;
        }
        var wholeValue = analysis.Document.Kind == DaxDocumentKind.Expression && start == 0 && end == all.Length - 1;
        wholeValue |= analysis.Document.Kind == DaxDocumentKind.Function && start > 0 && all[start - 1].Text == "=>" && end == all.Length - 1;
        wholeValue |= start > 0 && Is(all[start - 1], "RETURN") && (end == all.Length - 1 || all[end + 1].Text is ")" or "," or ";");
        if (frames.Count > 0)
        {
            var frame = frames.Peek(); var after = end + 1 < all.Length ? all[end + 1].Text : "";
            wholeValue |= frame.Start == start && after is "," or ";" or ")" or "}";
        }
        foreach (var declaration in analysis.Declarations.Where(item => item.Symbol.Kind == DaxSymbolKind.Variable))
        {
            var name = Array.FindIndex(all, token => token.Span == declaration.NameSpan);
            wholeValue |= name >= 0 && start == name + 2 && end + 1 < all.Length && all[end + 1].Span.Start == declaration.ScopeStart;
        }
        if (!wholeValue && analysis.BoundReferences.Any(reference => reference.IsDefinition && chosen.Any(token => token.Span == reference.Span))) return null;
        var atom = chosen.Length == 1 && (chosen[0].Kind is DaxTokenKind.Number or DaxTokenKind.String or DaxTokenKind.Date ||
            analysis.BoundReferences.Any(reference => reference.Span == chosen[0].Span && !reference.IsDefinition && reference.Symbol.Kind is DaxSymbolKind.Variable or DaxSymbolKind.Parameter or DaxSymbolKind.Measure or DaxSymbolKind.Column));
        atom |= chosen.Length == 2 && chosen[0].Kind is DaxTokenKind.Identifier or DaxTokenKind.QuotedIdentifier && chosen[1].Kind == DaxTokenKind.BracketIdentifier;
        var parenthesized = chosen[0].Text == "(" && MatchingClose(chosen, 0) == chosen.Length - 1;
        var call = chosen.Length >= 3 && chosen[0].Kind is DaxTokenKind.Identifier or DaxTokenKind.Keyword && chosen[1].Text == "(" && MatchingClose(chosen, 1) == chosen.Length - 1;
        if (!wholeValue && !atom && !parenthesized && !call) return null;
        // A name/string argument is schema syntax in several table constructors. ValueArgument
        // rejects those positions even if their text happens to be a valid scalar literal.
        var used = new HashSet<string>(analysis.Tokens.Select(token => token.Value).Concat(analysis.Metadata.Symbols.Select(symbol => symbol.Name)), StringComparer.OrdinalIgnoreCase);
        var nameCandidate = "__pbExtracted"; var suffix = 2; while (used.Contains(nameCandidate)) nameCandidate = "__pbExtracted" + suffix++;
        var text = analysis.Document.Text.Substring(requested.Start, requested.Length);
        var replacement = "( VAR " + nameCandidate + " =\n" + text + "\nRETURN " + nameCandidate + " )";
        var action = Action(analysis, "Extract selection to local VAR", "Wrap this complete value expression at its current evaluation point. The expression is not hoisted across row or filter context; a unique local name prevents shadowing.", new[] { new DaxTextEdit(requested, replacement) });
        var candidate = action.Apply(analysis.Document);
        return Analyze(analysis.Document with { Text = candidate }, analysis.Metadata).Diagnostics.Any(d => d.Severity == DaxDiagnosticSeverity.Error) ? null : action;
    }

    private static bool ValueArgument(string function, int argument) => function.ToUpperInvariant() switch
    {
        "CALCULATE" or "CALCULATETABLE" => argument == 0,
        "ROW" => argument % 2 == 1,
        "ADDCOLUMNS" or "SELECTCOLUMNS" => argument == 0 || argument % 2 == 0,
        "SUMX" or "AVERAGEX" or "MINX" or "MAXX" or "COUNTX" or "COUNTAX" or "PRODUCTX" or "FILTER" => argument <= 1,
        "COUNTROWS" or "ISEMPTY" or "ISBLANK" or "ISERROR" or "ABS" or "INT" or "LEN" or "LOWER" or "UPPER" => argument == 0,
        "IF" or "IFERROR" or "SWITCH" or "COALESCE" or "DIVIDE" or "ROUND" or "ROUNDUP" or "ROUNDDOWN" or "MOD" or "POWER" or "CONCATENATE" or "SUBSTITUTE" => true,
        _ => false
    };

    private static bool ConstantLiteral(string expression)
    {
        var errors = new List<DaxDiagnostic>(); var tokens = DaxTokenizer.Lex(expression, errors, CancellationToken.None).Where(token => !DaxTokenizer.IsTrivia(token)).ToArray();
        if (errors.Count != 0) return false;
        while (tokens.Length >= 2 && tokens[0].Text == "(" && MatchingClose(tokens, 0) == tokens.Length - 1) tokens = tokens.Skip(1).Take(tokens.Length - 2).ToArray();
        return tokens.Length == 1 && (tokens[0].Kind is DaxTokenKind.Number or DaxTokenKind.String or DaxTokenKind.Date || Is(tokens[0], "TRUE") || Is(tokens[0], "FALSE")) ||
            tokens.Length == 2 && tokens[0].Text is "+" or "-" && tokens[1].Kind == DaxTokenKind.Number ||
            tokens.Length == 3 && (Is(tokens[0], "BLANK") || Is(tokens[0], "TRUE") || Is(tokens[0], "FALSE")) && tokens[1].Text == "(" && tokens[2].Text == ")";
    }

    private DaxCodeAction? DefineFunctionWithDependencies(DaxAnalysis source, DaxSymbol root)
    {
        var ordered = new List<DaxSymbol>(); var visiting = new HashSet<string>(StringComparer.Ordinal); var done = new HashSet<string>(StringComparer.Ordinal); var bytes = 0;
        if (!Visit(root, 0) || ordered.Count < 2) return null;
        var first = source.Tokens.FirstOrDefault(token => !DaxTokenizer.IsTrivia(token)); var hasDefine = first != null && Is(first, "DEFINE");
        var text = (hasDefine ? "" : "DEFINE") + "\n" + string.Join("\n", ordered.Select(symbol => "    " + (symbol.Kind == DaxSymbolKind.Function ? "FUNCTION " + symbol.Name : "MEASURE " + symbol.QualifiedName) + " = " + symbol.Expression + "\n"));
        return Action(source, "Define UDF with dependencies", "Preview " + ordered.Count + " query-local UDF/measure definitions in dependency order. Table/column data remain engine references. Cycles, invalid bodies and existing query overrides are excluded.", new[] { new DaxTextEdit(new TextSpan(hasDefine ? first!.Span.End : 0, 0), text) });

        bool Visit(DaxSymbol symbol, int depth)
        {
            if (done.Contains(symbol.Id)) return true;
            if (depth > 16 || done.Count + visiting.Count >= 64 || !visiting.Add(symbol.Id) || string.IsNullOrWhiteSpace(symbol.Expression)) return false;
            bytes += symbol.Expression!.Length; if (bytes > 256 * 1024) return false;
            if (source.Declarations.Any(local => local.Symbol.Kind == symbol.Kind && Same(local.Symbol.Name, symbol.Name) && (symbol.Kind == DaxSymbolKind.Function || Same(local.Symbol.Table, symbol.Table)))) return false;
            if (symbol.Kind == DaxSymbolKind.Function && !TryFunctionSignature(symbol.Name, symbol.Expression!, out _)) return false;
            var analysis = Analyze(new DaxDocument("dependency:" + symbol.Id, symbol.Expression!, Kind: symbol.Kind == DaxSymbolKind.Function ? DaxDocumentKind.Function : DaxDocumentKind.Expression, CurrentTable: symbol.Table), source.Metadata);
            if (analysis.Diagnostics.Any(d => d.Severity == DaxDiagnosticSeverity.Error)) return false;
            var tokens = analysis.Tokens.Where(token => !DaxTokenizer.IsTrivia(token)).ToArray();
            // Invalid model UDFs are intentionally absent from bound references; don't silently omit them.
            if (tokens.Where((token, index) => index + 1 < tokens.Length && tokens[index + 1].Text == "(").Any(token => source.Metadata.Symbols.Any(item => item.Kind == DaxSymbolKind.Function && Same(item.Name, token.Value) && !TryFunctionSignature(item.Name, item.Expression ?? "", out _)))) return false;
            foreach (var dependency in analysis.BoundReferences.Where(reference => reference.Declaration == null && reference.Symbol.Kind is DaxSymbolKind.Function or DaxSymbolKind.Measure).Select(reference => reference.Symbol).GroupBy(item => item.Id).Select(group => group.First()).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                if (!Visit(dependency, depth + 1)) return false;
            visiting.Remove(symbol.Id); done.Add(symbol.Id); ordered.Add(symbol); return true;
        }
    }
}
