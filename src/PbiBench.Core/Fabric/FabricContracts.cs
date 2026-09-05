using PbiBench.Core.Queries;

namespace PbiBench.Core.Fabric;

public enum FabricStorageMode { DirectLakeOneLake, DirectLakeSql, Import, DirectQuery }
public sealed record FabricWorkspace(string Id, string Name);
public sealed record FabricSqlEndpoint(string Server, string Database);
public sealed record FabricItem(string WorkspaceId, string Id, string Name, string Kind, FabricSqlEndpoint? SqlEndpoint = null)
{
    public bool UseSqlCatalog { get; init; }
}
public sealed record FabricSourceRef(string WorkspaceId, string ItemId, string ItemKind, string Schema, string Table,
    string? Format = null, FabricSqlEndpoint? SqlEndpoint = null, bool IsView = false, string? Location = null)
{
    public string DisplayName => Schema + "." + Table;
}
public sealed record FabricColumnSchema(string Name, string SourceType, bool? IsNullable, int Ordinal = 0, string? Collation = null);
public sealed record FabricTableSchema(FabricSourceRef Source, IReadOnlyList<FabricColumnSchema> Columns,
    string Fingerprint, DateTimeOffset CapturedAt, IReadOnlyList<string> Warnings);
public sealed record FabricImportRequest(FabricTableSchema Schema, IReadOnlyList<string> Columns,
    FabricStorageMode Mode, string? TargetTableName = null);
public sealed record FabricDataPreviewRequest(FabricTableSchema Schema, IReadOnlyList<string> Columns, int RowLimit = 100, int TimeoutSeconds = 30);
public sealed record FabricDataPreview(FabricSourceRef Source, QueryResultSet Result, DateTimeOffset CapturedAt,
    string Query, IReadOnlyList<string> Warnings);
public interface IFabricCatalogService
{
    Task<IReadOnlyList<FabricWorkspace>> ListWorkspacesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FabricItem>> ListItemsAsync(string workspaceId, CancellationToken cancellationToken);
    Task<FabricItem> ResolveItemAsync(FabricItem item, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListSchemasAsync(FabricItem item, CancellationToken cancellationToken);
    Task<IReadOnlyList<FabricSourceRef>> ListTablesAsync(FabricItem item, string schema, CancellationToken cancellationToken);
    Task<FabricTableSchema> GetSchemaAsync(FabricSourceRef source, CancellationToken cancellationToken);
}
public interface IFabricDataPreviewService
{
    Task<FabricDataPreview> PreviewAsync(FabricDataPreviewRequest request, CancellationToken cancellationToken);
}
