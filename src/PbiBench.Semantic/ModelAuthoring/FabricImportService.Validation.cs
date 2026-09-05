using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PbiBench.Core.Fabric;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed partial class FabricImportService
{
    private static FabricTableSchema Freeze(FabricTableSchema schema)
    {
        FabricSchemaRules.Validate(schema);
        return schema with { Columns = schema.Columns.ToArray(), Warnings = schema.Warnings.ToArray() };
    }
    private static string[] Selected(IReadOnlyList<string> names, FabricTableSchema schema)
    {
        if (names == null || names.Count == 0 || names.Count > 4096 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count ||
            names.Any(name => !schema.Columns.Any(column => column.Name == name))) throw new ArgumentException("Choose unique columns from the captured source schema.");
        return names.ToArray();
    }
    private List<AuthoringIssue> ValidateSource(FabricTableSchema schema, FabricStorageMode mode)
    {
        var issues = schema.Warnings.Select(warning => new AuthoringIssue("FABRIC_SOURCE_NOTICE", warning, AuthoringIssueSeverity.Warning)).ToList();
        if (!Enum.IsDefined(typeof(FabricStorageMode), mode)) Error(issues, "FABRIC_MODE", "Choose a supported storage mode.");
        if (handler.CompatibilityLevel < 1400) Error(issues, "FABRIC_COMPATIBILITY", "Fabric authoring requires compatibility 1400 or later. Compatibility is never upgraded implicitly.");
        if (mode is FabricStorageMode.DirectLakeOneLake or FabricStorageMode.DirectLakeSql)
        {
            if (handler.CompatibilityLevel < 1604) Error(issues, "FABRIC_COMPATIBILITY", "Direct Lake requires compatibility 1604 or later. Choose the target compatibility explicitly before importing.");
            issues.Add(new("FABRIC_DIRECTLAKE_CAPACITY", "Direct Lake requires a suitable Fabric capacity, supported source region and cloud connection permissions. New tables require framing; querying selected columns can load entire columns into memory. No source access or capacity limit is proven by metadata preview.", AuthoringIssueSeverity.Warning));
        }
        if (mode == FabricStorageMode.DirectLakeOneLake)
        {
            if (!string.Equals(schema.Source.Format, "DELTA", StringComparison.OrdinalIgnoreCase)) Error(issues, "FABRIC_DELTA_REQUIRED", "OneLake requires a verified Delta table schema. Use OneLake discovery; SQL metadata does not prove Delta compatibility.");
            if (schema.Source.IsView) Error(issues, "FABRIC_VIEW", "A non-materialized SQL view cannot be imported as Direct Lake on OneLake. A materialized view is supported when discovery verifies its backing Delta table.");
            issues.Add(new("FABRIC_ONELAKE_SECURITY", "OneLake access does not inherit SQL endpoint row/column security and has no DirectQuery fallback. Review the actual OneLake/cloud-connection identity and semantic-model roles.", AuthoringIssueSeverity.Warning));
        }
        else
        {
            if (schema.Source.SqlEndpoint == null) Error(issues, "FABRIC_SQL_ENDPOINT", "This mode requires a discovered SQL endpoint and database. No endpoint is guessed from an item display name.");
            if (mode == FabricStorageMode.DirectLakeSql)
            {
                if (schema.Source.ItemKind is not ("Lakehouse" or "Warehouse")) Error(issues, "FABRIC_SQL_DIRECTLAKE_SOURCE", "Direct Lake on SQL supports a Lakehouse or Warehouse source. Use OneLake/Import/DirectQuery for other discovered item types.");
                if (schema.Source.SqlEndpoint != null && !Guid.TryParse(schema.Source.SqlEndpoint.Database, out _)) Error(issues, "FABRIC_SQL_ID", "Direct Lake on SQL needs the endpoint database GUID for supported Fabric edit/refresh workflows.");
                if (schema.Source.IsView) issues.Add(new("FABRIC_SQL_VIEW_FALLBACK", "This SQL view uses DirectQuery fallback, subject to the model's DirectLakeBehavior setting and source permissions. It is not a Delta-memory performance claim.", AuthoringIssueSeverity.Warning));
            }
        }
        var collations = schema.Columns.Select(column => column.Collation).Where(value => !string.IsNullOrEmpty(value)).Distinct().ToArray();
        issues.Add(new("FABRIC_COLLATION", "Model collation: " + (handler.Model.Collation ?? "engine default") + ". Captured source collations: " + (collations.Length == 0 ? "unavailable" : string.Join(", ", collations)) + ". Review text equality, sorting, relationships and SQL fallback; no collation is changed automatically.", AuthoringIssueSeverity.Warning));
        return issues;
    }
    private void ValidateModes(FabricStorageMode mode, FabricSourceRef source, Table? replacing, List<AuthoringIssue> issues)
    {
        foreach (var table in handler.Model.Tables.Where(table => !ReferenceEquals(table, replacing) && table is not CalculationGroupTable))
        {
            foreach (var partition in table.Partitions)
            {
                if (EffectiveMode(partition) != ModeType.DirectLake)
                {
                    if (mode == FabricStorageMode.DirectLakeSql) Error(issues, "FABRIC_MIXED_MODE", "Direct Lake on SQL cannot be mixed with the existing storage mode on " + table.Name + ". Parameter-table exceptions need separate verified authoring.");
                    continue;
                }
                var existing = DirectLakeKind(partition);
                if (existing == null) { Error(issues, "FABRIC_UNKNOWN_DIRECTLAKE", "The existing Direct Lake expression on " + table.Name + " cannot be classified safely. Review its source expression before mixing modes."); continue; }
                if (existing == FabricStorageMode.DirectLakeSql || mode == FabricStorageMode.DirectLakeSql)
                {
                    if (existing != mode) Error(issues, "FABRIC_MIXED_MODE", "SQL-backed Direct Lake cannot be mixed with OneLake, Import or DirectQuery tables.");
                    else if (partition is EntityPartition entity && CanonicalM(entity.ExpressionSource?.Expression ?? "") != CanonicalM(ConnectionM(source, mode)))
                        Error(issues, "FABRIC_SINGLE_SQL_SOURCE", "All SQL-backed Direct Lake tables must use the same source. The existing connection differs from this captured source.");
                }
            }
        }
    }
    private static DataType Map(FabricColumnSchema column, FabricSourceRef source, FabricStorageMode mode, List<AuthoringIssue> issues)
    {
        var raw = column.SourceType.Trim().ToLowerInvariant(); var type = Regex.Replace(raw, @"\s*\(.*\)$", "");
        switch (type)
        {
            case "long": case "integer": case "int": case "bigint": case "smallint": case "tinyint": case "short": case "byte": return DataType.Int64;
            case "float": case "real": case "double": return DataType.Double;
            case "decimal": case "numeric":
                issues.Add(new("FABRIC_DECIMAL_MAPPING", column.Name + ": " + raw + " maps to floating Decimal Number (Double). Precision can change. Review fixed-decimal/integer source modeling if exact arithmetic is required.", AuthoringIssueSeverity.Warning)); return DataType.Double;
            case "money": case "smallmoney": return DataType.Decimal;
            case "string": case "varchar": case "nvarchar": case "char": case "nchar": case "text": case "ntext": return DataType.String;
            case "boolean": case "bool": case "bit": return DataType.Boolean;
            case "date": case "datetime": case "datetime2": case "smalldatetime": case "timestamp_ntz": return DataType.DateTime;
            case "timestamp" when string.Equals(source.Format, "DELTA", StringComparison.OrdinalIgnoreCase): return DataType.DateTime;
            case "uniqueidentifier" when mode is FabricStorageMode.Import or FabricStorageMode.DirectQuery:
                issues.Add(new("FABRIC_GUID_TEXT", column.Name + ": SQL uniqueidentifier maps to text through Power Query. Direct Lake does not support GUID semantic columns.", AuthoringIssueSeverity.Warning)); return DataType.String;
            default:
                Error(issues, "FABRIC_UNSUPPORTED_TYPE", column.Name + ": source type " + raw + " has no lossless supported mapping in this wizard. Exclude it or transform it upstream; binary/complex/GUID Direct Lake columns are not silently coerced."); return DataType.String;
        }
    }
    private static string ExpressionName(FabricSourceRef source, FabricStorageMode mode)
    {
        using var hash = SHA256.Create();
        var identity = source.WorkspaceId + "|" + source.ItemId + "|" + mode + "|" + source.SqlEndpoint?.Server + "|" + source.SqlEndpoint?.Database;
        return "PbiBench Fabric " + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").Substring(0, 16);
    }
    // M treats #(...) as escapes inside strings; escape the introducer before quoting.
    internal static string MText(string value) => "\"" + value.Replace("#", "#(#)").Replace("\"", "\"\"") + "\"";
    internal static string ConnectionM(FabricSourceRef source, FabricStorageMode mode) => mode == FabricStorageMode.DirectLakeOneLake
        ? "let\n    Source = AzureStorage.DataLake(" + MText("https://onelake.dfs.fabric.microsoft.com/" + source.WorkspaceId + "/" + source.ItemId) + ", [HierarchicalNavigation=true])\nin\n    Source"
        : "let\n    Source = Sql.Database(" + MText(source.SqlEndpoint?.Server ?? "") + ", " + MText(source.SqlEndpoint?.Database ?? "") + ", [CreateNavigationProperties=false])\nin\n    Source";
    internal static string ImportM(FabricSourceRef source) => "let\n    Source = Sql.Database(" + MText(source.SqlEndpoint?.Server ?? "") + ", " + MText(source.SqlEndpoint?.Database ?? "") + ", [CreateNavigationProperties=false]),\n    Data = Source{[Schema=" + MText(source.Schema) + ",Item=" + MText(source.Table) + "]}[Data]\nin\n    Data";
    private static string CanonicalM(string expression)
    {
        var text = new StringBuilder(); var quoted = false;
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (character == '"') { text.Append(character); if (quoted && index + 1 < expression.Length && expression[index + 1] == '"') text.Append(expression[++index]); else quoted = !quoted; }
            else if (quoted || !char.IsWhiteSpace(character)) text.Append(character);
        }
        return text.ToString();
    }
    private static FabricStorageMode? DirectLakeKind(Partition partition)
    {
        if (partition is not EntityPartition entity || entity.ExpressionSource == null) return null;
        var expression = CanonicalM(entity.ExpressionSource.Expression ?? "");
        if (Regex.IsMatch(expression, "^letSource=AzureStorage\\.DataLake\\(\"https://onelake\\.dfs\\.fabric\\.microsoft\\.com/[0-9a-fA-F-]{36}/[0-9a-fA-F-]{36}\",\\[HierarchicalNavigation=true\\]\\)inSource$", RegexOptions.CultureInvariant)) return FabricStorageMode.DirectLakeOneLake;
        if (Regex.IsMatch(expression, "^letSource=Sql\\.Database\\(\"[A-Za-z0-9.-]+\",\"[0-9a-fA-F-]{36}\",\\[CreateNavigationProperties=false\\]\\)inSource$", RegexOptions.CultureInvariant)) return FabricStorageMode.DirectLakeSql;
        return null;
    }
}
