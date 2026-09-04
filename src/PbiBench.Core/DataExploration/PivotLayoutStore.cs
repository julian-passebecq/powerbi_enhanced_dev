using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PbiBench.Core.Queries;

namespace PbiBench.Core.DataExploration;

public static class PivotLayoutStore
{
    public static async Task SaveAsync(string path, PivotLayout layout, CancellationToken ct)
    {
        PivotQueryBuilder.ValidateShape(layout);
        await PivotJsonFile.SaveAsync(path, PivotQueryBuilder.Freeze(layout), ct).ConfigureAwait(false);
    }
    public static async Task<PivotLayout> LoadAsync(string path, CancellationToken ct)
    {
        var layout = await PivotJsonFile.LoadAsync<PivotLayout>(path, ct).ConfigureAwait(false);
        PivotQueryBuilder.ValidateShape(layout);
        return PivotQueryBuilder.Freeze(layout);
    }
}

internal static class PivotJsonFile
{
    private const int MaximumBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    internal static Task SaveAsync<T>(string path, T value, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(path);
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var json = JsonSerializer.Serialize(value, Options);
            if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Pivot files are limited to 2 MB.");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(json); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                AtomicQueryFile.Commit(temporary, fullPath, ct);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, ct);
    }
    internal static Task<T> LoadAsync<T>(string path, CancellationToken ct) where T : class
    {
        var fullPath = Path.GetFullPath(path);
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (new FileInfo(fullPath).Length > MaximumBytes) throw new InvalidDataException("Pivot files are limited to 2 MB.");
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            try
            {
                var result = JsonSerializer.Deserialize<T>(stream, Options) ?? throw new InvalidDataException("The pivot file is empty.");
                ct.ThrowIfCancellationRequested(); return result;
            }
            catch (JsonException ex) { throw new InvalidDataException("The pivot file is not valid JSON.", ex); }
        }, ct);
    }
}
