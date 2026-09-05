using System.Security.Cryptography;
using System.Text;
using PbiBench.Core.Queries;
using PbiBench.Core.Refresh;
using TabularEditor.TOMWrapper;
using TOM = Microsoft.AnalysisServices.Tabular;

namespace PbiBench.Semantic;

public static class RefreshMetadataProvider
{
    /// <summary>Read on the model-owning UI thread. Returned metadata contains no connection string or source expressions.</summary>
    public static RefreshMetadataSnapshot Capture(TabularModelHandler handler, string? server = null)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var endpoint = server ?? (handler.IsConnected ? QueryConnectionTarget.Server(handler.Database.Server.ConnectionString, handler.Database.Server.Name) : null) ?? "";
        return Capture(handler.Database, endpoint, handler.IsConnected, handler.HasUnsavedChanges);
    }
    public static RefreshMetadataSnapshot Capture(TOM.Database database, string server, bool connected = true, bool hasUnsavedChanges = false)
    {
        var model = database.Model;
        var tables = model.Tables.Select(table => new RefreshTableMetadata(table.Name, table.RefreshPolicy != null,
            Array.AsReadOnly(table.Partitions.Select(partition => new RefreshPartitionMetadata(partition.Name,
                (partition.Mode == TOM.ModeType.Default ? model.DefaultMode : partition.Mode).ToString(),
                Source(partition.Source), (partition.Source as TOM.QueryPartitionSource)?.DataSource?.Name)).ToArray()))).ToArray();
        // Hash only. Never return/persist serialized model metadata or its credentials in refresh plans.
        var json = TOM.JsonSerializer.SerializeDatabase(database); using var sha = SHA256.Create();
        var fingerprint = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(json)));
        return new(server, database.ID, database.Name, database.CompatibilityLevel, fingerprint, connected, hasUnsavedChanges,
            database.CompatibilityMode == Microsoft.AnalysisServices.CompatibilityMode.PowerBI, Array.AsReadOnly(tables));
    }
    private static RefreshSourceKind Source(TOM.PartitionSource? source) => source switch
    {
        TOM.MPartitionSource => RefreshSourceKind.M, TOM.QueryPartitionSource => RefreshSourceKind.Query, TOM.CalculatedPartitionSource => RefreshSourceKind.Calculated,
        TOM.EntityPartitionSource => RefreshSourceKind.Entity, null => RefreshSourceKind.None, _ => RefreshSourceKind.Unknown
    };
}
