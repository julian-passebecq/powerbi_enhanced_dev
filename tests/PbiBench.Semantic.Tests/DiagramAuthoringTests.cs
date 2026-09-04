using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class DiagramAuthoringTests
{
    [TestMethod]
    public void TableGroupsPreviewWithoutMutationAndApplyAsOneUndoableBatch()
    {
        using var handler = new TabularModelHandler(1600);
        var a = handler.Model.AddTable("Sales"); var b = handler.Model.AddTable("Products");
        a.SetAnnotation("User.Annotation", "preserve");
        var before = new SemanticModelService(handler).Fingerprint();
        var service = new TableGroupService(handler);
        var preview = service.PreviewAssign(new[] { a, b, a }, "Finance / Zürich");
        Assert.AreEqual(2, preview.Changes.Count);
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        preview.Apply(handler);
        Assert.AreEqual("Finance / Zürich", TableGroupService.Read(a).Group);
        Assert.AreEqual("Finance / Zürich", TableGroupService.Read(b).Group);
        Assert.AreEqual("preserve", a.GetAnnotation("User.Annotation"));
        Assert.AreEqual(0, handler.UndoManager.BatchDepth);
        Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(handler));
        handler.UndoManager.Undo();
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        handler.UndoManager.Redo();
        Assert.AreEqual("Finance / Zürich", TableGroupService.Read(a).Group);
    }

    [TestMethod]
    public void TableGroupAnnotationSurvivesTableRenameAndRoundTripsJson()
    {
        using var handler = new TabularModelHandler(1600);
        var a = handler.Model.AddTable("Old");
        new TableGroupService(handler).PreviewAssign(new[] { a }, "Quoted \"group\" \\ slash").Apply(handler);
        var annotation = a.GetAnnotation(TableGroupService.AnnotationName);
        Assert.IsTrue(annotation.Contains("version"));
        a.Name = "New";
        Assert.AreEqual("Quoted \"group\" \\ slash", TableGroupService.Read(a).Group);
        var b = handler.Model.AddTable("Round trip"); b.SetAnnotation(TableGroupService.AnnotationName, annotation);
        Assert.AreEqual(TableGroupService.Read(a).Group, TableGroupService.Read(b).Group);
        var graph = new SemanticModelService(handler).GetGraph();
        Assert.AreEqual("Quoted \"group\" \\ slash", graph.Tables.Single(t => t.Object == a).Group);
    }

    [TestMethod]
    public void RenameAndRemoveGroupsAffectOnlyTheirOwnAnnotation()
    {
        using var handler = new TabularModelHandler(1600);
        var a = handler.Model.AddTable("A"); var b = handler.Model.AddTable("B");
        var service = new TableGroupService(handler);
        service.PreviewAssign(new[] { a, b }, "Old").Apply(handler);
        service.PreviewRename("Old", "New").Apply(handler);
        Assert.IsTrue(service.Read().All(entry => entry.Group == "New"));
        service.PreviewRemove("New").Apply(handler);
        Assert.IsTrue(service.Read().All(entry => entry.Group == null));
        handler.UndoManager.Undo();
        Assert.IsTrue(service.Read().All(entry => entry.Group == "New"));
    }

    [TestMethod]
    public void MalformedOversizedOrFutureGroupAnnotationsArePreservedAndBlockOverwrite()
    {
        using var handler = new TabularModelHandler(1600);
        var table = handler.Model.AddTable("A"); var service = new TableGroupService(handler);
        foreach (var invalid in new[] { "{", "{\"version\":2,\"group\":\"future\"}", "{\"version\":\"1\",\"group\":\"wrong type\"}", "{\"version\":null,\"group\":\"wrong type\"}", "{\"version\":1,\"group\":\"x\",\"extra\":1}", new string('x', 4097), "{\"version\":1,\"group\":\"\"}" })
        {
            table.SetAnnotation(TableGroupService.AnnotationName, invalid);
            Assert.IsNotNull(TableGroupService.Read(table).Issue);
            var preview = service.PreviewAssign(new[] { table }, "Replacement");
            Assert.IsFalse(preview.CanApply);
            Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(handler));
            Assert.AreEqual(invalid, table.GetAnnotation(TableGroupService.AnnotationName));
            Assert.IsNull(new SemanticModelService(handler).GetGraph().Tables.Single().Group);
        }
        Assert.ThrowsExactly<ArgumentException>(() => service.PreviewAssign(new[] { table }, new string('g', 257)));
        Assert.ThrowsExactly<ArgumentException>(() => service.PreviewAssign(new[] { table }, "control\nname"));
    }

    [TestMethod]
    public void GroupPreviewsRejectStaleAndForeignModelSessions()
    {
        using var handler = new TabularModelHandler(1600);
        var table = handler.Model.AddTable("A");
        using var other = new TabularModelHandler(1600); var foreign = other.Model.AddTable("A");
        var service = new TableGroupService(handler); var preview = service.PreviewAssign(new[] { table }, "Group");
        Assert.ThrowsExactly<ArgumentException>(() => service.PreviewAssign(new[] { foreign }, "Group"));
        Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(other));
        table.Description = "Changed";
        Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(handler));
        Assert.IsNull(TableGroupService.Read(table).Group);
    }

    [TestMethod]
    public void RelationshipInversionUsesRealEndpointsAndOneUndoBatchWithRedo()
    {
        using var handler = new TabularModelHandler(1600); var relationship = Model(handler);
        var original = RelationshipAuthoringService.Capture(relationship);
        var before = new SemanticModelService(handler).Fingerprint(); var id = relationship.ID;
        var preview = new RelationshipAuthoringService(handler).PreviewInvert(relationship);
        Assert.AreEqual(4, preview.Changes.Count);
        Assert.IsTrue(preview.Issues.Any(issue => issue.Code == "INVERT_FILTER"));
        Assert.IsTrue(preview.Changes.All(change => change.ObjectPath.Contains(id)));
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        preview.Apply(handler);
        Assert.AreSame(original.ToColumn, relationship.FromColumn);
        Assert.AreSame(original.FromColumn, relationship.ToColumn);
        Assert.AreEqual(RelationshipEndCardinality.One, relationship.FromCardinality);
        Assert.AreEqual(RelationshipEndCardinality.Many, relationship.ToCardinality);
        Assert.AreEqual(id, relationship.ID);
        handler.UndoManager.Undo();
        Assert.AreEqual(original, RelationshipAuthoringService.Capture(relationship));
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        handler.UndoManager.Redo();
        Assert.AreSame(original.ToColumn, relationship.FromColumn);
        Assert.AreSame(original.FromColumn, relationship.ToColumn);
        Assert.AreEqual(0, handler.UndoManager.BatchDepth);
    }

    [TestMethod]
    public void RelationshipEditorAppliesExactFieldsAndRejectsStaleOrConsumedPreview()
    {
        using var handler = new TabularModelHandler(1600); var relationship = Model(handler);
        var original = RelationshipAuthoringService.Capture(relationship);
        var service = new RelationshipAuthoringService(handler);
        var requested = original with { IsActive = false, CrossFilteringBehavior = CrossFilteringBehavior.BothDirections,
            SecurityFilteringBehavior = SecurityFilteringBehavior.BothDirections };
        var preview = service.Preview(relationship, requested);
        Assert.AreEqual(3, preview.Changes.Count);
        preview.Apply(handler);
        Assert.AreEqual(requested, RelationshipAuthoringService.Capture(relationship));
        Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(handler));
        handler.UndoManager.Undo(); Assert.AreEqual(original, RelationshipAuthoringService.Capture(relationship));
        var stale = service.PreviewActive(relationship, false);
        relationship.ToColumn.Description = "new metadata";
        Assert.ThrowsExactly<InvalidOperationException>(() => stale.Apply(handler));
        Assert.IsTrue(relationship.IsActive);
    }

    [TestMethod]
    public void RelationshipValidationRejectsForeignMissingSelfAndMismatchedColumns()
    {
        using var handler = new TabularModelHandler(1600);
        var relationship = Model(handler);
        var definition = RelationshipAuthoringService.Capture(relationship); var service = new RelationshipAuthoringService(handler);
        var text = relationship.ToColumn.Table.AddDataColumn("Text", dataType: DataType.String);
        using var other = new TabularModelHandler(1600); var foreign = other.Model.AddTable("Foreign").AddDataColumn("Id", dataType: DataType.Int64);
        foreach (var invalid in new[] { definition with { FromColumn = foreign }, definition with { ToColumn = null! },
            definition with { ToColumn = relationship.FromColumn }, definition with { ToColumn = text } })
        {
            var preview = service.Preview(relationship, invalid);
            Assert.IsFalse(preview.CanApply); Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(handler));
        }
        Assert.AreEqual(definition, RelationshipAuthoringService.Capture(relationship));
    }

    [TestMethod]
    public void RelationshipValidationRejectsDuplicateAndParallelActiveRelationships()
    {
        using var handler = new TabularModelHandler(1600); var relationship = Model(handler);
        var second = handler.Model.AddRelationship();
        second.FromColumn = relationship.FromColumn.Table.AddDataColumn("OtherKey", dataType: DataType.Int64);
        second.ToColumn = relationship.ToColumn; second.IsActive = false;
        var service = new RelationshipAuthoringService(handler);
        Assert.IsTrue(service.PreviewActive(second, true).Issues.Any(issue => issue.Code == "ACTIVE_PARALLEL" && issue.Severity == AuthoringIssueSeverity.Error));
        var duplicate = service.Preview(second, RelationshipAuthoringService.Capture(second) with { FromColumn = relationship.ToColumn, ToColumn = relationship.FromColumn });
        Assert.IsTrue(duplicate.Issues.Any(issue => issue.Code == "DUPLICATE"));
        service.PreviewActive(relationship, false).Apply(handler);
        var active = service.PreviewActive(second, true); Assert.IsTrue(active.CanApply); active.Apply(handler);
        Assert.IsTrue(second.IsActive);
    }

    [TestMethod]
    public void CardinalitySecurityAndDateRulesUsePublicModelConstraints()
    {
        using var handler = new TabularModelHandler(1400); var relationship = Model(handler);
        var current = RelationshipAuthoringService.Capture(relationship); var service = new RelationshipAuthoringService(handler);
        Assert.IsTrue(service.Preview(relationship, current with { FromCardinality = RelationshipEndCardinality.One }).Issues.Any(issue => issue.Code == "ONE_TO_ONE"));
        Assert.IsTrue(service.Preview(relationship, current with { SecurityFilteringBehavior = SecurityFilteringBehavior.BothDirections }).Issues.Any(issue => issue.Code == "SECURITY_DIRECTION"));
        Assert.IsTrue(service.Preview(relationship, current with { SecurityFilteringBehavior = SecurityFilteringBehavior.None }).Issues.Any(issue => issue.Code == "SECURITY_COMPATIBILITY"));
        Assert.IsTrue(service.Preview(relationship, current with { JoinOnDateBehavior = DateTimeRelationshipBehavior.DatePartOnly }).Issues.Any(issue => issue.Code == "DATE_TYPE"));
        Assert.IsFalse(service.Preview(relationship, current with { FromCardinality = (RelationshipEndCardinality)999 }).CanApply);
        var one = service.Preview(relationship, current with { FromCardinality = RelationshipEndCardinality.One, CrossFilteringBehavior = CrossFilteringBehavior.BothDirections });
        Assert.IsTrue(one.CanApply); one.Apply(handler);
        Assert.AreEqual(RelationshipEndCardinality.One, relationship.FromCardinality);
    }

    [TestMethod]
    public void ManyToManyChecksCompatibilityAndExplainsLimitedSemantics()
    {
        using var old = new TabularModelHandler(1200); var relationship = Model(old);
        var rejected = new RelationshipAuthoringService(old).Preview(relationship, RelationshipAuthoringService.Capture(relationship) with { ToCardinality = RelationshipEndCardinality.Many });
        Assert.IsFalse(rejected.CanApply); Assert.IsTrue(rejected.Issues.Any(issue => issue.Code == "COMPATIBILITY"));
        using var current = new TabularModelHandler(1600); var supported = Model(current);
        var preview = new RelationshipAuthoringService(current).Preview(supported, RelationshipAuthoringService.Capture(supported) with { ToCardinality = RelationshipEndCardinality.Many });
        Assert.IsTrue(preview.CanApply); Assert.IsTrue(preview.Issues.Any(issue => issue.Code == "LIMITED"));
    }

    [TestMethod]
    public void ReferentialIntegrityRequiresDirectQueryAndNeverClaimsDataProof()
    {
        using var handler = new TabularModelHandler(1600); var relationship = Model(handler);
        var definition = RelationshipAuthoringService.Capture(relationship) with { RelyOnReferentialIntegrity = true };
        var service = new RelationshipAuthoringService(handler);
        Assert.IsTrue(service.Preview(relationship, definition).Issues.Any(issue => issue.Code == "REFERENTIAL_MODE"));
        relationship.FromTable.AddPartition("Data").Mode = ModeType.DirectQuery;
        relationship.ToTable.AddPartition("Data").Mode = ModeType.DirectQuery;
        foreach (var partition in relationship.FromTable.Partitions.Concat(relationship.ToTable.Partitions)) partition.Mode = ModeType.DirectQuery;
        var preview = service.Preview(relationship, definition);
        Assert.IsTrue(preview.CanApply);
        Assert.IsTrue(preview.Issues.Any(issue => issue.Code == "REFERENTIAL_DATA"));
        Assert.IsTrue(preview.Issues.Any(issue => issue.Code == "COMPOSITE_SOURCE"));
    }

    [TestMethod]
    public void GraphKeepsObjectIdentityTypedKeyColumnsAndCorrectActiveFilteringNeighbours()
    {
        using var handler = new TabularModelHandler(1600); var relationship = Model(handler);
        relationship.ToColumn.IsKey = true;
        var graph = new SemanticModelService(handler).GetGraph(); var edge = graph.Relationships.Single();
        Assert.AreSame(relationship, edge.Object); Assert.AreEqual(relationship.ID, edge.Id);
        Assert.AreSame(relationship.FromTable, graph.Tables.Single(table => table.Name == "Fact").Object);
        Assert.IsTrue(graph.Tables.Single(table => table.Name == "Dimension").ColumnMetadata!.Single().IsKey);
        Assert.IsTrue(graph.Tables.Single(table => table.Name == "Fact").ColumnMetadata!.Single().IsRelationshipKey);
        CollectionAssert.AreEqual(new[] { "Dimension" }, graph.RelatedTables("Fact", true).ToArray());
        Assert.AreEqual(0, graph.RelatedTables("Dimension", true).Count);
        relationship.IsActive = false; graph = new SemanticModelService(handler).GetGraph();
        Assert.AreEqual(0, graph.RelatedTables("Fact", true).Count);
        Assert.AreEqual(1, graph.RelatedTables("Fact").Count);
        relationship.IsActive = true; relationship.CrossFilteringBehavior = CrossFilteringBehavior.BothDirections;
        Assert.AreEqual(1, new SemanticModelService(handler).GetGraph().RelatedTables("Dimension", true).Count);
    }

    private static SingleColumnRelationship Model(TabularModelHandler handler)
    {
        var from = handler.Model.AddTable("Fact").AddDataColumn("Key", dataType: DataType.Int64);
        var to = handler.Model.AddTable("Dimension").AddDataColumn("Key", dataType: DataType.Int64);
        var relationship = handler.Model.AddRelationship(); relationship.FromColumn = from; relationship.ToColumn = to;
        relationship.FromCardinality = RelationshipEndCardinality.Many; relationship.ToCardinality = RelationshipEndCardinality.One;
        relationship.CrossFilteringBehavior = CrossFilteringBehavior.OneDirection; relationship.IsActive = true;
        return relationship;
    }
}
