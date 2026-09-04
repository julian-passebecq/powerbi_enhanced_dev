using PbiBench.Core.DataExploration;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic;

/// <summary>Detached read-only schema capture; call on the TE2 model-owning thread.</summary>
public static class DataModelSchemaProvider
{
    public static DataModelSchema Capture(TabularModelHandler handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var relationships = handler.Model.Relationships.OfType<SingleColumnRelationship>()
            .Where(item => item.FromColumn != null && item.ToColumn != null)
            .Select(item => new DataRelationshipSchema(item.Name, item.FromTable.Name, item.FromColumn.Name,
                item.ToTable.Name, item.ToColumn.Name, item.IsActive, item.FromCardinality.ToString(),
                item.ToCardinality.ToString(), item.CrossFilteringBehavior.ToString())).ToArray();
        var tables = handler.Model.Tables.Select(table =>
        {
            // Key metadata only nominates columns; the preview verifier must still prove uniqueness.
            var candidates = table.Columns.Where(column => column.IsKey).Select(column => column.Name)
                .Concat(relationships.Where(item => item.ToTable == table.Name && item.ToCardinality == "One").Select(item => item.ToColumn))
                .Concat(relationships.Where(item => item.FromTable == table.Name && item.FromCardinality == "One").Select(item => item.FromColumn))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var columns = table.Columns.Select(column => new DataColumnSchema(column.Name, column.DataType.ToString(), column.IsHidden, column.IsKey, column.Description)).ToArray();
            var measures = table.Measures.Select(measure => new DataMeasureSchema(measure.Name, measure.Expression ?? "", measure.FormatString, measure.Description)).ToArray();
            return new DataTableSchema(table.Name, StorageMode(table), Array.AsReadOnly(columns), Array.AsReadOnly(measures), Array.AsReadOnly(candidates));
        }).ToArray();
        return new(handler.Database.Name, Array.AsReadOnly(tables), Array.AsReadOnly(relationships));
    }

    private static DataStorageMode StorageMode(Table table)
    {
        var modes = table.Partitions.Select(partition => partition.Mode == ModeType.Default ? table.Model.DefaultMode : partition.Mode).Distinct().ToArray();
        if (modes.Length == 0) return DataStorageMode.Unknown;
        if (modes.Length > 1) return DataStorageMode.Mixed;
        return modes[0].ToString() switch
        {
            "Import" => DataStorageMode.Import,
            "DirectQuery" => DataStorageMode.DirectQuery,
            "Dual" => DataStorageMode.Dual,
            "DirectLake" => DataStorageMode.DirectLake,
            _ => DataStorageMode.Unknown
        };
    }
}
