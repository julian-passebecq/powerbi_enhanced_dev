using System.Globalization;
using System.Text.Json;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.ModelAuthoring;

public enum TranslationProperty { Name, Description, DisplayFolder }
public sealed record TranslationCell(string ObjectId, string Culture, TranslationProperty Property, string? Value);
public sealed record TranslationMember(string Id, string Name, string Kind, string? Table, string Description, string? DisplayFolder);
public sealed record TranslationSnapshot(IReadOnlyList<string> Cultures, IReadOnlyList<TranslationMember> Members, IReadOnlyList<TranslationCell> Cells);
public sealed record TranslationPackage(int FormatVersion, IReadOnlyList<TranslationCell> Cells);

public sealed class TranslationEditorService(TabularModelHandler handler)
{
    public TranslationSnapshot Capture()
    {
        var cultures = handler.Model.Cultures.Select(culture => culture.Name).ToArray();
        var members = AuthoringObjects.All(handler).Where(obj => obj is ITranslatableObject).ToArray();
        var cells = members.SelectMany(obj => cultures.SelectMany(culture => Properties(obj).Select(property =>
        {
            var index = Index(obj, property); var value = index.Contains(handler.Model.Cultures[culture]) ? index[culture] : null;
            return new TranslationCell(AuthoringObjects.Id(obj), culture, property, value);
        }))).ToArray();
        return new(Array.AsReadOnly(cultures), Array.AsReadOnly(members.Select(obj => new TranslationMember(AuthoringObjects.Id(obj), obj.Name, obj.ObjectType.ToString(),
            (obj as ITabularTableObject)?.Table.Name, (obj as IDescriptionObject)?.Description ?? "", obj is IFolderObject folder ? folder.DisplayFolder ?? "" : null)).ToArray()), Array.AsReadOnly(cells));
    }
    public AuthoringPreview PreviewCells(IEnumerable<TranslationCell> requestedCells, bool overwriteExisting = true)
    {
        var requested = requestedCells.ToArray();
        if (requested.Length > 200000) throw new ArgumentException("A translation import supports at most 200,000 cells.");
        if (requested.GroupBy(cell => (cell.ObjectId, cell.Culture.ToLowerInvariant(), cell.Property)).Any(group => group.Count() > 1)) throw new ArgumentException("The translation request contains duplicate cells.");
        var edits = new List<AuthoringEdit>();
        foreach (var cultureName in requested.Select(cell => cell.Culture).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ValidateCulture(cultureName);
            if (handler.Model.Cultures.Contains(cultureName)) continue;
            edits.Add(new(new(cultureName, "Culture", "(absent)", cultureName, "Create the culture required by these translation cells."), () => handler.Model.AddTranslation(cultureName), () => handler.Model.Cultures.Contains(cultureName)));
        }
        foreach (var cell in requested)
        {
            var obj = AuthoringObjects.Resolve(handler, cell.ObjectId); var index = Index(obj, cell.Property);
            if (cell.Value?.Length > 100000) throw new ArgumentException("A translation value exceeds 100,000 characters.");
            var exists = handler.Model.Cultures.Contains(cell.Culture) && index.Contains(handler.Model.Cultures[cell.Culture]);
            var before = exists ? index[cell.Culture] : null;
            // The public TOM SetTranslation operation removes empty text for all properties.
            var after = string.IsNullOrEmpty(cell.Value) || (cell.Property == TranslationProperty.Name && string.IsNullOrWhiteSpace(cell.Value)) ? null : cell.Value;
            if (before == after || (!overwriteExisting && exists)) continue;
            edits.Add(new(new(cell.ObjectId, cell.Culture + " / " + cell.Property, Display(before), Display(after), after == null ? "Remove this translation and inherit the model value." : "Set only this cell; unspecified translations are preserved."),
                () => index[cell.Culture] = after!, () => after == null ? !index.Contains(handler.Model.Cultures[cell.Culture]) : index.Contains(handler.Model.Cultures[cell.Culture]) && index[cell.Culture] == after));
        }
        return AuthoringPreview.Create(handler, "Metadata translations", edits);
    }
    public AuthoringPreview PreviewCreateCulture(string culture)
    {
        ValidateCulture(culture);
        if (handler.Model.Cultures.Contains(culture)) throw new ArgumentException("That culture already exists.");
        return AuthoringPreview.Create(handler, "Create translation culture", new[] { new AuthoringEdit(new(culture, "Culture", "(absent)", culture, "Add a metadata translation language."), () => handler.Model.AddTranslation(culture), () => handler.Model.Cultures.Contains(culture)) });
    }
    public AuthoringPreview PreviewRenameCulture(string originalName, string name)
    {
        ValidateCulture(name); var culture = handler.Model.Cultures[originalName];
        if (handler.Model.Cultures.Any(item => item != culture && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("That culture already exists.");
        return AuthoringPreview.Create(handler, "Rename translation culture", name == originalName ? Array.Empty<AuthoringEdit>() : new[] { new AuthoringEdit(new(originalName, "Culture name", originalName, name, "Reassign this language tag while preserving its translation cells."), () => culture.Name = name, () => culture.Name == name) });
    }
    public AuthoringPreview PreviewDeleteCulture(string name)
    {
        var culture = handler.Model.Cultures[name];
        var cells = Capture().Cells.Where(cell => cell.Culture == name && cell.Value != null).ToArray();
        var indexes = cells.Select(cell => Index(AuthoringObjects.Resolve(handler, cell.ObjectId), cell.Property)).ToArray();
        var issues = culture.ObjectTranslations.Count == cells.Length ? Array.Empty<AuthoringIssue>() : new[] { new AuthoringIssue("UNSUPPORTED_TRANSLATION_OBJECT", "This culture contains translations outside the editor's supported object types. Delete those cells with the native editor before deleting this culture.", AuthoringIssueSeverity.Error) };
        return AuthoringPreview.Create(handler, "Delete translation culture", new[] { new AuthoringEdit(new(name, "Culture and translations", name + "\n" + string.Join("\n", cells.Select(cell => cell.ObjectId + " / " + cell.Property + " = " + cell.Value)), "(removed)", "Delete all translations in this culture; Undo restores them."),
            () => { foreach (var index in indexes) index[name] = null!; culture.Delete(); }, () => !handler.Model.Cultures.Contains(name)) }, issues);
    }
    public string ExportJson(IEnumerable<string>? cultures = null)
    {
        var snapshot = Capture(); var selected = cultures?.ToArray() ?? snapshot.Cultures.ToArray();
        if (selected.Any(name => !snapshot.Cultures.Contains(name))) throw new ArgumentException("An export culture no longer exists.");
        return JsonSerializer.Serialize(new TranslationPackage(1, snapshot.Cells.Where(cell => selected.Contains(cell.Culture)).ToArray()), new JsonSerializerOptions { WriteIndented = true });
    }
    public AuthoringPreview PreviewImportJson(string json, bool overwriteExisting = false)
    {
        if (json == null || json.Length > 16 * 1024 * 1024) throw new ArgumentException("Choose a PbiBench translation JSON file smaller than 16 MB.");
        var package = JsonSerializer.Deserialize<TranslationPackage>(json) ?? throw new ArgumentException("The translation file is empty.");
        if (package.FormatVersion != 1 || package.Cells == null || package.Cells.Any(cell => cell == null)) throw new ArgumentException("Unsupported PbiBench translation file format.");
        return PreviewCells(package.Cells, overwriteExisting);
    }
    private static IEnumerable<TranslationProperty> Properties(TabularNamedObject obj) => obj is IFolderObject ? new[] { TranslationProperty.Name, TranslationProperty.Description, TranslationProperty.DisplayFolder } : new[] { TranslationProperty.Name, TranslationProperty.Description };
    private static TranslationIndexer Index(TabularNamedObject obj, TranslationProperty property)
    {
        if (obj is not ITranslatableObject translated) throw new ArgumentException("This object cannot be translated: " + AuthoringObjects.Id(obj));
        return property switch
        {
            TranslationProperty.Name => translated.TranslatedNames,
            TranslationProperty.Description => translated.TranslatedDescriptions,
            TranslationProperty.DisplayFolder when obj is IFolderObject folder => folder.TranslatedDisplayFolders,
            _ => throw new ArgumentException("This translation property is not supported by the selected object.")
        };
    }
    private static void ValidateCulture(string name)
    {
        AuthoringObjects.Name(name);
        var culture = CultureInfo.GetCultureInfo(name);
        if (string.IsNullOrEmpty(culture.Name) || culture.IsNeutralCulture || !culture.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use a specific recognized culture such as de-CH or fr-FR.");
    }
    private static string Display(string? value) => value == null ? "(inherit model value)" : value.Length == 0 ? "(empty text)" : value;
}
