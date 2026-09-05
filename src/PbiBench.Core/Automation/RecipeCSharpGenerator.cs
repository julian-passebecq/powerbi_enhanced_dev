using System.Text;

namespace PbiBench.Core.Automation;

public sealed record GeneratedRecipeScript(string Source, IReadOnlyList<string> Notices);
public static class RecipeCSharpGenerator
{
    public static GeneratedRecipeScript Generate(ActionRecipe recipe, IReadOnlyList<string>? recorderNotices = null)
    {
        ActionRecipeRules.Validate(recipe);
        var notices = recorderNotices?.ToArray() ?? Array.Empty<string>();
        var output = new StringBuilder("// Generated text only. Review with Safe Preview before Apply.\n");
        foreach (var notice in notices) output.Append("// RECORDER LIMITATION: ").AppendLine(notice.Replace("\r", " ").Replace("\n", " "));
        foreach (var step in recipe.Steps)
        {
            var target = step.Target;
            var collection = target.Scope switch { RecipeScope.SelectedMeasures => "Selected.Measures", RecipeScope.SelectedColumns => "Selected.Columns", RecipeScope.SelectedTables => "Selected.Tables", RecipeScope.AllMeasures => "Model.AllMeasures", RecipeScope.AllColumns => "Model.AllColumns", RecipeScope.AllTables => "Model.Tables", _ => null };
            var receiver = collection != null ? "o" : target.Scope == RecipeScope.Table ? "Model.Tables[" + Quote(target.Name!) + "]" : "Model.Tables[" + Quote(target.Table!) + "]." + (target.Scope == RecipeScope.Measure ? "Measures" : "Columns") + "[" + Quote(target.Name!) + "]";
            if (collection != null) output.Append("foreach (var o in ").Append(collection).AppendLine(")\n{");
            var value = Value(step.Value, receiver, target.Scope is RecipeScope.Table or RecipeScope.SelectedTables or RecipeScope.AllTables);
            if (step.Operation == RecipeOperation.SetProperty)
            {
                if (step.Property is "IsHidden" or "SummarizeBy")
                {
                    if (step.Value.Parts.Count != 1 || step.Value.Parts[0].Kind != RecipeValueKind.Literal) throw new ArgumentException("Enum and boolean assignments require literal values.");
                    value = step.Value.Parts[0].Text;
                    if (step.Property == "IsHidden" && value is not ("true" or "false")) throw new ArgumentException("Invalid boolean recipe value.");
                    if (step.Property == "SummarizeBy")
                    { if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z]+$")) throw new ArgumentException("Invalid aggregation recipe value."); value = "AggregateFunction." + value; }
                }
                output.Append(receiver).Append('.').Append(step.Property).Append(" = ").Append(value).AppendLine(";");
            }
            else if (step.Operation == RecipeOperation.DeleteMeasure) output.Append(receiver).AppendLine(".Delete();");
            else output.Append(receiver).Append(".AddMeasure(").Append(value).Append(", ").Append(Value(step.Expression!, receiver, true)).Append(", ").Append(step.DisplayFolder == null ? Quote("") : Value(step.DisplayFolder, receiver, true)).AppendLine(");");
            if (collection != null) output.AppendLine("}");
        }
        var source = output.ToString();
        var parsed = SafeCSharpParser.Parse(source);
        if (!parsed.IsValid) throw new InvalidOperationException("Recipe cannot be represented by the safe script grammar: " + string.Join("; ", parsed.Issues.Select(i => i.Message)));
        return new(source, notices);
    }
    private static string Value(RecipeValue value, string receiver, bool table) => value.Parts.Count == 0 ? Quote("") : string.Join(" + ", value.Parts.Select(p => p.Kind switch { RecipeValueKind.Literal => Quote(p.Text), RecipeValueKind.ObjectName => receiver + ".Name", RecipeValueKind.TableName when !table => receiver + ".Table.Name", _ => throw new ArgumentException("TableName requires a table child.") }));
    public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
}
