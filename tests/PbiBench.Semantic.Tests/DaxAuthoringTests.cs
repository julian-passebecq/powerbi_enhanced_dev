using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Dax.LanguageService;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class DaxAuthoringTests
{
    private static TabularModelHandler Fixture(int compatibility = 1702)
    {
        var handler = new TabularModelHandler(compatibility); var table = handler.Model.AddTable("Sales"); table.AddDataColumn("Amount", "Amount", dataType: DataType.Decimal); table.AddMeasure("Revenue", "SUM('Sales'[Amount])").FormatString = "#,0";
        table.AddCalculatedColumn("Tax", "'Sales'[Amount] * 0.2"); handler.UndoManager.Clear(); handler.UndoManager.SetCheckpoint(); return handler;
    }
    [TestMethod]
    public void ExportIsExactNoOpAndPartialApplyIsOneUndoBatch()
    {
        using var handler = Fixture(); var service = new DaxAuthoringService(handler); var fingerprint = new SemanticModelService(handler).Fingerprint();
        Assert.IsFalse(service.PreviewScript(service.ExportScript()).CanApply);
        var parsed = DaxModelScript.Parse(service.ExportScript()); var entries = parsed.Entries.Select(entry => entry with { Expression = "2" }).ToArray(); var chosen = entries.Single(entry => entry.Kind == DaxScriptObjectKind.Measure);
        var plan = service.PreviewScript(DaxModelScript.Serialize(entries), new[] { chosen.Key }); Assert.IsTrue(plan.CanApply); Assert.AreEqual(1, plan.Changes.Count); Assert.AreEqual(fingerprint, new SemanticModelService(handler).Fingerprint());
        plan.Apply(handler); Assert.AreEqual("2", handler.Model.Tables["Sales"].Measures["Revenue"].Expression); Assert.AreEqual("'Sales'[Amount] * 0.2", ((CalculatedColumn)handler.Model.Tables["Sales"].Columns["Tax"]).Expression);
        handler.UndoManager.Undo(); Assert.AreEqual(fingerprint, new SemanticModelService(handler).Fingerprint()); Assert.ThrowsExactly<InvalidOperationException>(() => plan.Apply(handler));
    }
    [TestMethod]
    public void StaleScriptPreviewAndMalformedSourceNeverMutate()
    {
        using var handler = Fixture(); var service = new DaxAuthoringService(handler); var plan = service.PreviewScript("MEASURE 'Sales'[Revenue] = 2;"); handler.Model.Tables["Sales"].Description = "Intervening edit";
        Assert.ThrowsExactly<InvalidOperationException>(() => plan.Apply(handler)); Assert.AreEqual("SUM('Sales'[Amount])", handler.Model.Tables["Sales"].Measures["Revenue"].Expression);
        Assert.IsFalse(service.PreviewScript("MEASURE 'Sales'[Revenue] = SUM((1);").CanApply);
    }
    [TestMethod]
    public void ScriptCreatesAllSupportedScopesAndUndoRestoresEntireModel()
    {
        using var handler = Fixture(); handler.Model.AddCalculationGroup("Time"); handler.UndoManager.Clear(); var service = new DaxAuthoringService(handler); var before = new SemanticModelService(handler).Fingerprint();
        var script = "FUNCTION Finance.Tax = (x : NUMERIC) => x * 0.2;\nTABLE 'Generated' = {1,2};\nMEASURE 'Sales'[Net] = [Revenue] - Finance.Tax([Revenue]);\nCOLUMN 'Sales'[Double] = 'Sales'[Amount] * 2;\nCALCULATIONITEM 'Time'[Current] = SELECTEDMEASURE();";
        var plan = service.PreviewScript(script); Assert.IsTrue(plan.CanApply, string.Join(";", plan.Issues.Select(i => i.Message))); plan.Apply(handler);
        Assert.AreEqual(1, handler.Model.Functions.Count); Assert.AreEqual(1, ((CalculationGroupTable)handler.Model.Tables["Time"]).CalculationItems.Count); Assert.IsTrue(handler.Model.Tables.Contains("Generated")); Assert.IsTrue(handler.Model.Tables["Sales"].Measures.Contains("Net"));
        handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }
    [TestMethod]
    public void OmittedNewDependencyBlocksPartialApply()
    {
        using var handler = Fixture(); var service = new DaxAuthoringService(handler); var text = "FUNCTION Finance.Tax = (x : NUMERIC) => x; MEASURE 'Sales'[Net] = Finance.Tax([Revenue]);";
        var measure = DaxModelScript.Parse(text).Entries.Single(entry => entry.Kind == DaxScriptObjectKind.Measure); var preview = service.PreviewScript(text, new[] { measure.Key });
        Assert.IsFalse(preview.CanApply); Assert.IsTrue(preview.Issues.Any(i => i.Code == "DAXSCRIPT_DEPENDENCY"));
    }
    [TestMethod]
    public void FunctionCreationRequires1702WithoutUpgrading()
    {
        using var handler = Fixture(1600); var preview = new DaxAuthoringService(handler).PreviewFunction(new DaxFunctionEdit(null, "Finance.Tax", "(x : NUMERIC) => x"));
        Assert.IsFalse(preview.CanApply); Assert.IsTrue(preview.Issues.Any(i => i.Code == "UDF_COMPATIBILITY")); Assert.AreEqual(1600, handler.CompatibilityLevel); Assert.AreEqual(0, handler.Model.Functions.Count);
    }
    [TestMethod]
    public void FunctionEditAndNamespacedRenamePreviewEveryResolvedCallerAndUndo()
    {
        using var handler = Fixture(); var service = new DaxAuthoringService(handler); var create = service.PreviewFunction(new DaxFunctionEdit(null, "Finance.Tax", "(x : NUMERIC) => x * 0.2", "Tax rate", true)); Assert.IsTrue(create.CanApply); create.Apply(handler);
        var sales = handler.Model.Tables["Sales"]; sales.AddMeasure("AfterTax", "Finance.Tax([Revenue]) + LEN(\"Finance.Tax(\") /* Finance.Tax( */"); var unrelated = handler.Model.AddFunction("Other.Tax"); unrelated.Expression = "(x : NUMERIC) => x"; sales.AddMeasure("Other", "Other.Tax([Revenue])");
        handler.UndoManager.Clear(); var before = new SemanticModelService(handler).Fingerprint(); var id = service.GetFunctions().Single(f => f.Name == "Finance.Tax").Id; var preview = service.PreviewFunctionRename(id, "Finance.Levy");
        Assert.IsTrue(preview.CanApply); Assert.AreEqual(2, preview.Changes.Count); Assert.IsTrue(preview.Changes.Any(change => change.After == "Finance.Levy([Revenue]) + LEN(\"Finance.Tax(\") /* Finance.Tax( */")); var autoFixup = handler.Settings.AutoFixup;
        preview.Apply(handler); Assert.AreEqual(autoFixup, handler.Settings.AutoFixup); Assert.AreEqual("Other.Tax([Revenue])", sales.Measures["Other"].Expression); Assert.AreEqual("Tax rate", handler.Model.Functions["Finance.Levy"].Description); Assert.IsTrue(handler.Model.Functions["Finance.Levy"].IsHidden);
        handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }
    [TestMethod]
    public void FunctionNameAndSignatureValidationRejectMalformedOrReservedNames()
    {
        using var handler = Fixture(); var service = new DaxAuthoringService(handler);
        foreach (var name in new[] { "SUM", "Bad..Name", "Name.", "1Wrong", "VAR" }) Assert.IsFalse(service.PreviewFunction(new DaxFunctionEdit(null, name, "(x : NUMERIC) => x")).CanApply);
        Assert.IsFalse(service.PreviewFunction(new DaxFunctionEdit(null, "Valid", "(x, x) => x")).CanApply);
    }
    [TestMethod]
    public void DynamicFormatPreviewIncludesImplicitStaticFormatClear()
    {
        using var handler = Fixture(); var service = new DaxAuthoringService(handler); var measure = handler.Model.Tables["Sales"].Measures["Revenue"]; var before = new SemanticModelService(handler).Fingerprint();
        var preview = service.PreviewScript("FORMATSTRINGEXPRESSION MEASURE 'Sales'[Revenue] = \"0.00\";"); Assert.IsTrue(preview.CanApply); Assert.AreEqual(2, preview.Changes.Count); Assert.IsTrue(preview.Changes.Any(c => c.Property == "FormatString" && c.Before == "#,0" && c.After == ""));
        preview.Apply(handler); Assert.IsTrue(string.IsNullOrEmpty(measure.FormatString)); Assert.AreEqual(" \"0.00\"", measure.FormatStringExpression); handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }
    [TestMethod]
    public void SearchSupportsRegexCaseWholeWordDescriptionsAndLiteralDollarReplacement()
    {
        using var handler = Fixture(); var table = handler.Model.Tables["Sales"]; var measure = table.Measures["Revenue"]; measure.Description = "Revenue revenue revenues"; var service = new DaxAuthoringService(handler);
        Assert.AreEqual(1, service.Search(new DaxTextSearch("Revenue", MatchCase: true, WholeWord: true, IncludeDescriptions: true)).Count);
        var search = new DaxTextSearch("revenue", "$1", WholeWord: true, IncludeDescriptions: true); var preview = service.PreviewReplace(search); Assert.IsTrue(preview.CanApply); preview.Apply(handler); Assert.AreEqual("$1 $1 revenues", measure.Description); handler.UndoManager.Undo();
        var regexPreview = service.PreviewReplace(new DaxTextSearch("(Revenue)", "$1 checked", UseRegex: true, MatchCase: true, WholeWord: true, IncludeDescriptions: true)); regexPreview.Apply(handler); Assert.AreEqual("Revenue checked revenue revenues", measure.Description);
    }
    [TestMethod]
    public void InvalidReplacementCannotBreakStructureAndSelectionLimitsScope()
    {
        using var handler = Fixture(); var service = new DaxAuthoringService(handler); var preview = service.PreviewReplace(new DaxTextSearch("SUM", "SUM(")); Assert.IsFalse(preview.CanApply);
        var id = service.GetObjects().Single(item => item.Kind == DaxScriptObjectKind.Column).Id; var selected = service.PreviewReplace(new DaxTextSearch("Amount", "Revenue"), new[] { id }); Assert.AreEqual(1, selected.Changes.Count);
    }
    [TestMethod]
    public void ExplainFindsVariablesDependenciesAndCallers()
    {
        using var handler = Fixture(); handler.Model.Tables["Sales"].AddMeasure("Double", "[Revenue] * 2"); var service = new DaxAuthoringService(handler); var item = service.GetObjects().Single(o => o.Name == "Revenue"); var result = service.Explain(item.Id, "VAR total = SUM('Sales'[Amount]) RETURN total");
        Assert.IsTrue(result.Variables.Contains("total")); Assert.IsTrue(result.Dependencies.Contains("Amount")); Assert.IsTrue(result.Callers.Any(c => c.Contains("Double")));
        var nested = service.Explain(service.GetObjects().Single(o => o.Name == "Double").Id); Assert.IsTrue(nested.DependencyTree.Single(node => node.Name == "Revenue").Children.Any(node => node.Name == "Amount"));
    }
    [TestMethod]
    public void FailedNativeCreationRollsBackEarlierEdits()
    {
        using var handler = Fixture(); var service = new DaxAuthoringService(handler); var before = new SemanticModelService(handler).Fingerprint();
        // TOM reserves Measures and transparently chooses another name; the postcondition must reject that unreviewed result.
        var preview = service.PreviewScript("MEASURE 'Sales'[Revenue] = 8; TABLE 'Measures' = {1};"); Assert.IsTrue(preview.CanApply); Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(handler)); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }
}
