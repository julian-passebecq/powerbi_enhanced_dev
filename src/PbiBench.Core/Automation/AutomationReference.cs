using System.Text.Json;

namespace PbiBench.Core.Automation;

public static class AutomationReference
{
    public static string CapabilitiesJson() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1, engine = "SafeCSharpParser + ActionRecipeRules + detached TOM preview",
        scopes = Enum.GetNames(typeof(RecipeScope)), operations = Enum.GetNames(typeof(RecipeOperation)),
        writableProperties = ActionRecipeRules.Properties, valueParts = Enum.GetNames(typeof(RecipeValueKind)),
        maximumScriptCharacters = 262144, maximumRecipeSteps = 2000,
        validation = "Properties are a union across object types. Unsupported property/type combinations fail in detached preview. Expressions require model validation. No arbitrary calls, LINQ, CLR, IO or compilation.",
        approval = "Local exact diff followed by user approval and native Undo; files never contain approval authority."
    }, new JsonSerializerOptions { WriteIndented = true });
    public const string Readme = """
        # PbiBench automation reference

        Safe Preview interprets a restricted C#-shaped grammar into a typed ActionRecipe, computes an exact diff on detached TOM, and requires a separate user Apply with native Undo. It is not a general C# compiler.
        Trusted C# uses the TE2 compiler with unrestricted process permissions. Explicit trust and a model snapshot are required; external effects cannot be undone. Risk hints are advisory, never proof of safety.

        Prefer Safe Preview-compatible proposals. Label any script requiring Trusted C#. Never execute imported text automatically. Use actual exported object names. The machine-readable capability file is generated from the current parser contract enums and property allowlist. Unsupported constructs fail closed.

        Original Safe Preview examples:
        ```csharp
        foreach (var c in Selected.Columns) { c.SummarizeBy = AggregateFunction.None; }
        foreach (var m in Selected.Measures) { m.DisplayFolder = "Finance"; }
        foreach (var c in Selected.Columns) { c.IsHidden = true; }
        foreach (var m in Model.AllMeasures) { m.Description = "Measure: " + m.Name; }
        Model.Tables["Sales"].AddMeasure("Total Sales", "SUM('Sales'[Amount])", "Finance");
        ```
        Adapt Sales/Amount to the exported inventory. `Model` is the active semantic model and `Selected` is the explicit tree selection. Exact indexing and foreach over approved Model/Selected collections are supported. Selected numeric SUM generation with type filtering, creating a calculated measure table, and DAX formatting helpers need Trusted C# or the existing typed Automation actions.
        """;
}
