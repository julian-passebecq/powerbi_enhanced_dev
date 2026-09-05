using System.Text.Json;
using PbiBench.Core.Automation;
using PbiBench.CSharp.LanguageService;
using Xunit;

namespace PbiBench.V11.Tests;
public sealed class LanguageTests
{
    [Fact] public void NameCompletionReplacesTheEntirePrefixIncludingSpaces()
    {
        var text = "Model.Tables[\"Sales Led"; var completion = Assert.Single(new CSharpLanguageService().Complete(text, text.Length, new[] { new AutomationSymbol("Table", "Sales Ledger") }));
        Assert.Equal("Sales Led", text.Substring(completion.ReplaceStart!.Value, completion.ReplaceLength));
        Assert.Equal("Model.Tables[\"Sales Ledger", text.Substring(0, completion.ReplaceStart.Value) + completion.Text);
    }
    [Theory] [InlineData("Model.", "AllMeasures")] [InlineData("Selected.", "Measures")] [InlineData("foreach (var m in Selected.Measures) { m.", "Expression")] [InlineData("Model.Tables[\"Sales\"].", "AddMeasure")]
    public void SemanticCompletionUsesReceiver(string text, string expected) => Assert.Contains(new CSharpLanguageService().Complete(text, text.Length, Array.Empty<AutomationSymbol>()), c => c.Text == expected);
    [Fact] public void InventoryCompletionHonorsTableAndMemberType()
    {
        var text = "Model.Tables[\"Sales\"].Columns[\"A";
        var result = new CSharpLanguageService().Complete(text, text.Length, new[] { new AutomationSymbol("Column", "Amount", "Sales", true), new AutomationSymbol("Measure", "Average", "Sales"), new AutomationSymbol("Column", "Another", "Other") }); Assert.Equal("Amount", Assert.Single(result).Text);
    }
    [Theory] [InlineData("Model.Tables[\"Sales\"].AddMeasure(", "string name")] [InlineData("Model.AddCalculatedTable(", "expression")]
    public void CallTipsExplainSemanticParameters(string text, string expected) => Assert.Contains(expected, new CSharpLanguageService().Signature(text, text.Length));
    [Theory] [InlineData("File.WriteAllText(path, data)", "Filesystem")] [InlineData("new HttpClient()", "Network")] [InlineData("Process.Start(path)", "Process")] [InlineData("Registry.SetValue()", "Registry / environment")] [InlineData("Assembly.Load(x)", "Reflection / loading")] [InlineData("[DllImport(\"native\")]", "Native interop")] [InlineData("while(true) {}", "Potential long loop")]
    public void RiskHintsAreAdvisoryAndLocateLine(string text, string category) { var risk = Assert.Single(new CSharpLanguageService().Risks("\n" + text)); Assert.Equal(category, risk.Category); Assert.Equal(2, risk.Line); Assert.Contains("Advisory", risk.Message); }
    [Fact] public void SafeSnippetsParseAndCapabilitiesReflectActualContract()
    {
        foreach (var snippet in ScriptSnippets.All.Where(s => !s.TrustedOnly)) Assert.True(SafeCSharpParser.Parse(snippet.Source).IsValid, snippet.Name);
        using var doc = JsonDocument.Parse(AutomationReference.CapabilitiesJson()); Assert.Equal(ActionRecipeRules.Properties, doc.RootElement.GetProperty("writableProperties").EnumerateArray().Select(v => v.GetString()));
    }
    [Fact] public void GeneratedRecipePreservesOperationOrderValuesAndNotices()
    {
        var source = "foreach (var m in Selected.Measures) { m.Description = \"Measure: \" + m.Name; }\nModel.Tables[\"Sales\"].AddMeasure(\"Test\", \"1\", \"Tests\");\nModel.Tables[\"Sales\"].Measures[\"Test\"].Delete();";
        var recipe = SafeCSharpParser.Parse(source).Recipe!; var generated = RecipeCSharpGenerator.Generate(recipe, new[] { "Unsupported hierarchy edit remains in model." }); var parsed = SafeCSharpParser.Parse(generated.Source);
        Assert.True(parsed.IsValid); Assert.Equal(JsonSerializer.Serialize(recipe.Steps), JsonSerializer.Serialize(parsed.Recipe!.Steps)); Assert.Contains("RECORDER LIMITATION", generated.Source);
    }
    [Fact] public async Task RecoveryPreservesUnsavedDocumentsAndNoExecutionAuthority()
    {
        var path = Path.GetTempFileName(); try
        {
            var a = new ScriptDocument(Guid.NewGuid().ToString(), "a.csx", "unsaved", SavedText: "old"); var b = new ScriptDocument(Guid.NewGuid().ToString(), "b.cs", "saved", SavedText: "saved");
            await ScriptWorkspaceFiles.SaveRecoveryAsync(path, new(new[] { a, b }, a.Id), default); var saved = await ScriptWorkspaceFiles.LoadRecoveryAsync(path, default);
            Assert.Equal(a.Id, saved.ActiveId); Assert.True(saved.Documents[0].IsDirty); Assert.False(saved.Documents[1].IsDirty); Assert.DoesNotContain("trust", File.ReadAllText(path).ToLowerInvariant());
        } finally { File.Delete(path); }
    }
    [Fact] public async Task MacroMetadataIsBackwardCompatibleAndRetainsLane()
    {
        var path = Path.GetTempFileName(); try
        {
            var macro = new ScriptMacro(Guid.NewGuid().ToString(), "Review", MacroMode.TrustedLegacy, "source") { Tags = new[] { "finance" }, Favorite = true };
            await RecipeFiles.SaveLibraryAsync(path, new(new[] { macro }), default); var saved = await RecipeFiles.LoadLibraryAsync(path, default); Assert.True(saved.Macros[0].Favorite); Assert.Equal(MacroMode.TrustedLegacy, saved.Macros[0].Mode);
            File.WriteAllText(path, "{\"Version\":1,\"Macros\":[{\"Id\":\"" + macro.Id + "\",\"Name\":\"Old\",\"Mode\":0,\"Source\":\"\"}]}"); Assert.Empty((await RecipeFiles.LoadLibraryAsync(path, default)).Macros[0].Tags);
        } finally { File.Delete(path); }
    }
}
