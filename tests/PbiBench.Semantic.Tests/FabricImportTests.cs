using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Core.Fabric;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class FabricImportTests
{
    [TestMethod]
    [DataRow(FabricStorageMode.DirectLakeOneLake)]
    [DataRow(FabricStorageMode.DirectLakeSql)]
    [DataRow(FabricStorageMode.Import)]
    [DataRow(FabricStorageMode.DirectQuery)]
    public void ImportPreviewCreatesExactMappingsAndOneUndoRestoresFullModel(FabricStorageMode mode)
    {
        using var handler = new TabularModelHandler(1702); var before = Fingerprint(handler); var schema = Schema(mode == FabricStorageMode.DirectLakeOneLake);
        var service = new FabricImportService(handler); var preview = service.PreviewImport(new(schema, new[] { "Id", "Amount" }, mode));
        Assert.IsTrue(preview.CanApply, Issues(preview)); Assert.AreEqual(before, Fingerprint(handler));
        preview.Apply(handler); var table = handler.Model.Tables["Orders"];
        Assert.AreEqual(2, table.Columns.Count); Assert.AreEqual(1, table.Partitions.Count);
        Assert.AreEqual("Id", ((DataColumn)table.Columns["Id"]).SourceColumn); Assert.AreEqual(AggregateFunction.None, table.Columns["Id"].SummarizeBy);
        if (mode is FabricStorageMode.DirectLakeOneLake or FabricStorageMode.DirectLakeSql)
        {
            var partition = (EntityPartition)table.Partitions[0]; Assert.AreEqual("Orders", partition.EntityName); Assert.AreEqual("dbo", partition.SchemaName);
            Assert.IsFalse(string.IsNullOrWhiteSpace(partition.ExpressionSource.Expression)); Assert.AreEqual(ModeType.DirectLake, partition.Mode);
        }
        else Assert.AreEqual(mode == FabricStorageMode.Import ? ModeType.Import : ModeType.DirectQuery, table.Partitions[0].Mode);
        Assert.AreEqual(0, handler.Model.DataSources.Count, "No unrelated provider source should be synthesized.");
        handler.UndoManager.Undo(); Assert.AreEqual(before, Fingerprint(handler));
        handler.UndoManager.Redo(); Assert.AreEqual(2, handler.Model.Tables["Orders"].Columns.Count);
    }
    [TestMethod]
    public void ImportToOneLakeShowsLostTransformationsAndUndoRestoresPartitions()
    {
        using var handler = new TabularModelHandler(1702); var service = new FabricImportService(handler);
        service.PreviewImport(new(Schema(false), new[] { "Id", "Amount" }, FabricStorageMode.Import)).Apply(handler);
        var table = handler.Model.Tables["Orders"]; ((MPartition)table.Partitions[0]).Expression = "let Source = #table({\"Id\",\"Amount\"},{{1,2}}) in Table.FirstN(Source,1)";
        var before = Fingerprint(handler); var plan = service.PreviewConversion("Orders", Schema(true), FabricStorageMode.DirectLakeOneLake);
        Assert.IsTrue(plan.CanApply, Issues(plan)); Assert.IsTrue(plan.Changes.Any(change => change.Before.Contains("Table.FirstN")));
        Assert.IsTrue(plan.Issues.Any(issue => issue.Code == "FABRIC_TRANSFORMATION_LOSS"));
        plan.Apply(handler); Assert.IsInstanceOfType<EntityPartition>(table.Partitions[0]);
        handler.UndoManager.Undo(); Assert.AreEqual(before, Fingerprint(handler));
    }
    [TestMethod]
    public void MultiPartitionConversionShowsEveryRemovalAndUndoRestoresFilters()
    {
        using var handler = new TabularModelHandler(1702); var service = new FabricImportService(handler);
        service.PreviewImport(new(Schema(false), new[] { "Id", "Amount" }, FabricStorageMode.Import)).Apply(handler);
        var table = handler.Model.Tables["Orders"]; var first = (MPartition)table.Partitions[0]; first.Name = "Earlier";
        first.Expression += " // earlier partition filter";
        table.AddMPartition("Recent", "let Source = #table({\"Id\",\"Amount\"},{{1,2}}) in Source");
        var before = Fingerprint(handler); var plan = service.PreviewConversion(table.Name, Schema(true), FabricStorageMode.DirectLakeOneLake);
        Assert.IsTrue(plan.CanApply, Issues(plan)); Assert.AreEqual(2, plan.Changes.Count(change => change.Property == "Remove partition"));
        plan.Apply(handler); Assert.AreEqual(1, table.Partitions.Count); Assert.IsInstanceOfType<EntityPartition>(table.Partitions[0]);
        handler.UndoManager.Undo(); Assert.AreEqual(before, Fingerprint(handler));
        Assert.AreEqual("Earlier", table.Partitions[0].Name); Assert.AreEqual("Recent", table.Partitions[1].Name);
    }
    [TestMethod]
    public void OneLakeToImportUsesExplicitSqlMappingAndRetainsSharedSource()
    {
        using var handler = new TabularModelHandler(1702); var service = new FabricImportService(handler);
        service.PreviewImport(new(Schema(true), new[] { "Id", "Amount" }, FabricStorageMode.DirectLakeOneLake)).Apply(handler);
        var before = Fingerprint(handler); var plan = service.PreviewConversion("Orders", Schema(false), FabricStorageMode.Import);
        Assert.IsTrue(plan.CanApply, Issues(plan)); plan.Apply(handler); Assert.IsInstanceOfType<MPartition>(handler.Model.Tables["Orders"].Partitions[0]);
        Assert.AreEqual(1, handler.Model.Expressions.Count); handler.UndoManager.Undo(); Assert.AreEqual(before, Fingerprint(handler));
    }
    [TestMethod]
    public void ConversionRejectsTransformationMappingsAndRefreshPolicies()
    {
        using var handler = new TabularModelHandler(1702); var service = new FabricImportService(handler);
        service.PreviewImport(new(Schema(false), new[] { "Id", "Amount" }, FabricStorageMode.Import)).Apply(handler);
        var table = handler.Model.Tables["Orders"]; ((DataColumn)table.Columns["Id"]).SourceColumn = "TransformedId";
        var plan = service.PreviewConversion("Orders", Schema(true), FabricStorageMode.DirectLakeOneLake);
        Assert.IsFalse(plan.CanApply); Assert.IsTrue(plan.Issues.Any(issue => issue.Code == "FABRIC_MAPPING"));
        ((DataColumn)table.Columns["Id"]).SourceColumn = "Id"; table.EnableRefreshPolicy = true;
        Assert.IsTrue(service.PreviewConversion("Orders", Schema(true), FabricStorageMode.DirectLakeOneLake).Issues.Any(issue => issue.Code == "FABRIC_REFRESH_POLICY"));
    }
    [TestMethod]
    public void SqlDirectLakeRejectsMixedStorageButOneLakeAllowsImport()
    {
        using var handler = new TabularModelHandler(1702); var service = new FabricImportService(handler);
        service.PreviewImport(new(Schema(false), new[] { "Id" }, FabricStorageMode.Import, "Local")).Apply(handler);
        Assert.IsFalse(service.PreviewImport(new(Schema(false), new[] { "Id" }, FabricStorageMode.DirectLakeSql)).CanApply);
        Assert.IsTrue(service.PreviewImport(new(Schema(true), new[] { "Id" }, FabricStorageMode.DirectLakeOneLake)).CanApply);
    }
    [TestMethod]
    public void ExistingSqlDirectLakeRejectsImportAndDifferentSqlSource()
    {
        using var handler = new TabularModelHandler(1702); var service = new FabricImportService(handler);
        service.PreviewImport(new(Schema(false), new[] { "Id" }, FabricStorageMode.DirectLakeSql)).Apply(handler);
        Assert.IsFalse(service.PreviewImport(new(Schema(false), new[] { "Id" }, FabricStorageMode.Import, "Other")).CanApply);
        var next = Schema(false); next = Rehash(next with { Source = next.Source with { SqlEndpoint = new("another.datawarehouse.fabric.microsoft.com", "22222222-2222-2222-2222-222222222222") } });
        Assert.IsTrue(service.PreviewImport(new(next, new[] { "Id" }, FabricStorageMode.DirectLakeSql, "Other")).Issues.Any(issue => issue.Code == "FABRIC_SINGLE_SQL_SOURCE"));
    }
    [TestMethod]
    public void SchemaCompareClassifiesChangesAndOnlySelectedColumnsAreUpdated()
    {
        using var handler = new TabularModelHandler(1702); var service = new FabricImportService(handler);
        service.PreviewImport(new(Schema(false), new[] { "Id", "Amount" }, FabricStorageMode.Import)).Apply(handler);
        var source = Schema(false); source = Rehash(source with { Columns = new[] { new FabricColumnSchema("Amount", "bigint", true), new FabricColumnSchema("NewId", "bigint", false, 1) } });
        var diff = service.CompareSchema("Orders", source);
        foreach (var category in new[] { "New source column", "Removed source column", "Type change", "Rename candidate" }) Assert.IsTrue(diff.Any(row => row.Category == category), category);
        var before = Fingerprint(handler); var plan = service.PreviewSchemaUpdate("Orders", source, new[] { "NewId", "Amount" });
        Assert.IsTrue(plan.CanApply, Issues(plan)); plan.Apply(handler);
        var table = handler.Model.Tables["Orders"]; Assert.AreEqual(3, table.Columns.Count); Assert.IsTrue(table.Columns.Contains("Id")); Assert.AreEqual(DataType.Int64, table.Columns["Amount"].DataType);
        handler.UndoManager.Undo(); Assert.AreEqual(before, Fingerprint(handler));
    }
    [TestMethod]
    public void SchemaUpdateRefusesUnmatchedSourceAndRelationshipTypeChanges()
    {
        using var handler = new TabularModelHandler(1702); var service = new FabricImportService(handler);
        service.PreviewImport(new(Schema(false), new[] { "Id" }, FabricStorageMode.Import)).Apply(handler);
        var source = Schema(false); var foreign = Rehash(source with { Source = source.Source with { Table = "Other" } });
        Assert.IsFalse(service.PreviewSchemaUpdate("Orders", foreign, new[] { "Amount" }).CanApply);
        service.PreviewImport(new(source, new[] { "Id" }, FabricStorageMode.Import, "Related")).Apply(handler);
        var relationship = handler.Model.AddRelationship(); relationship.FromColumn = handler.Model.Tables["Orders"].Columns["Id"]; relationship.ToColumn = handler.Model.Tables["Related"].Columns["Id"];
        var changed = Rehash(source with { Columns = new[] { new FabricColumnSchema("Id", "varchar", true) } });
        Assert.IsTrue(service.PreviewSchemaUpdate("Orders", changed, new[] { "Id" }).Issues.Any(issue => issue.Code == "FABRIC_RELATIONSHIP_TYPE"));
    }
    [TestMethod]
    public void TypesViewsCompatibilityAndSchemaProvenanceAreValidated()
    {
        using var handler = new TabularModelHandler(1600); var service = new FabricImportService(handler);
        Assert.IsFalse(service.PreviewImport(new(Schema(true), new[] { "Id" }, FabricStorageMode.DirectLakeOneLake)).CanApply);
        var schema = Schema(false); Assert.ThrowsExactly<ArgumentException>(() => service.PreviewImport(new(schema with { Fingerprint = "wrong" }, new[] { "Id" }, FabricStorageMode.Import)));
        var timestamp = Rehash(schema with { Columns = new[] { new FabricColumnSchema("Version", "timestamp", false) } });
        Assert.IsFalse(service.PreviewImport(new(timestamp, new[] { "Version" }, FabricStorageMode.Import)).CanApply, "SQL timestamp is rowversion, not a date.");
        var view = Rehash(Schema(true) with { Source = Schema(true).Source with { IsView = true } }); Assert.IsTrue(service.PreviewImport(new(view, new[] { "Id" }, FabricStorageMode.DirectLakeOneLake)).Issues.Any(issue => issue.Code == "FABRIC_VIEW"));
    }
    [TestMethod]
    public void ImportedNamesCannotInjectMAndPreviewsRejectInterveningModelEdits()
    {
        using var handler = new TabularModelHandler(1702); var service = new FabricImportService(handler); var source = Schema(false);
        source = Rehash(source with { Source = source.Source with { Table = "Orders#(lf)\"quoted" } });
        var plan = service.PreviewImport(new(source, new[] { "Id" }, FabricStorageMode.Import)); Assert.IsTrue(plan.CanApply, Issues(plan));
        Assert.IsTrue(plan.Changes.Any(row => row.After.Contains("Orders#(#)(lf)\"\"quoted")));
        handler.Model.Description = "intervening"; Assert.ThrowsExactly<InvalidOperationException>(() => plan.Apply(handler));
    }
    private static string Fingerprint(TabularModelHandler handler) => new SemanticModelService(handler).Fingerprint();
    private static string Issues(AuthoringPreview preview) => string.Join("; ", preview.Issues.Select(issue => issue.Message));
    private static FabricTableSchema Schema(bool delta)
    {
        var source = new FabricSourceRef("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "Lakehouse", "dbo", "Orders", delta ? "DELTA" : "SQL", new("example.datawarehouse.fabric.microsoft.com", "33333333-3333-3333-3333-333333333333"));
        return Rehash(new(source, new[] { new FabricColumnSchema("Id", delta ? "long" : "bigint", false), new FabricColumnSchema("Amount", "double", true, 1) }, "", DateTimeOffset.UtcNow, Array.Empty<string>()));
    }
    private static FabricTableSchema Rehash(FabricTableSchema schema) => schema with { Fingerprint = FabricSchemaRules.Fingerprint(schema.Source, schema.Columns) };
}
