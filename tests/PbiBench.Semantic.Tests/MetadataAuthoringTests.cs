using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public sealed class MetadataAuthoringTests
{
    [TestMethod]
    public void SharedPreviewRejectsStaleWrongSessionAndRepeatApplyAndSupportsUndo()
    {
        using var handler = new TabularModelHandler(1600);
        var service = new PerspectiveEditorService(handler);
        var stale = service.PreviewCreate("Stale"); handler.Model.AddTable("Changed");
        Assert.ThrowsExactly<InvalidOperationException>(() => stale.Apply(handler));
        var preview = service.PreviewCreate("Sales");
        preview.Apply(handler); Assert.IsTrue(handler.Model.Perspectives.Contains("Sales"));
        Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(handler));
        handler.UndoManager.Undo(); Assert.IsFalse(handler.Model.Perspectives.Contains("Sales"));
        using (var other = new TabularModelHandler(1600)) Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(other));
    }
    [TestMethod]
    public void SharedPreviewRollsBackTheWholeBatchOnSetterFailureOrPostconditionMismatch()
    {
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("Sales"); table.Description = "Before";
        var fingerprint = new SemanticModelService(handler).Fingerprint();
        var failure = AuthoringPreview.Create(handler, "Fail", new[] { new AuthoringEdit(new("Sales", "Description", "Before", "After", "Test rollback"), () => { table.Description = "After"; throw new InvalidOperationException("Expected"); }, () => true) });
        Assert.ThrowsExactly<InvalidOperationException>(() => failure.Apply(handler));
        Assert.AreEqual(fingerprint, new SemanticModelService(handler).Fingerprint()); Assert.AreEqual(0, handler.UndoManager.BatchDepth);
        var mismatch = AuthoringPreview.Create(handler, "Mismatch", new[] { new AuthoringEdit(new("Sales", "Description", "Before", "After", "Test postcondition"), () => table.Description = "After", () => false) });
        Assert.ThrowsExactly<InvalidOperationException>(() => mismatch.Apply(handler)); Assert.AreEqual("Before", table.Description); Assert.AreEqual(0, handler.UndoManager.BatchDepth);
        var invalid = AuthoringPreview.Create(handler, "Invalid", new[] { new AuthoringEdit(new("Sales", "Description", "Before", "After", "Error guard"), () => table.Description = "After", () => true) }, new[] { new AuthoringIssue("INVALID", "Invalid metadata", AuthoringIssueSeverity.Error) });
        Assert.IsFalse(invalid.CanApply); Assert.ThrowsExactly<InvalidOperationException>(() => invalid.Apply(handler)); Assert.AreEqual("Before", table.Description);
    }
    [TestMethod]
    public void PerspectiveTableTriStateExpandsPartialMembershipAndOneUndoRestoresIt()
    {
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("Sales");
        var first = table.AddDataColumn("First", "First"); var second = table.AddDataColumn("Second", "Second");
        var measure = table.AddMeasure("Revenue", "1"); measure.IsHidden = true;
        var perspective = handler.Model.AddPerspective("Business"); first.InPerspective[perspective] = true;
        var service = new PerspectiveEditorService(handler); var snapshot = service.Capture(); var tableRow = snapshot.Members.Single(member => member.Name == table.Name);
        Assert.IsNull(tableRow.Membership["Business"]);
        var before = new SemanticModelService(handler).Fingerprint(); var preview = service.PreviewMembership(new[] { new PerspectiveMembershipChange(tableRow.Id, "Business", true) });
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint()); Assert.IsTrue(preview.Issues.Any(issue => issue.Code == "HIDDEN_MEMBER"));
        preview.Apply(handler); Assert.IsTrue(second.InPerspective[perspective]); Assert.IsTrue(measure.InPerspective[perspective]);
        Assert.AreEqual(true, service.Capture().Members.Single(member => member.Id == tableRow.Id).Membership["Business"]);
        handler.UndoManager.Undo(); Assert.IsTrue(first.InPerspective[perspective]); Assert.IsFalse(second.InPerspective[perspective]); Assert.IsFalse(measure.InPerspective[perspective]);
        service.PreviewRename("Business", "Executive").Apply(handler); Assert.IsTrue(first.InPerspective["Executive"]); handler.UndoManager.Undo();
        service.PreviewDelete("Business").Apply(handler); Assert.IsFalse(handler.Model.Perspectives.Contains("Business")); handler.UndoManager.Undo(); Assert.IsTrue(first.InPerspective["Business"]);
    }
    [TestMethod]
    public void TranslationCellEditsPreserveUnspecifiedMetadataAndNormalizeEmptyTextAsTomDoes()
    {
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("Sales"); var measure = table.AddMeasure("Revenue", "1");
        handler.Model.AddTranslation("de-CH"); handler.Model.AddTranslation("fr-FR");
        measure.TranslatedNames["de-CH"] = "Umsatz"; measure.TranslatedDescriptions["de-CH"] = "Beschreibung"; measure.TranslatedNames["fr-FR"] = "Recettes";
        var service = new TranslationEditorService(handler); var id = service.Capture().Members.Single(member => member.Name == "Revenue").Id;
        Assert.AreEqual("", service.Capture().Members.Single(member => member.Id == id).DisplayFolder, "Supported empty display folders remain editable in the matrix.");
        var preview = service.PreviewCells(new[] { new TranslationCell(id, "de-CH", TranslationProperty.Description, "") });
        Assert.AreEqual("Beschreibung", measure.TranslatedDescriptions["de-CH"]); preview.Apply(handler);
        Assert.AreEqual("Umsatz", measure.TranslatedNames["de-CH"]); Assert.AreEqual("Recettes", measure.TranslatedNames["fr-FR"]); Assert.AreEqual("", measure.TranslatedDescriptions["de-CH"]);
        Assert.IsFalse(measure.TranslatedDescriptions.Contains(handler.Model.Cultures["de-CH"])); handler.UndoManager.Undo();
        service.PreviewCells(new[] { new TranslationCell(id, "de-CH", TranslationProperty.Name, null) }).Apply(handler);
        Assert.IsFalse(measure.TranslatedNames.Contains(handler.Model.Cultures["de-CH"])); handler.UndoManager.Undo(); Assert.AreEqual("Umsatz", measure.TranslatedNames["de-CH"]);
    }
    [TestMethod]
    public void TranslationImportPreviewsOnlyExplicitCellsAndRejectsInvalidObjectsBeforeMutation()
    {
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("Sales"); var measure = table.AddMeasure("Revenue", "1");
        handler.Model.AddTranslation("de-CH"); measure.TranslatedNames["de-CH"] = "Existing";
        var service = new TranslationEditorService(handler); var id = service.Capture().Members.Single(member => member.Name == "Revenue").Id;
        var json = System.Text.Json.JsonSerializer.Serialize(new TranslationPackage(1, new[] { new TranslationCell(id, "de-CH", TranslationProperty.Name, "Imported"), new TranslationCell(id, "de-CH", TranslationProperty.Description, "Added") }));
        var fill = service.PreviewImportJson(json); Assert.AreEqual(1, fill.Changes.Count); fill.Apply(handler);
        Assert.AreEqual("Existing", measure.TranslatedNames["de-CH"]); Assert.AreEqual("Added", measure.TranslatedDescriptions["de-CH"]);
        service.PreviewImportJson(json, true).Apply(handler); Assert.AreEqual("Imported", measure.TranslatedNames["de-CH"]);
        Assert.IsFalse(service.PreviewImportJson(service.ExportJson(), true).CanApply);
        var fingerprint = new SemanticModelService(handler).Fingerprint();
        Assert.ThrowsExactly<ArgumentException>(() => service.PreviewCells(new[] { new TranslationCell(id, "it-IT", TranslationProperty.Name, "Ricavo"), new TranslationCell("missing", "it-IT", TranslationProperty.Name, "Invalid") }));
        Assert.AreEqual(fingerprint, new SemanticModelService(handler).Fingerprint()); Assert.IsFalse(handler.Model.Cultures.Contains("it-IT"));
        Assert.ThrowsExactly<ArgumentException>(() => service.PreviewCreateCulture("de"));
    }
    [TestMethod]
    public void TranslationCultureRenameAndDeletionPreserveCellsThroughUndo()
    {
        using var handler = new TabularModelHandler(1600); var table = handler.Model.AddTable("Sales"); var measure = table.AddMeasure("Revenue", "1");
        var service = new TranslationEditorService(handler); service.PreviewCreateCulture("de-CH").Apply(handler); measure.TranslatedNames["de-CH"] = "Umsatz";
        service.PreviewRenameCulture("de-CH", "de-DE").Apply(handler);
        Assert.AreEqual("Umsatz", measure.TranslatedNames["de-DE"], "After rename");
        handler.UndoManager.Undo(); Assert.AreEqual("Umsatz", measure.TranslatedNames["de-CH"], "After undo rename");
        service.PreviewDeleteCulture("de-CH").Apply(handler); Assert.IsFalse(handler.Model.Cultures.Contains("de-CH"));
        handler.UndoManager.Undo(); Assert.AreEqual("Umsatz", measure.TranslatedNames["de-CH"], "After undo delete");
        handler.UndoManager.Redo(); Assert.IsFalse(handler.Model.Cultures.Contains("de-CH")); handler.UndoManager.Undo(); Assert.AreEqual("Umsatz", measure.TranslatedNames["de-CH"]);
    }
    [TestMethod]
    public void CalendarCompatibilityAndMappingValidationDoNotMutateTheModel()
    {
        using var handler = CalendarModel(1600); var service = new CalendarEditorService(handler); var before = new SemanticModelService(handler).Fingerprint();
        var draft = Calendar(); var preview = service.Preview(draft); Assert.IsFalse(preview.CanApply); Assert.IsTrue(preview.Issues.Any(issue => issue.Code == "CALENDAR_COMPATIBILITY"));
        Assert.ThrowsExactly<InvalidOperationException>(() => preview.Apply(handler)); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        using var modern = CalendarModel(1701); var current = new CalendarEditorService(modern);
        var invalid = draft with { Mappings = new[] { new CalendarMapping("Month", "Month", new[] { "Month" }) }, TimeRelatedColumns = new[] { "Missing" } };
        Assert.IsFalse(current.Preview(invalid).CanApply);
        Assert.IsTrue(current.Validate(invalid).Any(issue => issue.Code == "CALENDAR_DUPLICATE_COLUMN"));
        Assert.IsFalse(current.Preview(draft with { Name = "Dates" }).CanApply);
        Assert.IsTrue(current.Validate(draft with { SortChanges = new[] { new CalendarSortChange("Month", "Month Name"), new CalendarSortChange("Month Name", "Month") } }).Any(issue => issue.Code == "CALENDAR_SORT_CYCLE"));
    }
    [TestMethod]
    public void CalendarAt1701UsesWrapperUndoAndFreezesDraftCollections()
    {
        using var handler = CalendarModel(1701); var service = new CalendarEditorService(handler);
        var names = new[] { "Month Name" }; var draft = Calendar() with { Mappings = new[] { new CalendarMapping("Year", "Year", Array.Empty<string>()), new CalendarMapping("Month", "Month", names) }, SortChanges = new[] { new CalendarSortChange("Month Name", "Month") } };
        var preview = service.Preview(draft); names[0] = "Missing"; preview.Apply(handler);
        var calendar = handler.Model.Tables["Dates"].Calendars["Fiscal"];
        Assert.AreEqual("Month Name", calendar.FindTimeUnit(TimeUnit.Month).AssociatedColumns.Single().Name);
        Assert.AreEqual("Month", handler.Model.Tables["Dates"].Columns["Month Name"].SortByColumn.Name);
        Assert.IsTrue(calendar.CalendarColumnGroups.OfType<TimeRelatedColumnGroup>().Single().Columns.Any(column => column.Name == "Working Day"));
        handler.UndoManager.Undo(); Assert.AreEqual(0, handler.Model.Tables["Dates"].Calendars.Count); Assert.IsNull(handler.Model.Tables["Dates"].Columns["Month Name"].SortByColumn);
    }
    [TestMethod]
    public void CalendarRenamePreviewsCallersAndPreservesIdentityAndUndo()
    {
        using var handler = CalendarModel(1701); var service = new CalendarEditorService(handler); service.Preview(Calendar()).Apply(handler);
        var calendar = handler.Model.Tables["Dates"].Calendars["Fiscal"]; var lineage = calendar.LineageTag;
        var measure = handler.Model.Tables["Dates"].AddMeasure("YTD", "TOTALYTD(1, 'Fiscal')");
        var draft = service.Capture().Calendars.Single() with { Name = "Fiscal's New" };
        var preview = service.Preview(draft); Assert.IsTrue(preview.Changes.Any(change => change.Property == "Expression"));
        preview.Apply(handler); Assert.AreEqual("TOTALYTD(1, 'Fiscal''s New')", measure.Expression); Assert.AreEqual(lineage, calendar.LineageTag);
        handler.UndoManager.Undo(); Assert.AreEqual("Fiscal", calendar.Name); Assert.AreEqual("TOTALYTD(1, 'Fiscal')", measure.Expression);
        Assert.IsFalse(service.PreviewDelete("Dates", "Fiscal").CanApply);
        measure.Expression = "1"; service.PreviewDelete("Dates", "Fiscal").Apply(handler); Assert.AreEqual(0, handler.Model.Tables["Dates"].Calendars.Count); handler.UndoManager.Undo(); Assert.AreEqual(lineage, handler.Model.Tables["Dates"].Calendars["Fiscal"].LineageTag);
        handler.UndoManager.Redo(); Assert.AreEqual(0, handler.Model.Tables["Dates"].Calendars.Count); handler.UndoManager.Undo(); Assert.AreEqual(lineage, handler.Model.Tables["Dates"].Calendars["Fiscal"].LineageTag);
    }
    [TestMethod]
    public void CalendarMappingRemovalAndAssociatedColumnEditsRoundTripThroughUndoAndRedo()
    {
        using var handler = CalendarModel(1701); var service = new CalendarEditorService(handler); service.Preview(Calendar()).Apply(handler);
        var before = CalendarCanonicalJson(handler);
        var draft = service.Capture().Calendars.Single() with { Mappings = new[] { new CalendarMapping("Year", "Year", Array.Empty<string>()) }, TimeRelatedColumns = Array.Empty<string>() };
        service.Preview(draft).Apply(handler); Assert.AreEqual(1, handler.Model.Tables["Dates"].Calendars["Fiscal"].CalendarColumnGroups.Count);
        handler.UndoManager.Undo(); Assert.AreEqual(before, CalendarCanonicalJson(handler));
        handler.UndoManager.Redo(); Assert.AreEqual(1, handler.Model.Tables["Dates"].Calendars["Fiscal"].CalendarColumnGroups.Count);
        handler.UndoManager.Undo(); Assert.AreEqual(before, CalendarCanonicalJson(handler));
        Assert.AreEqual(4, handler.Model.Tables["Dates"].Columns.Count(column => column.Name != "RowNumber"));
    }
    [TestMethod]
    public void CalendarCrossCalendarCategoryAndPeriodIssuesAreVisibleAndValidationQueriesAreReadOnly()
    {
        using var handler = CalendarModel(1701); var service = new CalendarEditorService(handler); service.Preview(Calendar()).Apply(handler);
        var invalid = Calendar() with { Name = "Other", Mappings = new[] { new CalendarMapping("Quarter", "Month", Array.Empty<string>()) } };
        Assert.IsTrue(service.Validate(invalid).Any(issue => issue.Code == "CALENDAR_CROSS_CATEGORY"));
        var partial = Calendar() with { Name = "Partial", Mappings = new[] { new CalendarMapping("WeekOfYear", "Working Day", Array.Empty<string>()) }, TimeRelatedColumns = Array.Empty<string>() };
        Assert.IsTrue(service.Validate(partial).Any(issue => issue.Code == "CALENDAR_PERIOD_PATH"));
        var query = service.GenerateValidationQuery(service.Capture().Calendars.Single()); Assert.IsTrue(query.Contains("EVALUATE")); Assert.IsTrue(query.Contains("ISBLANK('Dates'[Month Name])")); Assert.IsTrue(query.Contains("One-to-one"));
        handler.Model.Tables["Dates"].AddMeasure("Rows", "1"); Assert.IsTrue(service.GenerateSample(service.Capture().Calendars.Single(), "Dates", "Rows").Contains("TOTALYTD('Dates'[Rows], 'Fiscal')"));
    }
    private static TabularModelHandler CalendarModel(int compatibility)
    {
        var handler = new TabularModelHandler(compatibility); var table = handler.Model.AddTable("Dates");
        table.AddDataColumn("Year", "Year", dataType: DataType.Int64); table.AddDataColumn("Month", "Month", dataType: DataType.Int64); table.AddDataColumn("Month Name", "Month Name", dataType: DataType.String); table.AddDataColumn("Working Day", "Working Day", dataType: DataType.Boolean); return handler;
    }
    private static CalendarDraft Calendar() => new("Dates", null, "Fiscal", "Fiscal calendar", new[] { new CalendarMapping("Year", "Year", Array.Empty<string>()), new CalendarMapping("Month", "Month", new[] { "Month Name" }) }, new[] { "Working Day" });
    private static string CalendarCanonicalJson(TabularModelHandler handler)
    {
        // TE2 reattaches removed column groups at the end; category order has no ordinal
        // semantics. Compare all metadata exactly after normalizing just that collection.
        var json = System.Text.Json.Nodes.JsonNode.Parse(Microsoft.AnalysisServices.Tabular.JsonSerializer.SerializeDatabase(handler.Database))!;
        foreach (var table in json["model"]!["tables"]!.AsArray())
            if (table!["calendars"] is System.Text.Json.Nodes.JsonArray calendars)
                foreach (var calendar in calendars)
                    if (calendar!["calendarColumnGroups"] is System.Text.Json.Nodes.JsonArray groups)
                        calendar["calendarColumnGroups"] = new System.Text.Json.Nodes.JsonArray(groups.Select(group => group!.DeepClone()).OrderBy(group => group.ToJsonString(), StringComparer.Ordinal).ToArray());
        return json.ToJsonString();
    }
}
