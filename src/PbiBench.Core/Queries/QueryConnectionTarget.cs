using System.Data.Common;

namespace PbiBench.Core.Queries;

/// <summary>Extracts the public routing endpoint without forwarding transport authentication into UI/history.</summary>
public static class QueryConnectionTarget
{
    public static string? Server(string? connectionString, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return fallback;
        DbConnectionStringBuilder connection;
        try { connection = new DbConnectionStringBuilder { ConnectionString = connectionString }; }
        catch (ArgumentException) { throw new InvalidOperationException("The model connection string could not be read. Reconnect to the model before opening a query session."); }
        var endpoints = connection.Keys.Cast<string>().Where(key => IsServerKey(key)).Select(key => Convert.ToString(connection[key])?.Trim())
            .Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (endpoints.Length > 1) throw new InvalidOperationException("The model connection contains conflicting server aliases. Reconnect with one endpoint before opening a query session.");
        return endpoints.FirstOrDefault() ?? fallback;
    }
    public static bool IsServerKey(string key)
    {
        var normalized = key.Replace(" ", "").ToLowerInvariant();
        return normalized is "datasource" or "server" or "address" or "addr" or "networkaddress";
    }
}
