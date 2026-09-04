using System.Text;

namespace PbiBench.Dax.LanguageService;

public enum DaxScriptObjectKind { Measure, Column, Table, CalculationItem, Function }
public sealed record DaxScriptEntry(DaxScriptObjectKind Kind, string? Table, string Name, string Expression,
    string Property = "Expression", TextSpan Span = default, TextSpan ExpressionSpan = default)
{
    public string ObjectKey => Kind + ":" + DisplayName;
    public string Key => ObjectKey + ":" + Property;
    public string DisplayName => Kind == DaxScriptObjectKind.Function ? Name : Kind == DaxScriptObjectKind.Table ? DaxSymbol.QuoteTable(Name)
        : DaxSymbol.QuoteTable(Table ?? "") + DaxSymbol.QuoteMember(Name);
}
public sealed record DaxScriptParseResult(IReadOnlyList<DaxScriptEntry> Entries, IReadOnlyList<DaxDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != DaxDiagnosticSeverity.Error);
}

/// <summary>Original multi-object DAX format. Top-level semicolons separate definitions; quoted text and comments are untouched.</summary>
public static class DaxModelScript
{
    public static string Serialize(IEnumerable<DaxScriptEntry> entries)
    {
        var output = new StringBuilder("-- PbiBench DAX Script v1\n-- Only listed objects/properties change. Missing objects are never deleted.\n\n");
        foreach (var entry in entries)
        {
            if (entry.Property != "Expression") output.Append(entry.Property.ToUpperInvariant()).Append(' ');
            output.Append(entry.Kind.ToString().ToUpperInvariant()).Append(' ').Append(entry.DisplayName).Append(" =\n");
            output.Append(entry.Expression).Append("\n;\n\n");
        }
        return output.ToString();
    }

    public static DaxScriptParseResult Parse(string text, CancellationToken ct = default)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (text.Length > 16 * 1024 * 1024) throw new ArgumentException("DAX scripts are limited to 16 MB.", nameof(text));
        var diagnostics = new List<DaxDiagnostic>();
        var tokens = DaxTokenizer.Lex(text, diagnostics, ct).Where(token => !DaxTokenizer.IsTrivia(token)).ToArray();
        var entries = new List<DaxScriptEntry>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = 0; var depth = 0;
        for (var index = 0; index < tokens.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            if (tokens[index].Text is "(" or "{") depth++;
            else if (tokens[index].Text is ")" or "}")
            {
                depth--;
                if (depth < 0) { diagnostics.Add(Error("SCRIPT001", "Unexpected closing delimiter.", tokens[index].Span)); depth = 0; }
            }
            if (tokens[index].Text != ";" || depth != 0) continue;
            if (index > start) ParseEntry(start, index);
            else diagnostics.Add(Error("SCRIPT002", "Empty script statement.", tokens[index].Span));
            start = index + 1;
        }
        if (start < tokens.Length) diagnostics.Add(Error("SCRIPT003", "End each complete object definition with a top-level semicolon.", new TextSpan(tokens[start].Span.Start, text.Length - tokens[start].Span.Start)));
        return new DaxScriptParseResult(Array.AsReadOnly(entries.ToArray()), Array.AsReadOnly(diagnostics.ToArray()));

        void ParseEntry(int begin, int end)
        {
            var current = begin; var property = "Expression";
            if (Same(tokens[current].Value, "FORMATSTRINGEXPRESSION")) { property = "FormatStringExpression"; current++; }
            if (current >= end || tokens[current].Kind is not (DaxTokenKind.Identifier or DaxTokenKind.Keyword) || !Enum.TryParse<DaxScriptObjectKind>(tokens[current].Value, true, out var kind) || !Enum.IsDefined(typeof(DaxScriptObjectKind), kind))
            { diagnostics.Add(Error("SCRIPT004", "Expected MEASURE, COLUMN, TABLE, CALCULATIONITEM or FUNCTION.", tokens[begin].Span)); return; }
            current++;
            if (current >= end) { diagnostics.Add(Error("SCRIPT005", "Object name is missing.", tokens[begin].Span)); return; }
            string? table = null; string name;
            if (kind is DaxScriptObjectKind.Table or DaxScriptObjectKind.Function)
            {
                var token = tokens[current++]; name = token.Value;
                if (kind == DaxScriptObjectKind.Function ? token.Kind != DaxTokenKind.Identifier : token.Kind is not (DaxTokenKind.Identifier or DaxTokenKind.QuotedIdentifier))
                { diagnostics.Add(Error("SCRIPT006", "Use a quoted table name or a valid function name.", token.Span)); return; }
            }
            else
            {
                var tableToken = tokens[current++];
                if (tableToken.Kind is not (DaxTokenKind.Identifier or DaxTokenKind.QuotedIdentifier) || current >= end || tokens[current].Kind != DaxTokenKind.BracketIdentifier)
                { diagnostics.Add(Error("SCRIPT007", "Use a qualified object name: 'Table'[Object].", tableToken.Span)); return; }
                table = tableToken.Value; name = tokens[current++].Value;
            }
            if (property == "FormatStringExpression" && kind is not (DaxScriptObjectKind.Measure or DaxScriptObjectKind.CalculationItem))
            { diagnostics.Add(Error("SCRIPT008", "Dynamic format expressions apply to measures and calculation items.", tokens[begin].Span)); return; }
            if (current >= end || tokens[current].Text != "=")
            { diagnostics.Add(Error("SCRIPT009", "Expected = before the DAX expression.", tokens[Math.Min(current, end)].Span)); return; }
            var expressionStart = tokens[current].Span.End;
            var expressionEnd = tokens[end].Span.Start;
            // Serialization adds one separator newline on each side. Removing precisely those
            // preserves existing leading/trailing whitespace and line comments on round trip.
            if (expressionStart + 1 < expressionEnd && text.Substring(expressionStart, 2) == "\r\n") expressionStart += 2;
            else if (expressionStart < expressionEnd && text[expressionStart] == '\n') expressionStart++;
            if (expressionEnd - 2 >= expressionStart && text.Substring(expressionEnd - 2, 2) == "\r\n") expressionEnd -= 2;
            else if (expressionEnd > expressionStart && text[expressionEnd - 1] == '\n') expressionEnd--;
            var expression = text.Substring(expressionStart, expressionEnd - expressionStart);
            if (string.IsNullOrWhiteSpace(expression) && property == "Expression")
            { diagnostics.Add(Error("SCRIPT010", "The DAX expression is empty.", new TextSpan(expressionStart, Math.Max(1, expressionEnd - expressionStart)))); return; }
            var entry = new DaxScriptEntry(kind, table, name, expression, property, new TextSpan(tokens[begin].Span.Start, tokens[end].Span.End - tokens[begin].Span.Start), new TextSpan(expressionStart, expressionEnd - expressionStart));
            if (!seen.Add(entry.Key)) diagnostics.Add(Error("SCRIPT011", "An object property is defined more than once: " + entry.DisplayName, entry.Span));
            else entries.Add(entry);
        }
    }
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static DaxDiagnostic Error(string code, string message, TextSpan span) => new(code, DaxDiagnosticSeverity.Error, message, span);
}
