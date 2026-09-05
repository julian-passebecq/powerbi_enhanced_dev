using System.Text;
using PbiBench.Core.Commands;
using PbiBench.Core.Queries;
using PbiBench.Core.Workspaces;
using PbiBench.Semantic.Workspaces;
using PbiBench.Workspace;

namespace PbiBench.Automation.Commands;

public sealed record CommandModelSnapshot(WorkspaceSemanticSnapshot Snapshot, string ContentHash, string LoadPath);
internal sealed record CommandOutput(string Path, string BeforeHash, string? BeforeContent);
public static class CommandModelFiles
{
    public static CommandModelSnapshot Read(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var full = Path.GetFullPath(path); WorkspaceDiskStore.RejectLinks(full); var codec = new TmdlWorkspaceCodec();
        if (File.Exists(full) && (full.EndsWith(".bim", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var text = ReadBounded(full); ct.ThrowIfCancellationRequested(); return new(codec.Normalize(text), WorkspaceSemanticSnapshot.HashText(text), full);
        }
        if (File.Exists(full) && full.EndsWith(".pbip", StringComparison.OrdinalIgnoreCase)) full = Path.GetDirectoryName(full)!;
        var directory = WorkspaceDiskStore.ResolveDefinitionDirectory(full); var files = new WorkspaceDiskStore().Capture(directory, ct);
        return new(codec.Parse(files, ct), files.Hash, files.IsBim ? Path.Combine(directory, "model.bim") : directory);
    }
    private static string ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Model files are limited to 64 MB.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true); return reader.ReadToEnd();
    }
    internal static CommandOutput? PrepareOutput(string? path)
    {
        if (path == null) return null; var full = Path.GetFullPath(path);
        if (!full.EndsWith(".bim", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Local CLI edits export an explicit .bim destination. TMDL input remains supported; use Workspace synchronization to write a definition folder.");
        WorkspaceDiskStore.RejectLinks(full); if (Directory.Exists(full)) throw new ArgumentException("The BIM output must be a file.");
        var content = File.Exists(full) ? ReadBounded(full) : null;
        return new(full, content == null ? "(absent)" : WorkspaceSemanticSnapshot.HashText(content), content);
    }
    internal static void VerifyOutput(CommandOutput output)
    { if (PrepareOutput(output.Path)!.BeforeHash != output.BeforeHash) throw new InvalidOperationException("The output changed after review. Prepare a new preview."); }
    internal static string? WriteOutput(CommandOutput output, string json, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); VerifyOutput(output); var directory = Path.GetDirectoryName(output.Path)!; WorkspaceDiskStore.RejectLinks(directory); Directory.CreateDirectory(directory);
        string? backup = null;
        if (output.BeforeContent != null)
        {
            backup = output.Path + "." + Guid.NewGuid().ToString("N") + ".backup";
            using var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None); var bytes = new UTF8Encoding(false).GetBytes(output.BeforeContent); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
        }
        ct.ThrowIfCancellationRequested(); WorkspaceDiskStore.WriteReviewedFile(output.Path, output.BeforeContent, json); return backup;
    }
}
