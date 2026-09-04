using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Core.DataExploration;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class DataModelSchemaTests
{
    [TestMethod]
    public void SchemaCapturePreservesStorageMetadataAndKeyCandidatesWithoutMutatingTheModel()
    {
        using var handler = new TabularModelHandler(1600);
        var dimension = handler.Model.AddTable("Product");
        var key = dimension.AddDataColumn("Product ID", "ID", dataType: DataType.Int64); key.IsKey = true;
        dimension.AddMPartition("Import partition", "#table({\"ID\"}, {{1}})").Mode = ModeType.Import;
        var fact = handler.Model.AddTable("Sales");
        var foreignKey = fact.AddDataColumn("Product ID", "ID", dataType: DataType.Int64);
        fact.AddMPartition("Source partition", "#table({\"ID\"}, {{1}})").Mode = ModeType.DirectQuery;
        foreach (var partition in fact.Partitions) partition.Mode = ModeType.DirectQuery;
        fact.AddMeasure("Rows", "COUNTROWS(Sales)");
        var relationship = handler.Model.AddRelationship(); relationship.FromColumn = foreignKey; relationship.ToColumn = key;
        relationship.FromCardinality = RelationshipEndCardinality.Many; relationship.ToCardinality = RelationshipEndCardinality.One;
        var before = new SemanticModelService(handler).Fingerprint();
        var captured = DataModelSchemaProvider.Capture(handler);
        Assert.AreEqual(DataStorageMode.Import, captured.GetTable("Product").StorageMode);
        Assert.AreEqual(DataStorageMode.DirectQuery, captured.GetTable("Sales").StorageMode);
        CollectionAssert.AreEqual(new[] { "Product ID" }, captured.GetTable("Product").CandidateKeyColumns.ToArray());
        Assert.AreEqual(0, captured.GetTable("Sales").CandidateKeyColumns.Count);
        Assert.AreEqual("One", captured.Relationships.Single().ToCardinality);
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        fact.AddMPartition("Import tail", "#table({\"ID\"}, {{2}})").Mode = ModeType.Import;
        Assert.AreEqual(DataStorageMode.Mixed, DataModelSchemaProvider.Capture(handler).GetTable("Sales").StorageMode);
        Assert.AreEqual(DataStorageMode.DirectQuery, captured.GetTable("Sales").StorageMode);
        dimension.Name = "Products renamed"; key.Name = "Key renamed";
        Assert.AreEqual("Product", captured.Tables[0].Name);
        Assert.AreEqual("Product ID", captured.GetTable("Product").Columns[0].Name);
        Assert.AreEqual("Product", captured.Relationships.Single().ToTable);
    }
}
