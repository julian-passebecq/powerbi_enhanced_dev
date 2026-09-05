using System.IO;
using System.Text;
using PbiBench.Core.Workspaces;
using PbiBench.Workspace;
using TabularEditor.TOMWrapper;
using TOM = Microsoft.AnalysisServices.Tabular;

namespace PbiBench.Semantic.Workspaces;

/// <summary>Public TOM serialization on detached databases; never constructs another native handler.</summary>
public sealed class TmdlWorkspaceCodec
{
    public WorkspaceSemanticSnapshot CaptureLoaded(TabularModelHandler handler) => WorkspaceSemanticSnapshot.Parse(TOM.JsonSerializer.SerializeDatabase(handler.Database));
    public WorkspaceSemanticSnapshot Parse(WorkspaceDiskSnapshot files, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (files.IsBim) return Normalize(files.Files["model.bim"].Content);
        return WithStage(directory =>
        {
            foreach (var file in files.Files.Values) { ct.ThrowIfCancellationRequested(); var path = WorkspaceDiskStore.SafePath(directory, file.Path); if (!path.EndsWith(".tmdl", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Expected TMDL files only."); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, file.Content, new UTF8Encoding(false)); }
            var database = TOM.TmdlSerializer.DeserializeDatabaseFromFolder(directory); ct.ThrowIfCancellationRequested(); return WorkspaceSemanticSnapshot.Parse(TOM.JsonSerializer.SerializeDatabase(database));
        });
    }
    public IReadOnlyList<WorkspaceFile> Serialize(WorkspaceSemanticSnapshot snapshot, bool isBim, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var database = TOM.JsonSerializer.DeserializeDatabase(snapshot.DatabaseJson);
        if (isBim) return new[] { new WorkspaceFile("model.bim", TOM.JsonSerializer.SerializeDatabase(database)) };
        return WithStage(directory => { TOM.TmdlSerializer.SerializeDatabaseToFolder(database, directory); ct.ThrowIfCancellationRequested(); return (IReadOnlyList<WorkspaceFile>)new WorkspaceDiskStore().Capture(directory, ct).Files.Values.ToArray(); });
    }
    public WorkspaceSemanticSnapshot Normalize(string databaseJson) => WorkspaceSemanticSnapshot.Parse(TOM.JsonSerializer.SerializeDatabase(TOM.JsonSerializer.DeserializeDatabase(databaseJson)));
    private static T WithStage<T>(Func<string, T> operation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PbiBench-workspace-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try { return operation(directory); }
        finally
        {
            var full = Path.GetFullPath(directory); var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar), parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("PbiBench-workspace-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected workspace staging directory.");
            WorkspaceDiskStore.RejectLinks(full); Directory.Delete(full, true);
        }
    }
}
