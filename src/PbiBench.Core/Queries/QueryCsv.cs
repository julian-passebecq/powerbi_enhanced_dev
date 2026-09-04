using System.Globalization;
using System.Text;

namespace PbiBench.Core.Queries;

public static class QueryCsv
{
    public static string ToCsv(QueryResultSet result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Write(result, writer, CancellationToken.None); return writer.ToString();
    }

    public static Task ExportAsync(QueryResultSet result, string path, CancellationToken cancellationToken)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a CSV file.", nameof(path));
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(path); var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(true))) Write(result, writer, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, cancellationToken);
    }

    private static void Write(QueryResultSet result, TextWriter writer, CancellationToken token)
    {
        writer.Write(string.Join(",", result.Columns.Select(c => Escape(c.Name)))); writer.Write("\r\n");
        foreach (var row in result.Rows)
        {
            token.ThrowIfCancellationRequested();
            writer.Write(string.Join(",", row.Select(v => Escape(Format(v))))); writer.Write("\r\n");
        }
    }
    private static string Format(object? value) => value == null || value == DBNull.Value ? string.Empty : value is DateTime date ? date.ToString("O", CultureInfo.InvariantCulture) :
        value is DateTimeOffset offset ? offset.ToString("O", CultureInfo.InvariantCulture) : value is double number ? number.ToString("R", CultureInfo.InvariantCulture) :
        value is float single ? single.ToString("R", CultureInfo.InvariantCulture) : value is byte[] bytes ? Convert.ToBase64String(bytes) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Escape(string value) => value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
