using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PbiBench.Core.Workspaces;

public sealed record WorkspaceProperty(string Key, string ObjectPath, string Property, string Value);
public sealed class WorkspaceSemanticSnapshot
{
    private WorkspaceSemanticSnapshot(string json, IReadOnlyDictionary<string, WorkspaceProperty> properties)
    { DatabaseJson = json; Properties = properties; Hash = HashText(string.Join("\n", properties.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => JsonSerializer.Serialize(new[] { pair.Key, pair.Value.Value })))); }
    public string DatabaseJson { get; }
    public string Hash { get; }
    public IReadOnlyDictionary<string, WorkspaceProperty> Properties { get; }
    public static WorkspaceSemanticSnapshot Parse(string databaseJson)
    {
        if (databaseJson == null || databaseJson.Length > 64 * 1024 * 1024) throw new ArgumentException("Workspace metadata is limited to 64 MiB characters.");
        using var json = JsonDocument.Parse(databaseJson, new JsonDocumentOptions { MaxDepth = 100 });
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object) throw new ArgumentException("A database definition containing a model is required.");
        var properties = new Dictionary<string, WorkspaceProperty>(StringComparer.Ordinal);
        Walk(model, "model", "Model", properties);
        if (json.RootElement.TryGetProperty("compatibilityLevel", out var compatibility)) properties.Add("compatibilityLevel", new("compatibilityLevel", "Database", "compatibilityLevel", Canonical(compatibility)));
        return new(databaseJson, new ReadOnlyDictionary<string, WorkspaceProperty>(properties));
    }
    private static void Walk(JsonElement value, string key, string path, Dictionary<string, WorkspaceProperty> result)
    {
        Add("$exists", "object");
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0 && property.Value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String))
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in property.Value.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString()!;
                    var identity = item.TryGetProperty("lineageTag", out var tag) && tag.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tag.GetString()) ? "lineage:" + tag.GetString() : "name:" + name;
                    if (!seen.Add(identity)) throw new ArgumentException("Duplicate semantic object identity in " + path + "/" + property.Name);
                    Walk(item, key + "/" + Escape(property.Name) + "/" + Escape(identity), path + "/" + property.Name + "/" + name, result);
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.Object) Walk(property.Value, key + "/" + Escape(property.Name), path + "/" + property.Name, result);
            else if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() != 0) Add(property.Name, Canonical(property.Value));
        }
        void Add(string name, string text) { var id = key + "/" + Escape(name); if (result.Count >= 300000) throw new ArgumentException("Workspace snapshots are limited to 300,000 metadata properties."); result.Add(id, new(id, path, name, text)); }
    }
    internal static string Canonical(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) return "{" + string.Join(",", value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => JsonSerializer.Serialize(item.Name) + ":" + Canonical(item.Value))) + "}";
        if (value.ValueKind == JsonValueKind.Array) return "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]";
        return value.GetRawText();
    }
    private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
    public static string HashText(string text) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
}

public enum WorkspaceChangeKind { DiskOnly, LiveOnly, SameChange, Conflict }
public sealed record WorkspaceChange(string Key, string ObjectPath, string Property, string? Baseline, string? Disk, string? Live, WorkspaceChangeKind Kind);
public sealed record WorkspaceComparison(WorkspaceSemanticSnapshot Baseline, WorkspaceSemanticSnapshot Disk, WorkspaceSemanticSnapshot? Live,
    IReadOnlyList<WorkspaceChange> Changes, long DiskSequence, long LiveSequence, bool HasUnsavedModelEdits, string BaselineSource)
{
    public bool HasConflicts => Changes.Any(change => change.Kind == WorkspaceChangeKind.Conflict);
}
public static class WorkspaceSemanticDiff
{
    public static WorkspaceComparison Compare(WorkspaceSemanticSnapshot baseline, WorkspaceSemanticSnapshot disk, WorkspaceSemanticSnapshot? live,
        long diskSequence = 0, long liveSequence = 0, bool hasUnsavedModelEdits = false, string baselineSource = "Session baseline")
    {
        var rows = new List<WorkspaceChange>();
        foreach (var key in baseline.Properties.Keys.Concat(disk.Properties.Keys).Concat(live?.Properties.Keys ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
        {
            baseline.Properties.TryGetValue(key, out var original); disk.Properties.TryGetValue(key, out var file); WorkspaceProperty? remote = null; live?.Properties.TryGetValue(key, out remote);
            var diskChanged = original?.Value != file?.Value; var liveChanged = live != null && original?.Value != remote?.Value;
            if (!diskChanged && !liveChanged) continue;
            var selected = file ?? remote ?? original!;
            var kind = diskChanged && liveChanged ? file?.Value == remote?.Value ? WorkspaceChangeKind.SameChange : WorkspaceChangeKind.Conflict : diskChanged ? WorkspaceChangeKind.DiskOnly : WorkspaceChangeKind.LiveOnly;
            // A deletion of an enclosing object conflicts with a modification inside that object.
            if (live != null && (file == null && remote != null && original?.Value != remote.Value || remote == null && file != null && original?.Value != file.Value) && baseline.Properties.ContainsKey(key)) kind = WorkspaceChangeKind.Conflict;
            rows.Add(new(key, selected.ObjectPath, selected.Property, original?.Value, file?.Value, remote?.Value, kind));
        }
        return new(baseline, disk, live, rows.AsReadOnly(), diskSequence, liveSequence, hasUnsavedModelEdits, baselineSource);
    }
    public static IReadOnlyList<WorkspaceChange> Between(WorkspaceSemanticSnapshot before, WorkspaceSemanticSnapshot after) => Compare(before, after, null).Changes;
    public static string DisplayValue(string property, string? value) => property.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || property.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0 || property.IndexOf("connectionString", StringComparison.OrdinalIgnoreCase) >= 0 || property.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0 ? "[restricted metadata]" : value ?? "(absent)";
}
