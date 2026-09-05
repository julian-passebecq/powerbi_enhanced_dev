using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed record SemanticAnnotationRequest(TabularNamedObject Object, string Name, string Value);
public sealed class SemanticAnnotationService(TabularModelHandler handler)
{
    public AuthoringPreview Preview(IEnumerable<SemanticAnnotationRequest> requests)
    {
        var rows = requests.Take(201).ToArray();
        if (rows.Length is < 1 or > 200) throw new ArgumentException("Select 1–200 annotation changes.");
        var edits = new List<AuthoringEdit>(); var keys = new HashSet<string>();
        foreach (var row in rows)
        {
            if (!ReferenceEquals(row.Object.Model, handler.Model) || row.Object is not IAnnotationObject target) throw new ArgumentException("Annotation target must belong to the current model.");
            if (string.IsNullOrWhiteSpace(row.Name) || row.Name.Length > 128 || row.Name.Any(char.IsControl) || row.Value.Length > 4096 || row.Value.Any(c => char.IsControl(c) && c != '\n')) throw new ArgumentException("Invalid annotation name or value.");
            var path = SemanticModelService.ObjectPath(row.Object);
            if (!keys.Add(path + "\0" + row.Name)) throw new ArgumentException("Duplicate annotation target.");
            var before = target.GetAnnotation(row.Name); if (before == row.Value) continue;
            edits.Add(new(new(path, "Annotation: " + row.Name, before ?? "(absent)", row.Value, "Metadata annotation only; one local Undo batch."),
                () => target.SetAnnotation(row.Name, row.Value), () => target.GetAnnotation(row.Name) == row.Value));
        }
        return AuthoringPreview.Create(handler, "Set reviewed annotations", edits);
    }
}
