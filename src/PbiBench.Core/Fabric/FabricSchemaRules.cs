using System.Security.Cryptography;
using System.Text;

namespace PbiBench.Core.Fabric;

public static class FabricSchemaRules
{
    public static string Fingerprint(FabricSourceRef source, IReadOnlyList<FabricColumnSchema> columns)
    {
        var text = new StringBuilder();
        void Add(string? value) { text.Append(value?.Length ?? -1).Append(':').Append(value).Append('|'); }
        Add(source.WorkspaceId); Add(source.ItemId); Add(source.ItemKind); Add(source.Schema); Add(source.Table); Add(source.Format);
        Add(source.SqlEndpoint?.Server); Add(source.SqlEndpoint?.Database); Add(source.IsView.ToString()); Add(source.Location);
        foreach (var column in columns.OrderBy(column => column.Ordinal))
        { Add(column.Name); Add(column.SourceType); Add(column.IsNullable?.ToString()); Add(column.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)); Add(column.Collation); }
        using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
    }
    public static void Validate(FabricTableSchema schema)
    {
        if (schema == null || schema.Source == null) throw new ArgumentException("A captured source schema is required.");
        ValidateSource(schema.Source);
        if (schema.Warnings == null || schema.Warnings.Count > 100 || schema.Warnings.Any(warning => warning == null || warning.Length > 4096))
            throw new ArgumentException("Source schema warnings must be a bounded non-null collection.");
        if (schema.Columns == null || schema.Columns.Count == 0 || schema.Columns.Count > 4096) throw new ArgumentException("A complete source schema with 1 to 4,096 columns is required.");
        if (schema.Columns.Any(column => column == null) || schema.Columns.Select(column => column.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != schema.Columns.Count ||
            schema.Columns.Select(column => column.Ordinal).Distinct().Count() != schema.Columns.Count) throw new ArgumentException("Source columns must have unique names and ordinals.");
        foreach (var column in schema.Columns)
        { Name(column.Name); Name(column.SourceType); if (column.Ordinal < 0) throw new ArgumentException("Column ordinals must be nonnegative."); if (column.Collation != null) Name(column.Collation); }
        if (schema.CapturedAt == default || schema.CapturedAt > DateTimeOffset.UtcNow.AddMinutes(1)) throw new ArgumentException("Source schema capture time is invalid.");
        if (schema.Fingerprint != Fingerprint(schema.Source, schema.Columns)) throw new ArgumentException("Source schema fingerprint no longer matches its captured metadata. Reload the schema.");
    }
    public static void ValidateSource(FabricSourceRef source)
    {
        Id(source.WorkspaceId); Id(source.ItemId); Name(source.Schema); Name(source.Table);
        if (!new[] { "Lakehouse", "Warehouse", "SQLDatabase", "MirroredDatabase", "MirroredWarehouse" }.Contains(source.ItemKind, StringComparer.Ordinal))
            throw new ArgumentException("This Fabric item kind is not supported by the import wizard.");
        if (source.Format != null) Name(source.Format);
        if (source.SqlEndpoint != null) ValidateEndpoint(source.SqlEndpoint);
        if (source.Location != null)
        {
            if (source.Location.Length > 4096 || source.Location.Any(char.IsControl) || !Uri.TryCreate(source.Location, UriKind.Absolute, out var location) ||
                location.Host != "onelake.dfs.fabric.microsoft.com" || !location.IsDefaultPort || location.Query.Length != 0 || location.Fragment.Length != 0 ||
                (location.Scheme != "https" && location.Scheme != "abfss") ||
                (location.Scheme == "https" && location.UserInfo.Length != 0) || (location.Scheme == "abfss" && location.UserInfo != source.WorkspaceId))
                throw new ArgumentException("Source location must be a public OneLake path without credentials, query tokens, or fragments.");
        }
    }
    public static void ValidateEndpoint(FabricSqlEndpoint endpoint)
    {
        var host = endpoint.Server;
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(character => !char.IsLetterOrDigit(character) && character != '.' && character != '-') ||
            (!host.EndsWith(".datawarehouse.fabric.microsoft.com", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".database.windows.net", StringComparison.OrdinalIgnoreCase) &&
             !host.EndsWith(".database.fabric.microsoft.com", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Use the public SQL server hostname returned by Fabric, without connection-string options.");
        Name(endpoint.Database);
    }
    public static string Id(string id)
    { if (!Guid.TryParse(id, out var value) || value == Guid.Empty) throw new ArgumentException("A Fabric workspace or item GUID is required."); return value.ToString("D"); }
    public static string Name(string name)
    { if (string.IsNullOrWhiteSpace(name) || name.Length > 512 || name.Any(char.IsControl)) throw new ArgumentException("A source name or type is empty or invalid."); return name; }
}
