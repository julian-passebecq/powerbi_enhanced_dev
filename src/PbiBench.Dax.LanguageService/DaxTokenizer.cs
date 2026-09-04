namespace PbiBench.Dax.LanguageService;

/// <summary>Original source-preserving tolerant lexer. Incomplete text remains available to completion.</summary>
public static class DaxTokenizer
{
    public static readonly IReadOnlyCollection<string> Keywords = Array.AsReadOnly(new[]
    {
        "DEFINE", "EVALUATE", "MEASURE", "COLUMN", "TABLE", "FUNCTION", "VAR", "RETURN", "ORDER", "BY", "START", "AT",
        "ASC", "DESC", "IN", "TRUE", "FALSE", "SCALAR", "ANYVAL", "ANYREF", "COLUMNREF", "MEASUREREF", "TABLEREF", "CALENDARREF",
        "BOOLEAN", "DATETIME", "DECIMAL", "DOUBLE", "INT64", "NUMERIC", "STRING", "VARIANT", "VAL", "EXPR"
    });
    private static readonly HashSet<string> KeywordSet = new(Keywords, StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<DaxToken> Tokenize(string text, CancellationToken ct = default) => Lex(text, new List<DaxDiagnostic>(), ct);

    internal static List<DaxToken> Lex(string text, List<DaxDiagnostic> diagnostics, CancellationToken ct)
    {
        var result = new List<DaxToken>();
        for (var index = 0; index < text.Length;)
        {
            ct.ThrowIfCancellationRequested();
            var start = index;
            var c = text[index++];
            var kind = DaxTokenKind.Operator;
            string? value = null;
            if (char.IsWhiteSpace(c))
            {
                kind = DaxTokenKind.Whitespace;
                while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
            }
            else if ((c == '/' || c == '-') && index < text.Length && text[index] == c)
            {
                kind = DaxTokenKind.Comment;
                while (index < text.Length && text[index] != '\r' && text[index] != '\n') index++;
            }
            else if (c == '/' && index < text.Length && text[index] == '*')
            {
                kind = DaxTokenKind.Comment; index++;
                var depth = 1;
                while (index < text.Length && depth > 0)
                {
                    if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '*') { depth++; index += 2; }
                    else if (index + 1 < text.Length && text[index] == '*' && text[index + 1] == '/') { depth--; index += 2; }
                    else index++;
                }
                if (depth != 0) diagnostics.Add(Error("DAX001", "Unterminated block comment.", start, index));
            }
            else if (c is '"' or '\'' or '[' || ((c == 'd' || c == 'D') && index + 1 < text.Length &&
                     (text[index] == 't' || text[index] == 'T') && text[index + 1] == '"'))
            {
                var date = c is 'd' or 'D';
                if (date) index += 2;
                var close = c == '[' ? ']' : date ? '"' : c;
                kind = date ? DaxTokenKind.Date : c == '"' ? DaxTokenKind.String : c == '[' ? DaxTokenKind.BracketIdentifier : DaxTokenKind.QuotedIdentifier;
                var contentStart = index;
                var closed = false;
                while (index < text.Length)
                {
                    if (text[index++] != close) continue;
                    if (index < text.Length && text[index] == close) { index++; continue; }
                    closed = true; break;
                }
                value = text.Substring(contentStart, index - contentStart - (closed ? 1 : 0)).Replace(new string(close, 2), close.ToString());
                if (!closed) diagnostics.Add(Error("DAX002", "Unterminated string or identifier.", start, index));
            }
            else if (char.IsDigit(c) || c == '.' && index < text.Length && char.IsDigit(text[index]))
            {
                kind = DaxTokenKind.Number;
                while (index < text.Length && char.IsDigit(text[index])) index++;
                if (c != '.' && index < text.Length && text[index] == '.') { index++; while (index < text.Length && char.IsDigit(text[index])) index++; }
                if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
                {
                    var exponent = index + 1;
                    if (exponent < text.Length && text[exponent] is '+' or '-') exponent++;
                    if (exponent < text.Length && char.IsDigit(text[exponent]))
                    { index = exponent + 1; while (index < text.Length && char.IsDigit(text[index])) index++; }
                }
            }
            else if (char.IsLetter(c) || c == '_')
            {
                while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '.')) index++;
                kind = KeywordSet.Contains(text.Substring(start, index - start)) ? DaxTokenKind.Keyword : DaxTokenKind.Identifier;
            }
            else if (c is '(' or ')' or '{' or '}' or ',' or ';') kind = DaxTokenKind.Punctuation;
            else if (index < text.Length && new[] { "<=", ">=", "<>", "==", "&&", "||", "=>", ":=" }.Contains(text.Substring(start, 2))) index++;
            var raw = text.Substring(start, index - start);
            result.Add(new DaxToken(kind, raw, value ?? raw, new TextSpan(start, index - start)));
        }
        return result;
    }
    internal static DaxDiagnostic Error(string id, string message, int start, int end) =>
        new(id, DaxDiagnosticSeverity.Error, message, new TextSpan(start, Math.Max(1, end - start)));
    internal static bool IsTrivia(DaxToken token) => token.Kind is DaxTokenKind.Comment or DaxTokenKind.Whitespace;
}
