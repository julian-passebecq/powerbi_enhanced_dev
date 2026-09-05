using PbiBench.Core.Automation;

namespace PbiBench.CSharp.LanguageService;

public sealed record GalleryParameter(string Name, string Default, int MaxLength, IReadOnlyList<string>? Choices = null);
public enum ImplementationOrigin { PbiBenchOriginal, PbiBenchNative, AdaptedMIT, ExternalReference }
public enum GalleryVerification { Verified, Preview, Reference }
public enum GalleryExecutionMode { NativeReadOnly, SafeRecipe, SafeScript, TrustedDraft }
public sealed record PowerBiGalleryCard(string Id, string Title, string Category, string Purpose, string Selection,
    string Mode, string Compatibility, string Risk, string License, IReadOnlyList<GalleryParameter> Parameters,
    ImplementationOrigin ImplementationOrigin = ImplementationOrigin.PbiBenchOriginal,
    string? ReferenceUrl = null, string? ReferencePin = null,
    GalleryVerification Verification = GalleryVerification.Verified,
    GalleryExecutionMode ExecutionMode = GalleryExecutionMode.SafeRecipe);

/// <summary>Original native/recipe implementations of common public automation patterns. No upstream script is auto-loaded or executed.</summary>
public static partial class PowerBiGallery
{
    public const string Upstream = "https://github.com/TabularEditor/Scripts";
    public const string CompatibilityReference = "https://docs.tabulareditor.com/common/CSharpScripts/";
    private static GalleryParameter P(string name, string value, int max = 128, params string[] choices) => new(name, value, max, choices.Length == 0 ? null : Array.AsReadOnly(choices));
    private static PowerBiGalleryCard Card(string id, string title, string category, string purpose, string selection, string risk, params GalleryParameter[] parameters) =>
        new(id, title, category, purpose, selection, "SAFE RECIPE", "Local/offline or connected metadata · model Undo · Save remains separate", risk, "PbiBench original implementation (repository license); no upstream code copied.", Array.AsReadOnly(parameters), ReferenceUrl: Upstream);
    public static IReadOnlyList<PowerBiGalleryCard> All { get; } = Array.AsReadOnly(new[] {
        Card("sum", "SUM measures", "Measures", "Create an explicit SUM measure for each selected numeric column.", "Column", "Non-numeric columns are rejected; duplicate names are checked in preview.", P("Prefix", "Total "), P("Display folder", "Measures")),
        Card("countrows", "COUNTROWS measures", "Measures", "Create a row-count measure for each selected table.", "Table", "Counts table rows; confirm business semantics.", P("Suffix", " Count"), P("Display folder", "Counts")),
        Card("explicit", "Explicit aggregation measures", "Measures", "Create measures with a chosen aggregation.", "Column", "Choose the aggregation appropriate for these numeric columns.", P("Aggregation", "SUM", 8, "SUM", "AVERAGE", "MIN", "MAX"), P("Prefix", "Total "), P("Display folder", "Measures")),
        Card("format", "Bulk format strings", "Measures", "Set a selected set of measure format strings.", "Measure", "Check units and percentages in exact preview.", P("Format string", "#,0.00", 256)),
        Card("folder", "Organize measure folders", "Measures", "Move selected measures to a display folder.", "Measure", "Replaces existing folders only for the captured selection.", P("Display folder", "Measures", 256)),
        Card("summarize", "Disable implicit summarization", "Hygiene", "Set selected columns to SummarizeBy=None.", "Column", "Can change how visuals aggregate these columns."),
        Card("hide", "Hide selected technical columns", "Hygiene", "Hide explicitly selected relationship keys or technical columns.", "Column", "Review selection; hiding does not remove existing report usage."),
        Card("clean", "Clean object names", "Hygiene", "Replace a bounded text fragment and trim object names.", "Measure/Column/Table", "Renaming can affect report references; inspect Report Usage before applying.", P("Find", "_"), P("Replace", " ")),
        Card("describe", "Template descriptions", "Hygiene", "Add or update descriptions from a name/table template.", "Measure/Column/Table", "Replaces descriptions on the captured selection.", P("Template", "{Name} in {Table}.", 1024)),
        Card("measure-table", "Create measure table", "Measures", "Use the existing native measure-table action.", "Model", "Creates a local metadata table; inspect its storage/refresh implications.", P("Table name", "_Measures")) with { Mode = "SAFE RECIPE · native", ImplementationOrigin = ImplementationOrigin.PbiBenchNative, ReferenceUrl = null },
        Card("format-dax", "Format DAX expressions", "Measures", "Use PbiBench's existing local DAX formatter and model preview.", "Measure", "Review expression changes; no remote formatting service.") with { Mode = "SAFE RECIPE · native", ImplementationOrigin = ImplementationOrigin.PbiBenchNative, ReferenceUrl = null },
        Card("references", "Scan broken object references", "Quality", "Open native BPA and semantic reference diagnostics.", "Model", "Read-only findings; review any proposed fix separately.") with { Mode = "NATIVE · read-only", ImplementationOrigin = ImplementationOrigin.PbiBenchNative, ReferenceUrl = null, ExecutionMode = GalleryExecutionMode.NativeReadOnly },
        Card("profile", "Relationship / data quality", "Quality", "Open PbiBench Explore/Profile for bounded quality queries.", "Model", "Data profiling requires a live engine and explicit query execution.") with { Mode = "NATIVE · read-only", ImplementationOrigin = ImplementationOrigin.PbiBenchNative, ReferenceUrl = null, ExecutionMode = GalleryExecutionMode.NativeReadOnly }
    }.Concat(DepthCards()).ToArray());
    public static ActionRecipe Generate(PowerBiGalleryCard card, IReadOnlyList<AutomationSymbol> symbols, IReadOnlyDictionary<string, string> parameters)
    {
        if (!All.Contains(card) || card.Mode != "SAFE RECIPE") throw new ArgumentException("This gallery entry routes to a native PbiBench command.");
        var selected = symbols.Where(s => s.Selected).ToArray();
        if (selected.Length is < 1 or > 200) throw new ArgumentException("Select 1–200 objects before preparing this recipe.");
        if (selected.Any(s => !card.Selection.Split('/').Contains(s.Kind))) throw new ArgumentException("Required selection: " + card.Selection);
        string Value(string name)
        {
            var definition = card.Parameters.Single(p => p.Name == name); var value = parameters.TryGetValue(name, out var supplied) ? supplied : definition.Default;
            if (value.Length > definition.MaxLength || value.Any(c => char.IsControl(c) && c != '\n') || definition.Choices != null && !definition.Choices.Contains(value)) throw new ArgumentException("Invalid parameter: " + name);
            return value;
        }
        RecipeValue V(string value) => RecipeValue.Literal(value);
        var steps = new List<RecipeStep>();
        foreach (var symbol in selected)
        {
            var target = new RecipeTarget((RecipeScope)Enum.Parse(typeof(RecipeScope), symbol.Kind), symbol.Table, symbol.Name);
            if (card.Id is "sum" or "countrows" or "explicit")
            {
                if (card.Id != "countrows" && symbol.DataType is not ("Int64" or "Double" or "Decimal")) throw new ArgumentException("Select numeric columns only.");
                var table = symbol.Kind == "Table" ? symbol.Name : symbol.Table ?? throw new ArgumentException("Missing table.");
                var quoted = "'" + table.Replace("'", "''") + "'";
                var name = card.Id == "countrows" ? symbol.Name + Value("Suffix") : Value("Prefix") + symbol.Name;
                var expression = card.Id == "countrows" ? "COUNTROWS ( " + quoted + " )" : (card.Id == "sum" ? "SUM" : Value("Aggregation")) + " ( " + quoted + "[" + symbol.Name.Replace("]", "]]") + "] )";
                steps.Add(new(new(RecipeScope.Table, Name: table), RecipeOperation.CreateMeasure, "", V(name), V(expression), V(Value("Display folder"))));
            }
            else
            {
                var property = card.Id switch { "dynamic-format" => "FormatStringExpression", "format" => "FormatString", "folder" => "DisplayFolder", "summarize" => "SummarizeBy", "hide" => "IsHidden", "clean" => "Name", "describe" => "Description", _ => throw new ArgumentException("Unknown recipe.") };
                var value = card.Id switch { "dynamic-format" => Value("Format expression"), "format" => Value("Format string"), "folder" => Value("Display folder"), "summarize" => "None", "hide" => "true", "clean" => string.IsNullOrEmpty(Value("Find")) ? throw new ArgumentException("Find text cannot be empty.") : symbol.Name.Replace(Value("Find"), Value("Replace")).Trim(), _ => Value("Template").Replace("{Name}", symbol.Name).Replace("{Table}", symbol.Table ?? symbol.Name) };
                steps.Add(new(target, RecipeOperation.SetProperty, property, V(value)));
            }
        }
        var recipe = new ActionRecipe(card.Title, steps.AsReadOnly()); ActionRecipeRules.Validate(recipe); return recipe;
    }
}
