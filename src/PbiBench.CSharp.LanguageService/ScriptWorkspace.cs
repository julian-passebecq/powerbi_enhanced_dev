using System.Text;
using System.Text.Json;
using PbiBench.Core.Queries;

namespace PbiBench.CSharp.LanguageService;

public sealed record ScriptDocument(string Id, string Name, string Text, string? FilePath = null, string SavedText = "")
{ public bool IsDirty => Text != SavedText; }
public sealed record ScriptRecovery(IReadOnlyList<ScriptDocument> Documents, string ActiveId, int SchemaVersion = 1);
public static class ScriptWorkspaceFiles
{
    public static async Task<string> ReadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("Script files are limited to 1 MiB.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true); var text = await reader.ReadToEndAsync().ConfigureAwait(false); ct.ThrowIfCancellationRequested(); return text;
    }
    public static Task SaveAsync(string path, string source, CancellationToken ct) => WriteAsync(path, source, 1024 * 1024, ct);
    public static Task SaveRecoveryAsync(string path, ScriptRecovery recovery, CancellationToken ct)
    { Validate(recovery); return WriteAsync(path, JsonSerializer.Serialize(recovery), 8 * 1024 * 1024, ct); }
    public static async Task<ScriptRecovery> LoadRecoveryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); using var stream = File.OpenRead(path); if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("Recovery file exceeds 8 MiB.");
        var recovery = await JsonSerializer.DeserializeAsync<ScriptRecovery>(stream, cancellationToken: ct).ConfigureAwait(false) ?? throw new InvalidDataException("Empty recovery."); Validate(recovery); return recovery;
    }
    private static void Validate(ScriptRecovery recovery)
    {
        if (recovery.SchemaVersion != 1 || recovery.Documents == null || recovery.Documents.Count is < 1 or > 24 || recovery.Documents.Any(d => d == null || !Guid.TryParse(d.Id, out _) || d.Text == null || d.SavedText == null || d.Text.Length > 1024 * 1024 || d.SavedText.Length > 1024 * 1024 || string.IsNullOrWhiteSpace(d.Name)) || recovery.Documents.Select(d => d.Id).Distinct().Count() != recovery.Documents.Count || !recovery.Documents.Any(d => d.Id == recovery.ActiveId)) throw new InvalidDataException("Invalid script recovery format.");
    }
    private static async Task WriteAsync(string path, string text, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var bytes = Encoding.UTF8.GetBytes(text); if (bytes.Length > limit) throw new InvalidDataException("Script storage size limit exceeded.");
        var destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true)) { await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false); await stream.FlushAsync(ct).ConfigureAwait(false); } AtomicQueryFile.Commit(temporary, destination, ct); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
