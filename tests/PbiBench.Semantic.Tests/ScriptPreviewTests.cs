using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Core.Automation;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class ScriptPreviewTests
{
    private static TabularModelHandler Fixture()
    {
        var handler = new TabularModelHandler(1702); var sales = handler.Model.AddTable("Sales"); sales.AddDataColumn("Amount", "Amount", dataType: DataType.Decimal);
        sales.AddMeasure("Revenue", "SUM('Sales'[Amount])").FormatString = "#,0"; sales.AddMeasure("Double", "[Revenue] * 2"); handler.UndoManager.Clear(); handler.UndoManager.SetCheckpoint(); return handler;
    }
    [TestMethod]
    public void ClonePreviewDoesNotMutateLiveModelAndApplyIsOneUndoBatch()
    {
        using var handler = Fixture(); var service = new ScriptPreviewService(handler); var before = new SemanticModelService(handler).Fingerprint();
        var preview = service.PreviewScript("foreach(var m in Model.AllMeasures) { m.DisplayFolder = \"Finance\"; m.Description = \"Measure: \" + m.Name; }", Array.Empty<TabularNamedObject>());
        Assert.IsTrue(preview.CanApply); Assert.AreEqual(4, preview.Changes.Count); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        preview.Apply(handler); Assert.AreEqual("Finance", handler.Model.Tables["Sales"].Measures["Revenue"].DisplayFolder); Assert.AreEqual("Measure: Double", handler.Model.Tables["Sales"].Measures["Double"].Description);
        handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        // Creating another native object after the detached clone proves no second handler displaced TE2's global construction context.
        var after = handler.Model.Tables["Sales"].AddMeasure("Still native", "1"); Assert.AreSame(handler.Model, after.Model);
    }
    [TestMethod]
    public async Task PreparedInputComputesOnWorkerAndRejectsInterveningModelEdit()
    {
        using var handler = Fixture(); var service = new ScriptPreviewService(handler); var prepared = service.PrepareScript("Model.Tables[\"Sales\"].Measures[\"Revenue\"].Description = \"new\";", Array.Empty<TabularNamedObject>());
        var computed = await Task.Run(() => service.Compute(prepared)); Assert.AreEqual(1, computed.ChangeCount); handler.Model.Tables["Sales"].Description = "Intervening edit";
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Materialize(computed));
    }
    [TestMethod]
    public void ExplicitSelectionNeverFallsBackToEntireModel()
    {
        using var handler = Fixture(); var service = new ScriptPreviewService(handler);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.PreviewScript("foreach(var m in Selected.Measures) { m.IsHidden = true; }", Array.Empty<TabularNamedObject>()));
        var selected = handler.Model.Tables["Sales"].Measures["Revenue"]; var preview = service.PreviewScript("foreach(var m in Selected.Measures) { m.IsHidden = true; }", new[] { selected }); Assert.AreEqual(1, preview.Changes.Count); preview.Apply(handler); Assert.IsTrue(selected.IsHidden); Assert.IsFalse(handler.Model.Tables["Sales"].Measures["Double"].IsHidden);
    }
    [TestMethod]
    public void RenameShowsDaxCallersAndPreservesStringsCommentsAndUndo()
    {
        using var handler = Fixture(); var sales = handler.Model.Tables["Sales"]; sales.Measures["Double"].Expression = "[Revenue] * 2 + LEN(\"[Revenue]\") /* [Revenue] */"; handler.UndoManager.Clear(); var before = new SemanticModelService(handler).Fingerprint();
        var preview = new ScriptPreviewService(handler).PreviewScript("Model.Tables[\"Sales\"].Measures[\"Revenue\"].Name = \"Gross\";", Array.Empty<TabularNamedObject>());
        Assert.AreEqual(2, preview.Changes.Count); Assert.IsTrue(preview.Changes.Any(change => change.After.Contains("[Gross] * 2 + LEN(\"[Revenue]\") /* [Revenue] */")), string.Join("|", preview.Changes.Select(change => change.Property + "=" + change.After)));
        preview.Apply(handler); Assert.IsTrue(sales.Measures.Contains("Gross"), "Tables=" + string.Join("|", handler.Model.Tables.Select(table => table.Name)) + "; Old table=" + sales.Name + "; Measures=" + string.Join("|", sales.Measures.Select(measure => measure.Name)) + "; Changes=" + string.Join("|", preview.Changes.Select(change => change.ObjectPath + "/" + change.Property + "=" + change.After))); handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }
    [TestMethod]
    public void TableAndColumnRenamesIncludeCalculatedAndRoleExpressions()
    {
        using var handler = Fixture(); var initialSales = handler.Model.Tables["Sales"]; handler.Model.AddCalculatedTable("Copy", "'Sales'"); initialSales.AddCalculatedColumn("Computed", "'Sales'[Amount] * 2");
        var role = handler.Model.AddRole("Readers"); initialSales.RowLevelSecurity[role.Name] = "'Sales'[Amount] > 0";
        var preview = new ScriptPreviewService(handler).PreviewScript("Model.Tables[\"Sales\"].Name = \"Ledger\"; Model.Tables[\"Ledger\"].Columns[\"Amount\"].Name = \"Value\";", Array.Empty<TabularNamedObject>());
        Assert.IsTrue(preview.Changes.Any(change => change.After.Contains("'Ledger'[Value] > 0"))); preview.Apply(handler); Assert.AreEqual("SUM('Ledger'[Value])", handler.Model.Tables["Ledger"].Measures["Revenue"].Expression); Assert.AreEqual("'Ledger'", ((CalculatedTable)handler.Model.Tables["Copy"]).Expression);
    }
    [TestMethod]
    public void FormatAnnotationRemovalAndDynamicFormatClearingAreExplicit()
    {
        using var handler = Fixture(); var measure = handler.Model.Tables["Sales"].Measures["Revenue"]; measure.SetAnnotation("Format", "legacy annotation"); handler.UndoManager.Clear(); var before = new SemanticModelService(handler).Fingerprint();
        var preview = new ScriptPreviewService(handler).PreviewScript("Model.Tables[\"Sales\"].Measures[\"Revenue\"].FormatStringExpression = \"\\\"0.00\\\"\";", Array.Empty<TabularNamedObject>());
        Assert.IsTrue(preview.Changes.Any(change => change.Property == "FormatString" && change.After == "")); Assert.IsTrue(preview.Changes.Any(change => change.Property == "Annotation:Format" && change.Before.Contains("legacy annotation") && change.After == "null"));
        preview.Apply(handler); Assert.AreEqual("\"0.00\"", measure.FormatStringExpression); Assert.IsNull(measure.GetAnnotation("Format")); handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint(), "Format=" + measure.FormatString + "; annotation=" + measure.GetAnnotation("Format") + "; dynamic=" + measure.FormatStringExpression);
    }
    [TestMethod]
    public void CreateAndDeleteMeasureRecipesAreReviewableAndUndoable()
    {
        using var handler = Fixture(); var service = new ScriptPreviewService(handler); var before = new SemanticModelService(handler).Fingerprint();
        var create = service.PreviewScript("Model.Tables[\"Sales\"].AddMeasure(\"Units\", \"1\", \"Counts\");", Array.Empty<TabularNamedObject>()); Assert.AreEqual("New measure", create.Changes.Single().Property); create.Apply(handler); Assert.AreEqual("Counts", handler.Model.Tables["Sales"].Measures["Units"].DisplayFolder); handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        Assert.ThrowsExactly<InvalidOperationException>(() => service.PreviewScript("Model.Tables[\"Sales\"].Measures[\"Revenue\"].Delete();", Array.Empty<TabularNamedObject>()));
        var delete = service.PreviewScript("Model.Tables[\"Sales\"].Measures[\"Double\"].Delete();", Array.Empty<TabularNamedObject>()); delete.Apply(handler); Assert.IsFalse(handler.Model.Tables["Sales"].Measures.Contains("Double")); handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }
    [TestMethod]
    public void UnsafeUnsupportedInvalidTargetsAndStalePlansCannotApply()
    {
        using var handler = Fixture(); var service = new ScriptPreviewService(handler); var before = new SemanticModelService(handler).Fingerprint();
        foreach (var source in new[] { "System.IO.File.WriteAllText(\"x\",\"y\");", "Model.SaveChanges();", "Model.Tables[\"Sales\"].Delete();", "Model.Tables[\"Sales\"].Columns[\"Amount\"].Expression = \"1\";", "Model.Tables[\"Sales\"].Measures[\"Revenue\"].Name = \"Double\";" })
        { try { service.PreviewScript(source, Array.Empty<TabularNamedObject>()); Assert.Fail("Unsupported source was accepted: " + source); } catch (ArgumentException) { } catch (InvalidOperationException) { } }
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        var plan = service.PreviewScript("Model.Tables[\"Sales\"].IsHidden = true;", Array.Empty<TabularNamedObject>()); handler.Model.Tables["Sales"].Description = "change"; Assert.ThrowsExactly<InvalidOperationException>(() => plan.Apply(handler));
    }
    [TestMethod]
    public void RecorderUsesStableIdentityForRenamePropertyCreateDeleteAndReplaysAfterUndo()
    {
        using var handler = Fixture(); var recorder = new ActionRecorder(); var before = new SemanticModelService(handler).Fingerprint(); recorder.Start(handler);
        handler.BeginUpdate("Recorded actions"); var sales = handler.Model.Tables["Sales"]; sales.Measures["Revenue"].Name = "Gross"; sales.Measures["Gross"].DisplayFolder = "Finance"; sales.AddMeasure("New", "2"); sales.Measures["Double"].Delete(); handler.EndUpdate();
        var recording = recorder.Stop(handler, "Recorded finance recipe"); Assert.IsTrue(recording.Recipe.Steps.Any(step => step.Property == "Name")); Assert.IsTrue(recording.Recipe.Steps.Any(step => step.Operation == RecipeOperation.CreateMeasure)); Assert.IsTrue(recording.Recipe.Steps.Any(step => step.Operation == RecipeOperation.DeleteMeasure));
        handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint()); new ScriptPreviewService(handler).PreviewRecipe(recording.Recipe, Array.Empty<TabularNamedObject>()).Apply(handler); Assert.IsTrue(sales.Measures.Contains("Gross")); Assert.IsTrue(sales.Measures.Contains("New")); Assert.IsFalse(sales.Measures.Contains("Double"));
    }
    [TestMethod]
    public void RecorderReportsUnsupportedMetadataWithoutRecordingUiGestures()
    {
        using var handler = Fixture(); var recorder = new ActionRecorder(); recorder.Start(handler); handler.Model.Tables["Sales"].SetAnnotation("Custom", "new"); var recording = recorder.Stop(handler, "Annotations"); Assert.AreEqual(0, recording.Recipe.Steps.Count); Assert.IsTrue(recording.Notices.Any(notice => notice.Contains("Unsupported metadata")));
    }
}
