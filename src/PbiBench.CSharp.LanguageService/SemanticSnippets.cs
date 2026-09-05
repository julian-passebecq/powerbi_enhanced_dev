namespace PbiBench.CSharp.LanguageService;

public sealed record SemanticSnippet(string Id, string Name, string SelectionKind, bool TrustedOnly = false)
{ public override string ToString() => Name + (TrustedOnly ? " · Trusted text" : " · Safe Preview"); }
public sealed record GeneratedSnippet(bool Enabled, string Reason, string Source, bool TrustedOnly);
public static class SemanticSnippets
{
    public static IReadOnlyList<SemanticSnippet> All { get; } = Array.AsReadOnly(new[] {
        new SemanticSnippet("sum", "SUM measures from numeric columns", "Column"),
        new SemanticSnippet("hide", "Hide selected key columns", "Column"),
        new SemanticSnippet("folder", "Set selected measure folders", "Measure"),
        new SemanticSnippet("description", "Describe selected measures", "Measure"),
        new SemanticSnippet("format-string", "Set measure format strings", "Measure"),
        new SemanticSnippet("format-dax", "Format selected DAX", "Measure", true),
        new SemanticSnippet("countrows", "COUNTROWS for selected tables", "Table") });
    public static GeneratedSnippet Generate(SemanticSnippet snippet, IReadOnlyList<AutomationSymbol> symbols)
    {
        if (!All.Contains(snippet)) throw new ArgumentException("Unknown semantic snippet.");
        var selected = symbols.Where(s => s.Selected).ToArray();
        if (selected.Length is < 1 or > 200) return new(false, "Select 1–200 " + snippet.SelectionKind.ToLowerInvariant() + " objects.", "", snippet.TrustedOnly);
        if (selected.Any(s => s.Kind != snippet.SelectionKind)) return new(false, "Select only " + snippet.SelectionKind.ToLowerInvariant() + " objects.", "", snippet.TrustedOnly);
        var usable = snippet.Id == "sum" ? selected.Where(s => s.DataType is "Int64" or "Double" or "Decimal").ToArray() : selected;
        if (usable.Length == 0) return new(false, "Select numeric columns (Int64, Double or Decimal).", "", snippet.TrustedOnly);
        string Q(string v) => "\"" + CSharpLanguageService.EscapeLiteral(v) + "\"";
        string Table(string name) => "Model.Tables[" + Q(name) + "]";
        string Reference(AutomationSymbol s) => s.Kind == "Table" ? Table(s.Name) : Table(s.Table ?? throw new ArgumentException("A semantic object requires its table name.")) + "." + s.Kind + "s[" + Q(s.Name) + "]";
        string DaxTable(string name) => "'" + name.Replace("'", "''") + "'";
        var lines = usable.Select(s => snippet.Id switch {
            "sum" => Table(s.Table!) + ".AddMeasure(" + Q("Sum " + s.Name) + ", " + Q("SUM(" + DaxTable(s.Table!) + "[" + s.Name.Replace("]", "]]") + "])" ) + ", \"Totals\");",
            "countrows" => Table(s.Name) + ".AddMeasure(" + Q(s.Name + " Count") + ", " + Q("COUNTROWS(" + DaxTable(s.Name) + ")") + ", \"Counts\");",
            "hide" => Reference(s) + ".IsHidden = true;",
            "folder" => Reference(s) + ".DisplayFolder = \"Finance\";",
            "description" => Reference(s) + ".Description = " + Q("Measure: " + s.Name) + ";",
            "format-string" => Reference(s) + ".FormatString = \"#,0.00\";",
            "format-dax" => Reference(s) + ".FormatDax();",
            _ => throw new ArgumentException("Unknown snippet.")
        });
        var source = "// Captured selection: " + usable.Length + " objects. Review names and values before preview/run.\n" + string.Join("\n", lines);
        if (source.Length > 262144) throw new ArgumentException("Generated source exceeds 256 KiB.");
        return new(true, usable.Length + " objects captured" + (usable.Length < selected.Length ? "; non-numeric columns skipped." : "."), source, snippet.TrustedOnly);
    }
}
