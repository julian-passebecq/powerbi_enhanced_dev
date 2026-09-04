namespace PbiBench.Core.DataExploration;

public enum DataStorageMode { Unknown, Import, DirectQuery, Dual, DirectLake, Mixed }
public sealed record DataColumnSchema(string Name, string DataType, bool IsHidden = false, bool IsKey = false, string? Description = null);
public sealed record DataMeasureSchema(string Name, string Expression, string? FormatString = null, string? Description = null);
public sealed record DataTableSchema(string Name, DataStorageMode StorageMode, IReadOnlyList<DataColumnSchema> Columns,
    IReadOnlyList<DataMeasureSchema> Measures, IReadOnlyList<string> CandidateKeyColumns);
public sealed record DataRelationshipSchema(string Name, string FromTable, string FromColumn, string ToTable,
    string ToColumn, bool IsActive, string FromCardinality = "Many", string ToCardinality = "One", string FilterDirection = "OneDirection");
public sealed record DataModelSchema(string Name, IReadOnlyList<DataTableSchema> Tables, IReadOnlyList<DataRelationshipSchema> Relationships)
{
    public DataTableSchema GetTable(string name) => Tables.FirstOrDefault(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Table '{name}' is not in the current model schema.", nameof(name));
}
public sealed record DataSort(string Column, bool Descending = false);
public enum DataFilterOperator { Equals, NotEquals, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Contains, StartsWith, EndsWith, IsBlank, IsNotBlank, In, NotIn }
public sealed record DataFilter(string Table, string Column, DataFilterOperator Operator, string? Value = null)
{
    public IReadOnlyList<string?>? Values { get; init; }
}
