using System.Text.Json;

namespace PbiBench.Core.Compiler;

public enum CompilerSeverity { Information, Warning, Error }
public sealed record CompilerDiagnostic(string Code, string Message, CompilerSeverity Severity, int Line = 0);
public sealed record SemanticDimensionIntent(string Name, string SqlExpression, string Comment, string? SourceColumn, int Line);
public sealed record SemanticMeasureIntent(string Name, string SqlExpression, string Comment, string? Aggregate, string? SourceColumn, int Line);
public sealed record SemanticJoinIntent(string Name, string Source, string Condition, string Cardinality, int Line);
public sealed record SemanticIntent(string Name, string FormatVersion, string Source, string Comment,
    IReadOnlyList<SemanticDimensionIntent> Dimensions, IReadOnlyList<SemanticMeasureIntent> Measures,
    IReadOnlyList<SemanticJoinIntent> Relationships, string OriginalYaml);
public sealed class SemanticCompilation
{
    internal SemanticCompilation(SemanticIntent intent, IEnumerable<CompilerDiagnostic> diagnostics)
    { Intent = intent; Diagnostics = Array.AsReadOnly(diagnostics.ToArray()); }
    public string Prototype => "PbiBench semantic compiler prototype: reviewed intent, not SQL-to-DAX equivalence";
    public SemanticIntent Intent { get; }
    public IReadOnlyList<CompilerDiagnostic> Diagnostics { get; }
    public bool CanProposeMetadata => !Diagnostics.Any(item => item.Severity == CompilerSeverity.Error) && Intent.Measures.Any(item => item.Aggregate != null);
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}
