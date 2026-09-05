using System.Text.Json.Serialization;
using PbiBench.Core.Domain;

namespace PbiBench.Core.Workspaces;

public sealed class WorkspaceConnection
{
    public WorkspaceConnection(string server, string database, string? connectionString = null, int timeoutSeconds = 120)
    { if (string.IsNullOrWhiteSpace(server) || server.Length > 4096 || server.IndexOf(';') >= 0 || server.Any(char.IsControl) || string.IsNullOrWhiteSpace(database) || database.Length > 512 || database.Any(char.IsControl) || timeoutSeconds < 5 || timeoutSeconds > 3600) throw new ArgumentException("Enter a live endpoint, database and timeout from 5 to 3600 seconds; authentication options belong in the transient connection string."); Server = server; Database = database; ConnectionString = connectionString; TimeoutSeconds = timeoutSeconds; }
    public string Server { get; }
    public string Database { get; }
    [JsonIgnore] public string? ConnectionString { get; }
    public int TimeoutSeconds { get; }
    public override string ToString() => Server + "/" + Database;
}
public sealed record WorkspaceLiveCapture(string DatabaseId, string DatabaseName, WorkspaceSemanticSnapshot Snapshot);
public interface IWorkspaceLiveSession : IDisposable
{
    void Open(WorkspaceConnection connection);
    WorkspaceLiveCapture Capture();
    void BeginTransaction();
    void CommitTransaction();
    void RollbackTransaction();
    void Execute(string tmsl);
    void Cancel();
}
public interface IWorkspaceLiveSessionFactory { IWorkspaceLiveSession Create(); }
public static class WorkspaceApproval
{
    public static void Validate(ChangePlan plan, ApprovedChangePlan approval)
    {
        var now = DateTimeOffset.UtcNow;
        if (!ReferenceEquals(plan, approval.Plan) || string.IsNullOrWhiteSpace(approval.ApprovalActor) || approval.ApprovedAt < plan.CreatedAt || approval.ApprovedAt > now.AddSeconds(5) || now - plan.CreatedAt > TimeSpan.FromMinutes(30)) throw new InvalidOperationException("The workspace approval is missing, mismatched or expired. Prepare and review a fresh plan.");
    }
}
public sealed class WorkspacePushPlan
{
    public WorkspacePushPlan(WorkspaceConnection connection, WorkspaceLiveCapture before, WorkspaceSemanticSnapshot disk, string tmsl, string diskHash, ChangePlan plan)
    { Connection = connection; Before = before; Disk = disk; Tmsl = tmsl; DiskHash = diskHash; Plan = plan; }
    public WorkspaceConnection Connection { get; }
    public WorkspaceLiveCapture Before { get; }
    public WorkspaceSemanticSnapshot Disk { get; }
    public string DiskHash { get; }
    public ChangePlan Plan { get; }
    public string Tmsl { get; }
}
