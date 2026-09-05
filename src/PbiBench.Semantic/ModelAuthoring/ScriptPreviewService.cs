using PbiBench.Core.Automation;
using PbiBench.Dax.LanguageService;
using TabularEditor.TOMWrapper;
using TOM = Microsoft.AnalysisServices.Tabular;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed class PreparedScriptPreview
{
    internal PreparedScriptPreview(Guid owner, string fingerprint, string json, ActionRecipe recipe, IReadOnlyList<string> selected, Dictionary<string, TabularNamedObject> native)
    { Owner = owner; Fingerprint = fingerprint; Json = json; Recipe = recipe; Selected = selected; Native = native; }
    internal Guid Owner { get; }
    internal string Fingerprint { get; }
    internal string Json { get; }
    internal ActionRecipe Recipe { get; }
    internal IReadOnlyList<string> Selected { get; }
    internal Dictionary<string, TabularNamedObject> Native { get; }
}
public sealed class ComputedScriptPreview
{
    internal ComputedScriptPreview(PreparedScriptPreview input, IReadOnlyList<ScriptDelta> changes) { Input = input; Changes = changes; }
    internal PreparedScriptPreview Input { get; }
    internal IReadOnlyList<ScriptDelta> Changes { get; }
    public int ChangeCount => Changes.Count;
}
internal sealed record ScriptDelta(string Id, RecipeTarget OriginalTarget, RecipeTarget FinalTarget, string Property, string Before, string After, IReadOnlyDictionary<string, string>? Created = null);

/// <summary>Strict interpreted edits on a detached TOM database. Live wrappers are touched only while capturing and materializing on the model-owning thread.</summary>
public sealed class ScriptPreviewService
{
    private readonly TabularModelHandler handler;
    private readonly Guid owner = Guid.NewGuid();
    public ScriptPreviewService(TabularModelHandler handler) => this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
    public PreparedScriptPreview PrepareScript(string source, IReadOnlyList<TabularNamedObject> selection)
    {
        var parsed = SafeCSharpParser.Parse(source);
        if (!parsed.IsValid) throw new ArgumentException(string.Join("\n", parsed.Issues.Select(issue => "Offset " + issue.Offset + ": " + issue.Message)));
        return PrepareRecipe(parsed.Recipe!, selection);
    }
    public PreparedScriptPreview PrepareRecipe(ActionRecipe recipe, IReadOnlyList<TabularNamedObject> selection)
    {
        ActionRecipeRules.Validate(recipe);
        if (selection.Any(item => !ReferenceEquals(item.Model, handler.Model))) throw new ArgumentException("The selection belongs to another model.");
        // Freeze every recipe collection before a background computation can begin.
        var frozen = recipe with { Steps = recipe.Steps.Select(step => step with { Value = Freeze(step.Value), Expression = step.Expression == null ? null : Freeze(step.Expression), DisplayFolder = step.DisplayFolder == null ? null : Freeze(step.DisplayFolder) }).ToArray() };
        var native = NativeObjects(handler).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var selected = selection.Where(item => item is Table or Column or Measure).Select(Target).Select(Key).ToArray();
        var json = TOM.JsonSerializer.SerializeDatabase(handler.Database);
        if (json.Length > 64 * 1024 * 1024) throw new InvalidOperationException("Safe Preview metadata is limited to 64 MB.");
        return new PreparedScriptPreview(owner, new SemanticModelService(handler).Fingerprint(), json, frozen, selected, native);
    }
    public Task<ComputedScriptPreview> ComputeAsync(PreparedScriptPreview prepared, CancellationToken ct) => Task.FromResult(Compute(prepared, ct));
    public ComputedScriptPreview Compute(PreparedScriptPreview prepared, CancellationToken ct = default)
    {
        if (prepared.Owner != owner) throw new InvalidOperationException("This prepared script belongs to another preview service.");
        ct.ThrowIfCancellationRequested();
        var database = TOM.JsonSerializer.DeserializeDatabase(prepared.Json);
        var work = new DetachedScriptModel(database);
        var before = work.Objects.ToDictionary(item => item.Id, item => item.Values(), StringComparer.Ordinal);
        var targets = work.Objects.ToDictionary(item => item.Id, item => item.Target, StringComparer.Ordinal);
        var originalExpressions = work.Expressions.ToDictionary(item => item.Id, item => item.Get(), StringComparer.Ordinal);
        var selected = new HashSet<string>(prepared.Selected, StringComparer.Ordinal); var operations = 0;
        foreach (var step in prepared.Recipe.Steps)
        {
            ct.ThrowIfCancellationRequested(); var objects = work.Resolve(step.Target, selected).ToArray();
            if (objects.Length == 0) throw new InvalidOperationException("The recipe target has no objects: " + step.Target.Scope + ". Explicit selection is required for Selected scopes.");
            foreach (var item in objects)
            {
                ct.ThrowIfCancellationRequested(); if (++operations > 20000) throw new InvalidOperationException("Safe Preview is limited to 20,000 object operations.");
                var value = step.Value.Evaluate(item.Name, item.TableName);
                switch (step.Operation)
                {
                    case RecipeOperation.SetProperty: work.Set(item, step.Property, value, ct); break;
                    case RecipeOperation.CreateMeasure:
                        if (item.Kind != RecipeScope.Table) throw new InvalidOperationException("AddMeasure is supported only on a table target.");
                        work.CreateMeasure(item, value, step.Expression!.Evaluate(item.Name, item.TableName), step.DisplayFolder?.Evaluate(item.Name, item.TableName) ?? ""); break;
                    case RecipeOperation.DeleteMeasure:
                        if (item.Kind != RecipeScope.Measure) throw new InvalidOperationException("Safe deletion currently supports measures only. Other objects remain available through reviewed native modeling tools.");
                        work.DeleteMeasure(item, ct); break;
                    default: throw new InvalidOperationException("Unsupported recipe operation.");
                }
            }
        }
        var changes = new List<ScriptDelta>();
        foreach (var item in work.Objects)
        {
            ct.ThrowIfCancellationRequested();
            if (!before.TryGetValue(item.Id, out var old))
            { if (!item.Deleted) changes.Add(new ScriptDelta(item.Id, item.Target, item.Target, "New measure", "(absent)", Describe(item.Values()), item.Values())); continue; }
            if (item.Deleted) { changes.Add(new ScriptDelta(item.Id, targets[item.Id], item.Target, "Delete measure", Describe(old), "(deleted)")); continue; }
            foreach (var property in item.Values()) if (old[property.Key] != property.Value) changes.Add(new ScriptDelta(item.Id, targets[item.Id], item.Target, property.Key, old[property.Key], property.Value));
        }
        // Auxiliary DAX metadata participates in a rename diff even though it is not directly writable by the subset.
        foreach (var expression in work.Expressions.Where(expression => expression.Auxiliary))
            if (originalExpressions.TryGetValue(expression.Id, out var old) && old != expression.Get()) changes.Add(new ScriptDelta(expression.Id, expression.Target, expression.Target, expression.Property, old, expression.Get()));
        return new ComputedScriptPreview(prepared, changes);
    }
    public AuthoringPreview Materialize(ComputedScriptPreview computed)
    {
        var input = computed.Input;
        if (input.Owner != owner || new SemanticModelService(handler).Fingerprint() != input.Fingerprint) throw new InvalidOperationException("The model changed while the detached preview was being computed. Preview again.");
        var edits = new List<AuthoringEdit>();
        foreach (var delta in computed.Changes.OrderBy(change => change.Property == "Name" ? 0 : change.Property is "New measure" or "Annotation:Format" ? 1 : change.Property == "Delete measure" ? 3 : 2))
        {
            var path = Display(delta.OriginalTarget); var row = new AuthoringChange(path, delta.Property, delta.Before, delta.After, "Interpreted on detached metadata; this exact local change is reviewed before native apply.");
            if (delta.Created != null)
            {
                var table = handler.Model.Tables.FirstOrDefault(item => item.Name == delta.FinalTarget.Table) ?? input.Native.Values.OfType<Table>().FirstOrDefault(item => computed.Changes.Any(change => change.Id == Key(Target(item)) && change.Property == "Name" && change.After == delta.FinalTarget.Table));
                if (table == null) throw new InvalidOperationException("The destination table changed.");
                Measure? created = null; var values = delta.Created;
                edits.Add(new AuthoringEdit(row, () => { created = table.AddMeasure(values["Name"], values["Expression"], values["DisplayFolder"]); foreach (var value in values.Where(pair => pair.Key is not ("Name" or "Expression" or "DisplayFolder"))) SetNative(created, value.Key, value.Value); }, () => created != null && values.All(value => NativeValue(created, value.Key) == value.Value)));
            }
            else if (delta.Id.StartsWith("aux:", StringComparison.Ordinal))
            {
                var auxiliary = AuxiliaryNative(handler).FirstOrDefault(pair => pair.Id == delta.Id) ?? throw new InvalidOperationException("An auxiliary DAX object no longer exists.");
                edits.Add(new AuthoringEdit(row, () => auxiliary.Set(delta.After), () => auxiliary.Get() == delta.After));
            }
            else
            {
                if (!input.Native.TryGetValue(delta.Id, out var target)) throw new InvalidOperationException("A recipe object no longer exists.");
                if (delta.Property == "Name" && target is Column column && handler.Model.AllColumns.OfType<CalculatedTableColumn>().Any(candidate => (candidate.SourceColumn ?? "").Contains("[" + column.Name + "]"))) throw new InvalidOperationException("A calculated-table column may depend on this column's source name. Rename it through the native editor so inferred source/name changes remain available.");
                if (delta.Property == "Delete measure") { var measure = (Measure)target; var table = measure.Table; edits.Add(new AuthoringEdit(row, () => measure.Delete(), () => !table.Measures.Contains(measure))); }
                else edits.Add(new AuthoringEdit(row, () => SetNative(target, delta.Property, delta.After), () => NativeValue(target, delta.Property) == delta.After));
            }
        }
        return AuthoringPreview.Create(handler, input.Recipe.Name, edits, computed.ChangeCount == 0 ? new[] { new AuthoringIssue("SCRIPT_NO_CHANGES", "The detached model matched the current model. No changes to apply.", AuthoringIssueSeverity.Information) } : null);
    }
    public AuthoringPreview PreviewScript(string source, IReadOnlyList<TabularNamedObject> selection) => Materialize(Compute(PrepareScript(source, selection)));
    public AuthoringPreview PreviewRecipe(ActionRecipe recipe, IReadOnlyList<TabularNamedObject> selection) => Materialize(Compute(PrepareRecipe(recipe, selection)));
    internal static RecipeTarget Target(TabularNamedObject obj) => obj switch
    { Measure measure => new(RecipeScope.Measure, measure.Table.Name, measure.Name), Column column => new(RecipeScope.Column, column.Table.Name, column.Name), Table table => new(RecipeScope.Table, null, table.Name), _ => throw new ArgumentException("Only tables, columns and measures are supported recipe targets.") };
    internal static string Key(RecipeTarget target) => target.Scope + ":" + (target.Table?.Length ?? -1) + ":" + target.Table + ":" + (target.Name?.Length ?? -1) + ":" + target.Name;
    internal static string Display(RecipeTarget target) => target.Scope == RecipeScope.Table ? DaxSymbol.QuoteTable(target.Name ?? "") : DaxSymbol.QuoteTable(target.Table ?? "") + DaxSymbol.QuoteMember(target.Name ?? "");
    internal static IEnumerable<KeyValuePair<string, TabularNamedObject>> NativeObjects(TabularModelHandler handler) => handler.Model.Tables.SelectMany(table => new TabularNamedObject[] { table }.Concat(table.Columns).Concat(table.Measures)).Select(item => new KeyValuePair<string, TabularNamedObject>(Key(Target(item)), item));
    private static RecipeValue Freeze(RecipeValue value) => value with { Parts = value.Parts.ToArray() };
    internal static string Describe(IReadOnlyDictionary<string, string> values) => string.Join("\n", values.Select(pair => pair.Key + ": " + pair.Value));
    internal static string NativeValue(TabularNamedObject obj, string property) => property switch
    {
        "Name" => obj.Name, "Description" => ((IDescriptionObject)obj).Description ?? "", "IsHidden" => ((IHideableObject)obj).IsHidden ? "true" : "false",
        "DisplayFolder" => ((IFolderObject)obj).DisplayFolder ?? "", "Expression" => ((IExpressionObject)obj).Expression ?? "",
        "FormatString" => obj is Measure measure ? measure.FormatString ?? "" : ((Column)obj).FormatString ?? "",
        "FormatStringExpression" => ((Measure)obj).FormatStringExpression ?? "", "SummarizeBy" => ((Column)obj).SummarizeBy.ToString(),
        "Annotation:Format" => System.Text.Json.JsonSerializer.Serialize(((IAnnotationObject)obj).GetAnnotation("Format")), _ => throw new InvalidOperationException("Unsupported property.")
    };
    private void SetNative(TabularNamedObject obj, string property, string value)
    {
        switch (property)
        {
            case "Name": var fixup = handler.Settings.AutoFixup; try { handler.Settings.AutoFixup = false; obj.Name = value; } finally { handler.Settings.AutoFixup = fixup; } break;
            case "Description": ((IDescriptionObject)obj).Description = value; break;
            case "IsHidden": ((IHideableObject)obj).IsHidden = bool.Parse(value); break;
            case "DisplayFolder": ((IFolderObject)obj).DisplayFolder = value; break;
            case "Expression": ((IExpressionObject)obj).Expression = value; break;
            case "FormatString": if (obj is Measure measure) measure.FormatString = value; else ((Column)obj).FormatString = value; break;
            case "FormatStringExpression": ((Measure)obj).FormatStringExpression = value; break;
            case "SummarizeBy": ((Column)obj).SummarizeBy = (AggregateFunction)Enum.Parse(typeof(AggregateFunction), value); break;
            case "Annotation:Format": if (value != "null") throw new InvalidOperationException("Safe Preview does not assign arbitrary annotations."); ((IAnnotationObject)obj).RemoveAnnotation("Format"); break;
            default: throw new InvalidOperationException("Unsupported property.");
        }
    }
    internal sealed record AuxiliaryExpression(string Id, RecipeTarget Target, Func<string> Get, Action<string> Set);
    internal static IEnumerable<AuxiliaryExpression> AuxiliaryNative(TabularModelHandler handler)
    {
        foreach (var table in handler.Model.Tables)
        {
            yield return new("aux:table-detail:" + table.Name, new(RecipeScope.Table, null, table.Name), () => table.DefaultDetailRowsExpression ?? "", value => table.DefaultDetailRowsExpression = value);
            foreach (var measure in table.Measures) yield return new("aux:measure-detail:" + table.Name + ":" + measure.Name, new(RecipeScope.Measure, table.Name, measure.Name), () => measure.DetailRowsExpression ?? "", value => measure.DetailRowsExpression = value);
            if (table is CalculatedTable calculated) yield return new("aux:table:" + table.Name, new(RecipeScope.Table, null, table.Name), () => calculated.Expression ?? "", value => calculated.Expression = value);
            if (table is CalculationGroupTable group) foreach (var item in group.CalculationItems)
            {
                yield return new("aux:item:" + table.Name + ":" + item.Name, new(RecipeScope.Measure, table.Name, item.Name), () => item.Expression ?? "", value => item.Expression = value);
                yield return new("aux:item-format:" + table.Name + ":" + item.Name, new(RecipeScope.Measure, table.Name, item.Name), () => item.FormatStringExpression ?? "", value => item.FormatStringExpression = value);
            }
        }
        foreach (var function in handler.Model.Functions) yield return new("aux:function:" + function.Name, new(RecipeScope.Measure, "Functions", function.Name), () => function.Expression ?? "", value => function.Expression = value);
        foreach (var role in handler.Model.Roles) foreach (var permission in role.TablePermissions) yield return new("aux:role:" + role.Name + ":" + permission.Table.Name, new(RecipeScope.Table, role.Name, permission.Table.Name), () => permission.FilterExpression ?? "", value => permission.FilterExpression = value);
    }
}
