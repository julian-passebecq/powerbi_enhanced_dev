namespace PbiBench.Core.DataExploration;

public enum DataPreviewMode { StableWindow, FirstN }
public enum WindowSupport { Unknown, Supported, Unsupported }
public sealed record DataPreviewCapabilities(WindowSupport WindowSupport, IReadOnlyList<string> VerifiedKeyColumns,
    string VerificationMessage = "")
{
    public string? TableName { get; init; }
    public string? Server { get; init; }
    public string? Database { get; init; }
    public static DataPreviewCapabilities Unverified { get; } = new(WindowSupport.Unknown, Array.Empty<string>());
}
public sealed record DataPreviewRequest(string TableName, int Offset = 0, int PageSize = 200)
{
    public IReadOnlyList<DataSort> Sort { get; init; } = Array.Empty<DataSort>();
    public IReadOnlyList<DataFilter> Filters { get; init; } = Array.Empty<DataFilter>();
}
public sealed record DataPreviewPlan(string Query, DataPreviewMode Mode, int Offset, int PageSize, IReadOnlyList<string> Warnings)
{
    public bool CanPage => Mode == DataPreviewMode.StableWindow;
}

/// <summary>Query planning is detached from transport and never treats key metadata as a uniqueness proof.</summary>
public static class DataPreviewBuilder
{
    public static DataPreviewPlan Build(DataModelSchema model, DataPreviewRequest request, DataPreviewCapabilities? capabilities = null)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Offset < 0 || request.Offset > int.MaxValue - 10001) throw new ArgumentOutOfRangeException(nameof(request.Offset));
        if (request.PageSize < 1 || request.PageSize > 10000) throw new ArgumentOutOfRangeException(nameof(request.PageSize), "Preview pages must contain 1 to 10,000 rows.");
        var table = model.GetTable(request.TableName);
        if (table.Columns.Count == 0) throw new ArgumentException("The selected table has no previewable columns.");
        capabilities ??= DataPreviewCapabilities.Unverified;
        var keys = capabilities.VerifiedKeyColumns.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var capabilityMatches = capabilities.TableName == null || string.Equals(capabilities.TableName, table.Name, StringComparison.OrdinalIgnoreCase);
        var canPage = table.StorageMode == DataStorageMode.Import && capabilityMatches && capabilities.WindowSupport == WindowSupport.Supported && keys.Length > 0;
        if (canPage) foreach (var key in keys) ResolveColumn(table, key);
        var warnings = new List<string>();
        if (!canPage)
        {
            warnings.Add(table.StorageMode == DataStorageMode.Import
                ? "First-N preview: stable paging requires a successful WINDOW capability check and an explicitly verified unique key."
                : $"{table.StorageMode} uses an explicit first-N server query; additional client pages are not implied.");
            if (request.Offset != 0) warnings.Add("The requested offset was reset to zero because stable paging is unavailable.");
        }
        else warnings.Add("Stable paging assumes the model data does not change between pages. Reverify after a model refresh or connection change.");
        if (table.StorageMode == DataStorageMode.DirectLake)
            warnings.Add("Direct Lake preview runs through the connected semantic engine and may load queried columns into capacity memory.");
        if (table.StorageMode is DataStorageMode.DirectQuery or DataStorageMode.Dual or DataStorageMode.Mixed)
            warnings.Add("This query may contact the source system. Returned row limits do not bound source query work.");
        var ordering = request.Sort.Select(sort => new DataSort(ResolveColumn(table, sort.Column).Name, sort.Descending)).ToList();
        var duplicate = ordering.GroupBy(sort => sort.Column, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null) throw new ArgumentException("A sort column may only be specified once: " + duplicate.Key);
        foreach (var key in canPage ? keys : table.CandidateKeyColumns)
            if (!ordering.Any(sort => string.Equals(sort.Column, key, StringComparison.OrdinalIgnoreCase))) ordering.Add(new DataSort(ResolveColumn(table, key).Name));
        if (ordering.Count == 0) ordering.Add(new DataSort(table.Columns[0].Name));
        var relation = DaxDataSyntax.Table(table.Name);
        if (request.Filters.Count > 0)
        {
            if (request.Filters.Count > 100) throw new ArgumentException("A preview supports at most 100 typed filters.");
            var predicates = request.Filters.Select(filter =>
            {
                if (!string.Equals(filter.Table, table.Name, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Preview filters must belong to the selected table.");
                return "(" + DaxDataSyntax.Predicate(filter with { Table = table.Name }, ResolveColumn(table, filter.Column)) + ")";
            });
            relation = "FILTER(" + relation + ", " + string.Join(" && ", predicates) + ")";
        }
        var order = string.Join(", ", ordering.Select(sort => DaxDataSyntax.Column(table.Name, sort.Column) + (sort.Descending ? ", DESC" : ", ASC")));
        var displayOrder = string.Join(", ", ordering.Select(sort => DaxDataSyntax.Column(table.Name, sort.Column) + (sort.Descending ? " DESC" : " ASC")));
        string query;
        if (canPage)
            query = $"EVALUATE\nWINDOW({request.Offset + 1}, ABS, {request.Offset + request.PageSize}, ABS, {relation}, ORDERBY({order}))\nORDER BY {displayOrder}";
        else
        {
            var topOrder = string.Join(", ", ordering.Select(sort => DaxDataSyntax.Column(table.Name, sort.Column) + (sort.Descending ? ", 0" : ", 1")));
            query = $"EVALUATE\nTOPN({request.PageSize}, {relation}, {topOrder})\nORDER BY {displayOrder}";
            warnings.Add("TOPN may return extra tied rows; the preview client retains only its configured row limit and reports truncation.");
        }
        return new(query, canPage ? DataPreviewMode.StableWindow : DataPreviewMode.FirstN, canPage ? request.Offset : 0, request.PageSize, warnings.AsReadOnly());
    }

    public static string BuildWindowProbe(DataTableSchema table)
    {
        if (table.Columns.Count == 0) throw new ArgumentException("A capability probe requires a table with columns.");
        var orderColumn = table.CandidateKeyColumns.FirstOrDefault() ?? table.Columns[0].Name;
        ResolveColumn(table, orderColumn);
        return $"EVALUATE\nWINDOW(1, ABS, 1, ABS, FILTER({DaxDataSyntax.Table(table.Name)}, FALSE()), ORDERBY({DaxDataSyntax.Column(table.Name, orderColumn)}, ASC))";
    }

    public static string BuildKeyVerification(DataTableSchema table, IReadOnlyList<string>? candidateKeys = null)
    {
        var keys = (candidateKeys ?? table.CandidateKeyColumns).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (keys.Length == 0) throw new ArgumentException("The table has no candidate key columns. First-N preview remains available.");
        foreach (var key in keys) ResolveColumn(table, key);
        return $"EVALUATE\nROW(\"Rows\", COALESCE(COUNTROWS({DaxDataSyntax.Table(table.Name)}), 0), \"DistinctKeys\", COALESCE(COUNTROWS(SUMMARIZE({DaxDataSyntax.Table(table.Name)}, {string.Join(", ", keys.Select(key => DaxDataSyntax.Column(table.Name, key)))})), 0))";
    }

    private static DataColumnSchema ResolveColumn(DataTableSchema table, string name) => table.Columns.FirstOrDefault(column => string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Column '{name}' is not present in table '{table.Name}'.");
}
