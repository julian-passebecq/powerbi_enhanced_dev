using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using PbiBench.Core.Automation;
using TabularEditor.TOMWrapper;
using TOM = Microsoft.AnalysisServices.Tabular;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed record RecordedActionRecipe(ActionRecipe Recipe, IReadOnlyList<string> Notices);
public sealed class PreparedActionRecording
{
    internal PreparedActionRecording(string name, RecordingSnapshot before, RecordingSnapshot after) { Name = name; Before = before; After = after; }
    internal string Name { get; }
    internal RecordingSnapshot Before { get; }
    internal RecordingSnapshot After { get; }
}
internal sealed record RecordedObject(string Id, RecipeTarget Target, IReadOnlyDictionary<string, string> Values, string OtherMetadata);
internal sealed record RecordingSnapshot(IReadOnlyDictionary<string, RecordedObject> Objects, string OtherMetadata);

/// <summary>Records supported model changes between explicit checkpoints, using wrapper identity rather than UI gestures.</summary>
public sealed class ActionRecorder
{
    private TabularModelHandler? owner;
    private RecordingSnapshot? before;
    private readonly Dictionary<TabularNamedObject, string> identities = new(new ObjectIdentityComparer());
    public bool IsRecording => before != null;
    public void Start(TabularModelHandler handler)
    {
        if (IsRecording) throw new InvalidOperationException("Stop or discard the active recording first.");
        identities.Clear(); owner = handler; before = Capture(handler);
    }
    public void Discard() { before = null; owner = null; identities.Clear(); }
    public PreparedActionRecording PrepareStop(TabularModelHandler handler, string name)
    {
        if (!ReferenceEquals(owner, handler) || before == null) throw new InvalidOperationException("There is no recording for this model session.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) throw new ArgumentException("Enter a recipe name up to 128 characters.");
        var prepared = new PreparedActionRecording(name, before, Capture(handler)); Discard(); return prepared;
    }
    public RecordedActionRecipe Stop(TabularModelHandler handler, string name) => Compute(PrepareStop(handler, name));
    public static Task<RecordedActionRecipe> ComputeAsync(PreparedActionRecording prepared, CancellationToken ct) => Task.FromResult(Compute(prepared, ct));
    public static RecordedActionRecipe Compute(PreparedActionRecording prepared, CancellationToken ct = default)
    {
        var steps = new List<RecipeStep>(); var notices = new List<string>();
        var beforeJson = JsonNode.Parse(prepared.Before.OtherMetadata)!; var afterJson = JsonNode.Parse(prepared.After.OtherMetadata)!;
        if (OtherRoot(beforeJson) != OtherRoot(afterJson)) notices.Add("Other model metadata changed (for example relationships, roles, cultures or connection properties); those changes are not part of this recipe.");
        foreach (var old in prepared.Before.Objects.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (!prepared.After.Objects.TryGetValue(old.Id, out var current))
            {
                if (old.Target.Scope == RecipeScope.Measure) steps.Add(new(old.Target, RecipeOperation.DeleteMeasure, "", RecipeValue.Literal("")));
                else notices.Add("Deletion of " + ScriptPreviewService.Display(old.Target) + " is outside the current recipe subset.");
                continue;
            }
            var target = old.Target;
            if (target.Scope != RecipeScope.Table) target = target with { Table = current.Target.Table };
            if (OtherObject(beforeJson, old.Target) != OtherObject(afterJson, current.Target)) notices.Add("Unsupported metadata changed on " + ScriptPreviewService.Display(current.Target) + "; review it separately.");
            if (old.Values["Name"] != current.Values["Name"])
            { steps.Add(new(target, RecipeOperation.SetProperty, "Name", RecipeValue.Literal(current.Values["Name"]))); target = target with { Name = current.Values["Name"] }; }
            // Parent renames run before child changes; target the parent's resulting name.
            if (target.Scope != RecipeScope.Table) target = target with { Table = current.Target.Table };
            foreach (var value in current.Values.Where(pair => pair.Key != "Name" && ActionRecipeRules.Properties.Contains(pair.Key)))
                if (old.Values.TryGetValue(value.Key, out var oldValue) && oldValue != value.Value) steps.Add(new(target, RecipeOperation.SetProperty, value.Key, RecipeValue.Literal(value.Value)));
        }
        foreach (var current in prepared.After.Objects.Values.Where(item => !prepared.Before.Objects.ContainsKey(item.Id)))
        {
            ct.ThrowIfCancellationRequested();
            if (current.Target.Scope != RecipeScope.Measure) { notices.Add("Creation of " + ScriptPreviewService.Display(current.Target) + " is outside the current recipe subset."); continue; }
            steps.Add(new(new(RecipeScope.Table, null, current.Target.Table), RecipeOperation.CreateMeasure, "", RecipeValue.Literal(current.Values["Name"]), RecipeValue.Literal(current.Values["Expression"]), RecipeValue.Literal(current.Values["DisplayFolder"])));
            foreach (var property in current.Values.Where(pair => ActionRecipeRules.Properties.Contains(pair.Key) && pair.Key is not ("Name" or "Expression" or "DisplayFolder") && pair.Value != "" && pair.Value != "false")) steps.Add(new(current.Target, RecipeOperation.SetProperty, property.Key, RecipeValue.Literal(property.Value)));
            if (OtherObject(afterJson, current.Target) != "{}") notices.Add("New measure " + ScriptPreviewService.Display(current.Target) + " includes metadata outside the recipe subset.");
        }
        // Explicit object operations, not UI events; deletions follow changes that remove their callers.
        var ordered = steps.OrderBy(step => step.Target.Scope == RecipeScope.Table && step.Property == "Name" ? 0 : step.Operation == RecipeOperation.DeleteMeasure ? 3 : 1).ToArray();
        if (ordered.Length == 0) notices.Add("No supported model changes were recorded.");
        var recipe = new ActionRecipe(prepared.Name, ordered); ActionRecipeRules.Validate(recipe); return new(recipe, notices.Distinct().ToArray());
    }
    private RecordingSnapshot Capture(TabularModelHandler handler)
    {
        var json = TOM.JsonSerializer.SerializeDatabase(handler.Database); var result = new Dictionary<string, RecordedObject>(StringComparer.Ordinal);
        foreach (var pair in ScriptPreviewService.NativeObjects(handler))
        {
            if (!identities.TryGetValue(pair.Value, out var id)) identities[pair.Value] = id = Guid.NewGuid().ToString("N");
            var properties = new List<string> { "Name", "Description", "IsHidden" };
            if (pair.Value is Measure) properties.AddRange(new[] { "DisplayFolder", "Expression", "FormatString", "FormatStringExpression" });
            if (pair.Value is Column) { properties.AddRange(new[] { "DisplayFolder", "FormatString", "SummarizeBy" }); if (pair.Value is CalculatedColumn) properties.Add("Expression"); }
            result[id] = new(id, ScriptPreviewService.Target(pair.Value), properties.ToDictionary(property => property, property => ScriptPreviewService.NativeValue(pair.Value, property)), "");
        }
        return new RecordingSnapshot(result, json);
    }
    private static string OtherRoot(JsonNode root) { var copy = JsonNode.Parse(root.ToJsonString())!.AsObject(); if (copy["model"] is JsonObject model) model.Remove("tables"); return copy.ToJsonString(); }
    private static string OtherObject(JsonNode root, RecipeTarget target)
    {
        var tableName = target.Scope == RecipeScope.Table ? target.Name : target.Table;
        var table = root["model"]?["tables"]?.AsArray().FirstOrDefault(item => item?["name"]?.GetValue<string>() == tableName);
        var node = target.Scope == RecipeScope.Table ? table : table?[target.Scope == RecipeScope.Measure ? "measures" : "columns"]?.AsArray().FirstOrDefault(item => item?["name"]?.GetValue<string>() == target.Name);
        if (node == null) return "{}"; var copy = JsonNode.Parse(node.ToJsonString())!.AsObject();
        foreach (var property in ActionRecipeRules.Properties) copy.Remove(char.ToLowerInvariant(property[0]) + property.Substring(1));
        copy.Remove("lineageTag"); copy.Remove("sourceLineageTag"); copy.Remove("formatStringDefinition");
        if (target.Scope == RecipeScope.Table) { copy.Remove("measures"); copy.Remove("columns"); }
        if (copy["annotations"] is JsonArray annotations)
        { foreach (var annotation in annotations.Where(item => item?["name"]?.GetValue<string>() == "Format").ToArray()) annotations.Remove(annotation); if (annotations.Count == 0) copy.Remove("annotations"); }
        return copy.ToJsonString();
    }
    private sealed class ObjectIdentityComparer : IEqualityComparer<TabularNamedObject>
    { public bool Equals(TabularNamedObject? left, TabularNamedObject? right) => ReferenceEquals(left, right); public int GetHashCode(TabularNamedObject obj) => RuntimeHelpers.GetHashCode(obj); }
}
