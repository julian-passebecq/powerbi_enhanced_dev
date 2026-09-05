using System.Text.Json;

namespace PbiBench.CSharp.LanguageService;

public static partial class PowerBiGallery
{
    private static IEnumerable<PowerBiGalleryCard> DepthCards()
    {
        yield return Card("annotation", "Annotation helper", "Hygiene", "Preview a bounded annotation on selected objects through the native model service.", "Measure/Column/Table", "Replaces this named annotation only.", P("Name", "PbiBench.Review"), P("Value", "Reviewed", 2048))
            with { Mode = "SAFE RECIPE · native", ImplementationOrigin = ImplementationOrigin.PbiBenchNative, ReferenceUrl = null };
        yield return Card("translations", "Translation / description helper", "Hygiene", "Open the existing translation matrix with draft, import and preview support.", "Model", "Review culture and inherited values before applying.")
            with { Mode = "SAFE RECIPE · native", ImplementationOrigin = ImplementationOrigin.PbiBenchNative, ReferenceUrl = null };
        yield return Card("dynamic-format", "Dynamic measure formats", "Measures", "Use the existing typed FormatStringExpression action.", "Measure", "Requires compatibility 1601; replaces static format state in the reviewed plan.", P("Format expression", "\"#,0.00\"", 2048)) with { ReferenceUrl = null };
        yield return Card("inactive", "Inactive relationship usage", "Quality", "Inspect inactive relationships and measures mentioning USERELATIONSHIP using local metadata.", "Model", "Expression matches are advisory; inspect DAX and relationship endpoints.")
            with { Mode = "NATIVE · read-only", ExecutionMode = GalleryExecutionMode.NativeReadOnly, ImplementationOrigin = ImplementationOrigin.PbiBenchNative, ReferenceUrl = null };
        yield return Draft("selector", "Dynamic measure selector template", "Create a disconnected selector and SWITCH measure from selected measures.", "Measure", P("Selector table", "Metric Selector"), P("Target table", "_Measures"), P("Measure name", "Selected Metric"));
        yield return Draft("time-calc", "Time-intelligence calculation group", "Scaffold Current, YTD and Previous Year calculation items.", "Model", P("Group name", "Time Intelligence"), P("Date table", "Date"), P("Date column", "Date"));
        yield return Draft("advanced-calc", "Advanced calculation-group scaffolding", "Scaffold Current, Previous Year and YoY percentage with explicit precedence.", "Model", P("Group name", "Period Comparison"), P("Date table", "Date"), P("Date column", "Date"));
    }
    private static PowerBiGalleryCard Draft(string id, string title, string purpose, string selection, params GalleryParameter[] parameters) =>
        Card(id, title, "Advanced drafts", purpose, selection, "Draft only. Review names, dates, precedence, relationships and engine compatibility. Never auto-run.", parameters)
        with { Mode = "TRUSTED DRAFT · text only", Compatibility = "Calculation groups require 1500+; selector requires an existing target table. Compile and independently review before any Trusted execution.",
            ReferenceUrl = null, Verification = GalleryVerification.Preview, ExecutionMode = GalleryExecutionMode.TrustedDraft };
    public static string CompatibilityReason(PowerBiGalleryCard card, IReadOnlyList<AutomationSymbol> symbols, int? compatibility)
    {
        if (compatibility == null) return "Open a model to check selection compatibility.";
        if (card.Id == "dynamic-format" && compatibility < 1601) return "Dynamic formats require compatibility 1601 or higher; this action does not upgrade the model.";
        if (card.Id is "time-calc" or "advanced-calc" && compatibility < 1500) return "Calculation groups require compatibility 1500 or higher.";
        var selected = symbols.Where(s => s.Selected).ToArray();
        if (card.Selection != "Model" && (selected.Length == 0 || selected.Any(s => !card.Selection.Split('/').Contains(s.Kind)))) return "Required selection: " + card.Selection;
        if (selected.Length > (card.Id == "selector" ? 20 : 200)) return "Selection exceeds this action's bound.";
        if (card.Id is "sum" or "explicit" && selected.Any(s => s.DataType is not ("Int64" or "Double" or "Decimal"))) return "Select numeric columns only.";
        return "Selection compatible · " + (card.ExecutionMode == GalleryExecutionMode.TrustedDraft ? "text generation only" : "review exact changes before applying");
    }
    public static string GenerateDraft(PowerBiGalleryCard card, IReadOnlyList<AutomationSymbol> symbols, IReadOnlyDictionary<string, string> parameters)
    {
        if (!All.Contains(card) || card.ExecutionMode != GalleryExecutionMode.TrustedDraft) throw new ArgumentException("Select an advanced draft card.");
        string Value(string key)
        {
            var p = card.Parameters.Single(p => p.Name == key); var value = parameters.TryGetValue(key, out var supplied) ? supplied : p.Default;
            if (string.IsNullOrWhiteSpace(value) || value.Length > p.MaxLength || value.Any(char.IsControl)) throw new ArgumentException("Invalid draft parameter: " + key);
            return value;
        }
        string Cs(string value) => JsonSerializer.Serialize(value);
        string Table(string value) => "'" + value.Replace("'", "''") + "'";
        string Field(string value) => "[" + value.Replace("]", "]]") + "]";
        const string heading = "// PbiBench original · Preview · TRUSTED DRAFT ONLY\n// Generated text only. Review source, compile, snapshot and acknowledge trust separately.\n";
        if (card.Id == "selector")
        {
            var selected = symbols.Where(s => s.Selected).ToArray();
            if (selected.Length is < 1 or > 20 || selected.Any(s => s.Kind != "Measure" || string.IsNullOrEmpty(s.Table))) throw new ArgumentException("Select 1–20 measures.");
            var selector = Value("Selector table"); var target = Value("Target table"); var name = Value("Measure name");
            var choices = selected.Select((s, i) => "{ " + (i + 1) + ", \"" + s.Name.Replace("\"", "\"\"") + "\" }");
            var tableExpression = "DATATABLE ( \"Id\", INTEGER, \"Metric\", STRING, { " + string.Join(", ", choices) + " } )";
            var expression = "SWITCH ( SELECTEDVALUE ( " + Table(selector) + "[Id] ), " + string.Join(", ", selected.Select((s, i) => (i + 1) + ", " + Table(s.Table!) + Field(s.Name))) + ", BLANK() )";
            return heading + "// Keep the selector disconnected. Confirm the output measure's units and formatting.\n" +
                "if (Model.Tables.Contains(" + Cs(selector) + ")) throw new InvalidOperationException(\"Selector table already exists.\");\n" +
                "var target = Model.Tables[" + Cs(target) + "];\nif (Model.AllMeasures.Any(m => m.Name == " + Cs(name) + ")) throw new InvalidOperationException(\"Measure already exists.\");\n" +
                "Model.AddCalculatedTable(" + Cs(selector) + ", " + Cs(tableExpression) + ");\ntarget.AddMeasure(" + Cs(name) + ", " + Cs(expression) + ");\n";
        }
        var group = Value("Group name"); var dates = Table(Value("Date table")) + Field(Value("Date column"));
        var previous = "CALCULATE ( SELECTEDMEASURE(), DATEADD ( " + dates + ", -1, YEAR ) )";
        var source = heading + "// Requires compatibility 1500+, a valid marked date table, and reviewed calculation-group precedence.\n" +
            "if (Model.Database.CompatibilityLevel < 1500) throw new InvalidOperationException(\"Requires compatibility 1500.\");\n" +
            "if (Model.Tables.Contains(" + Cs(group) + ")) throw new InvalidOperationException(\"Group already exists.\");\n" +
            "var dates = Model.Tables[" + Cs(Value("Date table")) + "].Columns[" + Cs(Value("Date column")) + "];\n" +
            "var group = Model.AddCalculationGroup(" + Cs(group) + ");\ngroup.CalculationGroup.Precedence = 10; // REVIEW relative to existing groups\n" +
            "group.AddCalculationItem(\"Current\", \"SELECTEDMEASURE()\");\n";
        if (card.Id == "time-calc") source += "group.AddCalculationItem(\"YTD\", " + Cs("TOTALYTD ( SELECTEDMEASURE(), " + dates + " )") + ");\n";
        source += "group.AddCalculationItem(\"Previous Year\", " + Cs(previous) + ");\n";
        if (card.Id == "advanced-calc") source += "var yoy = group.AddCalculationItem(\"YoY %\", " + Cs("VAR Previous = " + previous + " RETURN DIVIDE ( SELECTEDMEASURE() - Previous, Previous )") + ");\nyoy.FormatStringExpression = \"\\\"0.0%\\\"\";\n";
        return source;
    }
}
