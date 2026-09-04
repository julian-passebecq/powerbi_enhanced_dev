using System.Text;
using TabularEditor.TOMWrapper.Utils;

namespace PbiBench.Semantic;

/// <summary>Conservative offline layout formatter. Literal, identifier, number and comment tokens remain byte-for-byte intact.</summary>
public sealed class LocalDaxFormatter
{
    public string Format(string expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        var tokens = Tokenize(expression);
        var result = new StringBuilder();
        var depth = 0;
        Token? previous = null;
        foreach (var token in tokens)
        {
            if (token.Text == ")" || token.Text == "}") depth = Math.Max(0, depth - 1);
            var keyword = token.Kind == TokenKind.Word && (token.Text.Equals("VAR", StringComparison.OrdinalIgnoreCase) || token.Text.Equals("RETURN", StringComparison.OrdinalIgnoreCase));
            var newline = result.Length > 0 && (keyword || previous?.Kind == TokenKind.LineComment || previous?.Text == ",");
            if (newline) result.AppendLine().Append(new string(' ', depth * 4));
            else if (result.Length > 0 && !(token.Kind == TokenKind.Bracket && previous != null && (previous.Kind == TokenKind.Word || previous.Kind == TokenKind.QuotedName))) result.Append(' ');
            result.Append(token.Text);
            if (token.Text == "(" || token.Text == "{") depth++;
            previous = token;
        }
        var formatted = result.ToString();
        if (!tokens.Select(t => t.Text).SequenceEqual(Tokenize(formatted).Select(t => t.Text)))
            throw new InvalidOperationException("Formatting could change DAX tokens; no changes were made.");
        // Also ask the pinned TE2 lexer: spacing must preserve its actual semantic token stream.
        var originalDaxTokens = DaxDependencyHelper.Tokenize(expression, false).Select(t => t.Type + ":" + t.Text);
        var formattedDaxTokens = DaxDependencyHelper.Tokenize(formatted, false).Select(t => t.Type + ":" + t.Text);
        if (!originalDaxTokens.SequenceEqual(formattedDaxTokens))
            throw new InvalidOperationException("The TE2 lexer detected a token change. Keep this expression as written.");
        return formatted;
    }

    private static List<Token> Tokenize(string source)
    {
        var result = new List<Token>();
        for (var i = 0; i < source.Length;)
        {
            if (char.IsWhiteSpace(source[i])) { i++; continue; }
            var start = i;
            var ch = source[i++];
            var kind = TokenKind.Symbol;
            if (ch == '"' || ch == '\'' || ch == '[')
            {
                var close = ch == '[' ? ']' : ch;
                kind = ch == '"' ? TokenKind.String : ch == '[' ? TokenKind.Bracket : TokenKind.QuotedName;
                var closed = false;
                while (i < source.Length)
                {
                    if (source[i++] != close) continue;
                    if (i < source.Length && source[i] == close) { i++; continue; }
                    closed = true; break;
                }
                if (!closed) throw new FormatException("Unterminated DAX string or identifier.");
            }
            else if (i < source.Length && ((ch == '/' && source[i] == '/') || (ch == '-' && source[i] == '-')))
            {
                kind = TokenKind.LineComment;
                while (i < source.Length && source[i] != '\r' && source[i] != '\n') i++;
            }
            else if (ch == '/' && i < source.Length && source[i] == '*')
            {
                kind = TokenKind.Comment;
                i++;
                var end = source.IndexOf("*/", i, StringComparison.Ordinal);
                if (end < 0) throw new FormatException("Unterminated DAX comment.");
                i = end + 2;
            }
            else if (char.IsDigit(ch) || (ch == '.' && i < source.Length && char.IsDigit(source[i])))
            {
                kind = TokenKind.Number;
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) i++;
                if (i < source.Length && (source[i] == 'e' || source[i] == 'E'))
                {
                    i++;
                    if (i < source.Length && (source[i] == '+' || source[i] == '-')) i++;
                    while (i < source.Length && char.IsDigit(source[i])) i++;
                }
            }
            else if (char.IsLetter(ch) || ch == '_')
            {
                kind = TokenKind.Word;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '.')) i++;
                // DAX date/time literals have an adjacent dt prefix; it is part of the literal.
                if (source.Substring(start, i - start).Equals("dt", StringComparison.OrdinalIgnoreCase) && i < source.Length && source[i] == '"')
                {
                    kind = TokenKind.String;
                    i++;
                    var end = source.IndexOf('"', i);
                    if (end < 0) throw new FormatException("Unterminated DAX date/time literal.");
                    i = end + 1;
                }
            }
            else if (i < source.Length && new[] { "<=", ">=", "<>", "==", "&&", "||", ":=", "=>" }.Contains(source.Substring(start, 2))) i++;
            result.Add(new Token(source.Substring(start, i - start), kind));
        }
        return result;
    }

    private enum TokenKind { Symbol, Word, String, QuotedName, Bracket, Number, Comment, LineComment }
    private sealed record Token(string Text, TokenKind Kind);
}
