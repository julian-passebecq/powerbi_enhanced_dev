using PbiBench.Core.Compiler;
using PbiBench.Dax.LanguageService;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Compiler;

public sealed class SemanticCompilerService
{
    private readonly TabularModelHandler handler;
    public SemanticCompilerService(TabularModelHandler handler) => this.handler = handler;
    public AuthoringPreview Preview(SemanticCompilation compilation, string targetTable)
    {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        // Reparse the preserved input rather than accepting a mutable or fabricated IR as authority.
        compilation = new MetricViewCompiler().Compile(compilation.Intent.OriginalYaml, compilation.Intent.Name);
        var issues = compilation.Diagnostics.Select(item => new AuthoringIssue(item.Code, item.Message, item.Severity switch { CompilerSeverity.Error => AuthoringIssueSeverity.Error, CompilerSeverity.Warning => AuthoringIssueSeverity.Warning, _ => AuthoringIssueSeverity.Information })).ToList();
        var edits = new List<AuthoringEdit>(); var table = handler.Model.Tables.FirstOrDefault(item => string.Equals(item.Name, targetTable, StringComparison.Ordinal));
        if (table == null) issues.Add(new("COMPILER_TABLE", "Explicitly map the source intent to an existing model table.", AuthoringIssueSeverity.Error));
        else foreach (var measure in compilation.Intent.Measures.Where(item => item.Aggregate != null))
        {
            if (handler.Model.AllMeasures.Any(item => string.Equals(item.Name, measure.Name, StringComparison.OrdinalIgnoreCase)) || table.Columns.Any(item => string.Equals(item.Name, measure.Name, StringComparison.OrdinalIgnoreCase))) { issues.Add(new("COMPILER_COLLISION", "An existing measure or target column already uses " + measure.Name + ". No existing object is overwritten.", AuthoringIssueSeverity.Error)); continue; }
            var column = measure.SourceColumn == null ? null : table.Columns.FirstOrDefault(item => string.Equals(item.Name, measure.SourceColumn, StringComparison.Ordinal));
            if (measure.Aggregate != "COUNTROWS" && (column == null || column.DataType is not (DataType.Int64 or DataType.Double or DataType.Decimal))) { issues.Add(new("COMPILER_COLUMN", "Map " + measure.SourceColumn + " to an identically named numeric column in " + targetTable + ". String/date aggregates and inferred coercion are unsupported.", AuthoringIssueSeverity.Error)); continue; }
            var expression = measure.Aggregate == "COUNTROWS" ? "COALESCE(COUNTROWS(" + DaxSymbol.QuoteTable(table.Name) + "), 0)" : (measure.Aggregate == "AVG" ? "AVERAGE" : measure.Aggregate) + "(" + DaxSymbol.QuoteTable(table.Name) + DaxSymbol.QuoteMember(column!.Name) + ")";
            var description = measure.Comment + (measure.Comment.Length == 0 ? "" : "\n\n") + "Prototype intent from " + compilation.Intent.Source + ": " + measure.SqlExpression + ". Validate results and source mapping before deployment.";
            edits.Add(new(new(table.Name + "/" + measure.Name, "New measure", "(absent)", expression + "\nDescription: " + description,
                "Explicit mapping: " + compilation.Intent.Source + " → " + table.Name + ". This creates only measure metadata, with no SQL execution or data equivalence claim."),
                () => { var created = table.AddMeasure(measure.Name, expression); created.Description = description; },
                () => table.Measures.Any(item => item.Name == measure.Name && item.Expression == expression && item.Description == description)));
        }
        return AuthoringPreview.Create(handler, "Prototype · create reviewed aggregate measures", edits, issues);
    }
}
