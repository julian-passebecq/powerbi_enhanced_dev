using System.Text.Json;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed record TableGroupEntry(Table Table, string? Group, string? Issue);

/// <summary>PbiBench virtual groups live in a versioned, undoable annotation on each table.</summary>
public sealed class TableGroupService
{
    public const string AnnotationName = "PbiBench.TableGroup";
    public const int MaximumAnnotationLength = 4096;
    public const int MaximumGroupLength = 256;
    private readonly TabularModelHandler handler;
    public TableGroupService(TabularModelHandler handler) => this.handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public IReadOnlyList<TableGroupEntry> Read() => handler.Model.Tables.Select(Read).ToArray();

    public static TableGroupEntry Read(Table table)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        var value = table.GetAnnotation(AnnotationName);
        if (value == null) return new(table, null, null);
        try
        {
            if (value.Length > MaximumAnnotationLength) throw new FormatException("The group annotation exceeds 4096 characters.");
            using var json = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 4 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
                throw new FormatException("The group annotation has an unsupported format or version.");
            if (root.EnumerateObject().Count() != 2 || !root.TryGetProperty("group", out var group) || group.ValueKind != JsonValueKind.String)
                throw new FormatException("The group annotation must contain version and group only.");
            var name = Normalize(group.GetString());
            if (name == null) throw new FormatException("The group annotation has an empty group.");
            return new(table, name, null);
        }
        catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is ArgumentException)
        { return new(table, null, ex.Message); }
    }

    public AuthoringPreview PreviewAssign(IEnumerable<Table> tables, string? group)
    {
        if (tables == null) throw new ArgumentNullException(nameof(tables));
        var name = Normalize(group);
        var issues = new List<AuthoringIssue>();
        var edits = new List<AuthoringEdit>();
        var after = name == null ? null : JsonSerializer.Serialize(new { version = 1, group = name });
        foreach (var table in tables.Distinct())
        {
            if (table == null || !handler.Model.Tables.Any(item => ReferenceEquals(item, table)))
                throw new ArgumentException("Every table must belong to the current model.", nameof(tables));
            var entry = Read(table);
            var path = SemanticModelService.ObjectPath(table);
            if (entry.Issue != null)
            {
                issues.Add(new("GROUP_FORMAT", entry.Issue + " Preserve the annotation and repair it explicitly in the Model editor before assigning a group.", AuthoringIssueSeverity.Error, path));
                continue;
            }
            var before = table.GetAnnotation(AnnotationName);
            if (before == after) continue;
            edits.Add(new(new(path, "Table group", entry.Group ?? "(ungrouped)", name ?? "(ungrouped)", "Virtual PbiBench group; table identity, display folders and relationships are unchanged."),
                () => { if (after == null) table.RemoveAnnotation(AnnotationName); else table.SetAnnotation(AnnotationName, after); },
                () => table.GetAnnotation(AnnotationName) == after));
        }
        return AuthoringPreview.Create(handler, name == null ? "Remove table group assignment" : "Assign table group: " + name, edits, issues);
    }

    public AuthoringPreview PreviewRename(string group, string newName)
    {
        var before = Normalize(group) ?? throw new ArgumentException("A group is required.", nameof(group));
        var after = Normalize(newName) ?? throw new ArgumentException("A new group name is required.", nameof(newName));
        return PreviewAssign(Read().Where(entry => entry.Group == before).Select(entry => entry.Table), after);
    }

    public AuthoringPreview PreviewRemove(string group)
    {
        var name = Normalize(group) ?? throw new ArgumentException("A group is required.", nameof(group));
        return PreviewAssign(Read().Where(entry => entry.Group == name).Select(entry => entry.Table), null);
    }

    private static string? Normalize(string? group)
    {
        var name = group?.Trim();
        if (name == null || name.Length == 0) return null;
        if (name.Length > MaximumGroupLength || name.Any(char.IsControl)) throw new ArgumentException("A group must be at most 256 characters and cannot contain control characters.");
        return name;
    }
}
