using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PbiBench.Core.Domain;
using PbiBench.Core.Workspaces;

namespace PbiBench.Workspace;

public sealed record WorkspaceFile(string Path, string Content)
{
    public string Hash => WorkspaceSemanticSnapshot.HashText(Content);
}
public sealed class WorkspaceDiskSnapshot
{
    public WorkspaceDiskSnapshot(string directory, IEnumerable<WorkspaceFile> files, bool isBim = false)
    { Directory = System.IO.Path.GetFullPath(directory); Files = new ReadOnlyDictionary<string, WorkspaceFile>(files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase)); IsBim = isBim; Hash = WorkspaceSemanticSnapshot.HashText(string.Join("\n", Files.Values.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).Select(file => JsonSerializer.Serialize(new[] { file.Path.ToLowerInvariant(), file.Hash })))); }
    public string Directory { get; }
    public IReadOnlyDictionary<string, WorkspaceFile> Files { get; }
    public bool IsBim { get; }
    public string Hash { get; }
}
public sealed class WorkspaceDiskPlan
{
    internal WorkspaceDiskPlan(WorkspaceDiskSnapshot before, WorkspaceDiskSnapshot after, ChangePlan plan) { Before = before; After = after; Plan = plan; }
    public WorkspaceDiskSnapshot Before { get; }
    public WorkspaceDiskSnapshot After { get; }
    public ChangePlan Plan { get; }
    private int claimed;
    internal bool TryClaim() => Interlocked.CompareExchange(ref claimed, 1, 0) == 0;
}
public sealed record WorkspaceWriteResult(string BackupDirectory, int ChangedFiles);
public sealed class WorkspaceDiskStore
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public event Action<int, int>? Progress;
    public static string ResolveDefinitionDirectory(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (File.Exists(full)) { if (string.Equals(System.IO.Path.GetExtension(full), ".pbip", StringComparison.OrdinalIgnoreCase)) return ResolveDefinitionDirectory(System.IO.Path.GetDirectoryName(full)!); if (!string.Equals(System.IO.Path.GetFileName(full), "model.bim", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a PBIP semantic-model folder, TMDL definition folder, or model.bim."); return System.IO.Path.GetDirectoryName(full)!; }
        RejectLinks(full);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        if (Directory.Exists(System.IO.Path.Combine(full, "definition"))) return System.IO.Path.Combine(full, "definition");
        if (File.Exists(System.IO.Path.Combine(full, "model.bim")) || Directory.EnumerateFiles(full, "*.tmdl").Any()) return full;
        var candidates = Directory.EnumerateDirectories(full, "*.SemanticModel").Where(folder => Directory.Exists(System.IO.Path.Combine(folder, "definition")) || File.Exists(System.IO.Path.Combine(folder, "model.bim"))).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("Select one semantic-model folder; this workspace contains zero or multiple models.");
        return ResolveDefinitionDirectory(candidates[0]);
    }
    public WorkspaceDiskSnapshot Capture(string directory, CancellationToken ct = default)
    {
        directory = System.IO.Path.GetFullPath(directory); RejectLinks(directory); if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var bim = File.Exists(System.IO.Path.Combine(directory, "model.bim"));
        var files = new List<WorkspaceFile>(); long total = 0;
        var paths = bim ? new[] { System.IO.Path.Combine(directory, "model.bim") } : EnumerateTmdl(directory, ct).ToArray();
        if (bim && Directory.EnumerateFiles(directory, "*.tmdl").Any()) throw new InvalidOperationException("Both model.bim and TMDL exist in this folder. Choose one unambiguous definition.");
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested(); RejectLinks(path); if (files.Count >= 10000) throw new InvalidDataException("Workspace definitions are limited to 10,000 files.");
            var length = new FileInfo(path).Length; total += length; if (length > 16 * 1024 * 1024 || total > 64 * 1024 * 1024) throw new InvalidDataException("Workspace definitions exceed the 16 MiB file or 64 MiB total bound.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); if (stream.Length != length || stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("A definition file changed or grew during capture. Compare again.");
            using var reader = new StreamReader(stream, Utf8, true); var text = reader.ReadToEnd(); if (Utf8.GetByteCount(text) > 16 * 1024 * 1024) throw new InvalidDataException("A definition file grew beyond its capture bound.");
            files.Add(new(path.Substring(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/'), text));
        }
        if (files.Count == 0) throw new InvalidDataException("No model.bim or TMDL definition was found."); return new(directory, files, bim);
    }
    public WorkspaceDiskPlan Prepare(WorkspaceDiskSnapshot before, IEnumerable<WorkspaceFile> proposed)
    {
        var after = new WorkspaceDiskSnapshot(before.Directory, proposed, before.IsBim);
        if (after.Files.Count == 0 || after.Files.Count > 10000 || after.Files.Values.Sum(file => (long)Utf8.GetByteCount(file.Content)) > 64 * 1024 * 1024) throw new InvalidDataException("The proposed definition is empty or too large.");
        foreach (var file in after.Files.Values) { SafePath(before.Directory, file.Path); if (file.Path.Split('/', '\\').Any(part => new[] { ".git", ".pbi", ".pbibench", "DAXQueries" }.Contains(part, StringComparer.OrdinalIgnoreCase)) || Utf8.GetByteCount(file.Content) > 16 * 1024 * 1024 || (before.IsBim ? file.Path != "model.bim" : !file.Path.EndsWith(".tmdl", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Only the captured semantic definition format may be written."); }
        var changes = Changed(before, after).Select(path => new PlannedChange(path, !after.Files.ContainsKey(path) ? "Delete definition file" : !before.Files.ContainsKey(path) ? "Create definition file" : "Replace definition file", before.Files.TryGetValue(path, out var old) ? old.Hash : "(absent)", after.Files.TryGetValue(path, out var next) ? next.Hash : "(absent)", new[] { "Expected file hashes are rechecked under exclusive access; PBIR, DAXQueries, cache and unknown files are preserved." })).ToArray();
        var plan = new ChangePlan(Guid.NewGuid(), DateTimeOffset.UtcNow, ApprovalLevel.WorkspaceWrite, new("pbip", null, null, before.Directory, "SemanticDefinition", before.Directory), changes, "Complete definition backup before writes", "Restore changed files from backup; preserve newer external changes and report recovery path on conflict");
        return new(before, after, plan);
    }
    public async Task<WorkspaceWriteResult> ApplyAsync(WorkspaceDiskPlan prepared, ApprovedChangePlan approved, CancellationToken ct)
        => await Task.Run(() => Apply(prepared, approved, ct), CancellationToken.None).ConfigureAwait(false);
    private WorkspaceWriteResult Apply(WorkspaceDiskPlan prepared, ApprovedChangePlan approved, CancellationToken ct)
    {
        WorkspaceApproval.Validate(prepared.Plan, approved); ct.ThrowIfCancellationRequested(); if (!prepared.TryClaim()) throw new InvalidOperationException("This disk plan is already consumed.");
        if (Capture(prepared.Before.Directory, ct).Hash != prepared.Before.Hash) throw new InvalidOperationException("Disk changed after preview. Compare and preview again.");
        var changes = Changed(prepared.Before, prepared.After).ToArray(); if (changes.Length == 0) throw new InvalidOperationException("There are no disk changes to apply.");
        var backup = System.IO.Path.Combine(prepared.Before.Directory, ".pbibench", "workspace-backups", prepared.Plan.Id.ToString("N")); RejectLinks(backup); if (Directory.Exists(backup) || File.Exists(backup)) throw new IOException("The unique recovery backup path already exists; prepare a fresh plan."); Directory.CreateDirectory(backup);
        foreach (var file in prepared.Before.Files.Values) { ct.ThrowIfCancellationRequested(); var path = SafePath(backup, file.Path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, file.Content, Utf8); }
        File.WriteAllText(System.IO.Path.Combine(backup, "_manifest.json"), JsonSerializer.Serialize(new { Version = 1, Directory = prepared.Before.Directory, prepared.Before.Hash, Changed = changes }), Utf8);
        var completed = new List<string>();
        try
        {
            foreach (var relative in changes)
            {
                ct.ThrowIfCancellationRequested(); var path = SafePath(prepared.Before.Directory, relative); prepared.Before.Files.TryGetValue(relative, out var old); prepared.After.Files.TryGetValue(relative, out var next);
                Commit(path, old, next); completed.Add(relative); Progress?.Invoke(completed.Count, changes.Length);
            }
            ct.ThrowIfCancellationRequested(); if (Capture(prepared.Before.Directory, ct).Hash != prepared.After.Hash) throw new InvalidOperationException("The definition changed during commit."); return new(backup, completed.Count);
        }
        catch (Exception error)
        {
            var recovered = true;
            foreach (var relative in completed.AsEnumerable().Reverse())
            {
                try { prepared.Before.Files.TryGetValue(relative, out var old); prepared.After.Files.TryGetValue(relative, out var next); Commit(SafePath(prepared.Before.Directory, relative), next, old); }
                catch { recovered = false; }
            }
            if (recovered && error is OperationCanceledException) throw;
            throw new IOException((recovered ? "The disk update failed and changed files were restored. " : "The disk update failed; newer or locked files were preserved. Restore the reviewed definition manually from ") + "Backup: " + backup, error);
        }
    }
    /// <summary>Writes one explicitly reviewed file under the same exclusive validation/rollback boundary as workspace commits.</summary>
    public static void WriteReviewedFile(string path, string? expectedContent, string nextContent)
    {
        if (Utf8.GetByteCount(nextContent) > 16 * 1024 * 1024 || expectedContent != null && Utf8.GetByteCount(expectedContent) > 16 * 1024 * 1024) throw new InvalidDataException("Reviewed output files are limited to 16 MB.");
        Commit(System.IO.Path.GetFullPath(path), expectedContent == null ? null : new WorkspaceFile(System.IO.Path.GetFileName(path), expectedContent), new WorkspaceFile(System.IO.Path.GetFileName(path), nextContent));
    }
    private static void Commit(string path, WorkspaceFile? expected, WorkspaceFile? next)
    {
        RejectLinks(path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (expected == null)
        { if (next == null) return; using var created = OpenDeleteCapable(path, true); try { var bytes = Utf8.GetBytes(next.Content); created.Write(bytes, 0, bytes.Length); created.Flush(true); } catch { DeleteOpenedFile(created); throw; } return; }
        using var stream = next == null ? OpenDeleteCapable(path, false) : new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("A definition file became oversized.");
        using var reader = new StreamReader(stream, Utf8, true, 8192, true); if (WorkspaceSemanticSnapshot.HashText(reader.ReadToEnd()) != expected.Hash) throw new InvalidOperationException("A definition file changed after preview.");
        if (next == null) { DeleteOpenedFile(stream); return; }
        try { var bytes = Utf8.GetBytes(next.Content); stream.Position = 0; stream.SetLength(0); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        catch { var bytes = Utf8.GetBytes(expected.Content); stream.Position = 0; stream.SetLength(0); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); throw; }
    }
    private static void DeleteOpenedFile(FileStream stream)
    {
        // Mark the already validated, exclusively locked file handle for deletion. A path-based
        // close/delete sequence could delete a replacement created by another process in between.
        var disposition = new FileDisposition { DeleteFile = 1 };
        if (!SetFileInformationByHandle(stream.SafeFileHandle, 4, ref disposition, Marshal.SizeOf(typeof(FileDisposition)))) throw new IOException("Could not mark the verified definition file for deletion.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    }
    private static FileStream OpenDeleteCapable(string path, bool createNew)
    {
        var handle = CreateFile(path, 0xC0010000, 0, IntPtr.Zero, createNew ? 1u : 3u, 0x80, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("Could not exclusively open a definition file.", new System.ComponentModel.Win32Exception(error)); }
        return new FileStream(handle, FileAccess.ReadWrite);
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [StructLayout(LayoutKind.Sequential)] private struct FileDisposition { public byte DeleteFile; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref FileDisposition information, int bufferSize);
    private static IEnumerable<string> Changed(WorkspaceDiskSnapshot before, WorkspaceDiskSnapshot after) => before.Files.Keys.Concat(after.Files.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Where(path => !before.Files.TryGetValue(path, out var old) || !after.Files.TryGetValue(path, out var next) || old.Hash != next.Hash).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    public static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0 || relative.Split('/', '\\').Any(part => part is "" or "." or ".." || part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) || IsReserved(part))) throw new InvalidDataException("Unsafe definition path.");
        root = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar); var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Definition path escaped its root."); RejectLinks(path); return path;
    }
    private static bool IsReserved(string part) { var name = part.Split('.')[0].ToUpperInvariant(); return name is "CON" or "PRN" or "AUX" or "NUL" || name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] >= '0' && name[3] <= '9'; }
    public static void RejectLinks(string path)
    {
        var full = System.IO.Path.GetFullPath(path); var current = full;
        while (!string.IsNullOrEmpty(current)) { if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked paths are not supported for workspace synchronization."); current = System.IO.Path.GetDirectoryName(current); }
    }
    private static IEnumerable<string> EnumerateTmdl(string root, CancellationToken ct)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            ct.ThrowIfCancellationRequested(); var name = System.IO.Path.GetFileName(entry); if (name is ".git" or ".pbi" or ".pbibench" or "DAXQueries") continue;
            RejectLinks(entry); if (Directory.Exists(entry)) { foreach (var file in EnumerateTmdl(entry, ct)) yield return file; } else if (entry.EndsWith(".tmdl", StringComparison.OrdinalIgnoreCase)) yield return entry;
        }
    }
}
