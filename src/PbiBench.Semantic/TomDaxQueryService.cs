using System.Data;
using System.Data.Common;
using System.Xml.Linq;
using Microsoft.AnalysisServices;
using PbiBench.Core.Queries;
using TOM = Microsoft.AnalysisServices.Tabular;

namespace PbiBench.Semantic;

/// <summary>Public Microsoft XMLA/TOM transport. Each execution owns a separate connection and session.</summary>
public sealed class TomDaxQueryService : DaxQueryService
{
    public TomDaxQueryService() : base(new TomQuerySessionFactory()) { }
    public TomDaxQueryService(IQuerySessionFactory sessions) : base(sessions) { }
}

public sealed class TomQuerySessionFactory : IQuerySessionFactory
{
    private readonly Action<TOM.Server>? configureAuthentication;
    /// <param name="configureAuthentication">Optional transient token configuration on each newly created server; never persisted.</param>
    public TomQuerySessionFactory(Action<TOM.Server>? configureAuthentication = null) => this.configureAuthentication = configureAuthentication;
    public IQuerySession Create() => new TomQuerySession(configureAuthentication);
}

internal sealed class TomQuerySession : IQuerySession
{
    private readonly TOM.Server server = new();
    private readonly Action<TOM.Server>? configureAuthentication;
    private QueryRequest? request;
    private bool disposed;
    internal TomQuerySession(Action<TOM.Server>? configureAuthentication) => this.configureAuthentication = configureAuthentication;

    public void Open(QueryRequest request)
    {
        this.request = request;
        try
        {
            var connectionString = BuildConnectionString(request);
            configureAuthentication?.Invoke(server);
            // Query execution needs no metadata enumeration on its private connection.
            server.Connect(connectionString, propertiesOnly: true);
        }
        catch (Exception)
        {
            // Connection exceptions can contain credentials. Keep them out of the exception chain/logs.
            throw new QueryExecutionException($"Could not open a DAX query connection to {request.Server}/{request.Database}. Verify the endpoint, database, and authentication.");
        }
    }

    internal static string BuildConnectionString(QueryRequest request)
    {
        var connection = new DbConnectionStringBuilder { ConnectionString = request.ConnectionString ?? string.Empty };
        // The authentication snapshot may use aliases. Remove routing/session aliases before
        // applying this run's captured target, and never borrow the model editor's session.
        foreach (var key in connection.Keys.Cast<string>().ToArray())
        {
            var normalized = key.Replace(" ", "").ToLowerInvariant();
            if (normalized == "datasource" || normalized == "server" || normalized == "address" || normalized == "networkaddress" ||
                normalized == "catalog" || normalized == "initialcatalog" || normalized == "sessionid" || normalized == "session" ||
                normalized == "applicationname" || normalized == "connecttimeout" || normalized == "timeout") connection.Remove(key);
        }
        connection["Data Source"] = request.Server; connection["Initial Catalog"] = request.Database;
        connection["Connect Timeout"] = Math.Min(15, request.TimeoutSeconds); connection["Timeout"] = request.TimeoutSeconds;
        connection["Application Name"] = "PbiBench DAX";
        return connection.ConnectionString;
    }

    public IDataReader Execute(string query)
    {
        if (request == null || !server.Connected) throw new QueryExecutionException("Connect to a live semantic model before running DAX.");
        var reader = server.ExecuteReader(BuildStatement(query), out var results, new Dictionary<string, string> { ["Catalog"] = request.Database });
        if (results != null && results.ContainsErrors)
        {
            reader?.Dispose();
            var errors = results.Cast<XmlaResult>().SelectMany(r => r.Messages.Cast<XmlaMessage>()).OfType<XmlaError>().Select(e => e.Description).ToArray();
            throw new QueryExecutionException(SafeMessage(string.Join(Environment.NewLine, errors)));
        }
        return reader ?? throw new QueryExecutionException("The XMLA endpoint did not return a DAX result.");
    }

    public void Cancel()
    {
        if (!disposed && server.Connected) server.CancelCommand();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; server.Dispose();
    }

    private string SafeMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return "The server rejected the DAX query.";
        if (!string.IsNullOrEmpty(request?.ConnectionString))
        {
            message = message.Replace(request!.ConnectionString!, "[connection]");
            var values = new DbConnectionStringBuilder { ConnectionString = request.ConnectionString };
            foreach (string key in values.Keys)
                if (key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || key.Equals("pwd", StringComparison.OrdinalIgnoreCase) || key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var secret = Convert.ToString(values[key]);
                    if (!string.IsNullOrEmpty(secret)) message = message.Replace(secret, "[redacted]");
                }
        }
        return message;
    }

    internal static string BuildStatement(string query) => new XElement("Statement", query).ToString(SaveOptions.DisableFormatting);
}
