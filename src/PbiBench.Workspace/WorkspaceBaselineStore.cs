using System.Text;
using PbiBench.Core.Workspaces;

namespace PbiBench.Workspace;

/// <summary>Per-profile, per-root-and-target baseline. Never contains authentication settings.</summary>
public sealed class WorkspaceBaselineStore
{
    private readonly string path;
    public WorkspaceBaselineStore(string settingsDirectory, string definitionDirectory, string? server, string? database)
    { var id = WorkspaceSemanticSnapshot.HashText(Path.GetFullPath(definitionDirectory).ToUpperInvariant() + "\n" + server + "\n" + database); path = Path.Combine(Path.GetFullPath(settingsDirectory), "WorkspaceBaselines", id + ".bim"); }
    public Task<WorkspaceSemanticSnapshot?> LoadAsync(CancellationToken ct) => Task.Run(() => { ct.ThrowIfCancellationRequested(); WorkspaceDiskStore.RejectLinks(path); if (!File.Exists(path)) return null; if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Saved workspace baseline is oversized."); return WorkspaceSemanticSnapshot.Parse(File.ReadAllText(path)); }, ct);
    public async Task SaveAsync(WorkspaceSemanticSnapshot snapshot, CancellationToken ct)
    {
        WorkspaceDiskStore.RejectLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { var bytes = new UTF8Encoding(false).GetBytes(snapshot.DatabaseJson); using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true)) { await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false); await stream.FlushAsync(ct).ConfigureAwait(false); } ct.ThrowIfCancellationRequested(); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
