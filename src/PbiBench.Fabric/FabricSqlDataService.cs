using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PbiBench.Core.Abstractions;
using PbiBench.Core.Fabric;
using PbiBench.Core.Queries;

namespace PbiBench.Fabric;

public interface IFabricSqlConnectionFactory { DbConnection Create(FabricSqlEndpoint endpoint, string accessToken); }
public sealed class FabricSqlConnectionFactory : IFabricSqlConnectionFactory
{
    public DbConnection Create(FabricSqlEndpoint endpoint, string accessToken)
    {
        FabricSchemaRules.ValidateEndpoint(endpoint);
        var settings = new SqlConnectionStringBuilder
        {
            DataSource = endpoint.Server, InitialCatalog = endpoint.Database, Encrypt = true, TrustServerCertificate = false,
            IntegratedSecurity = false, PersistSecurityInfo = false, Pooling = false, ConnectTimeout = 30,
            ApplicationName = "PbiBench Fabric read-only preview", ApplicationIntent = ApplicationIntent.ReadOnly
        };
        return new SqlConnection(settings.ConnectionString) { AccessToken = accessToken };
    }
}

/// <summary>Independent SQL sessions. Public entry points construct only bounded SELECT statements from validated names.</summary>
public sealed class FabricSqlDataService(IAccessTokenProvider tokens, IFabricSqlConnectionFactory? factory = null) : IFabricDataPreviewService
{
    private readonly IFabricSqlConnectionFactory factory = factory ?? new FabricSqlConnectionFactory();
    public async Task<IReadOnlyList<string>> ListSchemasAsync(FabricItem item, CancellationToken cancellationToken)
    {
        var result = await Query(Endpoint(item), "SELECT TOP (10001) DISTINCT s.name FROM sys.schemas AS s JOIN sys.objects AS o ON o.schema_id=s.schema_id WHERE o.type IN ('U','V') ORDER BY s.name", null, 10000, 30, cancellationToken).ConfigureAwait(false);
        Complete(result); return result.Rows.Select(row => Convert.ToString(row[0], CultureInfo.InvariantCulture)!).ToArray();
    }
    public async Task<IReadOnlyList<FabricSourceRef>> ListTablesAsync(FabricItem item, string schema, CancellationToken cancellationToken)
    {
        FabricSchemaRules.Name(schema);
        var result = await Query(Endpoint(item), "SELECT TOP (10001) o.name, o.type FROM sys.objects AS o JOIN sys.schemas AS s ON s.schema_id=o.schema_id WHERE s.name=@schema AND o.type IN ('U','V') ORDER BY o.name", new Dictionary<string, string> { ["@schema"] = schema }, 10000, 30, cancellationToken).ConfigureAwait(false);
        Complete(result); return result.Rows.Select(row => new FabricSourceRef(item.WorkspaceId, item.Id, item.Kind, schema,
            Convert.ToString(row[0], CultureInfo.InvariantCulture)!, "SQL", item.SqlEndpoint, Convert.ToString(row[1], CultureInfo.InvariantCulture)!.Trim() == "V")).ToArray();
    }
    public async Task<FabricTableSchema> GetSchemaAsync(FabricSourceRef source, CancellationToken cancellationToken)
    {
        FabricSchemaRules.ValidateSource(source);
        const string query = "SELECT TOP (4097) c.name, ty.name, c.is_nullable, c.column_id, c.collation_name, c.precision, c.scale FROM sys.columns AS c JOIN sys.objects AS o ON c.object_id=o.object_id JOIN sys.schemas AS s ON o.schema_id=s.schema_id JOIN sys.types AS ty ON c.user_type_id=ty.user_type_id WHERE s.name=@schema AND o.name=@table AND o.type IN ('U','V') ORDER BY c.column_id";
        var result = await Query(source.SqlEndpoint ?? throw new InvalidOperationException("Fabric did not expose a SQL endpoint for this item."), query,
            new Dictionary<string, string> { ["@schema"] = source.Schema, ["@table"] = source.Table }, 4096, 30, cancellationToken).ConfigureAwait(false);
        Complete(result);
        var columns = result.Rows.Select(row =>
        {
            var type = Convert.ToString(row[1], CultureInfo.InvariantCulture)!;
            if (type is "decimal" or "numeric") type += "(" + Convert.ToString(row[5], CultureInfo.InvariantCulture) + "," + Convert.ToString(row[6], CultureInfo.InvariantCulture) + ")";
            return new FabricColumnSchema(Convert.ToString(row[0], CultureInfo.InvariantCulture)!, type, Convert.ToBoolean(row[2], CultureInfo.InvariantCulture),
                Convert.ToInt32(row[3], CultureInfo.InvariantCulture), row[4] == null ? null : Convert.ToString(row[4], CultureInfo.InvariantCulture));
        }).ToArray();
        source = source with { Format = "SQL" };
        var schema = new FabricTableSchema(source, columns, FabricSchemaRules.Fingerprint(source, columns), DateTimeOffset.UtcNow,
            new[] { "SQL endpoint schema reflects the current identity's metadata permissions and may lag OneLake schema synchronization.", "SQL timestamp/rowversion is binary; it is not a date/time type." });
        FabricSchemaRules.Validate(schema); return schema;
    }
    public async Task<FabricDataPreview> PreviewAsync(FabricDataPreviewRequest request, CancellationToken cancellationToken)
    {
        var query = PreviewSql(request);
        var result = await Query(request.Schema.Source.SqlEndpoint ?? throw new InvalidOperationException("No SQL endpoint is available. Preview the imported table through a connected semantic model after a reviewed save."),
            query, null, request.RowLimit, request.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
        return new FabricDataPreview(request.Schema.Source, result, DateTimeOffset.UtcNow, query, new[]
        {
            "Explicit SQL source preview under your signed-in identity. SQL security and source values can differ from semantic-model RLS and Direct Lake OneLake security.",
            "First N rows without guaranteed order; this is a sample, not a profile or complete table. Large values are clipped to 8,192 characters/bytes; unsupported complex cells are marked unavailable. The result is limited to 200,000 cells.",
            "The SQL endpoint uses source/capacity resources. Direct Lake model previews can additionally load referenced columns into capacity memory."
        });
    }
    public static string PreviewSql(FabricDataPreviewRequest request)
    {
        FabricSchemaRules.Validate(request.Schema);
        if (request.RowLimit < 1 || request.RowLimit > 1000 || request.TimeoutSeconds < 1 || request.TimeoutSeconds > 120) throw new ArgumentException("Choose 1 to 1,000 preview rows and a timeout from 1 to 120 seconds.");
        if (request.Columns.Count == 0 || request.Columns.Count > 200 || request.Columns.Distinct(StringComparer.Ordinal).Count() != request.Columns.Count ||
            request.Columns.Any(name => !request.Schema.Columns.Any(column => column.Name == name))) throw new ArgumentException("Choose 1 to 200 distinct source columns from the captured schema.");
        return "SELECT TOP (" + (request.RowLimit + 1).ToString(CultureInfo.InvariantCulture) + ") " + string.Join(", ", request.Columns.Select(Quote)) +
            " FROM " + Quote(request.Schema.Source.Schema) + "." + Quote(request.Schema.Source.Table);
    }
    private async Task<QueryResultSet> Query(FabricSqlEndpoint endpoint, string text, IReadOnlyDictionary<string, string>? parameters,
        int maximumRows, int timeout, CancellationToken ct)
    {
        FabricSchemaRules.ValidateEndpoint(endpoint); ct.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
        var token = await tokens.GetAccessTokenAsync(EntraPublicClientTokenProvider.Scopes(FabricAudience.Sql), deadline.Token).ConfigureAwait(false);
        using var connection = factory.Create(endpoint, token);
        try
        {
            await connection.OpenAsync(deadline.Token).ConfigureAwait(false);
            using var command = connection.CreateCommand(); command.CommandText = text; command.CommandTimeout = timeout;
            if (parameters != null) foreach (var item in parameters)
            { var parameter = command.CreateParameter(); parameter.ParameterName = item.Key; parameter.DbType = DbType.String; parameter.Value = item.Value; command.Parameters.Add(parameter); }
            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, deadline.Token).ConfigureAwait(false);
            if (reader.FieldCount < 1 || reader.FieldCount > 4096) throw new InvalidDataException("SQL returned an invalid column count.");
            var columns = Enumerable.Range(0, reader.FieldCount).Select(index => new QueryColumn("C" + index, reader.GetName(index), reader.GetFieldType(index).Name)).ToArray();
            var rows = new List<object?[]>(); var truncated = false; long capturedBytes = 0;
            while (await reader.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                if (rows.Count >= maximumRows || (long)(rows.Count + 1) * columns.Length > 200000) { truncated = true; break; }
                var values = new object?[columns.Length];
                for (var index = 0; index < values.Length; index++)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    if (await reader.IsDBNullAsync(index, deadline.Token).ConfigureAwait(false)) continue;
                    if (reader.GetFieldType(index) == typeof(string))
                    {
                        using var value = reader.GetTextReader(index); var buffer = new char[8193]; var read = 0;
                        while (read < buffer.Length) { var count = await value.ReadAsync(buffer, read, buffer.Length - read).ConfigureAwait(false); if (count == 0) break; read += count; deadline.Token.ThrowIfCancellationRequested(); }
                        if (read > 8192) truncated = true; values[index] = new string(buffer, 0, Math.Min(read, 8192)); capturedBytes += read * 2L;
                    }
                    else if (reader.GetFieldType(index) == typeof(byte[]))
                    {
                        var length = reader.GetBytes(index, 0, null, 0, 0); if (length > 8192) truncated = true;
                        var bytes = new byte[(int)Math.Min(length, 8192)]; reader.GetBytes(index, 0, bytes, 0, bytes.Length); values[index] = bytes; capturedBytes += bytes.Length;
                    }
                    else if (Scalar(reader.GetFieldType(index))) { values[index] = reader.GetValue(index); capturedBytes += 32; }
                    else { values[index] = "[Complex source value unavailable in bounded preview]"; capturedBytes += 100; truncated = true; }
                    if (capturedBytes > 16777216) throw new InvalidDataException("SQL preview exceeds the 16 MB capture budget. Select fewer columns or rows.");
                }
                rows.Add(values);
            }
            deadline.Token.ThrowIfCancellationRequested(); return new QueryResultSet(0, "Fabric source preview", columns, rows, truncated);
        }
        catch (DbException) { ct.ThrowIfCancellationRequested(); if (deadline.IsCancellationRequested) throw new TimeoutException("Fabric SQL preview timed out."); throw new FabricApiException("SQL source access failed. Check SQL permissions, schema synchronization, and endpoint availability.", 0); }
    }
    private static string Quote(string name) { FabricSchemaRules.Name(name); if (name.Length > 128) throw new ArgumentException("SQL identifiers cannot exceed 128 characters."); return "[" + name.Replace("]", "]]") + "]"; }
    private static bool Scalar(Type type) => type.IsPrimitive || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid);
    private static FabricSqlEndpoint Endpoint(FabricItem item) => item.SqlEndpoint ?? throw new InvalidOperationException("This Fabric item has no discovered SQL endpoint. Use OneLake metadata browsing.");
    private static void Complete(QueryResultSet result) { if (result.IsTruncated) throw new InvalidDataException("SQL schema metadata exceeded its bounded capture. A partial schema cannot be imported."); }
}
