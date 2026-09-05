using PbiBench.Core.Automation;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class SafeScriptTests
{
    [Fact]
    public void ParserProducesTypedOperationsWithLiteralEscapesAndNameTemplates()
    {
        var parsed = SafeCSharpParser.Parse("// no code execution\nforeach(var m in Selected.Measures) { m.Description = @\"Measure: \" + m.Table.Name + \" / \" + m.Name; m.IsHidden = false; }\nModel.Tables[\"Sales\"].AddMeasure(\"Count\", \"COUNTROWS('Sales')\");");
        Assert.True(parsed.IsValid); Assert.Equal(3, parsed.Recipe!.Steps.Count); Assert.Equal("Measure: Sales / Revenue", parsed.Recipe.Steps[0].Value.Evaluate("Revenue", "Sales")); Assert.Equal(RecipeOperation.CreateMeasure, parsed.Recipe.Steps[2].Operation);
    }
    [Theory]
    [InlineData("System.IO.File.WriteAllText(\"x\",\"y\");")]
    [InlineData("Process.Start(\"cmd\");")]
    [InlineData("using System; Model.SaveChanges();")]
    [InlineData("Model.GetType().Assembly.GetTypes();")]
    [InlineData("while(true) { }")]
    [InlineData("foreach(var m in Model.AllMeasures) { foreach(var n in Model.AllMeasures) { n.IsHidden = true; } }")]
    [InlineData("foreach(var m in Model.AllMeasures) { m.Description = Environment.GetEnvironmentVariable(\"SECRET\"); }")]
    [InlineData("Model.Tables[\"Sales\"].Name = $\"{System.IO.File.ReadAllText(\"x\")}\";")]
    [InlineData("#r \"malicious.dll\"")]
    [InlineData("foreach(var Model in Selected.Measures) { Model.IsHidden = true; }")]
    [InlineData("Model.AllMeasures.IsHidden = true;")]
    [InlineData("foreach(var m in Model.AllMeasures) { Model.Tables[\"Sales\"].Description = m.Name; }")]
    [InlineData("Model.Tables[\"Sales\"].IsHidden = true; System.Net.WebClient client = new System.Net.WebClient();")]
    public void ParserRejectsUnsupportedOrExternalAccessWithoutReturningPartialRecipe(string source)
    { var parsed = SafeCSharpParser.Parse(source); Assert.False(parsed.IsValid); Assert.Null(parsed.Recipe); Assert.NotEmpty(parsed.Issues); }
    [Fact]
    public void CommentsAndStringsNeverBecomeExecutableSyntax()
    {
        var parsed = SafeCSharpParser.Parse("/* File.Delete(\"x\") */ Model.Tables[\"Sales\"].Description = \"Process.Start(\\\"x\\\"); // harmless text\";"); Assert.True(parsed.IsValid); Assert.Contains("Process.Start", parsed.Recipe!.Steps.Single().Value.Evaluate("Sales", null));
        Assert.False(SafeCSharpParser.Parse("/* unterminated").IsValid); Assert.False(SafeCSharpParser.Parse(new string('x', 262145)).IsValid);
    }
    [Fact]
    public void RequiredValueAndExpandedValueBoundsAreValidatedBeforeInterpretation()
    {
        var target = new RecipeTarget(RecipeScope.Table, null, "Sales");
        Assert.Throws<ArgumentException>(() => ActionRecipeRules.Validate(new ActionRecipe("Invalid", new[] { new RecipeStep(target, RecipeOperation.SetProperty, "Name", null!) })));
        Assert.Throws<InvalidOperationException>(() => new RecipeValue(new[] { new RecipeValuePart(RecipeValueKind.Literal, new string('x', 262144)), new RecipeValuePart(RecipeValueKind.Literal, "extra") }).Evaluate("", null));
    }
    [Fact]
    public async Task RecipeAndExplicitTrustMacroModesRoundTripWithoutExecuting()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pbibench-macro-" + Guid.NewGuid().ToString("N")); var path = Path.Combine(directory, "recipes.json");
        try
        {
            var recipe = SafeCSharpParser.Parse("Model.Tables[\"Sales\"].IsHidden = true;").Recipe!; await RecipeFiles.SaveRecipeAsync(path, recipe, CancellationToken.None); Assert.Equal(RecipeScope.Table, (await RecipeFiles.LoadRecipeAsync(path, CancellationToken.None)).Steps.Single().Target.Scope);
            var library = new MacroLibrary(new[] { new ScriptMacro(Guid.NewGuid().ToString(), "Legacy", MacroMode.TrustedLegacy, "System.IO.File.Delete(\"never-run\");"), new ScriptMacro(Guid.NewGuid().ToString(), "Typed", MacroMode.Recipe, "", recipe) });
            await RecipeFiles.SaveLibraryAsync(path, library, CancellationToken.None); var loaded = await RecipeFiles.LoadLibraryAsync(path, CancellationToken.None); Assert.Equal(MacroMode.TrustedLegacy, loaded.Macros[0].Mode); Assert.Contains("never-run", loaded.Macros[0].Source);
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecipeFiles.SaveLibraryAsync(path, library, cancel.Token)); Assert.Equal(2, (await RecipeFiles.LoadLibraryAsync(path, CancellationToken.None)).Macros.Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
