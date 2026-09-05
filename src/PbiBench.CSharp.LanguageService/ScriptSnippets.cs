namespace PbiBench.CSharp.LanguageService;

public sealed record ScriptSnippet(string Name, bool TrustedOnly, string Source)
{ public override string ToString() => Name + (TrustedOnly ? " · Trusted C#" : " · Safe Preview"); }
public static class ScriptSnippets
{
    public static IReadOnlyList<ScriptSnippet> All { get; } = Array.AsReadOnly(new[]
    {
        new ScriptSnippet("SUM measures from numeric selection", true, "foreach (var c in Selected.Columns.Where(c => c.DataType == DataType.Int64 || c.DataType == DataType.Double || c.DataType == DataType.Decimal))\n{\n    c.Table.AddMeasure(\"Sum \" + c.Name, \"SUM(\" + c.DaxObjectFullName + \")\", \"Totals\");\n}"),
        new ScriptSnippet("No default summarization", false, "foreach (var c in Selected.Columns)\n{\n    c.SummarizeBy = AggregateFunction.None;\n}"),
        new ScriptSnippet("Measure display folder", false, "foreach (var m in Selected.Measures)\n{\n    m.DisplayFolder = \"Finance\";\n}"),
        new ScriptSnippet("Hide selected technical columns", false, "foreach (var c in Selected.Columns)\n{\n    c.IsHidden = true;\n}"),
        new ScriptSnippet("Describe selected measures", false, "foreach (var m in Selected.Measures)\n{\n    m.Description = \"Measure: \" + m.Name;\n}"),
        new ScriptSnippet("Create a measure table", true, "var t = Model.AddCalculatedTable(\"Measures\", \"{ BLANK() }\");\nforeach (var c in t.Columns) c.IsHidden = true;"),
        new ScriptSnippet("Format selected measures", true, "foreach (var m in Selected.Measures)\n{\n    m.FormatDax();\n}"),
        new ScriptSnippet("Year to date template", false, "// Adapt object names to the loaded model before preview.\nModel.Tables[\"Sales\"].AddMeasure(\"Revenue YTD\", \"TOTALYTD([Revenue], 'Date'[Date])\", \"Time intelligence\");"),
        new ScriptSnippet("Selected object loop", false, "foreach (var m in Selected.Measures)\n{\n    m.Description = m.Name;\n}")
    });
}
