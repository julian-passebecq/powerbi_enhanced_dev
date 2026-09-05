namespace PbiBench.Core.Automation;

public enum RecipeScope { SelectedMeasures, SelectedColumns, SelectedTables, AllMeasures, AllColumns, AllTables, Measure, Column, Table }
public enum RecipeOperation { SetProperty, CreateMeasure, DeleteMeasure }
public enum RecipeValueKind { Literal, ObjectName, TableName }
public sealed record RecipeValuePart(RecipeValueKind Kind, string Text = "");
public sealed record RecipeValue(IReadOnlyList<RecipeValuePart> Parts)
{
    public static RecipeValue Literal(string value) => new(new[] { new RecipeValuePart(RecipeValueKind.Literal, value) });
    public string Evaluate(string objectName, string? tableName)
    {
        var value = new System.Text.StringBuilder();
        foreach (var part in Parts)
        {
            var text = part.Kind switch { RecipeValueKind.Literal => part.Text, RecipeValueKind.ObjectName => objectName, RecipeValueKind.TableName => tableName ?? throw new InvalidOperationException("This object has no containing table."), _ => throw new InvalidOperationException("Unknown recipe value.") };
            if (value.Length + (long)text.Length > 262144) throw new InvalidOperationException("Expanded recipe values are limited to 256 KiB."); value.Append(text);
        }
        return value.ToString();
    }
}
public sealed record RecipeTarget(RecipeScope Scope, string? Table = null, string? Name = null);
public sealed record RecipeStep(RecipeTarget Target, RecipeOperation Operation, string Property, RecipeValue Value,
    RecipeValue? Expression = null, RecipeValue? DisplayFolder = null);
public sealed record ActionRecipe(string Name, IReadOnlyList<RecipeStep> Steps, int Version = 1);
public sealed record ScriptParseIssue(int Offset, string Message);
public sealed record ScriptParseResult(ActionRecipe? Recipe, IReadOnlyList<ScriptParseIssue> Issues)
{
    public bool IsValid => Recipe != null && Issues.Count == 0;
}

public static class ActionRecipeRules
{
    public static readonly IReadOnlyList<string> Properties = Array.AsReadOnly(new[] { "Name", "Description", "IsHidden", "DisplayFolder", "Expression", "FormatString", "FormatStringExpression", "SummarizeBy" });
    public static void Validate(ActionRecipe recipe)
    {
        if (recipe == null || recipe.Version != 1 || string.IsNullOrWhiteSpace(recipe.Name) || recipe.Name.Length > 128 || recipe.Steps == null || recipe.Steps.Count > 2000) throw new ArgumentException("Invalid recipe version, name or step count (maximum 2,000). ");
        foreach (var step in recipe.Steps)
        {
            if (step == null || step.Target == null || step.Value == null || !Enum.IsDefined(typeof(RecipeScope), step.Target.Scope) || !Enum.IsDefined(typeof(RecipeOperation), step.Operation)) throw new ArgumentException("Missing required value or unknown recipe target/operation.");
            if (step.Target.Table?.Length > 512 || step.Target.Name?.Length > 512) throw new ArgumentException("Object names are limited to 512 characters.");
            if (step.Operation == RecipeOperation.SetProperty && !Properties.Contains(step.Property)) throw new ArgumentException("This model property is not supported by Safe Preview: " + step.Property);
            if (step.Operation == RecipeOperation.CreateMeasure && step.Expression == null) throw new ArgumentException("A new measure requires an expression.");
            if (step.Operation != RecipeOperation.SetProperty && step.Property != "" || step.Operation != RecipeOperation.CreateMeasure && (step.Expression != null || step.DisplayFolder != null)) throw new ArgumentException("Unexpected fields for this recipe operation.");
            if (step.Target.Scope is RecipeScope.Table or RecipeScope.Column or RecipeScope.Measure && string.IsNullOrWhiteSpace(step.Target.Name) || step.Target.Scope is RecipeScope.Column or RecipeScope.Measure && string.IsNullOrWhiteSpace(step.Target.Table)) throw new ArgumentException("Explicit object targets require their names and containing table.");
            foreach (var value in new[] { step.Value, step.Expression, step.DisplayFolder }.Where(value => value != null))
                if (value!.Parts == null || value.Parts.Count > 100 || value.Parts.Any(part => part == null || !Enum.IsDefined(typeof(RecipeValueKind), part.Kind) || part.Text == null || part.Text.Length > 262144)) throw new ArgumentException("Invalid or oversized recipe value.");
        }
    }
}
