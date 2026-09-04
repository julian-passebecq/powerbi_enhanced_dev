using System.Text;

namespace PbiBench.Dax.LanguageService;

/// <summary>Persists original script source, including incomplete drafts; parsing and model apply remain separate.</summary>
public static class DaxScriptFile
{
    public static async Task<string> LoadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("DAX scripts are limited to 16 MB.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false); ct.ThrowIfCancellationRequested(); return text;
    }
    public static async Task SaveAsync(string path, string text, CancellationToken ct)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("DAX scripts are limited to 16 MB.");
        ct.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(path); var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            { await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false); await stream.FlushAsync(ct).ConfigureAwait(false); }
            ct.ThrowIfCancellationRequested();
            if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
