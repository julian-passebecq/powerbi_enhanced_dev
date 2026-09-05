using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class SemanticImpactTests
{
    [TestMethod] public void GuardCancelsNativeRenameDeleteAndRefactorButPreservesUndo()
    {
        using var handler = new TabularModelHandler(1702); var table = handler.Model.AddTable("Sales"); var measure = table.AddMeasure("Revenue", "1");
        handler.UndoManager.Clear(); var calls = new List<SemanticRefactorRequest>(); var allow = false;
        using var guard = new SemanticRefactorGuard(handler, r => { calls.Add(r); return allow; });
        measure.Name = "Renamed"; measure.Delete(); measure.Expression = "2"; table.Name = "Other";
        Assert.AreEqual("Revenue", measure.Name); Assert.AreEqual("1", measure.Expression); Assert.AreEqual("Sales", table.Name); Assert.AreEqual(1, table.Measures.Count); Assert.AreEqual(4, calls.Count);
        allow = true; measure.Name = "Reviewed"; Assert.AreEqual("Reviewed", measure.Name); var count = calls.Count;
        handler.UndoManager.Undo(); Assert.AreEqual("Revenue", measure.Name); Assert.AreEqual(count, calls.Count);
    }
    [TestMethod] public void AnnotationPreviewAndUndoPreserveExactModel()
    {
        using var handler = new TabularModelHandler(1702); var measure = handler.Model.AddTable("T").AddMeasure("M", "1"); handler.UndoManager.Clear();
        var before = new SemanticModelService(handler).Fingerprint();
        var preview = new SemanticAnnotationService(handler).Preview(new[] { new SemanticAnnotationRequest(measure, "PbiBench.DisplayName", "Revenue") });
        Assert.IsTrue(preview.CanApply); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        preview.Apply(handler); Assert.AreEqual("Revenue", measure.GetAnnotation("PbiBench.DisplayName"));
        handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }
    [TestMethod] public void TypedDynamicFormatAppliesOnCompatibleModelAndOneUndoRestoresStaticFormat()
    {
        using var handler = new TabularModelHandler(1702); var measure = handler.Model.AddTable("T").AddMeasure("M", "1"); measure.FormatString = "0.00"; handler.UndoManager.Clear();
        var before = new SemanticModelService(handler).Fingerprint();
        var preview = new ScriptPreviewService(handler).PreviewScript("Model.Tables[\"T\"].Measures[\"M\"].FormatStringExpression = \"\\\"#,0\\\"\";", Array.Empty<TabularNamedObject>());
        Assert.IsTrue(preview.CanApply); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        preview.Apply(handler); Assert.AreEqual("\"#,0\"", measure.FormatStringExpression);
        handler.UndoManager.Undo(); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }
}
