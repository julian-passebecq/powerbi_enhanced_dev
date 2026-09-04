using System.Globalization;
using PbiBench.Core.Queries;

namespace PbiBench.Core.DataExploration;

/// <summary>Called by the explicit Verify paging action: key verification can scan the entire visible table.</summary>
public sealed class DataPreviewCapabilityService(IDaxQueryService queries)
{
    public async Task<DataPreviewCapabilities> VerifyPagingAsync(QueryRequest connection, DataTableSchema table, CancellationToken cancellationToken)
    {
        if (connection == null) throw new ArgumentNullException(nameof(connection));
        if (table == null) throw new ArgumentNullException(nameof(table));
        cancellationToken.ThrowIfCancellationRequested();
        DataPreviewCapabilities Result(WindowSupport support, IReadOnlyList<string> keys, string message) =>
            new(support, keys, message) { TableName = table.Name, Server = connection.Server, Database = connection.Database };
        if (table.StorageMode != DataStorageMode.Import)
            return Result(WindowSupport.Unknown, Array.Empty<string>(), "Stable paging verification is available for Import tables. This storage mode uses first-N preview.");
        if (table.CandidateKeyColumns.Count == 0)
            return Result(WindowSupport.Unknown, Array.Empty<string>(), "No candidate key is declared in the model metadata. First-N preview remains available.");
        var supported = false;
        try
        {
            await queries.ExecuteAsync(connection with { Query = DataPreviewBuilder.BuildWindowProbe(table), RowLimit = 1, MaximumResultSets = 1 }, cancellationToken).ConfigureAwait(false);
            supported = true;
            var keyResult = await queries.ExecuteAsync(connection with { Query = DataPreviewBuilder.BuildKeyVerification(table), RowLimit = 1, MaximumResultSets = 1 }, cancellationToken).ConfigureAwait(false);
            var result = keyResult.Results.SingleOrDefault();
            if (result == null || result.IsTruncated || result.Rows.Count != 1 || result.Columns.Count != 2 || result.Rows[0].Length != 2)
                return Result(WindowSupport.Supported, Array.Empty<string>(), "The key verification result was incomplete. First-N preview remains active.");
            var rows = Count(result.Rows[0][0]); var distinctKeys = Count(result.Rows[0][1]);
            if (rows != distinctKeys)
                return Result(WindowSupport.Supported, Array.Empty<string>(), $"The candidate key is not unique: {rows:N0} rows and {distinctKeys:N0} distinct keys. First-N preview remains active.");
            return Result(WindowSupport.Supported, Array.AsReadOnly(table.CandidateKeyColumns.ToArray()), $"WINDOW is supported and the candidate key uniquely identifies {rows:N0} visible rows. Reverify after a model refresh or connection change.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is QueryExecutionException or TimeoutException or InvalidOperationException or FormatException or OverflowException)
        {
            // A timeout or permission failure does not establish that the function is unsupported.
            return Result(supported ? WindowSupport.Supported : WindowSupport.Unknown, Array.Empty<string>(), "Paging verification did not complete; first-N preview remains active. " + ex.Message);
        }
    }

    private static long Count(object? value)
    {
        if (value == null || value == DBNull.Value) throw new FormatException("The verification query did not return row counts.");
        var count = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        if (count < 0 || count != decimal.Truncate(count) || count > long.MaxValue) throw new FormatException("The verification result contained an invalid row count.");
        return (long)count;
    }
}
