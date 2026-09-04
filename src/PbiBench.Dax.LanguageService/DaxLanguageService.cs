using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace PbiBench.Dax.LanguageService;

/// <summary>Original offline editor assistance. Engine execution remains authoritative for complete DAX semantics.</summary>
public sealed partial class DaxLanguageService
{
    public DaxAnalysis Analyze(DaxDocument document, DaxMetadataSnapshot metadata, CancellationToken ct = default)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (metadata == null) throw new ArgumentNullException(nameof(metadata));
        ct.ThrowIfCancellationRequested();
        var diagnostics = new List<DaxDiagnostic>();
        var tokens = DaxTokenizer.Lex(document.Text, diagnostics, ct);
        var significant = tokens.Where(token => !DaxTokenizer.IsTrivia(token)).ToArray();
        CheckDelimiters(significant, diagnostics);
        var functions = DaxFunctionCatalog.BuiltIns.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var validModelFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in metadata.Symbols.Where(symbol => symbol.Kind == DaxSymbolKind.Function))
        {
            ct.ThrowIfCancellationRequested();
            if (TryFunctionSignature(symbol.Name, symbol.Expression ?? "", out var signature))
            { functions[symbol.Name] = signature!; validModelFunctions.Add(symbol.Id); }
        }
        var declarations = ReadDeclarations(document, significant, diagnostics, functions, ct);
        var references = Bind(document, metadata, significant, declarations, diagnostics, validModelFunctions, ct);
        return new DaxAnalysis(document, metadata, tokens, diagnostics, declarations, references, new ReadOnlyDictionary<string, DaxSignature>(functions));
    }

    public IReadOnlyList<DaxCompletion> Complete(DaxAnalysis analysis, int caret)
    {
        caret = Clamp(caret, 0, analysis.Document.Text.Length);
        var active = analysis.Tokens.LastOrDefault(token => token.Span.Start < caret && token.Span.End >= caret);
        if (active?.Kind is DaxTokenKind.Comment or DaxTokenKind.String or DaxTokenKind.Date) return Array.Empty<DaxCompletion>();
        var span = active != null && active.Kind is DaxTokenKind.Identifier or DaxTokenKind.Keyword or DaxTokenKind.QuotedIdentifier or DaxTokenKind.BracketIdentifier
            ? active.Span : new TextSpan(caret, 0);
        var prefix = analysis.Document.Text.Substring(span.Start, caret - span.Start).TrimStart('[', '\'').Replace("''", "'").Replace("]]", "]");
        var previous = analysis.Tokens.LastOrDefault(token => !DaxTokenizer.IsTrivia(token) && token.Span.End <= span.Start);
        var qualifiedTable = active?.Kind == DaxTokenKind.BracketIdentifier && previous != null &&
            (previous.Kind is DaxTokenKind.Identifier or DaxTokenKind.QuotedIdentifier || previous.Kind == DaxTokenKind.Keyword && analysis.Metadata.Symbols.Any(symbol => symbol.Kind == DaxSymbolKind.Table && Same(symbol.Name, previous.Value))) ? previous.Value : null;
        var bracket = active?.Kind == DaxTokenKind.BracketIdentifier;
        var quote = active?.Kind == DaxTokenKind.QuotedIdentifier;
        var items = new List<DaxCompletion>();
        var signatureHelp = GetSignatureHelp(analysis, caret);
        var parameterHint = signatureHelp != null && signatureHelp.ActiveParameter < signatureHelp.Signature.Parameters.Count
            ? signatureHelp.Signature.Parameters[signatureHelp.ActiveParameter].ToUpperInvariant() : "";
        DaxSymbolKind? referenceKind = parameterHint.Contains("COLUMNREF") ? DaxSymbolKind.Column : parameterHint.Contains("MEASUREREF") ? DaxSymbolKind.Measure : parameterHint.Contains("TABLEREF") ? DaxSymbolKind.Table : (DaxSymbolKind?)null;
        var symbols = VisibleDeclarations(analysis, caret).Select(declaration => declaration.Symbol)
            .Concat(analysis.Metadata.Symbols).GroupBy(SymbolKey, StringComparer.OrdinalIgnoreCase).Select(group => group.First());
        foreach (var symbol in symbols)
        {
            if (!symbol.Name.StartsWith(prefix.TrimEnd(']', '\''), StringComparison.OrdinalIgnoreCase)) continue;
            if (referenceKind != null && symbol.Kind != referenceKind && symbol.Kind is not (DaxSymbolKind.Variable or DaxSymbolKind.Parameter)) continue;
            if (symbol.Kind == DaxSymbolKind.Function && !analysis.Functions.ContainsKey(symbol.Name)) continue;
            if (qualifiedTable != null && (!Same(symbol.Table, qualifiedTable) || symbol.Kind is not (DaxSymbolKind.Column or DaxSymbolKind.Measure))) continue;
            if (bracket && symbol.Kind is not (DaxSymbolKind.Column or DaxSymbolKind.Measure or DaxSymbolKind.CalculationItem)) continue;
            if (quote && symbol.Kind != DaxSymbolKind.Table) continue;
            var insert = symbol.Kind switch
            {
                DaxSymbolKind.Table => DaxSymbol.QuoteTable(symbol.Name),
                DaxSymbolKind.Column => qualifiedTable != null ? DaxSymbol.QuoteMember(symbol.Name) : symbol.QualifiedName,
                DaxSymbolKind.Measure or DaxSymbolKind.CalculationItem => DaxSymbol.QuoteMember(symbol.Name),
                _ => symbol.Name
            };
            items.Add(new DaxCompletion(symbol.Name, insert, symbol.Kind,
                symbol.QualifiedName + (symbol.DataType == null ? "" : " · " + symbol.DataType) + (symbol.Description == null ? "" : "\n" + symbol.Description), span));
        }
        if (!bracket && !quote && referenceKind == null)
        {
            foreach (var signature in analysis.Functions.Values.Where(signature => signature.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                if (!items.Any(item => item.Kind == DaxSymbolKind.Function && Same(item.Label, signature.Name)))
                    items.Add(new DaxCompletion(signature.Name, signature.Name, DaxSymbolKind.Function, signature.Label + "\n" + signature.Description, span));
            foreach (var keyword in DaxTokenizer.Keywords.Where(keyword => keyword.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                if (!items.Any(item => Same(item.Label, keyword))) items.Add(new DaxCompletion(keyword, keyword, DaxSymbolKind.Variable, "DAX keyword", span));
        }
        return items.OrderBy(item => item.Kind is DaxSymbolKind.Variable or DaxSymbolKind.Parameter ? 0 : 1)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase).Take(400).ToArray();
    }

    public DaxSignatureHelp? GetSignatureHelp(DaxAnalysis analysis, int caret)
    {
        caret = Clamp(caret, 0, analysis.Document.Text.Length);
        var containing = analysis.Tokens.FirstOrDefault(token => token.Span.Contains(caret));
        if (containing?.Kind is DaxTokenKind.Comment or DaxTokenKind.String or DaxTokenKind.Date) return null;
        var tokens = analysis.Tokens.Where(token => !DaxTokenizer.IsTrivia(token) && token.Span.Start < caret).ToArray();
        var stack = new Stack<(int Index, int Parameter)>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Text is "(" or "{") stack.Push((i, 0));
            else if (tokens[i].Text is ")" or "}") { if (stack.Count > 0) stack.Pop(); }
            else if (tokens[i].Text is "," or ";" && stack.Count > 0)
            { var frame = stack.Pop(); stack.Push((frame.Index, frame.Parameter + 1)); }
        }
        foreach (var frame in stack)
        {
            if (frame.Index == 0 || tokens[frame.Index].Text != "(") continue;
            var function = tokens[frame.Index - 1];
            if (analysis.Functions.TryGetValue(function.Value, out var signature))
                return new DaxSignatureHelp(signature, frame.Parameter, new TextSpan(function.Span.Start, caret - function.Span.Start));
        }
        return null;
    }

    public DaxSymbolLocation? FindDefinition(DaxAnalysis analysis, int caret)
    {
        var reference = ReferenceAt(analysis, caret);
        if (reference == null) return null;
        var declaration = reference.Declaration;
        return new DaxSymbolLocation(reference.Symbol.Id, reference.Symbol.Name, reference.Symbol.Kind,
            declaration?.DocumentId, declaration?.NameSpan, reference.Symbol.Expression, reference.Symbol.Description);
    }

    public IReadOnlyList<DaxReference> FindReferences(DaxAnalysis analysis, int caret)
    {
        var symbol = ReferenceAt(analysis, caret)?.Symbol;
        return symbol == null ? Array.Empty<DaxReference>() : analysis.BoundReferences.Where(reference => reference.Symbol.Id == symbol.Id)
            .Select(reference => new DaxReference(symbol.Id, analysis.Document.Id, reference.Span, reference.IsDefinition)).ToArray();
    }

    public IReadOnlyList<DaxCodeAction> GetCodeActions(DaxAnalysis analysis, TextSpan selection)
    {
        var result = new List<DaxCodeAction>();
        if (selection.Start < 0 || selection.Length < 0 || (long)selection.Start + selection.Length > analysis.Document.Text.Length) return result;
        AddSafeExpressionActions(analysis, selection, result);
        var reference = ReferenceAt(analysis, selection.Start);
        if (reference == null) return result;
        if (!reference.IsDefinition && reference.Symbol.Kind is DaxSymbolKind.Column or DaxSymbolKind.Measure && reference.Symbol.Table != null)
        {
            var token = analysis.Tokens.FirstOrDefault(item => item.Span == reference.Span);
            var previous = analysis.Tokens.LastOrDefault(item => !DaxTokenizer.IsTrivia(item) && item.Span.End <= reference.Span.Start);
            if (token?.Kind == DaxTokenKind.BracketIdentifier && previous?.Kind is not (DaxTokenKind.Identifier or DaxTokenKind.QuotedIdentifier or DaxTokenKind.Keyword))
                result.Add(Action(analysis, "Qualify model reference", "Preview an explicit table-qualified reference to the same resolved model object.", new[] { new DaxTextEdit(reference.Span, reference.Symbol.QualifiedName) }));
        }
        if (analysis.Document.Kind is DaxDocumentKind.Query or DaxDocumentKind.Script && reference.Symbol.Kind == DaxSymbolKind.Measure && reference.Declaration == null && !string.IsNullOrWhiteSpace(reference.Symbol.Expression))
        {
            var first = analysis.Tokens.FirstOrDefault(token => !DaxTokenizer.IsTrivia(token));
            var hasDefine = first != null && Is(first, "DEFINE");
            var at = hasDefine ? first!.Span.End : 0;
            var text = (hasDefine ? "" : "DEFINE") + "\n    MEASURE " + reference.Symbol.QualifiedName + " = " + reference.Symbol.Expression + "\n";
            result.Add(Action(analysis, "Define measure in query", "Preview a query-local measure definition. The model is unchanged.", new[] { new DaxTextEdit(new TextSpan(at, 0), text) }));
        }
        if (analysis.Document.Kind is DaxDocumentKind.Query or DaxDocumentKind.Script && reference.Symbol.Kind == DaxSymbolKind.Function && reference.Declaration == null && !string.IsNullOrWhiteSpace(reference.Symbol.Expression))
        {
            var first = analysis.Tokens.FirstOrDefault(token => !DaxTokenizer.IsTrivia(token));
            var hasDefine = first != null && Is(first, "DEFINE");
            result.Add(Action(analysis, "Define UDF in query", "Preview a query-local function definition. The model is unchanged.", new[]
            { new DaxTextEdit(new TextSpan(hasDefine ? first!.Span.End : 0, 0), (hasDefine ? "" : "DEFINE") + "\n    FUNCTION " + reference.Symbol.Name + " = " + reference.Symbol.Expression + "\n") }));
            var dependencies = DefineFunctionWithDependencies(analysis, reference.Symbol);
            if (dependencies != null) result.Add(dependencies);
        }
        return result;
    }

    public DaxCodeAction RenameLocalVariable(DaxAnalysis analysis, int caret, string newName)
    {
        var reference = ReferenceAt(analysis, caret);
        if (reference?.Declaration == null || reference.Symbol.Kind is not (DaxSymbolKind.Variable or DaxSymbolKind.Parameter))
            throw new InvalidOperationException("Select a local variable or function parameter to rename.");
        if (!Regex.IsMatch(newName ?? "", @"^[\p{L}_][\p{L}\p{N}_]*$") || DaxTokenizer.Keywords.Any(keyword => Same(keyword, newName)) || DaxFunctionCatalog.BuiltIns.ContainsKey(newName!))
            throw new ArgumentException("Use a valid nonreserved DAX variable name.", nameof(newName));
        if (analysis.Declarations.Any(declaration => declaration.Symbol.Id != reference.Symbol.Id && Same(declaration.Symbol.Name, newName) &&
            declaration.ScopeStart < reference.Declaration.ScopeEnd && declaration.ScopeEnd > reference.Declaration.ScopeStart))
            throw new InvalidOperationException("That name is already declared in an overlapping scope.");
        if (analysis.Metadata.Symbols.Any(symbol => symbol.Kind == DaxSymbolKind.Table && Same(symbol.Name, newName)))
            throw new InvalidOperationException("A local variable cannot use an existing model table name.");
        var edits = analysis.BoundReferences.Where(item => item.Symbol.Id == reference.Symbol.Id).Select(item => new DaxTextEdit(item.Span, newName!)).ToArray();
        return Action(analysis, "Rename " + reference.Symbol.Name + " to " + newName, "Preview the declaration and resolved references. Comments, strings and other scopes are preserved.", edits);
    }

    private static DaxCodeAction Action(DaxAnalysis analysis, string title, string description, IReadOnlyList<DaxTextEdit> edits) =>
        new(title, description, analysis.Document.Id, analysis.Document.Version, analysis.Document.Text, edits);
    private static IEnumerable<Declaration> VisibleDeclarations(DaxAnalysis analysis, int caret) => analysis.Declarations.Where(item => item.Valid && item.ScopeStart <= caret && caret <= item.ScopeEnd)
        .OrderBy(item => item.ScopeEnd - item.ScopeStart).ThenByDescending(item => item.NameSpan.Start);
    private static BoundReference? ReferenceAt(DaxAnalysis analysis, int caret) => analysis.BoundReferences.FirstOrDefault(reference => reference.Span.Contains(caret))
        ?? analysis.BoundReferences.LastOrDefault(reference => reference.Span.End == caret);
    private static string SymbolKey(DaxSymbol symbol) => symbol.Kind + ":" + symbol.Table + ":" + symbol.Name;
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool Is(DaxToken token, string value) => token.Kind is DaxTokenKind.Identifier or DaxTokenKind.Keyword && Same(token.Value, value);
    private static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(maximum, value));

    private static void CheckDelimiters(IReadOnlyList<DaxToken> tokens, List<DaxDiagnostic> diagnostics)
    {
        var stack = new Stack<DaxToken>();
        foreach (var token in tokens)
        {
            if (token.Text is "(" or "{") stack.Push(token);
            else if (token.Text is ")" or "}")
            {
                if (stack.Count == 0 || stack.Peek().Text != (token.Text == ")" ? "(" : "{"))
                    diagnostics.Add(DaxTokenizer.Error("DAX003", "Closing delimiter has no matching opening delimiter.", token.Span.Start, token.Span.End));
                else stack.Pop();
            }
        }
        foreach (var token in stack) diagnostics.Add(DaxTokenizer.Error("DAX004", "Opening delimiter is not closed.", token.Span.Start, token.Span.End));
    }

    private static List<Declaration> ReadDeclarations(DaxDocument document, DaxToken[] tokens, List<DaxDiagnostic> diagnostics,
        Dictionary<string, DaxSignature> functions, CancellationToken ct)
    {
        var result = new List<Declaration>();
        if (document.Kind == DaxDocumentKind.Function && tokens.Length > 0 && tokens[0].Text == "(")
        {
            var signature = ParseSignature(document.Id, tokens, 0, document.Text.Length, document.Text, diagnostics, out var parameters, out var bodyStart);
            foreach (var parameter in parameters)
                result.Add(new Declaration(new DaxSymbol(document.Id + ":parameter:" + parameter.Span.Start, parameter.Value, DaxSymbolKind.Parameter),
                    parameter.Span, bodyStart, document.Text.Length, document.Id, Valid: signature != null));
        }
        for (var i = 0; i < tokens.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (Is(tokens[i], "FUNCTION") && i + 1 < tokens.Length)
            {
                var name = tokens[i + 1];
                var validName = name.Kind == DaxTokenKind.Identifier;
                if (!validName || i + 3 >= tokens.Length || tokens[i + 2].Text != "=" || tokens[i + 3].Text != "(")
                { diagnostics.Add(DaxTokenizer.Error("DAX010", "Expected FUNCTION name = (parameters) => expression.", name.Span.Start, name.Span.End)); continue; }
                var end = DefinitionEnd(tokens, i + 3, document.Text.Length);
                var expression = document.Text.Substring(tokens[i + 3].Span.Start, end - tokens[i + 3].Span.Start).Trim();
                var errors = new List<DaxDiagnostic>();
                var signature = ParseSignature(name.Value, tokens, i + 3, end, document.Text, errors, out var parameters, out var bodyStart);
                diagnostics.AddRange(errors);
                if (diagnostics.Any(error => error.Severity == DaxDiagnosticSeverity.Error && error.Span.Start >= tokens[i + 3].Span.Start && error.Span.Start < end)) signature = null;
                var symbol = new DaxSymbol(document.Id + ":function:" + name.Span.Start, name.Value, DaxSymbolKind.Function, Expression: expression);
                result.Add(new Declaration(symbol, name.Span, 0, document.Text.Length, document.Id, new TextSpan(tokens[i + 3].Span.Start, end - tokens[i + 3].Span.Start), signature != null));
                if (signature != null) functions[name.Value] = signature;
                else functions.Remove(name.Value);
                foreach (var parameter in parameters)
                    result.Add(new Declaration(new DaxSymbol(symbol.Id + ":parameter:" + parameter.Span.Start, parameter.Value, DaxSymbolKind.Parameter), parameter.Span, bodyStart, end, document.Id, Valid: signature != null));
            }
            else if (Is(tokens[i], "VAR") && i + 1 < tokens.Length)
            {
                var name = tokens[i + 1];
                if (name.Kind != DaxTokenKind.Identifier || i + 2 >= tokens.Length || tokens[i + 2].Text != "=")
                { diagnostics.Add(DaxTokenizer.Error("DAX011", "Expected VAR name = expression.", name.Span.Start, name.Span.End)); continue; }
                var end = ScopeEnd(tokens, i, document.Text.Length);
                var enclosing = result.Where(declaration => declaration.ExpressionSpan?.Contains(name.Span.Start) == true)
                    .OrderBy(declaration => declaration.ExpressionSpan!.Value.Length).FirstOrDefault();
                if (enclosing?.ExpressionSpan != null) end = Math.Min(end, enclosing.ExpressionSpan.Value.End);
                var start = InitializerEnd(tokens, i + 3, end);
                var queryVariable = document.Kind == DaxDocumentKind.Query && tokens.Take(i).Any(token => Is(token, "DEFINE")) &&
                    !tokens.Take(i).Any(token => Is(token, "EVALUATE")) &&
                    !result.Any(declaration => declaration.ExpressionSpan?.Contains(name.Span.Start) == true && declaration.Symbol.Kind is DaxSymbolKind.Function or DaxSymbolKind.Measure or DaxSymbolKind.Column);
                if (queryVariable) end = document.Text.Length;
                var expressionEnd = Math.Max(tokens[i + 2].Span.End, start);
                var expression = document.Text.Substring(tokens[i + 2].Span.End, expressionEnd - tokens[i + 2].Span.End).Trim();
                if (expression.Length == 0) diagnostics.Add(DaxTokenizer.Error("DAX012", "Variable initializer is empty.", name.Span.Start, name.Span.End));
                var symbol = new DaxSymbol(document.Id + ":variable:" + name.Span.Start, name.Value, DaxSymbolKind.Variable, Expression: expression);
                if (result.Any(item => item.Symbol.Kind == DaxSymbolKind.Variable && Same(item.Symbol.Name, name.Value) && item.ScopeEnd == end))
                    diagnostics.Add(DaxTokenizer.Error("DAX013", "Variable is declared more than once in this scope.", name.Span.Start, name.Span.End));
                result.Add(new Declaration(symbol, name.Span, start, end, document.Id));
            }
            else if ((Is(tokens[i], "MEASURE") || Is(tokens[i], "COLUMN")) && i + 3 < tokens.Length)
            {
                var memberIndex = tokens[i + 1].Kind == DaxTokenKind.BracketIdentifier ? i + 1 : i + 2;
                if (memberIndex >= tokens.Length || tokens[memberIndex].Kind != DaxTokenKind.BracketIdentifier || memberIndex + 1 >= tokens.Length || tokens[memberIndex + 1].Text != "=") continue;
                var member = tokens[memberIndex]; var table = memberIndex == i + 2 ? tokens[i + 1].Value : document.CurrentTable;
                var end = DefinitionEnd(tokens, memberIndex + 2, document.Text.Length);
                var expressionStart = tokens[memberIndex + 1].Span.End;
                var symbol = new DaxSymbol(document.Id + ":definition:" + member.Span.Start, member.Value,
                    Is(tokens[i], "MEASURE") ? DaxSymbolKind.Measure : DaxSymbolKind.Column, table, document.Text.Substring(expressionStart, end - expressionStart).Trim());
                result.Add(new Declaration(symbol, member.Span, 0, document.Text.Length, document.Id, new TextSpan(expressionStart, end - expressionStart)));
            }
        }
        return result;
    }

    private static List<BoundReference> Bind(DaxDocument document, DaxMetadataSnapshot metadata, DaxToken[] tokens,
        List<Declaration> declarations, List<DaxDiagnostic> diagnostics, HashSet<string> validModelFunctions, CancellationToken ct)
    {
        var result = new List<BoundReference>();
        var virtualColumns = VirtualColumns(tokens);
        for (var i = 0; i < tokens.Length; i++)
        {
            ct.ThrowIfCancellationRequested(); var token = tokens[i];
            var definition = declarations.FirstOrDefault(item => item.NameSpan == token.Span);
            if (definition != null) { result.Add(new BoundReference(definition.Symbol, token.Span, definition, true)); continue; }
            var visible = declarations.Where(item => item.Valid && item.ScopeStart <= token.Span.Start && token.Span.Start < item.ScopeEnd)
                .OrderBy(item => item.ScopeEnd - item.ScopeStart).ThenByDescending(item => item.NameSpan.Start).ToArray();
            DaxSymbol? symbol = null; Declaration? local = null;
            if (token.Kind == DaxTokenKind.BracketIdentifier)
            {
                var previous = i > 0 ? tokens[i - 1] : null;
                var table = previous?.Kind is DaxTokenKind.QuotedIdentifier or DaxTokenKind.Identifier ? previous.Value : null;
                if (table == null && virtualColumns.Any(column => Same(column.Name, token.Value) && column.AvailableAfter <= token.Span.Start)) continue;
                var candidates = visible.Where(item => item.Symbol.Kind is DaxSymbolKind.Column or DaxSymbolKind.Measure && Same(item.Symbol.Name, token.Value) && (table == null || Same(item.Symbol.Table, table))).ToArray();
                local = candidates.FirstOrDefault(); symbol = local?.Symbol;
                if (symbol == null)
                {
                    var modelCandidates = metadata.Symbols.Where(item => item.Kind is DaxSymbolKind.Column or DaxSymbolKind.Measure or DaxSymbolKind.CalculationItem && Same(item.Name, token.Value) && (table == null || Same(item.Table, table))).ToArray();
                    symbol = modelCandidates.FirstOrDefault(item => Same(item.Table, document.CurrentTable))
                        ?? (modelCandidates.Count(item => item.Kind == DaxSymbolKind.Measure) == 1 ? modelCandidates.Single(item => item.Kind == DaxSymbolKind.Measure) : modelCandidates.Length == 1 ? modelCandidates[0] : null);
                }
                if (symbol == null && table != null && metadata.Symbols.Any(item => item.Kind == DaxSymbolKind.Table && Same(item.Name, table)))
                    diagnostics.Add(new DaxDiagnostic("DAX020", DaxDiagnosticSeverity.Warning, "No model column or measure named " + token.Value + " was found in " + table + ".", token.Span));
                // Unqualified names may denote virtual columns created by table expressions; leave those to the engine.
            }
            else if (token.Kind is DaxTokenKind.Identifier or DaxTokenKind.QuotedIdentifier or DaxTokenKind.Keyword)
            {
                var functionCall = i + 1 < tokens.Length && tokens[i + 1].Text == "(";
                local = visible.FirstOrDefault(item => Same(item.Symbol.Name, token.Value) && (functionCall ? item.Symbol.Kind == DaxSymbolKind.Function : item.Symbol.Kind is DaxSymbolKind.Variable or DaxSymbolKind.Parameter));
                symbol = local?.Symbol;
                if (symbol == null) symbol = metadata.Symbols.FirstOrDefault(item => Same(item.Name, token.Value) &&
                    (functionCall ? item.Kind == DaxSymbolKind.Function && validModelFunctions.Contains(item.Id) : item.Kind == DaxSymbolKind.Table));
            }
            if (symbol != null) result.Add(new BoundReference(symbol, token.Span, local));
        }
        return result;
    }

    private static IReadOnlyList<(string Name, int AvailableAfter)> VirtualColumns(DaxToken[] tokens)
    {
        var result = new List<(string, int)>();
        for (var index = 1; index < tokens.Length; index++)
        {
            if (tokens[index].Text != "(" || !new[] { "ROW", "ADDCOLUMNS", "SELECTCOLUMNS", "SUMMARIZE", "SUMMARIZECOLUMNS" }.Any(name => Is(tokens[index - 1], name))) continue;
            var close = MatchingClose(tokens, index);
            if (close < 0) continue;
            var depth = 0; var argumentStart = index + 1;
            for (var at = index + 1; at <= close; at++)
            {
                if (at != close && tokens[at].Text is "(" or "{") depth++;
                else if (at != close && tokens[at].Text is ")" or "}") depth--;
                if (at != close && !(depth == 0 && tokens[at].Text is "," or ";")) continue;
                // A standalone string argument names a generated column in these table constructors.
                if (at == argumentStart + 1 && tokens[argumentStart].Kind == DaxTokenKind.String)
                    result.Add((tokens[argumentStart].Value, tokens[close].Span.End));
                argumentStart = at + 1;
            }
        }
        return result;
    }

    public static bool TryFunctionSignature(string name, string expression, out DaxSignature? signature)
    {
        var errors = new List<DaxDiagnostic>();
        var tokens = DaxTokenizer.Lex(expression, errors, CancellationToken.None).Where(token => !DaxTokenizer.IsTrivia(token)).ToArray();
        CheckDelimiters(tokens, errors);
        var start = tokens.Length > 0 && Is(tokens[0], "FUNCTION") ? 3 : tokens.Length > 1 && Is(tokens[0], "DEFINE") && Is(tokens[1], "FUNCTION") ? 4 : 0;
        signature = ParseSignature(name, tokens, start, expression.Length, expression, errors, out _, out _);
        if (errors.Count > 0) signature = null;
        return signature != null;
    }

    private static DaxSignature? ParseSignature(string name, DaxToken[] tokens, int start, int end, string source,
        List<DaxDiagnostic> errors, out List<DaxToken> parameterNames, out int bodyStart)
    {
        parameterNames = new List<DaxToken>(); bodyStart = end;
        var firstError = errors.Count;
        if (start >= tokens.Length || tokens[start].Text != "(") return null;
        var close = MatchingClose(tokens, start);
        if (close < 0 || close + 1 >= tokens.Length || tokens[close + 1].Text != "=>")
        { errors.Add(DaxTokenizer.Error("DAX014", "A function requires a closed parameter list followed by =>.", tokens[start].Span.Start, tokens[start].Span.End)); return null; }
        bodyStart = tokens[close + 1].Span.End;
        var bodyOffset = bodyStart;
        if (!tokens.Any(token => token.Span.Start >= bodyOffset && token.Span.Start < end))
            errors.Add(DaxTokenizer.Error("DAX015", "Function body is empty.", tokens[close + 1].Span.Start, tokens[close + 1].Span.End));
        var parameters = new List<string>(); var segmentStart = start + 1; var depth = 0;
        for (var i = start + 1; i <= close; i++)
        {
            if (i != close && (tokens[i].Text is "(" or "{")) depth++;
            else if (i != close && (tokens[i].Text is ")" or "}")) depth--;
            if (i != close && !(depth == 0 && tokens[i].Text is "," or ";")) continue;
            if (i > segmentStart)
            {
                var parameter = tokens[segmentStart];
                if (parameter.Kind != DaxTokenKind.Identifier || parameter.Value.Contains('.'))
                    errors.Add(DaxTokenizer.Error("DAX016", "Function parameter must have a nonreserved local name.", parameter.Span.Start, parameter.Span.End));
                if (parameterNames.Any(item => Same(item.Value, parameter.Value)))
                    errors.Add(DaxTokenizer.Error("DAX017", "Function parameter names must be unique.", parameter.Span.Start, parameter.Span.End));
                parameterNames.Add(parameter);
                if (segmentStart + 1 < i && tokens[segmentStart + 1].Text is not (":" or "="))
                    errors.Add(DaxTokenizer.Error("DAX018", "Expected a parameter hint after : or a default expression after =.", tokens[segmentStart + 1].Span.Start, tokens[i - 1].Span.End));
                if (segmentStart + 1 < i && tokens[segmentStart + 1].Text == ":")
                {
                    string? type = null; string? subtype = null; string? mode = null;
                    for (var hint = segmentStart + 2; hint < i && tokens[hint].Text != "="; hint++)
                    {
                        var value = tokens[hint].Value.ToUpperInvariant();
                        var valid = true;
                        if (new[] { "ANYVAL", "SCALAR", "TABLE", "ANYREF", "COLUMNREF", "MEASUREREF", "TABLEREF", "CALENDARREF" }.Contains(value))
                        { valid = type == null; type = value; }
                        else if (new[] { "BOOLEAN", "DATETIME", "DECIMAL", "DOUBLE", "INT64", "NUMERIC", "STRING", "VARIANT" }.Contains(value))
                        { valid = subtype == null; subtype = value; }
                        else if (value is "VAL" or "EXPR") { valid = mode == null; mode = value; }
                        else valid = false;
                        if (!valid) errors.Add(DaxTokenizer.Error("DAX022", "Unrecognized or repeated function parameter hint.", tokens[hint].Span.Start, tokens[hint].Span.End));
                    }
                    if (subtype != null && type != null && type != "SCALAR")
                        errors.Add(DaxTokenizer.Error("DAX023", "A scalar subtype cannot be combined with this parameter type.", parameter.Span.Start, tokens[i - 1].Span.End));
                }
                if (tokens[i - 1].Text == "=") errors.Add(DaxTokenizer.Error("DAX019", "Parameter default expression is empty.", tokens[i - 1].Span.Start, tokens[i - 1].Span.End));
                parameters.Add(source.Substring(parameter.Span.Start, tokens[i - 1].Span.End - parameter.Span.Start));
            }
            else if (i != close || segmentStart != start + 1)
                errors.Add(DaxTokenizer.Error("DAX021", "Function parameter is missing.", tokens[i].Span.Start, tokens[i].Span.End));
            segmentStart = i + 1;
        }
        return errors.Count == firstError ? new DaxSignature(name, name + " ( " + string.Join(", ", parameters) + " )", Array.AsReadOnly(parameters.ToArray()), "User-defined DAX function.") : null;
    }

    private static int MatchingClose(DaxToken[] tokens, int open)
    {
        var depth = 0;
        for (var i = open; i < tokens.Length; i++)
        {
            if (tokens[i].Text is "(" or "{") depth++;
            else if (tokens[i].Text is ")" or "}") { depth--; if (depth == 0) return i; }
        }
        return -1;
    }
    private static int DefinitionEnd(DaxToken[] tokens, int start, int fallback)
    {
        if (start < tokens.Length && tokens[start].Text == "(")
        {
            var close = MatchingClose(tokens, start);
            if (close >= 0 && close + 1 < tokens.Length && tokens[close + 1].Text == "=>") start = close + 2;
        }
        var depth = 0;
        var variableBlock = start < tokens.Length && Is(tokens[start], "VAR");
        var returned = false;
        for (var i = start; i < tokens.Length; i++)
        {
            if (depth == 0 && i > start && new[] { "FUNCTION", "MEASURE", "COLUMN", "TABLE", "EVALUATE" }.Any(keyword => Is(tokens[i], keyword))) return tokens[i].Span.Start;
            if (depth == 0 && i > start && Is(tokens[i], "VAR") && (!variableBlock || returned)) return tokens[i].Span.Start;
            if (depth == 0 && Is(tokens[i], "RETURN")) returned = true;
            if (tokens[i].Text is "(" or "{") depth++;
            else if (tokens[i].Text is ")" or "}") depth--;
        }
        return fallback;
    }
    private static int ScopeEnd(DaxToken[] tokens, int start, int fallback)
    {
        var depth = 0;
        for (var i = start + 1; i < tokens.Length; i++)
        {
            if (depth == 0 && (tokens[i].Text is "," or ";" or ")" or "}" || Is(tokens[i], "EVALUATE") || Is(tokens[i], "MEASURE") || Is(tokens[i], "FUNCTION"))) return tokens[i].Span.Start;
            if (tokens[i].Text is "(" or "{") depth++;
            else if (tokens[i].Text is ")" or "}") depth--;
        }
        return fallback;
    }
    private static int InitializerEnd(DaxToken[] tokens, int start, int scopeEnd)
    {
        var depth = 0;
        for (var i = start; i < tokens.Length && tokens[i].Span.Start < scopeEnd; i++)
        {
            if (depth == 0 && (Is(tokens[i], "VAR") || Is(tokens[i], "RETURN"))) return tokens[i].Span.Start;
            if (tokens[i].Text is "(" or "{") depth++;
            else if (tokens[i].Text is ")" or "}") depth--;
        }
        return scopeEnd;
    }
}
