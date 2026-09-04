using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Automation;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

[assembly: DoNotParallelize] // TE2's existing model construction uses a singleton internally.

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class SemanticAndAutomationTests
{
    private static TabularModelHandler Fixture()
    {
        var handler = new TabularModelHandler(1600);
        var product = handler.Model.AddTable("Product");
        var key = product.AddDataColumn("Product ID", "ProductID", dataType: DataType.Int64);
        key.IsKey = true;
        key.SummarizeBy = AggregateFunction.Sum;
        var sales = handler.Model.AddTable("Sales");
        sales.AddDataColumn("Amount", "Amount", dataType: DataType.Decimal).SummarizeBy = AggregateFunction.Sum;
        var foreignKey = sales.AddDataColumn("Product ID", "ProductID", dataType: DataType.Int64);
        var measure = sales.AddMeasure("Revenue", "SUM(Sales[Amount])");
        sales.AddMeasure("Double Revenue", "[Revenue]*2");
        var rel = handler.Model.AddRelationship();
        rel.FromColumn = foreignKey; rel.ToColumn = key;
        rel.FromCardinality = RelationshipEndCardinality.Many; rel.ToCardinality = RelationshipEndCardinality.One;
        handler.UndoManager.Clear();
        handler.UndoManager.SetCheckpoint();
        return handler;
    }

    [TestMethod]
    public void Te2Characterization_MeasuresDependenciesRelationshipsAndUndoRemainAvailable()
    {
        using var handler = Fixture();
        var model = handler.Model;
        var measure = model.Tables["Sales"].Measures["Revenue"];
        var dependent = model.Tables["Sales"].Measures["Double Revenue"];
        Assert.IsTrue(dependent.DependsOn.ContainsKey(measure));
        measure.Name = "Revenue Renamed";
        Assert.IsTrue(dependent.Expression.Contains("[Revenue Renamed]"));
        handler.UndoManager.Undo();
        Assert.AreEqual("Revenue", measure.Name);
        Assert.AreEqual("[Revenue]*2", dependent.Expression);
        var rel = model.Relationships.OfType<SingleColumnRelationship>().Single();
        Assert.AreEqual("Product", rel.ToColumn.Table.Name);
        rel.IsActive = false;
        handler.UndoManager.Undo();
        Assert.IsTrue(rel.IsActive);
        handler.UndoManager.Redo();
        Assert.IsFalse(rel.IsActive);
    }

    [TestMethod]
    public void Te2Characterization_CalculatedTableAndHiddenColumn()
    {
        using var handler = Fixture();
        var table = handler.Model.AddCalculatedTable("Measures", "{ BLANK () }");
        var column = table.AddCalculatedTableColumn("Value", "[Value]", dataType: DataType.Int64);
        column.IsHidden = true;
        Assert.AreEqual("Measures 1", table.Name); // TOM reserves the exact table name "Measures".
        Assert.AreEqual("{ BLANK () }", table.Expression);
        Assert.AreEqual("Value", column.Name);
        Assert.IsTrue(column.IsHidden);
    }

    [TestMethod]
    public void FiveRequiredActionsPreviewWithoutMutatingTheModel()
    {
        using var handler = Fixture();
        var service = new AutomationService(handler);
        var semantic = new SemanticModelService(handler);
        var before = semantic.Fingerprint();
        var measure = handler.Model.Tables["Sales"].Measures["Revenue"];
        var amount = handler.Model.Tables["Sales"].Columns["Amount"];
        var cases = new[] {
            service.Preview(AutomationActionId.FormatMeasures, new[] { measure }),
            service.Preview(AutomationActionId.CreateSumMeasures, new[] { amount }),
            service.Preview(AutomationActionId.CreateMeasureTable, Array.Empty<TabularNamedObject>()),
            service.Preview(AutomationActionId.SetSummarizeByNone, new[] { amount }),
            service.Preview(AutomationActionId.OrganizeMeasures, new[] { measure }) };
        Assert.IsTrue(cases.All(p => p.Changes.Count > 0 && p.Changes.All(c => c.ObjectPath.Length > 0 && c.After.Length > 0)));
        Assert.AreEqual(before, semantic.Fingerprint());
        Assert.IsFalse(handler.UndoManager.CanUndo);
    }

    [TestMethod]
    public void AllSevenActionsApplyAndUndoRestoresWholeModel()
    {
        using var handler = Fixture();
        var service = new AutomationService(handler);
        var semantic = new SemanticModelService(handler);
        foreach (var action in AutomationService.Actions)
        {
            var before = semantic.Fingerprint();
            var selected = action.Id == AutomationActionId.CreateSumMeasures || action.Id == AutomationActionId.SetSummarizeByNone
                ? new TabularNamedObject[] { handler.Model.Tables["Sales"].Columns["Amount"] }
                : new TabularNamedObject[] { handler.Model.Tables["Sales"].Measures["Revenue"] };
            var preview = service.Preview(action.Id, selected);
            service.Apply(preview);
            Assert.AreNotEqual(before, semantic.Fingerprint(), action.Name);
            Assert.AreEqual(1, handler.UndoManager.UndoSteps, action.Name);
            service.Undo();
            Assert.AreEqual(before, semantic.Fingerprint(), action.Name);
        }
    }

    [TestMethod]
    public void StaleAndReplayedPreviewsCannotApply()
    {
        using var handler = Fixture();
        var service = new AutomationService(handler);
        var measure = handler.Model.Tables["Sales"].Measures["Revenue"];
        var stale = service.Preview(AutomationActionId.OrganizeMeasures, new[] { measure });
        handler.Model.Tables["Product"].Description = "Concurrent edit";
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Apply(stale));
        Assert.AreEqual("", measure.DisplayFolder ?? "");
        var fresh = service.Preview(AutomationActionId.OrganizeMeasures, new[] { measure });
        service.Apply(fresh);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Apply(fresh));
    }

    [TestMethod]
    public void MidBatchSetterFailureRollsBackEarlierEdits()
    {
        using var handler = Fixture();
        var service = new AutomationService(handler);
        var first = handler.Model.Tables["Sales"].Measures["Revenue"];
        var second = handler.Model.Tables["Sales"].Measures["Double Revenue"];
        var before = new SemanticModelService(handler).Fingerprint();
        var preview = service.Preview(AutomationActionId.OrganizeMeasures, new[] { first, second });
        second.PropertyChanging += (_, args) => { if (args.PropertyName == "DisplayFolder") throw new InvalidOperationException("Simulated editor failure"); };
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Apply(preview));
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        Assert.AreEqual(0, handler.UndoManager.BatchDepth);
        Assert.IsFalse(handler.UndoManager.CanUndo);
    }

    [TestMethod]
    public void CollisionHandlingUsesUniqueNamesAndEscapedDaxIdentifiers()
    {
        using var handler = Fixture();
        var sales = handler.Model.Tables["Sales"];
        sales.Name = "O'Brien Sales";
        sales.Columns["Amount"].Name = "Amount]USD";
        sales.AddMeasure("Total Amount]USD", "0");
        var service = new AutomationService(handler);
        var preview = service.Preview(AutomationActionId.CreateSumMeasures, new[] { sales.Columns["Amount]USD"] });
        StringAssert.Contains(preview.Changes.Single().ObjectPath, "Amount]]USD 2]");
        Assert.AreEqual("SUM ( 'O''Brien Sales'[Amount]]USD] )", preview.Changes.Single().After);
        service.Apply(preview);
        Assert.AreEqual(4, sales.Measures.Count);
    }

    [TestMethod]
    public void BpaFindingsHaveNavigableObjectsReasonsAndExactSafeFixes()
    {
        using var handler = Fixture();
        var automation = new AutomationService(handler);
        var before = new SemanticModelService(handler).Fingerprint();
        var findings = new BpaService(handler, automation).Scan();
        Assert.IsTrue(findings.Any(f => f.RuleId == "PBIBENCH004"));
        Assert.IsTrue(findings.All(f => f.Object != null && f.Source.Length > 0 && f.Reason.Length > 0));
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        var fix = findings.First(f => f.FixPreview != null);
        Assert.AreEqual(fix.After, fix.FixPreview!.Changes.Single().After);
        automation.Apply(fix.FixPreview);
        automation.Undo();
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }

    [TestMethod]
    public void GraphIncludesRelationshipsCardinalityDirectionAndLiveTableSelection()
    {
        using var handler = Fixture();
        var graph = new SemanticModelService(handler).GetGraph();
        Assert.AreEqual(2, graph.Tables.Count);
        Assert.AreSame(handler.Model.Tables["Sales"], graph.Tables.Single(t => t.Name == "Sales").Object);
        Assert.AreEqual("Fact", graph.Tables.Single(t => t.Name == "Sales").Role);
        Assert.AreEqual("Dimension", graph.Tables.Single(t => t.Name == "Product").Role);
        Assert.AreEqual("Many", graph.Relationships.Single().FromCardinality);
        Assert.AreEqual("One", graph.Relationships.Single().ToCardinality);
        Assert.IsTrue(graph.Relationships.Single().IsActive);
    }

    [TestMethod]
    public void FormatterPreservesStringsQuotedNamesEscapesAndComments()
    {
        var formatter = new LocalDaxFormatter();
        var result = formatter.Format("VAR x=\"a,  b\" // keep this comment\n RETURN IF(x=\"a,  b\",SUM('O''Brien'[A]]B]),1.25e-3)");
        StringAssert.Contains(result, "\"a,  b\"");
        StringAssert.Contains(result, "'O''Brien'[A]]B]");
        StringAssert.Contains(result, "// keep this comment\r\n");
        StringAssert.Contains(result, "1.25e-3");
        Assert.AreEqual(result, formatter.Format(result));
        Assert.ThrowsExactly<FormatException>(() => formatter.Format("SUM('Unclosed[Amount])"));
    }

    [TestMethod]
    public void FormatterPreservesDateTimeLiteralsNamespacedFunctionsAndUdfOperators()
    {
        var formatter = new LocalDaxFormatter();
        var formatted = formatter.Format("VAR d=dt\"2026-09-04T12:00:00\" RETURN IF(d>dt\"2020-01-01\",COUNTROWS(INFO.VIEW.TABLES()),0)");
        StringAssert.Contains(formatted, "dt\"2026-09-04T12:00:00\"");
        StringAssert.Contains(formatted, "INFO.VIEW.TABLES");
        StringAssert.Contains(formatter.Format("(x: scalar)=>x+1"), "=>");
    }

    [TestMethod]
    public void ExistingMeasureTableIsFocusedWithoutMutation()
    {
        using var handler = Fixture();
        var table = handler.Model.AddCalculatedTable("_Measures", "{ BLANK () }");
        var service = new AutomationService(handler);
        var before = new SemanticModelService(handler).Fingerprint();
        var preview = service.Preview(AutomationActionId.CreateMeasureTable, Array.Empty<TabularNamedObject>());
        Assert.AreSame(table, preview.FocusObject);
        Assert.IsFalse(preview.CanApply);
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
    }

    [TestMethod]
    public void UnsupportedSumSelectionCannotProducePartialChanges()
    {
        using var handler = Fixture();
        var sales = handler.Model.Tables["Sales"];
        var textColumn = sales.AddDataColumn("Comment", "Comment");
        var service = new AutomationService(handler);
        var before = new SemanticModelService(handler).Fingerprint();
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Preview(AutomationActionId.CreateSumMeasures, new[] { sales.Columns["Amount"], textColumn }));
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Preview(AutomationActionId.CreateSumMeasures, Array.Empty<TabularNamedObject>()));
    }

    [TestMethod]
    public void EmptySelectionFormatsAllOnlyWhenOptionAllowsIt()
    {
        using var handler = Fixture();
        var service = new AutomationService(handler);
        var all = service.Preview(AutomationActionId.FormatMeasures, Array.Empty<TabularNamedObject>());
        Assert.AreEqual(2, all.Changes.Count);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Preview(AutomationActionId.FormatMeasures, Array.Empty<TabularNamedObject>(), new AutomationOptions { AllMeasuresWhenSelectionEmpty = false }));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Preview(AutomationActionId.OrganizeMeasures, Array.Empty<TabularNamedObject>()));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Preview(AutomationActionId.SetSummarizeByNone, Array.Empty<TabularNamedObject>()));
    }

    [TestMethod]
    public void NewSessionAndUndoInvalidateExistingPreview()
    {
        using var handler = Fixture();
        var service = new AutomationService(handler);
        var measure = handler.Model.Tables["Sales"].Measures["Revenue"];
        measure.Description = "Before undo";
        var preview = service.Preview(AutomationActionId.OrganizeMeasures, new[] { measure });
        service.Undo();
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Apply(preview));
        var fresh = service.Preview(AutomationActionId.OrganizeMeasures, new[] { measure });
        Assert.ThrowsExactly<InvalidOperationException>(() => new AutomationService(handler).Apply(fresh));
        service.Apply(fresh);
        service.Undo();
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Apply(fresh));
    }

    [TestMethod]
    public void ReservedTableNameAndColumnMeasureCollisionMatchPreviewExactly()
    {
        using var handler = Fixture();
        var service = new AutomationService(handler);
        var tablePlan = service.Preview(AutomationActionId.CreateMeasureTable, Array.Empty<TabularNamedObject>(), new AutomationOptions { MeasureTableName = "Measures" });
        Assert.AreEqual("'Measures 1'", tablePlan.Changes.Single().ObjectPath);
        service.Apply(tablePlan);
        Assert.IsTrue(handler.Model.Tables.Any(t => t.Name == "Measures 1"));
        service.Undo();
        var column = handler.Model.Tables["Sales"].Columns["Amount"];
        var sumPlan = service.Preview(AutomationActionId.CreateSumMeasures, new[] { column }, new AutomationOptions { MeasurePrefix = "" });
        Assert.AreEqual("'Sales'[Amount 2]", sumPlan.Changes.Single().ObjectPath);
        service.Apply(sumPlan);
        Assert.IsTrue(handler.Model.Tables["Sales"].Measures.Any(m => m.Name == "Amount 2"));
    }
}

