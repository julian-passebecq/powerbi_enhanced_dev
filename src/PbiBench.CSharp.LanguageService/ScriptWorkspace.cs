using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using PbiBench.Core.Queries;

namespace PbiBench.CSharp.LanguageService;

public sealed record ScriptDocument(string Id, string Name, string Text, string? FilePath = null, string SavedText = "",
    string? PersistedHash = null, bool IsRecovered = false, string? RecoveredFrom = null)
{ public bool IsDirty => IsRecovered || Text != SavedText; }
public sealed record ScriptRecovery(IReadOnlyList<ScriptDocument> Documents, string ActiveId, int SchemaVersion = 2);
public sealed class ScriptFileConflictException(string path, string? observedHash)
    : IOException("The script file changed externally or has no verified save baseline. Reload, Save As, or explicitly Overwrite after review.")
{
    public string FilePath { get; } = path;
    public string? ObservedHash { get; } = observedHash;
}
public static class ScriptWorkspaceFiles
{
    public static async Task<ScriptDocument> OpenAsync(string path, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(path); var bytes = await ReadBytesAsync(fullPath, ct).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes); using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var text = reader.ReadToEnd();
        return new(Guid.NewGuid().ToString(), Path.GetFileName(fullPath), text, fullPath, text, Hash(bytes));
    }
    // A conflict is also the review token: explicit overwrite can only replace that observed version at that path.
    public static async Task<ScriptDocument> SaveAsync(ScriptDocument document, string path, CancellationToken ct,
        ScriptFileConflictException? overwrite = null)
    {
        var destination = Path.GetFullPath(path);
        if (overwrite != null && !SamePath(destination, overwrite.FilePath)) throw new ArgumentException("Overwrite review belongs to another file.");
        var expected = overwrite != null ? overwrite.ObservedHash : !document.IsRecovered && SamePath(destination, document.FilePath) ? document.PersistedHash : null;
        await WriteAsync(destination, document.Text, 1024 * 1024, ct, async () =>
        {
            string? actual;
            try { actual = Hash(await ReadBytesAsync(destination, ct).ConfigureAwait(false)); }
            catch (FileNotFoundException) { actual = null; }
            if (actual != expected) throw new ScriptFileConflictException(destination, actual);
            return actual != null;
        }).ConfigureAwait(false);
        return document with { FilePath = destination, Name = Path.GetFileName(destination), SavedText = document.Text,
            PersistedHash = Hash(Encoding.UTF8.GetBytes(document.Text)), IsRecovered = false, RecoveredFrom = null };
    }
    public static Task SaveRecoveryAsync(string path, ScriptRecovery recovery, CancellationToken ct)
    { Validate(recovery); return WriteAsync(path, JsonSerializer.Serialize(recovery), 8 * 1024 * 1024, ct); }
    public static async Task<ScriptRecovery> LoadRecoveryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); using var stream = File.OpenRead(path); if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("Recovery file exceeds 8 MiB.");
        var recovery = await JsonSerializer.DeserializeAsync<ScriptRecovery>(stream, cancellationToken: ct).ConfigureAwait(false) ?? throw new InvalidDataException("Empty recovery."); Validate(recovery);
        // Paths and hashes from either recovery schema are advisory, never write or execution authority.
        return recovery with { SchemaVersion = 2, Documents = recovery.Documents.Select(d => d with {
            RecoveredFrom = d.FilePath ?? d.RecoveredFrom, FilePath = null, PersistedHash = null, SavedText = "", IsRecovered = true }).ToArray() };
    }
    private static void Validate(ScriptRecovery recovery)
    {
        if (recovery.SchemaVersion is not (1 or 2) || recovery.Documents == null || recovery.Documents.Count is < 1 or > 24 || recovery.Documents.Any(d => d == null || !Guid.TryParse(d.Id, out _) || d.Text == null || d.SavedText == null || Encoding.UTF8.GetByteCount(d.Text) > 1024 * 1024 || Encoding.UTF8.GetByteCount(d.SavedText) > 1024 * 1024 || string.IsNullOrWhiteSpace(d.Name)) || recovery.Documents.Select(d => d.Id).Distinct().Count() != recovery.Documents.Count || !recovery.Documents.Any(d => d.Id == recovery.ActiveId)) throw new InvalidDataException("Invalid script recovery format.");
    }
    private static bool SamePath(string path, string? other) => other != null && string.Equals(path, Path.GetFullPath(other), StringComparison.OrdinalIgnoreCase);
    private static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static async Task<byte[]> ReadBytesAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("Script files are limited to 1 MiB.");
        using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes, 8192, ct).ConfigureAwait(false); ct.ThrowIfCancellationRequested(); return bytes.ToArray();
    }
    private static async Task WriteAsync(string path, string text, int limit, CancellationToken ct, Func<Task<bool>>? validateDestination = null)
    {
        ct.ThrowIfCancellationRequested(); var bytes = Encoding.UTF8.GetBytes(text); if (bytes.Length > limit) throw new InvalidDataException("Script storage size limit exceeded.");
        var destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true)) { await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false); await stream.FlushAsync(ct).ConfigureAwait(false); }
            if (validateDestination == null) AtomicQueryFile.Commit(temporary, destination, ct);
            else
            {
                var exists = await validateDestination().ConfigureAwait(false); ct.ThrowIfCancellationRequested();
                // Fail on a lock instead of retrying a replacement against an unchecked later version.
                if (exists) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
