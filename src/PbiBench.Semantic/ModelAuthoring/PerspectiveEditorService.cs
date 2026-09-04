using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed record PerspectiveDefinition(string Name, string Description);
public sealed record PerspectiveMember(string Id, string Name, string Kind, string? Table, bool IsHidden, IReadOnlyDictionary<string, bool?> Membership);
public sealed record PerspectiveSnapshot(IReadOnlyList<PerspectiveDefinition> Perspectives, IReadOnlyList<PerspectiveMember> Members);
public sealed record PerspectiveMembershipChange(string ObjectId, string Perspective, bool Included);

public sealed class PerspectiveEditorService(TabularModelHandler handler)
{
    public PerspectiveSnapshot Capture()
    {
        var perspectives = handler.Model.Perspectives.ToArray();
        var members = AuthoringObjects.All(handler).Where(obj => obj is ITabularPerspectiveObject).Select(obj =>
        {
            var member = (ITabularPerspectiveObject)obj;
            var memberships = perspectives.ToDictionary(p => p.Name, p => obj is Table table ? TableState(table, p) : (bool?)member.InPerspective[p]);
            return new PerspectiveMember(AuthoringObjects.Id(obj), obj.Name, obj.ObjectType.ToString(), (obj as ITabularTableObject)?.Table.Name, member.IsHidden,
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, bool?>(memberships));
        }).ToArray();
        return new(Array.AsReadOnly(perspectives.Select(p => new PerspectiveDefinition(p.Name, p.Description ?? "")).ToArray()), Array.AsReadOnly(members));
    }
    public AuthoringPreview PreviewMembership(IEnumerable<PerspectiveMembershipChange> requests)
    {
        var edits = new List<AuthoringEdit>(); var issues = new List<AuthoringIssue>();
        var expanded = new Dictionary<(string Id, string Perspective), bool>();
        foreach (var request in requests)
        {
            var obj = AuthoringObjects.Resolve(handler, request.ObjectId);
            if (!handler.Model.Perspectives.Contains(request.Perspective)) throw new ArgumentException("Unknown perspective: " + request.Perspective);
            var members = obj is Table table ? Children(table).Cast<TabularNamedObject>() : new[] { obj };
            if (!members.Any()) issues.Add(new("EMPTY_TABLE", "This table has no selectable fields.", AuthoringIssueSeverity.Warning, request.ObjectId));
            foreach (var member in members)
            {
                if (member is not ITabularPerspectiveObject) throw new ArgumentException("Object does not support perspective membership: " + request.ObjectId);
                expanded[(AuthoringObjects.Id(member), request.Perspective)] = request.Included;
            }
        }
        foreach (var item in expanded)
        {
            var obj = AuthoringObjects.Resolve(handler, item.Key.Id); var member = (ITabularPerspectiveObject)obj;
            var perspective = handler.Model.Perspectives[item.Key.Perspective]; var before = member.InPerspective[perspective]; var after = item.Value;
            if (before == after) continue;
            edits.Add(new(new(item.Key.Id, "Perspective: " + perspective.Name, before.ToString(), after.ToString(), "Change field membership; table controls expand to individual fields."),
                () => member.InPerspective[perspective] = after, () => member.InPerspective[perspective] == after));
            if (after && member.IsHidden) issues.Add(new("HIDDEN_MEMBER", "A hidden field remains hidden in client tools even when included in a perspective.", AuthoringIssueSeverity.Warning, item.Key.Id));
        }
        return AuthoringPreview.Create(handler, "Perspective membership", edits, issues);
    }
    public AuthoringPreview PreviewCreate(string name)
    {
        AuthoringObjects.Name(name);
        if (handler.Model.Perspectives.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("That perspective already exists.");
        return AuthoringPreview.Create(handler, "Create perspective", new[] { new AuthoringEdit(new(name, "Perspective", "(absent)", name, "Create an empty perspective, then assign fields."),
            () => handler.Model.AddPerspective(name), () => handler.Model.Perspectives.Contains(name)) }, new[] { new AuthoringIssue("EMPTY_PERSPECTIVE", "Assign fields before using this perspective in a client.", AuthoringIssueSeverity.Information) });
    }
    public AuthoringPreview PreviewRename(string originalName, string name)
    {
        AuthoringObjects.Name(name); var perspective = handler.Model.Perspectives[originalName];
        if (handler.Model.Perspectives.Any(p => p != perspective && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("That perspective already exists.");
        return AuthoringPreview.Create(handler, "Rename perspective", name == originalName ? Array.Empty<AuthoringEdit>() : new[] { new AuthoringEdit(new(originalName, "Name", originalName, name, "Preserve membership and translations."), () => perspective.Name = name, () => perspective.Name == name) });
    }
    public AuthoringPreview PreviewDelete(string name)
    {
        var perspective = handler.Model.Perspectives[name];
        var fields = AuthoringObjects.All(handler).Where(obj => obj is ITabularPerspectiveObject member && member.InPerspective[perspective]).Select(AuthoringObjects.Id).ToArray();
        return AuthoringPreview.Create(handler, "Delete perspective", new[] { new AuthoringEdit(new(name, "Perspective and membership", name + "\n" + string.Join("\n", fields), "(removed)", "Remove this perspective; model fields remain available."),
            () => perspective.Delete(), () => !handler.Model.Perspectives.Contains(name)) });
    }
    private static IEnumerable<ITabularPerspectiveObject> Children(Table table) => table.Columns.Cast<ITabularPerspectiveObject>().Concat(table.Measures).Concat(table.Hierarchies);
    private static bool? TableState(Table table, Perspective perspective)
    {
        var values = Children(table).Select(child => child.InPerspective[perspective]).ToArray();
        return values.Length == 0 ? table.InPerspective[perspective] : values.All(value => value) ? true : values.All(value => !value) ? false : null;
    }
}
