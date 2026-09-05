using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using PbiBench.Core.Fabric;
using PbiBench.Fabric;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class FabricSqlTests
{
    [Fact]
    public void SqlGenerationEscapesIdentifiersAndRejectsUnknownColumnsOrBudgetOverflow()
    {
        var source = FabricTransportTests.Source() with { Schema = "own]er's", Table = "T];DROP TABLE X;--" };
        var columns = new[] { new FabricColumnSchema("a]b", "string", true) };
        var schema = new FabricTableSchema(source, columns, FabricSchemaRules.Fingerprint(source, columns), DateTimeOffset.UtcNow, Array.Empty<string>());
        Assert.Equal("SELECT TOP (101) [a]]b] FROM [own]]er's].[T]];DROP TABLE X;--]", FabricSqlDataService.PreviewSql(new(schema, new[] { "a]b" })));
        Assert.Throws<ArgumentException>(() => FabricSqlDataService.PreviewSql(new(schema, new[] { "unknown" })));
        Assert.Throws<ArgumentException>(() => FabricSqlDataService.PreviewSql(new(schema, new[] { "a]b" }, 1001)));
    }
    [Fact]
    public void RealSqlConnectionFactoryUsesEncryptedPrivateSessionAndTransientToken()
    {
        using var connection = new FabricSqlConnectionFactory().Create(FabricTransportTests.Source().SqlEndpoint!, "transient-token");
        var sql = Assert.IsType<SqlConnection>(connection); var settings = new SqlConnectionStringBuilder(sql.ConnectionString);
        Assert.False(settings.Pooling); Assert.False(settings.TrustServerCertificate); Assert.False(settings.PersistSecurityInfo);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, settings.Encrypt); Assert.Equal("transient-token", sql.AccessToken);
        Assert.DoesNotContain("transient-token", sql.ConnectionString);
    }
    [Fact]
    public async Task PreviewUsesOnePrivateSessionPerRunAndReportsRowBound()
    {
        var factory = new Factory(() => Rows(1L, 2L, 3L)); var tokens = new FabricTransportTests.Tokens(); var service = new FabricSqlDataService(tokens, factory);
        for (var index = 0; index < 2; index++)
        {
            var result = await service.PreviewAsync(new(FabricTransportTests.Schema(), new[] { "Id" }, 2), CancellationToken.None);
            Assert.Equal(2, result.Result.Rows.Count); Assert.True(result.Result.IsTruncated); Assert.Equal(1L, result.Result.Rows[0][0]);
        }
        Assert.Equal(2, factory.Connections.Count); Assert.All(factory.Connections, connection => { Assert.True(connection.WasDisposed); Assert.Equal(CommandBehavior.SequentialAccess, connection.Behavior); Assert.StartsWith("SELECT TOP (3)", connection.Sql); });
        Assert.All(tokens.Requests, scopes => Assert.Equal("https://database.windows.net/.default", Assert.Single(scopes)));
    }
    [Fact]
    public async Task PreviewCancellationDisposesConnectingSessionAndNeverExecutes()
    {
        var factory = new Factory(() => Rows(1L)) { WaitOnOpen = true }; using var ct = new CancellationTokenSource();
        var run = new FabricSqlDataService(new FabricTransportTests.Tokens(), factory).PreviewAsync(new(FabricTransportTests.Schema(), new[] { "Id" }), ct.Token);
        await factory.OpenStarted.Task; ct.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var connection = Assert.Single(factory.Connections); Assert.True(connection.WasDisposed); Assert.Null(connection.Sql);
    }
    [Fact]
    public async Task SqlMetadataParametersNamesAndPreservesBinaryTimestampSemantics()
    {
        var metadata = new DataTable();
        metadata.Columns.Add("name", typeof(string)); metadata.Columns.Add("type", typeof(string)); metadata.Columns.Add("nullable", typeof(bool)); metadata.Columns.Add("ordinal", typeof(int)); metadata.Columns.Add("collation", typeof(string)); metadata.Columns.Add("precision", typeof(byte)); metadata.Columns.Add("scale", typeof(byte));
        metadata.Rows.Add("Version", "timestamp", false, 1, DBNull.Value, (byte)0, (byte)0);
        var factory = new Factory(() => metadata.CreateDataReader()); var source = FabricTransportTests.Source() with { Format = "SQL", Table = "O'Brien" };
        var result = await new FabricSqlDataService(new FabricTransportTests.Tokens(), factory).GetSchemaAsync(source, CancellationToken.None);
        Assert.Equal("SQL", result.Source.Format); Assert.Equal("timestamp", Assert.Single(result.Columns).SourceType);
        var connection = Assert.Single(factory.Connections); Assert.DoesNotContain("O'Brien", connection.Sql); Assert.Equal("O'Brien", connection.Parameters["@table"]);
        FabricSchemaRules.Validate(result);
    }
    [Fact]
    public async Task SqlErrorsDoNotExposeProviderCredentials()
    {
        var factory = new Factory(() => throw new ProviderException("Password=secret-token"));
        var error = await Assert.ThrowsAsync<FabricApiException>(() => new FabricSqlDataService(new FabricTransportTests.Tokens(), factory).PreviewAsync(new(FabricTransportTests.Schema(), new[] { "Id" }), CancellationToken.None));
        Assert.DoesNotContain("secret-token", error.ToString()); Assert.True(Assert.Single(factory.Connections).WasDisposed);
    }
    private static DbDataReader Rows(params long[] values) { var table = new DataTable(); table.Columns.Add("Id", typeof(long)); foreach (var value in values) table.Rows.Add(value); return table.CreateDataReader(); }
    private sealed class ProviderException(string message) : DbException(message);
    private sealed class Factory(Func<DbDataReader> rows) : IFabricSqlConnectionFactory
    {
        public bool WaitOnOpen { get; init; }
        public TaskCompletionSource<bool> OpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<Connection> Connections { get; } = new();
        public DbConnection Create(FabricSqlEndpoint endpoint, string accessToken) { var connection = new Connection(this, rows); Connections.Add(connection); return connection; }
    }
#pragma warning disable CS8765 // DbConnection/DbCommand setter annotations differ between net48 and current .NET.
    private sealed class Connection(Factory factory, Func<DbDataReader> rows) : DbConnection
    {
        public bool WasDisposed { get; private set; }
        public string? Sql { get; set; }
        public CommandBehavior Behavior { get; set; }
        public Dictionary<string, object> Parameters { get; } = new();
        public override string ConnectionString { get; set; } = "";
        public override string Database => "fixture"; public override string DataSource => "fixture"; public override string ServerVersion => "1"; public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException(); public override void Close() { } public override void Open() { }
        public override async Task OpenAsync(CancellationToken cancellationToken) { factory.OpenStarted.TrySetResult(true); if (factory.WaitOnOpen) await Task.Delay(Timeout.Infinite, cancellationToken); cancellationToken.ThrowIfCancellationRequested(); }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new Command(this, rows);
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
    private sealed class Command(Connection connection, Func<DbDataReader> rows) : DbCommand
    {
        private readonly SqlCommand parameters = new();
        public override string CommandText { get; set; } = ""; public override int CommandTimeout { get; set; } public override CommandType CommandType { get; set; } public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection; protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
        public override void Cancel() { } public override int ExecuteNonQuery() => throw new NotSupportedException(); public override object ExecuteScalar() => throw new NotSupportedException(); public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new SqlParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        { connection.Sql = CommandText; connection.Behavior = behavior; foreach (DbParameter parameter in parameters.Parameters) connection.Parameters[parameter.ParameterName] = parameter.Value!; return rows(); }
        protected override void Dispose(bool disposing) { parameters.Dispose(); base.Dispose(disposing); }
    }
#pragma warning restore CS8765
}
