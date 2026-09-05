using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AnalysisServices;
using PbiBench.Core.Domain;
using PbiBench.Core.Queries;
using PbiBench.Core.Workspaces;
using PbiBench.Workspace;
using TOM = Microsoft.AnalysisServices.Tabular;

namespace PbiBench.Semantic.Workspaces;

public sealed record WorkspacePushResult(string BackupPath, WorkspaceLiveCapture Live);
public sealed class TomWorkspaceSyncService
{
    private readonly IWorkspaceLiveSessionFactory sessions;
    private readonly Dictionary<Guid, WorkspacePushPlan> issued = new();
    public TomWorkspaceSyncService(IWorkspaceLiveSessionFactory? sessions = null) => this.sessions = sessions ?? new TomWorkspaceLiveSessionFactory();
    public Task<WorkspaceLiveCapture> CaptureAsync(WorkspaceConnection connection, CancellationToken ct) => WithSession(connection, (session, _) => session.Capture(), ct);
    public WorkspacePushPlan PreparePush(WorkspaceComparison comparison, WorkspaceLiveCapture live, WorkspaceConnection connection, string diskHash, bool resolveConflictsUsingDisk = false)
    {
        if (comparison.Live == null || comparison.Live.Hash != live.Snapshot.Hash) throw new InvalidOperationException("Capture the current live model before previewing a push.");
        if (comparison.HasUnsavedModelEdits) throw new InvalidOperationException("The loaded editor has unsaved metadata changes. Save or discard them before pushing disk metadata.");
        if (comparison.HasConflicts && !resolveConflictsUsingDisk) throw new InvalidOperationException("Resolve the displayed divergent changes explicitly before pushing.");
        var differences = WorkspaceSemanticDiff.Between(live.Snapshot, comparison.Disk); if (differences.Count == 0) throw new InvalidOperationException("Disk and Live already have matching metadata.");
        using var source = System.Text.Json.JsonDocument.Parse(comparison.Disk.DatabaseJson); using var destination = System.Text.Json.JsonDocument.Parse(live.Snapshot.DatabaseJson);
        if (source.RootElement.GetProperty("compatibilityLevel").GetInt32() != destination.RootElement.GetProperty("compatibilityLevel").GetInt32()) throw new InvalidOperationException("Workspace push requires matching compatibility levels; it never upgrades or downgrades the live model.");
        var tmsl = CreateOrReplace(comparison.Disk, live.DatabaseName, live.DatabaseId);
        var rows = differences.Select(row => new PlannedChange(row.ObjectPath, row.Property, WorkspaceSemanticDiff.DisplayValue(row.ObjectPath + "/" + row.Property, row.Baseline), WorkspaceSemanticDiff.DisplayValue(row.ObjectPath + "/" + row.Property, row.Disk), new[] { "Full metadata replacement on the explicit target; removed metadata and changed partitions may require data refresh." })).ToArray();
        var plan = new ChangePlan(Guid.NewGuid(), DateTimeOffset.UtcNow, ApprovalLevel.RemoteModelWrite, new("xmla", null, connection.Server, live.DatabaseId, "SemanticModel", live.DatabaseName), rows, "Fresh live BIM metadata snapshot before execution", "Private XMLA transaction rollback on failure; BIM recovery snapshot retained; processed data cannot be restored from metadata");
        var prepared = new WorkspacePushPlan(connection, live, comparison.Disk, tmsl, diskHash, plan); lock (issued) { if (issued.Count >= 32) issued.Clear(); issued.Add(plan.Id, prepared); } return prepared;
    }
    public async Task<WorkspacePushResult> ApplyPushAsync(WorkspacePushPlan prepared, ApprovedChangePlan approval, Func<CancellationToken, string> currentDiskHash, string backupDirectory, CancellationToken ct, Action? onRemoteDispatch = null)
    {
        WorkspaceApproval.Validate(prepared.Plan, approval);
        lock (issued)
        {
            if (!issued.TryGetValue(prepared.Plan.Id, out var original) || !ReferenceEquals(original, prepared) || !ReferenceEquals(approval.Plan, prepared.Plan) || string.IsNullOrWhiteSpace(approval.ApprovalActor)) throw new InvalidOperationException("This workspace push is unapproved, foreign or already consumed.");
            issued.Remove(prepared.Plan.Id);
        }
        return await WithSession(prepared.Connection, (session, token) =>
        {
            token.ThrowIfCancellationRequested(); if (currentDiskHash(token) != prepared.DiskHash) throw new InvalidOperationException("Disk changed after push preview. Compare again.");
            session.BeginTransaction(); var transaction = true; string? backup = null;
            try
            {
                var fresh = session.Capture();
                if (fresh.DatabaseId != prepared.Before.DatabaseId || fresh.DatabaseName != prepared.Before.DatabaseName || fresh.Snapshot.Hash != prepared.Before.Snapshot.Hash) throw new InvalidOperationException("Live metadata changed after preview. Compare again.");
                var directory = Path.GetFullPath(backupDirectory); WorkspaceDiskStore.RejectLinks(directory); Directory.CreateDirectory(directory); backup = WorkspaceDiskStore.SafePath(directory, prepared.Plan.Id.ToString("N") + ".bim");
                using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { var bytes = new UTF8Encoding(false).GetBytes(fresh.Snapshot.DatabaseJson); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                token.ThrowIfCancellationRequested(); if (currentDiskHash(token) != prepared.DiskHash) throw new InvalidOperationException("Disk changed while preparing the push snapshot.");
                onRemoteDispatch?.Invoke(); session.Execute(prepared.Tmsl); token.ThrowIfCancellationRequested(); session.CommitTransaction(); transaction = false;
                return new WorkspacePushResult(backup, session.Capture());
            }
            catch (Exception error)
            {
                if (transaction) { try { session.RollbackTransaction(); } catch { throw new InvalidOperationException("Workspace push did not complete and rollback could not be confirmed. Reconnect and compare the server before retrying. Recovery snapshot: " + (backup ?? "not created")); } }
                if (error is OperationCanceledException) throw;
                if (error is InvalidOperationException) throw;
                throw new InvalidOperationException("Workspace push failed. Reconnect and compare the server before retrying. Recovery snapshot: " + (backup ?? "not created"));
            }
        }, ct).ConfigureAwait(false);
    }
    public static string CreateOrReplace(WorkspaceSemanticSnapshot source, string databaseName, string databaseId)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(databaseId)) throw new ArgumentException("The resolved database target is required.");
        var database = TOM.JsonSerializer.DeserializeDatabase(source.DatabaseJson); var command = JsonNode.Parse(TOM.JsonScripter.ScriptCreateOrReplace(database, true))!.AsObject();
        var replace = command["createOrReplace"]!.AsObject(); replace["object"] = new JsonObject { ["database"] = databaseName };
        replace["database"]!["name"] = databaseName; replace["database"]!["id"] = databaseId; return command.ToJsonString();
    }
    private async Task<T> WithSession<T>(WorkspaceConnection connection, Func<IWorkspaceLiveSession, CancellationToken, T> operation, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(connection.TimeoutSeconds)); using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try { return await Task.Run(() => { linked.Token.ThrowIfCancellationRequested(); using var session = sessions.Create(); using var cancel = linked.Token.Register(() => { try { session.Cancel(); } catch { } }); session.Open(connection); linked.Token.ThrowIfCancellationRequested(); var result = operation(session, linked.Token); linked.Token.ThrowIfCancellationRequested(); return result; }, CancellationToken.None).ConfigureAwait(false); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested) { throw new TimeoutException("The private workspace connection timed out; reconnect and compare before retrying a write."); }
    }
}
public sealed class TomWorkspaceLiveSessionFactory : IWorkspaceLiveSessionFactory
{
    private readonly Action<TOM.Server>? authentication;
    public TomWorkspaceLiveSessionFactory(Action<TOM.Server>? authentication = null) => this.authentication = authentication;
    public IWorkspaceLiveSession Create() => new TomWorkspaceLiveSession(authentication);
}
internal sealed class TomWorkspaceLiveSession : IWorkspaceLiveSession
{
    private readonly TOM.Server server = new(); private readonly Action<TOM.Server>? authentication; private WorkspaceConnection? connection;
    public TomWorkspaceLiveSession(Action<TOM.Server>? authentication) => this.authentication = authentication;
    public void Open(WorkspaceConnection connection)
    {
        this.connection = connection;
        try { authentication?.Invoke(server); server.Connect(TomQuerySession.BuildConnectionString(new QueryRequest(connection.Server, connection.Database, "EVALUATE {1}", TimeoutSeconds: connection.TimeoutSeconds) { ConnectionString = connection.ConnectionString })); }
        catch { throw new InvalidOperationException("Could not connect to the workspace target. Verify its endpoint, database and authentication."); }
    }
    public WorkspaceLiveCapture Capture()
    {
        try
        {
            server.Refresh(); var matches = server.Databases.Cast<TOM.Database>().Where(database => database.ID == connection!.Database || database.Name == connection.Database).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException(); var database = matches[0]; database.Refresh(true); return new(database.ID, database.Name, WorkspaceSemanticSnapshot.Parse(TOM.JsonSerializer.SerializeDatabase(database)));
        }
        catch { throw new InvalidOperationException("Could not capture the explicit live database, or its identifier is ambiguous. Reconnect and compare."); }
    }
    public void BeginTransaction() { try { server.BeginTransaction(); } catch { throw new InvalidOperationException("The endpoint did not allow a workspace metadata transaction."); } }
    public void CommitTransaction() { try { server.CommitTransaction(); } catch { throw new InvalidOperationException("The workspace transaction could not be confirmed. Reconnect and compare before retrying."); } }
    public void RollbackTransaction() => server.RollbackTransaction();
    public void Execute(string tmsl)
    {
        try { var results = server.Execute(tmsl); if (results.ContainsErrors) throw new InvalidOperationException(); }
        catch { throw new InvalidOperationException("The endpoint rejected the reviewed metadata change. Verify write permissions and supported metadata; a refresh may be required after a successful push."); }
    }
    public void Cancel() { if (server.Connected) server.CancelCommand(); }
    public void Dispose() => server.Dispose();
}
