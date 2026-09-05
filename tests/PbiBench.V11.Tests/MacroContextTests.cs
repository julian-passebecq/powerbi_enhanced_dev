using System.Text.Json;
using PbiBench.Core.Automation;
using PbiBench.CSharp.LanguageService;
using Xunit;

namespace PbiBench.V11.Tests;

public sealed class MacroContextTests
{
    [Fact] public async Task OptionalRulesRoundtripAndLegacyMacrosRetainTheirModes()
    {
        var path = Path.GetTempFileName();
        try
        {
            var macro = new ScriptMacro(Guid.NewGuid().ToString(), "Connected measure", MacroMode.TrustedLegacy, "Model.Name = \"review\";") { Context = new(new[] { "Measure" }, 1, 3, true) };
            await RecipeFiles.SaveLibraryAsync(path, new(new[] { macro }), default); var restored = (await RecipeFiles.LoadLibraryAsync(path, default)).Macros[0];
            Assert.Equal(MacroMode.TrustedLegacy, restored.Mode); Assert.Equal(new[] { "Measure" }, restored.Context!.AllowedSelectionKinds); Assert.True(restored.Context.RequiresConnectedModel);
            File.WriteAllText(path, "{\"Version\":1,\"Macros\":[{\"Id\":\"" + macro.Id + "\",\"Name\":\"Old\",\"Mode\":0,\"Source\":\"\"}]}");
            var old = (await RecipeFiles.LoadLibraryAsync(path, default)).Macros[0]; Assert.Null(old.Context); Assert.Equal(MacroMode.SafeScript, old.Mode);
        }
        finally { File.Delete(path); }
    }
    [Fact] public void ContextRulesGiveReasonsForCountKindAndConnectionFailures()
    {
        var rule = new MacroContextRule(new[] { "Measure" }, 1, 2, true);
        foreach (var context in new[] { new MacroSelectionContext(false, false, Array.Empty<string>()), new(true, false, new[] { "Measure" }), new(true, true, Array.Empty<string>()), new(true, true, new[] { "Measure", "Column" }), new(true, true, new[] { "Measure", "Measure", "Measure" }) })
        { var availability = MacroContextRules.Evaluate(rule, context); Assert.False(availability.Enabled); Assert.NotEmpty(availability.Reason); }
        Assert.True(MacroContextRules.Evaluate(rule, new(true, true, new[] { "Measure" })).Enabled);
        Assert.True(MacroContextRules.Evaluate(null, new(true, false, Array.Empty<string>())).Enabled);
        foreach (var invalid in new[] { rule with { AllowedSelectionKinds = new[] { "RunCode()" } }, rule with { AllowedSelectionKinds = new[] { "Measure", "Measure" } }, rule with { MinSelectedCount = -1 }, rule with { MaxSelectedCount = 10001 }, rule with { MaxSelectedCount = 0 } }) Assert.Throws<InvalidDataException>(() => invalid.Validate());
    }
    [Fact] public async Task ExecutableEnableExpressionsAndUnknownModesAreRejected()
    {
        var path = Path.GetTempFileName(); var macro = new ScriptMacro(Guid.NewGuid().ToString(), "Invalid", MacroMode.SafeScript, "") { Context = new(new[] { "Measure" }) };
        try
        {
            var json = JsonSerializer.Serialize(new MacroLibrary(new[] { macro })).Replace("\"RequiresConnectedModel\":false", "\"RequiresConnectedModel\":false,\"EnableExpression\":\"Process.Start()\""); File.WriteAllText(path, json);
            await Assert.ThrowsAsync<JsonException>(() => RecipeFiles.LoadLibraryAsync(path, default));
            await Assert.ThrowsAsync<InvalidDataException>(() => RecipeFiles.SaveLibraryAsync(path, new(new[] { macro with { Mode = (MacroMode)99 } }), default));
        }
        finally { File.Delete(path); }
    }
    [Fact] public void SemanticGeneratorsCaptureNumericSelectionAndEscapeSourceNames()
    {
        var symbols = new[] { new AutomationSymbol("Column", "A\"mount]", "Sa'les\\", true, "Int64"), new AutomationSymbol("Column", "Label", "Sa'les\\", true, "String"), new AutomationSymbol("Column", "Ignored", "Other", false, "Decimal") };
        var sum = SemanticSnippets.Generate(SemanticSnippets.All.Single(s => s.Id == "sum"), symbols);
        Assert.True(sum.Enabled); Assert.False(sum.TrustedOnly); Assert.Contains("non-numeric columns skipped", sum.Reason); Assert.DoesNotContain("Ignored", sum.Source); Assert.DoesNotContain("Label", sum.Source);
        Assert.Contains("Sa''les", sum.Source); Assert.Contains("A\\\"mount]]", sum.Source);
        foreach (var snippet in SemanticSnippets.All) Assert.False(SemanticSnippets.Generate(snippet, Array.Empty<AutomationSymbol>()).Enabled);
        var format = SemanticSnippets.Generate(SemanticSnippets.All.Single(s => s.Id == "format-dax"), new[] { new AutomationSymbol("Measure", "Revenue", "Sales", true) }); Assert.True(format.TrustedOnly); Assert.Contains("FormatDax", format.Source);
        Assert.False(SemanticSnippets.Generate(SemanticSnippets.All.Single(s => s.Id == "folder"), symbols).Enabled);
    }
}
