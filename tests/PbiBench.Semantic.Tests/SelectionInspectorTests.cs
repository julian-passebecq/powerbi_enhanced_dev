using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class SelectionInspectorTests
{
    [TestMethod]
    public void InspectorTracksLiveObjectMetadataDependenciesAndFindingStateWithoutMutation()
    {
        using var handler = new TabularModelHandler(1600);
        var sales = handler.Model.AddTable("Sales");
        var amount = sales.AddDataColumn("Amount", "AmountSource", dataType: DataType.Decimal);
        amount.SummarizeBy = AggregateFunction.Sum;
        var revenue = sales.AddMeasure("Revenue", "SUM(Sales[Amount])", "Revenue");
        revenue.FormatString = "#,0.00";
        revenue.Description = "Gross sales";
        sales.AddMeasure("Twice", "[Revenue] * 2");
        var product = handler.Model.AddTable("Product");
        var key = product.AddDataColumn("Key", "Key", dataType: DataType.Int64);
        key.IsKey = true;
        var relation = handler.Model.AddRelationship();
        relation.FromColumn = amount;
        relation.ToColumn = key;
        relation.IsActive = false;
        relation.FromCardinality = RelationshipEndCardinality.Many;
        relation.ToCardinality = RelationshipEndCardinality.One;
        var before = new SemanticModelService(handler).Fingerprint();
        var measure = SelectionInspector.Create(new[] { revenue }, obj => ReferenceEquals(obj, revenue) ? 2 : 0);
        Assert.AreEqual("Measure", measure.Kind);
        Assert.AreEqual("SUM(Sales[Amount])", measure.Expression);
        Assert.IsTrue(measure.DependencyCount >= 1);
        Assert.AreEqual(1, measure.ReferenceCount);
        Assert.AreEqual(2, measure.BpaFindingCount);
        Assert.IsTrue(measure.Dependencies.Any(p => p.Contains("Amount")));
        Assert.IsTrue(measure.Actions.Contains(InspectorAction.AnalyzeInDaxStudio));
        Assert.AreEqual("Gross sales", measure.Fields.Single(f => f.Label == "Description").Value);

        var column = SelectionInspector.Create(new[] { amount });
        Assert.IsNull(column.BpaFindingCount); // Unscanned never masquerades as zero findings.
        Assert.AreEqual("AmountSource", column.Fields.Single(f => f.Label == "Source column").Value);
        Assert.AreEqual("Sum", column.Fields.Single(f => f.Label == "Summarize by").Value);
        Assert.IsTrue(column.Actions.Contains(InspectorAction.PreviewSafeFixes));
        var table = SelectionInspector.Create(new[] { sales });
        Assert.AreEqual("2", table.Fields.Single(f => f.Label == "Measures").Value);
        var relationship = SelectionInspector.Create(new[] { relation });
        Assert.AreEqual("Inactive", relationship.Fields.Single(f => f.Label == "State").Value);
        Assert.AreEqual("Many → One", relationship.Fields.Single(f => f.Label == "Cardinality").Value);
        Assert.IsTrue(relationship.Fields.Any(f => f.Label == "Security filtering"));
        Assert.IsTrue(relationship.Actions.Contains(InspectorAction.GoToToTable));
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());

        amount.SummarizeBy = AggregateFunction.None;
        Assert.AreEqual("None", SelectionInspector.Create(new[] { amount }).Fields.Single(f => f.Label == "Summarize by").Value);
    }

    [TestMethod]
    public void EmptyAndMultipleSelectionsAndCalculationObjectsHaveHonestContext()
    {
        using var handler = new TabularModelHandler(1600);
        Assert.AreEqual("No selection", SelectionInspector.Create(Array.Empty<TabularNamedObject>()).Kind);
        var group = handler.Model.AddCalculationGroup("Time intelligence");
        group.CalculationGroupPrecedence = 5;
        var item = group.AddCalculationItem("Current", "SELECTEDMEASURE()");
        item.FormatStringExpression = "SELECTEDMEASUREFORMATSTRING()";
        var groupSnapshot = SelectionInspector.Create(new[] { group });
        Assert.AreEqual("Calculation group", groupSnapshot.Kind);
        Assert.AreEqual("5", groupSnapshot.Fields.Single(f => f.Label == "Precedence").Value);
        var calculation = SelectionInspector.Create(new[] { item });
        Assert.AreEqual("Calculation item", calculation.Kind);
        Assert.AreEqual("SELECTEDMEASURE()", calculation.Expression);
        Assert.AreEqual("SELECTEDMEASUREFORMATSTRING()", calculation.Fields.Single(f => f.Label == "Format expression").Value);
        var mixed = SelectionInspector.Create(new TabularNamedObject[] { group, item });
        Assert.AreEqual("2 objects selected", mixed.Title);
        Assert.AreEqual(2, mixed.Fields.Count);
    }
}
