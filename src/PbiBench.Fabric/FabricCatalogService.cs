using System.Net.Http;
using System.Text.Json;
using PbiBench.Core.Abstractions;
using PbiBench.Core.Fabric;

namespace PbiBench.Fabric;

public sealed class FabricCatalogService(HttpClient http, IAccessTokenProvider tokens, FabricSqlDataService? sql = null) : IFabricCatalogService
{
    private readonly FabricSqlDataService sql = sql ?? new FabricSqlDataService(tokens);
    public async Task<IReadOnlyList<FabricWorkspace>> ListWorkspacesAsync(CancellationToken cancellationToken)
    {
        var rows = await Pages(new Uri(FabricApiClient.BaseUri, "workspaces"), "value", FabricAudience.Fabric, cancellationToken).ConfigureAwait(false);
        var result = rows.Select(row => new FabricWorkspace(FabricSchemaRules.Id(Required(row, "id")), Required(row, "displayName"))).ToArray();
        Unique(result.Select(item => item.Id)); return result;
    }
    public async Task<IReadOnlyList<FabricItem>> ListItemsAsync(string workspaceId, CancellationToken cancellationToken)
    {
        var workspace = FabricSchemaRules.Id(workspaceId);
        var rows = await Pages(new Uri(FabricApiClient.BaseUri, "workspaces/" + workspace + "/items"), "value", FabricAudience.Fabric, cancellationToken).ConfigureAwait(false);
        var result = rows.Where(row => Supported(Text(row, "type"))).Select(row => new FabricItem(workspace,
            FabricSchemaRules.Id(Required(row, "id")), Required(row, "displayName"), Required(row, "type"))).ToArray();
        Unique(result.Select(item => item.Id)); return result;
    }
    /// <summary>Platform inventory for Toolbox. The existing semantic source picker retains its supported-type filter.</summary>
    public async Task<IReadOnlyList<FabricItem>> ListAllItemsAsync(string workspaceId, CancellationToken cancellationToken)
    {
        var workspace = FabricSchemaRules.Id(workspaceId);
        var rows = await Pages(new Uri(FabricApiClient.BaseUri, "workspaces/" + workspace + "/items"), "value", FabricAudience.Fabric, cancellationToken).ConfigureAwait(false);
        var result = rows.Select(row => new FabricItem(workspace, FabricSchemaRules.Id(Required(row, "id")), Required(row, "displayName"), Required(row, "type"))).ToArray();
        Unique(result.Select(item => item.Id)); return result;
    }
    public async Task<FabricItem> ResolveItemAsync(FabricItem item, CancellationToken cancellationToken)
    {
        var group = item.Kind switch { "Lakehouse" => "lakehouses", "Warehouse" => "warehouses", "SQLDatabase" => "sqlDatabases", "MirroredDatabase" => "mirroredDatabases", "MirroredWarehouse" => "mirroredWarehouses", _ => throw new ArgumentException("Unsupported Fabric item kind.") };
        using var doc = await GetJson(new Uri(FabricApiClient.BaseUri, "workspaces/" + FabricSchemaRules.Id(item.WorkspaceId) + "/" + group + "/" + FabricSchemaRules.Id(item.Id)), FabricAudience.Fabric, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (FabricSchemaRules.Id(Required(root, "id")) != FabricSchemaRules.Id(item.Id) || Required(root, "type") != item.Kind ||
            (Text(root, "workspaceId") is { } workspace && FabricSchemaRules.Id(workspace) != FabricSchemaRules.Id(item.WorkspaceId))) throw new InvalidDataException("Fabric returned a different item than requested.");
        FabricSqlEndpoint? endpoint = null;
        if (root.TryGetProperty("properties", out var properties))
        {
            if (item.Kind == "SQLDatabase" && Text(properties, "serverFqdn") is { } server && Text(properties, "databaseName") is { } name)
                endpoint = new FabricSqlEndpoint(Host(server), name);
            else if (properties.TryGetProperty("sqlEndpointProperties", out var sqlEndpoint) && Text(sqlEndpoint, "connectionString") is { } endpointServer && Text(sqlEndpoint, "id") is { } database)
                endpoint = new FabricSqlEndpoint(Host(endpointServer), FabricSchemaRules.Id(database));
            else if (item.Kind is "Warehouse" or "MirroredWarehouse" && Text(properties, "connectionString") is { } warehouseServer)
                endpoint = new FabricSqlEndpoint(Host(warehouseServer), FabricSchemaRules.Id(item.Id));
        }
        if (endpoint != null) FabricSchemaRules.ValidateEndpoint(endpoint);
        return item with { Name = Required(root, "displayName"), SqlEndpoint = endpoint };
    }
    public async Task<IReadOnlyList<string>> ListSchemasAsync(FabricItem item, CancellationToken cancellationToken)
    {
        if (item.UseSqlCatalog) return await sql.ListSchemasAsync(item, cancellationToken).ConfigureAwait(false);
        var rows = await Pages(OneLake(item.WorkspaceId, item.Id, "schemas?catalog_name=" + E(item.Id)), "schemas", FabricAudience.OneLake, cancellationToken).ConfigureAwait(false);
        var result = rows.Select(row => Required(row, "name")).ToArray(); Unique(result); return result;
    }
    public async Task<IReadOnlyList<FabricSourceRef>> ListTablesAsync(FabricItem item, string schema, CancellationToken cancellationToken)
    {
        FabricSchemaRules.Name(schema);
        if (item.UseSqlCatalog) return await sql.ListTablesAsync(item, schema, cancellationToken).ConfigureAwait(false);
        var rows = await Pages(OneLake(item.WorkspaceId, item.Id, "tables?catalog_name=" + E(item.Id) + "&schema_name=" + E(schema)), "tables", FabricAudience.OneLake, cancellationToken).ConfigureAwait(false);
        var result = rows.Select(row =>
        {
            if (Text(row, "schema_name") is { } actual && actual != schema) throw new InvalidDataException("Fabric returned a table from a different schema.");
            return new FabricSourceRef(item.WorkspaceId, item.Id, item.Kind, schema, Required(row, "name"), Text(row, "data_source_format"), item.SqlEndpoint,
                Text(row, "table_type") == "VIEW", Text(row, "storage_location"));
        }).ToArray();
        foreach (var source in result) FabricSchemaRules.ValidateSource(source); Unique(result.Select(source => source.Table)); return result;
    }
    public async Task<FabricTableSchema> GetSchemaAsync(FabricSourceRef source, CancellationToken cancellationToken)
    {
        FabricSchemaRules.ValidateSource(source);
        if (source.Format == "SQL") return await sql.GetSchemaAsync(source, cancellationToken).ConfigureAwait(false);
        var full = source.ItemId + "." + source.Schema + "." + source.Table;
        using var doc = await GetJson(OneLake(source.WorkspaceId, source.ItemId, "tables/" + E(full) + "?catalog_name=" + E(source.ItemId) + "&schema_name=" + E(source.Schema)), FabricAudience.OneLake, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (Required(root, "name") != source.Table || Required(root, "schema_name") != source.Schema) throw new InvalidDataException("Fabric returned schema metadata for a different table.");
        if (!root.TryGetProperty("columns", out var values) || values.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Fabric did not return complete column metadata.");
        if (values.GetArrayLength() < 1 || values.GetArrayLength() > 4096) throw new InvalidDataException("Fabric schema must contain 1 to 4,096 columns.");
        var columns = values.EnumerateArray().Select((column, index) => new FabricColumnSchema(Required(column, "name"), Type(column),
            column.TryGetProperty("nullable", out var nullable) && nullable.ValueKind is JsonValueKind.True or JsonValueKind.False ? nullable.GetBoolean() : null,
            column.TryGetProperty("position", out var ordinal) && ordinal.TryGetInt32(out var position) ? position : index)).ToArray();
        source = source with { Format = Text(root, "data_source_format"), Location = Text(root, "storage_location"), IsView = Text(root, "table_type") == "VIEW" };
        var schema = new FabricTableSchema(source, columns, FabricSchemaRules.Fingerprint(source, columns), DateTimeOffset.UtcNow,
            new[] { "OneLake metadata is a point-in-time source schema. Recheck the source before applying later changes.", "Source collation is not exposed by the OneLake table API; model compatibility must be reviewed." });
        FabricSchemaRules.Validate(schema); return schema;
    }
    private async Task<IReadOnlyList<JsonElement>> Pages(Uri first, string arrayName, FabricAudience audience, CancellationToken ct)
    {
        var output = new List<JsonElement>(); var seen = new HashSet<string>(StringComparer.Ordinal); var next = first;
        for (var page = 0; page < 100; page++)
        {
            using var doc = await GetJson(next, audience, ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty(arrayName, out var rows) || rows.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Fabric omitted the expected catalog collection.");
            foreach (var row in rows.EnumerateArray()) { if (output.Count >= 10000) throw new InvalidDataException("Catalog exceeds 10,000 entries. Narrow the selection in Fabric."); output.Add(row.Clone()); }
            var token = Text(doc.RootElement, audience == FabricAudience.Fabric ? "continuationToken" : "next_page_token");
            if (string.IsNullOrEmpty(token)) return output;
            if (token!.Length > 16384 || !seen.Add(token)) throw new InvalidDataException("Fabric returned an invalid or repeated pagination token.");
            next = new Uri(first.AbsoluteUri + (first.Query.Length == 0 ? "?" : "&") + (audience == FabricAudience.Fabric ? "continuationToken=" : "page_token=") + E(token));
        }
        throw new InvalidDataException("Catalog exceeds 100 pages. Narrow the selection in Fabric.");
    }
    private async Task<JsonDocument> GetJson(Uri uri, FabricAudience audience, CancellationToken ct)
    {
        using var response = await FabricHttp.SendAsync(http, tokens, HttpMethod.Get, uri, audience, null, ct).ConfigureAwait(false);
        if ((int)response.StatusCode != 200) throw new FabricApiException("Fabric catalog returned an unexpected response status.", (int)response.StatusCode);
        return await FabricHttp.ReadJsonAsync(response.Content, ct).ConfigureAwait(false);
    }
    private static string Type(JsonElement column)
    {
        var type = Text(column, "type_text") ?? Required(column, "type_name");
        if (type.Equals("decimal", StringComparison.OrdinalIgnoreCase) && column.TryGetProperty("type_precision", out var precision) && column.TryGetProperty("type_scale", out var scale))
            return "decimal(" + precision.GetInt32() + "," + scale.GetInt32() + ")";
        return FabricSchemaRules.Name(type);
    }
    private static Uri OneLake(string workspace, string item, string path) => new("https://onelake.table.fabric.microsoft.com/delta/" + FabricSchemaRules.Id(workspace) + "/" + FabricSchemaRules.Id(item) + "/api/2.1/unity-catalog/" + path);
    private static bool Supported(string? kind) => kind is "Lakehouse" or "Warehouse" or "SQLDatabase" or "MirroredDatabase" or "MirroredWarehouse";
    private static string Host(string value) => value.EndsWith(",1433", StringComparison.Ordinal) ? value.Substring(0, value.Length - 5) : value;
    private static void Unique(IEnumerable<string> values) { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var value in values) if (!seen.Add(value)) throw new InvalidDataException("Catalog changed during pagination; reload to get a consistent selection."); }
    private static string Required(JsonElement value, string name) => FabricSchemaRules.Name(Text(value, name) ?? throw new InvalidDataException("Fabric metadata omitted " + name + "."));
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
    private static string E(string value) => Uri.EscapeDataString(value);
}
