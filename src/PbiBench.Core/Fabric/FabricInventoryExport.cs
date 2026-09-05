using System.Text;
using System.Text.Json;
using PbiBench.Core.Queries;

namespace PbiBench.Core.Fabric;

public static class FabricInventoryExport
{
    public const int MaximumRows = 10000, MaximumBytes = 4 * 1024 * 1024;
    public static IReadOnlyList<FabricItem> Filter(IReadOnlyList<FabricItem> items, string search, string? kind) => Array.AsReadOnly(items.Where(i =>
        (kind == null || i.Kind == kind) && (i.Name.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 || i.Kind.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)).ToArray());
    public static string Serialize(IReadOnlyList<FabricItem> items, bool csv)
    {
        if (items.Count > MaximumRows) throw new InvalidDataException("Inventory exports are limited to 10,000 rows.");
        // Explicit allowlist: never serialize FabricItem, auth state, endpoints, HTTP responses or arbitrary properties.
        var rows = items.Select(i => new { workspaceId = FabricSchemaRules.Id(i.WorkspaceId), itemId = FabricSchemaRules.Id(i.Id), name = FabricSchemaRules.Name(i.Name), type = FabricSchemaRules.Name(i.Kind) }).ToArray();
        var text = csv ? "WorkspaceId,ItemId,Name,Type\r\n" + string.Join("\r\n", rows.Select(r => string.Join(",", new[] { r.workspaceId, r.itemId, r.name, r.type }.Select(Cell)))) + "\r\n" :
            JsonSerializer.Serialize(new { schemaVersion = 1, items = rows }, new JsonSerializerOptions { WriteIndented = true });
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("Inventory export exceeds 4 MiB; narrow the filter.");
        return text;
    }
    private static string Cell(string value)
    {
        // CSV quoting alone does not prevent spreadsheet formula execution.
        var trimmed = value.TrimStart(); if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    public static async Task SaveAsync(string path, IReadOnlyList<FabricItem> items, bool csv, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); var bytes = new UTF8Encoding(false).GetBytes(Serialize(items, csv));
        var destination = Path.GetFullPath(path); var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            { await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false); await stream.FlushAsync(cancellationToken).ConfigureAwait(false); }
            AtomicQueryFile.Commit(temporary, destination, cancellationToken);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
