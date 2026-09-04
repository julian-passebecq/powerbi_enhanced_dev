using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Automation;

/// <summary>Typed, preview-only planning and one-batch local editing on the actual hosted TE2 model.</summary>
public sealed class AutomationService
{
    private readonly TabularModelHandler handler;
    private readonly SemanticModelService semantic;
    private readonly Guid owner = Guid.NewGuid();
    private readonly LocalDaxFormatter formatter = new();

    public AutomationService(TabularModelHandler handler)
    {
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        semantic = new SemanticModelService(handler);
    }

    public static IReadOnlyList<AutomationAction> Actions { get; } = Array.AsReadOnly(new[]
    {
        new AutomationAction(AutomationActionId.FormatMeasures, "Format measure DAX", "Apply conservative offline formatting; expressions never leave this computer.", "Selected measures, or all measures if selection is empty", "Local expression layout"),
        new AutomationAction(AutomationActionId.CreateSumMeasures, "Create explicit SUM measures", "Create one uniquely named SUM measure for each selected numeric column.", "Selected numeric columns", "Local model additions"),
        new AutomationAction(AutomationActionId.CreateMeasureTable, "Create / use measure table", "Create a calculated measure table with a hidden placeholder, or select the existing table.", "Any context", "Local model additions; new table requires refresh when deployed"),
        new AutomationAction(AutomationActionId.SetSummarizeByNone, "Set SummarizeBy to None", "Disable implicit aggregation on selected columns.", "Selected columns", "Changes report default aggregation"),
        new AutomationAction(AutomationActionId.OrganizeMeasures, "Organize measure display folders", "Set the exact folder shown in the preview.", "Selected measures", "Local presentation metadata"),
        new AutomationAction(AutomationActionId.AddDescriptions, "Add descriptions", "Fill blank descriptions using {Name} and {Table} placeholders.", "Selected tables, measures or columns", "Local documentation metadata"),
        new AutomationAction(AutomationActionId.LastRefreshScaffold, "Create Last Refresh scaffold", "Add an import M table that captures UTC refresh time and a measure over it.", "Any context", "Local model additions; value appears after data refresh")
    });

    public ChangePreview Preview(AutomationActionId actionId, IEnumerable<TabularNamedObject> selection, AutomationOptions? options = null)
        => BuildPreview(actionId, selection, options, null);

    // A synchronous BPA scan shares one immutable metadata snapshot across its findings.
    internal ChangePreview PreviewAtSnapshot(AutomationActionId actionId, IEnumerable<TabularNamedObject> selection, string fingerprint)
        => BuildPreview(actionId, selection, null, fingerprint);

    private ChangePreview BuildPreview(AutomationActionId actionId, IEnumerable<TabularNamedObject> selection, AutomationOptions? options, string? fingerprint)
    {
        if (selection == null) throw new ArgumentNullException(nameof(selection));
        options ??= new AutomationOptions();
        var selected = selection.Distinct().ToArray();
        if (selected.Any(obj => !ReferenceEquals(obj.Model, handler.Model)))
            throw new InvalidOperationException("The selection belongs to a different model. Select objects again.");
        var action = Actions.Single(a => a.Id == actionId);
        var edits = new List<PlannedEdit>();
        var notices = new List<string>();
        TabularNamedObject? focus = null;
        switch (actionId)
        {
            case AutomationActionId.FormatMeasures:
                var measures = selected.Length == 0 && options.AllMeasuresWhenSelectionEmpty ? handler.Model.AllMeasures.ToArray() : selected.OfType<Measure>().ToArray();
                Require(measures.Length > 0, "Select at least one measure, or clear the selection to format all measures.");
                foreach (var measure in measures)
                {
                    var after = formatter.Format(measure.Expression ?? "");
                    AddProperty(edits, measure, "Expression", measure.Expression, after, "Offline formatting preserves all DAX tokens.", () => measure.Expression = after, () => measure.Expression == after);
                }
                break;
            case AutomationActionId.CreateSumMeasures:
                var numeric = selected.OfType<Column>().Where(IsNumeric).ToArray();
                Require(numeric.Length > 0, "Select numeric columns (whole number, decimal or currency) in the Model editor.");
                Require(numeric.Length == selected.Length, "SUM measures require a selection containing only numeric columns. Remove text, date, Boolean and other objects from the selection.");
                var names = new HashSet<string>(handler.Model.AllMeasures.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var column in numeric)
                {
                    var basis = (options.MeasurePrefix ?? "") + column.Name;
                    var reservedNames = new HashSet<string>(names.Concat(column.Table.Columns.Select(c => c.Name)), StringComparer.OrdinalIgnoreCase);
                    var name = UniqueName(basis, reservedNames);
                    names.Add(name);
                    var expression = "SUM ( " + SemanticModelService.ObjectPath(column) + " )";
                    var table = column.Table;
                    var path = "'" + table.Name.Replace("'", "''") + "'[" + name.Replace("]", "]]") + "]";
                    edits.Add(new PlannedEdit(new ObjectChange(path, "New measure", "(does not exist)", expression, "Explicit SUM over " + SemanticModelService.ObjectPath(column), column),
                        () => table.AddMeasure(name, expression), () => table.Measures.Any(m => m.Name == name && m.Expression == expression)));
                }
                break;
            case AutomationActionId.CreateMeasureTable:
                var tableName = RequiredName(options.MeasureTableName, "Measure table name");
                focus = handler.Model.Tables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (focus != null) notices.Add("Use existing table '" + focus.Name + "'. No metadata changes are needed.");
                else
                {
                    var availableName = semantic.AvailableTableName(tableName);
                    if (availableName != tableName) notices.Add("The model reserves the requested name. The preview uses '" + availableName + "'.");
                    tableName = availableName;
                    const string expression = "{ BLANK () }";
                    edits.Add(new PlannedEdit(new ObjectChange("'" + tableName + "'", "New calculated table", "(does not exist)", "Expression: " + expression + "\nColumn: Value (Int64), SourceColumn: [Value], IsHidden: True", "The hidden placeholder keeps the table usable as a measure container.", null),
                        () => { var table = handler.Model.AddCalculatedTable(tableName, expression); table.AddCalculatedTableColumn("Value", "[Value]", dataType: DataType.Int64).IsHidden = true; },
                        () => handler.Model.Tables.Any(t => t.Name == tableName && t is CalculatedTable ct && ct.Expression == expression && t.Columns.Any(c => c.Name == "Value" && c.IsHidden))));
                }
                break;
            case AutomationActionId.SetSummarizeByNone:
                var columns = selected.OfType<Column>().ToArray();
                Require(columns.Length > 0, "Select columns in the Model editor.");
                foreach (var column in columns)
                    AddProperty(edits, column, "SummarizeBy", column.SummarizeBy.ToString(), AggregateFunction.None.ToString(), "Prevent implicit aggregation of this selected column.",
                        () => column.SummarizeBy = AggregateFunction.None, () => column.SummarizeBy == AggregateFunction.None);
                break;
            case AutomationActionId.OrganizeMeasures:
                var folderMeasures = selected.OfType<Measure>().ToArray();
                Require(folderMeasures.Length > 0, "Select measures in the Model editor.");
                var folder = options.DisplayFolder ?? "";
                Require(folder.IndexOfAny(new[] { '\r', '\n', '\0' }) < 0, "Display folder cannot contain control characters.");
                foreach (var measure in folderMeasures)
                    AddProperty(edits, measure, "DisplayFolder", measure.DisplayFolder, folder, "Organize the selected measure.", () => measure.DisplayFolder = folder, () => (measure.DisplayFolder ?? "") == folder);
                break;
            case AutomationActionId.AddDescriptions:
                Require(selected.OfType<IDescriptionObject>().Any(), "Select tables, measures or columns with a description property.");
                foreach (var obj in selected.Where(o => o is IDescriptionObject))
                {
                    var described = (IDescriptionObject)obj;
                    if (!string.IsNullOrWhiteSpace(described.Description)) continue;
                    var description = (options.DescriptionTemplate ?? "{Name}").Replace("{Name}", obj.Name).Replace("{Table}", obj is ITabularTableObject child ? child.Table.Name : obj.Name);
                    AddProperty(edits, obj, "Description", described.Description, description, "Fill an empty description; existing documentation is preserved.", () => described.Description = description, () => described.Description == description);
                }
                break;
            case AutomationActionId.LastRefreshScaffold:
                const string refreshTable = "Refresh Information";
                const string refreshMeasure = "Last Refresh UTC";
                const string refreshM = "let\n    CapturedUtc = DateTimeZone.RemoveZone(DateTimeZone.FixedUtcNow()),\n    Result = #table(type table [RefreshedUtc = datetime], {{CapturedUtc}})\nin\n    Result";
                const string refreshDax = "MAX ( 'Refresh Information'[RefreshedUtc] )";
                Require(!handler.Model.Tables.Any(t => t.Name.Equals(refreshTable, StringComparison.OrdinalIgnoreCase)) && !handler.Model.AllMeasures.Any(m => m.Name.Equals(refreshMeasure, StringComparison.OrdinalIgnoreCase)), "Refresh scaffold already exists. Existing refresh definitions will not be overwritten.");
                edits.Add(new PlannedEdit(new ObjectChange("'Refresh Information'", "New import table + measure", "(does not exist)", "M partition (Import):\n" + refreshM + "\nColumn: RefreshedUtc (DateTime, hidden, SummarizeBy None)\nMeasure: [Last Refresh UTC] = " + refreshDax + "\nFormatString: yyyy-MM-dd HH:mm:ss", "Captures UTC when this partition refreshes, not whenever a report is queried.", null),
                    () => { var table = handler.Model.AddTable(refreshTable); foreach (var partition in table.Partitions.ToArray()) partition.Delete(); table.AddMPartition(refreshTable, refreshM).Mode = ModeType.Import; var column = table.AddDataColumn("RefreshedUtc", "RefreshedUtc", dataType: DataType.DateTime); column.IsHidden = true; column.SummarizeBy = AggregateFunction.None; table.AddMeasure(refreshMeasure, refreshDax).FormatString = "yyyy-MM-dd HH:mm:ss"; },
                    () => handler.Model.Tables.Any(t => t.Name == refreshTable && t.Measures.Any(m => m.Name == refreshMeasure && m.Expression == refreshDax))));
                notices.Add("This is metadata only. Refresh the new table after saving/deploying to populate the timestamp.");
                break;
            default: throw new ArgumentOutOfRangeException(nameof(actionId));
        }
        if (edits.Count == 0 && notices.Count == 0) notices.Add("The selected objects already have the requested values. No changes are needed.");
        return new ChangePreview(owner, action, fingerprint ?? semantic.Fingerprint(), edits, notices, focus);
    }

    /// <summary>Call only after displaying this preview to the user. Does not persist or write to a server.</summary>
    public ApplyResult Apply(ChangePreview preview)
    {
        if (preview == null) throw new ArgumentNullException(nameof(preview));
        Require(preview.Owner == owner, "The preview belongs to another model session. Preview again.");
        Require(!preview.Consumed, "This preview has already been applied. Preview again.");
        Require(preview.CanApply, "There are no changes to apply.");
        Require(handler.UndoManager.Enabled && handler.UndoManager.BatchDepth == 0 && !handler.UpdateInProgress, "Wait for the current editor operation to finish; undo must be enabled.");
        Require(semantic.Fingerprint() == preview.Fingerprint, "The model changed after this preview. Preview again before applying.");
        handler.BeginUpdate("PbiBench: " + preview.Action.Name);
        try
        {
            foreach (var edit in preview.Edits) edit.Apply();
            var invalid = preview.Edits.FirstOrDefault(edit => !edit.Validate());
            if (invalid != null) throw new InvalidOperationException("The model did not match the preview for " + invalid.Change.ObjectPath + " (" + invalid.Change.Property + "). All changes were rolled back.");
            handler.EndUpdate();
            preview.Consumed = true;
            return new ApplyResult(preview.Changes.Select(c => c.ObjectPath).Distinct().Count(), "Applied locally. Use Undo to restore; save/deploy remains a separate editor operation.");
        }
        catch
        {
            // A TE2 setter may leave a nested batch open if governance rejects the operation.
            if (handler.UndoManager.BatchDepth > 0) handler.EndUpdateAll(rollback: true);
            throw;
        }
    }

    public void Undo()
    {
        Require(handler.UndoManager.Enabled && handler.UndoManager.BatchDepth == 0, "Finish the current editor operation before Undo.");
        Require(handler.UndoManager.CanUndo, "There are no local changes to undo.");
        handler.UndoManager.Undo();
    }

    private static void AddProperty(List<PlannedEdit> edits, TabularNamedObject obj, string property, string? before, string? after, string reason, Action apply, Func<bool> validate)
    {
        if ((before ?? "") == (after ?? "")) return;
        edits.Add(new PlannedEdit(new ObjectChange(SemanticModelService.ObjectPath(obj), property, before ?? "", after ?? "", reason, obj), apply, validate));
    }
    private static bool IsNumeric(Column column) => column.DataType == DataType.Int64 || column.DataType == DataType.Double || column.DataType == DataType.Decimal;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string RequiredName(string value, string label) { Require(!string.IsNullOrWhiteSpace(value) && value.IndexOfAny(new[] { '\r', '\n', '\0' }) < 0, label + " is required and cannot contain control characters."); return value.Trim(); }
    private static string UniqueName(string basis, HashSet<string> names) { var result = basis; var suffix = 2; while (!names.Add(result)) result = basis + " " + suffix++; return result; }
}
