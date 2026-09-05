using System.Text.RegularExpressions;

namespace PbiBench.CSharp.LanguageService;

public sealed record AutomationSymbol(string Kind, string Name, string? Table = null, bool Selected = false);
public sealed record CSharpCompletion(string Text, string Kind, string Signature, string Description, int? ReplaceStart = null, int ReplaceLength = 0);
public sealed record CSharpDiagnostic(int Line, int Column, string Code, string Message, bool IsWarning = false);
public sealed record ScriptRisk(string Category, int Line, string Message);
public interface ICSharpLanguageService
{
    IReadOnlyList<CSharpCompletion> Complete(string source, int offset, IReadOnlyList<AutomationSymbol> symbols);
    string? Signature(string source, int offset);
    IReadOnlyList<ScriptRisk> Risks(string source);
}

/// <summary>Bounded semantic metadata assistance. No compiler, UI, model globals or execution authority.</summary>
public sealed class CSharpLanguageService : ICSharpLanguageService
{
    private static readonly CSharpCompletion[] Members =
    {
        new("Name", "Property", "string Name", "Object name; renames can affect references."),
        new("Description", "Property", "string Description", "Model documentation."),
        new("IsHidden", "Property", "bool IsHidden", "Client visibility."),
        new("DisplayFolder", "Property", "string DisplayFolder", "Folder for columns and measures."),
        new("Expression", "Property", "string Expression", "DAX for measures and calculated objects."),
        new("FormatString", "Property", "string FormatString", "Value formatting."),
        new("SummarizeBy", "Property", "AggregateFunction SummarizeBy", "Column default aggregation."),
        new("Table", "Property", "Table Table", "Containing table of a column or measure."),
        new("AddMeasure", "Method", "Measure AddMeasure(string name = null, string expression = null, string displayFolder = null)", "Create a measure on a table; requires preview or explicit trusted run."),
        new("AddCalculatedTable", "Method", "CalculatedTable AddCalculatedTable(string name = null, string expression = null)", "Model method. Trusted C# only."),
        new("Delete", "Method", "void Delete()", "Delete this object; Safe Preview only supports measure deletion."),
        new("FormatDax", "Method", "void FormatDax() · object; collection: FormatDax(bool shortFormat = false, bool? skipSpaceAfterFunctionName = null)", "TE2 DAX formatting helper; Trusted C# only."),
        new("Output", "Method", "void Output(object value)", "TE2 output helper; Trusted C# only.")
    };
    public IReadOnlyList<CSharpCompletion> Complete(string source, int offset, IReadOnlyList<AutomationSymbol> symbols)
    {
        var prefix = Prefix(source, offset);
        var indexed = Match(prefix, "Model\\.Tables\\[\"((?:[^\"\\\\]|\\\\.)*)\"\\]\\.(Columns|Measures)\\[\"([^\"]*)$");
        if (indexed.Success)
            return symbols.Where(s => s.Table == indexed.Groups[1].Value && s.Kind == (indexed.Groups[2].Value == "Columns" ? "Column" : "Measure"))
                .Where(s => s.Name.StartsWith(indexed.Groups[3].Value, StringComparison.OrdinalIgnoreCase)).Take(200).Select(s => NameCompletion(s) with { ReplaceStart = offset - indexed.Groups[3].Value.Length, ReplaceLength = indexed.Groups[3].Value.Length }).ToArray();
        var tables = Match(prefix, "Model\\.Tables\\[\"([^\"]*)$");
        if (tables.Success) return symbols.Where(s => s.Kind == "Table" && s.Name.StartsWith(tables.Groups[1].Value, StringComparison.OrdinalIgnoreCase)).Take(200).Select(s => NameCompletion(s) with { ReplaceStart = offset - tables.Groups[1].Value.Length, ReplaceLength = tables.Groups[1].Value.Length }).ToArray();
        var member = Match(prefix, @"([A-Za-z_][\w]*(?:\.Tables\[""[^""]+""\])?)\.([\w]*)$");
        if (member.Success)
        {
            var receiver = member.Groups[1].Value; var partial = member.Groups[2].Value;
            if (receiver == "Model") return Filter(new[] { Collection("Tables"), Collection("AllColumns"), Collection("AllMeasures"), Members.Single(m => m.Text == "AddCalculatedTable") }, partial);
            if (receiver == "Selected") return Filter(new[] { Collection("Tables"), Collection("Columns"), Collection("Measures") }, partial);
            var loop = Match(prefix, @"foreach\s*\(\s*var\s+" + Regex.Escape(receiver) + @"\s+in\s+(?:Model|Selected)\.(All)?(Measures|Columns|Tables)\s*\)");
            var kind = receiver.Contains("Tables[") ? "Tables" : loop.Success ? loop.Groups[2].Value : "";
            return Filter(Members.Where(m => kind switch { "Tables" => m.Text is "Name" or "Description" or "IsHidden" or "AddMeasure" or "Delete", "Measures" => m.Text is not ("SummarizeBy" or "AddMeasure" or "AddCalculatedTable"), "Columns" => m.Text is not ("AddCalculatedTable" or "AddMeasure"), _ => true }).Concat(kind == "Tables" ? new[] { Collection("Columns"), Collection("Measures") } : Array.Empty<CSharpCompletion>()), partial);
        }
        return new[] { new CSharpCompletion("Model", "Model", "Model Model", "Active model."), new CSharpCompletion("Selected", "Selection", "UITreeSelection Selected", symbols.Count(s => s.Selected) + " selected semantic objects; selection is captured at preview/run.") };
    }
    public string? Signature(string source, int offset)
    {
        var prefix = Prefix(source, offset); var match = Match(prefix, @"\b(AddMeasure|AddCalculatedTable|Delete|FormatDax|Output)\s*\([^()]*$");
        return match.Success ? Members.First(m => m.Text == match.Groups[1].Value).Signature : null;
    }
    public IReadOnlyList<ScriptRisk> Risks(string source)
    {
        Prefix(source, source.Length); var risks = new List<ScriptRisk>();
        foreach (var rule in new[] { ("Filesystem", @"\b(File|Directory|FileStream|StreamWriter|StreamReader)\b"), ("Network", @"\b(HttpClient|WebClient|WebRequest|Socket|TcpClient)\b"), ("Process", @"\bProcess\b"), ("Registry / environment", @"\b(Registry|SetEnvironmentVariable|Environment\.Exit)\b"), ("Reflection / loading", @"\b(Assembly|Activator|Reflection|LoadFrom|LoadFile)\b"), ("Native interop", @"\b(DllImport|LibraryImport|Marshal|unsafe)\b"), ("Potential long loop", @"\b(while|for)\s*\(") })
            foreach (Match match in Regex.Matches(source, rule.Item2, RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                risks.Add(new(rule.Item1, 1 + source.Take(match.Index).Count(c => c == '\n'), "Review " + rule.Item1.ToLowerInvariant() + " effects. Advisory detection is incomplete and may flag comments or literals."));
                if (risks.Count == 200) return risks;
            }
        return risks.Take(200).ToArray();
    }
    private static string Prefix(string source, int offset) { if (source.Length > 1024 * 1024 || offset < 0 || offset > source.Length) throw new ArgumentException("Invalid script size or caret."); return source.Substring(0, offset); }
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
    private static CSharpCompletion Collection(string name) => new(name, "Collection", name, "Semantic objects; use foreach or exact name indexing where supported.");
    private static CSharpCompletion NameCompletion(AutomationSymbol s) => new(s.Name.Replace("\\", "\\\\").Replace("\"", "\\\""), s.Kind, s.Name, s.Selected ? "Selected model object" : "Loaded model object");
    private static CSharpCompletion[] Filter(IEnumerable<CSharpCompletion> source, string prefix) => source.Where(m => m.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
}
