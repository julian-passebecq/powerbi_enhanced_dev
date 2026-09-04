namespace PbiBench.Dax.LanguageService;

public enum DaxExecutionMode { All, Selection, CurrentStatement }
public sealed record DaxExecutionPlan(string QueryText, TextSpan SourceSpan, DaxExecutionMode Mode, int StatementCount);

/// <summary>Uses lexical statement boundaries, so comments, literals and nested expressions do not split a query.</summary>
public static class DaxQueryPlanner
{
    public static DaxExecutionPlan Prepare(DaxDocument document, DaxExecutionMode mode, int caret = 0, TextSpan? selection = null)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        var text = document.Text;
        var statements = StatementSpans(text);
        if (mode == DaxExecutionMode.All)
            return new DaxExecutionPlan(text, new TextSpan(0, text.Length), mode, statements.Count);
        var preambleEnd = statements.Count > 0 ? statements[0].Start : 0;
        var preamble = text.Substring(0, preambleEnd);
        if (mode == DaxExecutionMode.Selection)
        {
            var span = selection ?? new TextSpan(caret, 0);
            if (span.Start < 0 || span.Length <= 0 || span.End > text.Length)
                throw new InvalidOperationException("Select the DAX statement text to execute.");
            var chosen = text.Substring(span.Start, span.Length);
            var significant = DaxTokenizer.Tokenize(chosen).Where(token => !DaxTokenizer.IsTrivia(token)).ToArray();
            var containsDefine = significant.Any(token => EqualsKeyword(token, "DEFINE"));
            var needsPreamble = span.Start >= preambleEnd && preambleEnd > 0 && !containsDefine;
            return new DaxExecutionPlan((needsPreamble ? preamble + Environment.NewLine : "") + chosen, span, mode, StatementSpans(chosen).Count);
        }
        if (statements.Count == 0) throw new InvalidOperationException("No EVALUATE statement is available. Add EVALUATE to execute a DAX query.");
        caret = Math.Max(0, Math.Min(text.Length, caret));
        var current = statements.LastOrDefault(span => span.Start <= caret);
        if (current.Length == 0) current = statements[0];
        return new DaxExecutionPlan(preamble + Environment.NewLine + text.Substring(current.Start, current.Length), current, mode, 1);
    }

    public static IReadOnlyList<TextSpan> StatementSpans(string text)
    {
        var starts = new List<int>();
        var depth = 0;
        foreach (var token in DaxTokenizer.Tokenize(text))
        {
            if (DaxTokenizer.IsTrivia(token)) continue;
            if (depth == 0 && EqualsKeyword(token, "EVALUATE")) starts.Add(token.Span.Start);
            if (token.Text is "(" or "{") depth++;
            else if (token.Text is ")" or "}") depth = Math.Max(0, depth - 1);
        }
        return starts.Select((start, index) => new TextSpan(start, (index + 1 < starts.Count ? starts[index + 1] : text.Length) - start)).ToArray();
    }
    private static bool EqualsKeyword(DaxToken token, string keyword) => token.Kind == DaxTokenKind.Keyword && token.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);
}
