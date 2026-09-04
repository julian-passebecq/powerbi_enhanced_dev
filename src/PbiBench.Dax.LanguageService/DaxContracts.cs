namespace PbiBench.Dax.LanguageService;

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
    public bool Contains(int position) => position >= Start && position < End;
}
public enum DaxTokenKind { Whitespace, Comment, Keyword, Identifier, QuotedIdentifier, BracketIdentifier, String, Date, Number, Operator, Punctuation }
public enum DaxDiagnosticSeverity { Information, Warning, Error }
public enum DaxDocumentKind { Expression, Query, Script, Function }
public enum DaxSymbolKind { Table, Column, Measure, CalculationItem, Function, Variable, Parameter }
public sealed record DaxToken(DaxTokenKind Kind, string Text, string Value, TextSpan Span);
public sealed record DaxDiagnostic(string Id, DaxDiagnosticSeverity Severity, string Message, TextSpan Span);
public sealed record DaxDocument(string Id, string Text, int Version = 0, DaxDocumentKind Kind = DaxDocumentKind.Query, string? CurrentTable = null);
public sealed record DaxSymbol(string Id, string Name, DaxSymbolKind Kind, string? Table = null, string? Expression = null,
    string? Description = null, string? DataType = null, bool IsHidden = false)
{
    public string QualifiedName => Kind == DaxSymbolKind.Table ? QuoteTable(Name) :
        Kind is DaxSymbolKind.Column or DaxSymbolKind.Measure or DaxSymbolKind.CalculationItem ?
            (Table == null ? "" : QuoteTable(Table)) + QuoteMember(Name) : Name;
    public static string QuoteTable(string name) => "'" + name.Replace("'", "''") + "'";
    public static string QuoteMember(string name) => "[" + name.Replace("]", "]]") + "]";
}
public sealed class DaxMetadataSnapshot
{
    public static DaxMetadataSnapshot Empty { get; } = new(Array.Empty<DaxSymbol>());
    public IReadOnlyList<DaxSymbol> Symbols { get; }
    public int CompatibilityLevel { get; }
    public DaxMetadataSnapshot(IEnumerable<DaxSymbol> symbols, int compatibilityLevel = 1702)
    {
        Symbols = Array.AsReadOnly((symbols ?? throw new ArgumentNullException(nameof(symbols))).ToArray());
        CompatibilityLevel = compatibilityLevel;
    }
}
public sealed record DaxCompletion(string Label, string InsertText, DaxSymbolKind Kind, string Detail, TextSpan ReplaceSpan);
public sealed record DaxSignature(string Name, string Label, IReadOnlyList<string> Parameters, string Description, string? ReturnType = null);
public sealed record DaxSignatureHelp(DaxSignature Signature, int ActiveParameter, TextSpan CallSpan);
public sealed record DaxSymbolLocation(string SymbolId, string Name, DaxSymbolKind Kind, string? DocumentId, TextSpan? Span, string? Expression, string? Description);
public sealed record DaxReference(string SymbolId, string DocumentId, TextSpan Span, bool IsDefinition = false);
public sealed record DaxTextEdit(TextSpan Span, string NewText);
public sealed record DaxCodeAction(string Title, string Description, string DocumentId, int DocumentVersion, string OriginalText, IReadOnlyList<DaxTextEdit> Edits)
{
    public string Apply(DaxDocument current)
    {
        if (current.Id != DocumentId || current.Version != DocumentVersion || current.Text != OriginalText)
            throw new InvalidOperationException("The DAX document changed. Preview this action again before applying it.");
        var edits = Edits.OrderByDescending(edit => edit.Span.Start).ToArray();
        var lastStart = current.Text.Length;
        var result = current.Text;
        foreach (var edit in edits)
        {
            if (edit.Span.Start < 0 || edit.Span.Length < 0 || edit.Span.End > lastStart)
                throw new InvalidOperationException("The text edits overlap or fall outside the document.");
            result = result.Remove(edit.Span.Start, edit.Span.Length).Insert(edit.Span.Start, edit.NewText);
            lastStart = edit.Span.Start;
        }
        return result;
    }
}
public sealed class DaxAnalysis
{
    public DaxDocument Document { get; }
    public DaxMetadataSnapshot Metadata { get; }
    public IReadOnlyList<DaxToken> Tokens { get; }
    public IReadOnlyList<DaxDiagnostic> Diagnostics { get; }
    internal IReadOnlyList<Declaration> Declarations { get; }
    internal IReadOnlyList<BoundReference> BoundReferences { get; }
    internal IReadOnlyDictionary<string, DaxSignature> Functions { get; }
    internal DaxAnalysis(DaxDocument document, DaxMetadataSnapshot metadata, IEnumerable<DaxToken> tokens,
        IEnumerable<DaxDiagnostic> diagnostics, IEnumerable<Declaration> declarations, IEnumerable<BoundReference> references,
        IReadOnlyDictionary<string, DaxSignature> functions)
    {
        Document = document; Metadata = metadata; Tokens = Array.AsReadOnly(tokens.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray()); Declarations = Array.AsReadOnly(declarations.ToArray());
        BoundReferences = Array.AsReadOnly(references.ToArray()); Functions = functions;
    }
}
internal sealed record Declaration(DaxSymbol Symbol, TextSpan NameSpan, int ScopeStart, int ScopeEnd, string DocumentId,
    TextSpan? ExpressionSpan = null, bool Valid = true);
internal sealed record BoundReference(DaxSymbol Symbol, TextSpan Span, Declaration? Declaration = null, bool IsDefinition = false);
