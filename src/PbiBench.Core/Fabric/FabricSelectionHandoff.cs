using System.Text.Json;
using PbiBench.Core.Queries;

namespace PbiBench.Core.Fabric;

/// <summary>Selection only. No executable command, connection secret or approval can cross this boundary.</summary>
public sealed record FabricSelectionHandoff(int SchemaVersion, string Kind, string WorkspaceId, string ItemId, string ItemType, string DisplayName, string RequestedAction)
{
    public static FabricSelectionHandoff For(FabricItem item) => new(1, "FabricSelection", item.WorkspaceId, item.Id, item.Kind, item.Name, "ReviewSemanticSource");
    public static FabricSelectionHandoff Parse(string json)
    {
        if (json.Length > 16384) throw new InvalidDataException("Handoff exceeds 16 KiB.");
        using var doc = JsonDocument.Parse(json); if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a handoff object.");
        var allowed = new HashSet<string>(new[] { "SchemaVersion", "Kind", "WorkspaceId", "ItemId", "ItemType", "DisplayName", "RequestedAction" }, StringComparer.Ordinal);
        foreach (var field in doc.RootElement.EnumerateObject()) if (!allowed.Remove(field.Name)) throw new InvalidDataException("Unknown or duplicate handoff field.");
        if (allowed.Count != 0) throw new InvalidDataException("Incomplete handoff.");
        var value = JsonSerializer.Deserialize<FabricSelectionHandoff>(json) ?? throw new InvalidDataException("Empty handoff.");
        if (value.SchemaVersion != 1 || value.Kind != "FabricSelection" || value.RequestedAction != "ReviewSemanticSource") throw new InvalidDataException("Unsupported handoff contract.");
        FabricSchemaRules.Id(value.WorkspaceId); FabricSchemaRules.Id(value.ItemId); FabricSchemaRules.Name(value.ItemType); FabricSchemaRules.Name(value.DisplayName);
        if (value.ItemType.Length > 128 || value.DisplayName.Length > 512) throw new InvalidDataException("Handoff labels exceed their bounds.");
        return value;
    }
    public static async Task<FabricSelectionHandoff> LoadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); using var stream = File.OpenRead(path); if (stream.Length > 16384) throw new InvalidDataException("Oversized handoff.");
        using var reader = new StreamReader(stream); var json = await reader.ReadToEndAsync().ConfigureAwait(false); ct.ThrowIfCancellationRequested(); return Parse(json);
    }
    public async Task SaveAsync(string path, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }); Parse(json);
        var destination = Path.GetFullPath(path); var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true)) { var bytes = System.Text.Encoding.UTF8.GetBytes(json); await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false); } AtomicQueryFile.Commit(temp, destination, ct); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
