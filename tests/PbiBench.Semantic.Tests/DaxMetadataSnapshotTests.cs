using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Dax.LanguageService;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class DaxMetadataSnapshotTests
{
    [TestMethod]
    public void CapturedMetadataIsImmutableAndNavigationResolvesLiveObjects()
    {
        using var handler = new TabularModelHandler(1600);
        var table = handler.Model.AddTable("Sales ledger");
        var column = table.AddDataColumn("Amount", "Amount", dataType: DataType.Decimal);
        column.IsHidden = true;
        var measure = table.AddMeasure("Revenue", "SUM('Sales ledger'[Amount])");
        measure.Description = "Gross revenue";
        var group = handler.Model.AddCalculationGroup("Time intelligence");
        var item = group.AddCalculationItem("Current", "SELECTEDMEASURE()");
        var snapshot = DaxMetadataSnapshotProvider.Capture(handler);
        var captured = snapshot.Symbols.Single(symbol => symbol.Kind == DaxSymbolKind.Measure);
        Assert.AreEqual("Sales ledger", captured.Table);
        Assert.AreEqual("Gross revenue", captured.Description);
        Assert.AreEqual(1600, snapshot.CompatibilityLevel);
        Assert.IsTrue(snapshot.Symbols.Single(symbol => symbol.Name == "Amount").IsHidden);
        Assert.AreSame(measure, DaxMetadataSnapshotProvider.Resolve(handler, captured));
        Assert.AreSame(item, DaxMetadataSnapshotProvider.Resolve(handler, snapshot.Symbols.Single(symbol => symbol.Kind == DaxSymbolKind.CalculationItem).Id));
        measure.Description = "Changed after snapshot";
        Assert.AreEqual("Gross revenue", captured.Description);
        measure.Name = "Net revenue";
        Assert.AreEqual("Revenue", captured.Name);
        Assert.IsNull(DaxMetadataSnapshotProvider.Resolve(handler, captured.Id));
        Assert.AreEqual("Net revenue", DaxMetadataSnapshotProvider.Capture(handler).Symbols.Single(symbol => symbol.Kind == DaxSymbolKind.Measure).Name);
    }
}
