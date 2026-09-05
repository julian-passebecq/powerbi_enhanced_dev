using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PbiBench.Core.Queries;

namespace PbiBench.Core.Refresh;

public sealed record RefreshDevelopmentProfile(int FormatVersion, string Name, RefreshRequest Request);
public static class RefreshProfileStore
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter() } };
    public static string Serialize(RefreshDevelopmentProfile profile)
    {
        Validate(profile); var json = JsonSerializer.Serialize(profile, Options); if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("The refresh profile exceeds 16 MB."); return json;
    }
    public static RefreshDevelopmentProfile Deserialize(string json)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("The refresh profile exceeds 16 MB.");
        try { var profile = JsonSerializer.Deserialize<RefreshDevelopmentProfile>(json, Options) ?? throw new InvalidDataException("Empty refresh profile."); Validate(profile); return profile; }
        catch (JsonException) { throw new InvalidDataException("The refresh profile is invalid or contains unsupported fields."); }
    }
    private static void Validate(RefreshDevelopmentProfile profile)
    {
        if (profile == null || profile.FormatVersion != 1 || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 300 || profile.Request == null) throw new InvalidDataException("A version 1 refresh profile requires a name and typed request.");
        var request = profile.Request;
        if (!Enum.IsDefined(typeof(RefreshKind), request.Kind) || request.MaxParallelism < 1 || request.MaxParallelism > 256 || request.TimeoutSeconds < 1 || request.TimeoutSeconds > 86400) throw new InvalidDataException("Invalid refresh type, parallelism or timeout.");
        if (request.Objects == null || request.Objects.Count < 1 || request.Objects.Count > 10000 || request.Objects.Any(o => o == null || o.Table == null && o.Partition != null)) throw new InvalidDataException("Invalid refresh object scopes.");
        if (request.SourceOverrides == null || request.SourceOverrides.Count > 1000 || request.SourceOverrides.Any(o => o == null || string.IsNullOrWhiteSpace(o.Table) || string.IsNullOrWhiteSpace(o.Partition) || string.IsNullOrWhiteSpace(o.Expression) || o.Expression.Length > 1000000 || o.SourceKind != RefreshSourceKind.M && o.SourceKind != RefreshSourceKind.Query)) throw new InvalidDataException("Invalid typed source override.");
    }
    public static Task<RefreshDevelopmentProfile> LoadAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("The refresh profile exceeds 16 MB."); var result = Deserialize(File.ReadAllText(path)); token.ThrowIfCancellationRequested(); return result;
    }, token);
    public static Task SaveAsync(string path, RefreshDevelopmentProfile profile, CancellationToken token) => WriteAsync(path, Serialize(profile), token);
    public static Task ExportTmslAsync(string path, RefreshPlan plan, CancellationToken token) => WriteAsync(path, plan.Tmsl, token);
    private static Task WriteAsync(string path, string text, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); var destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, text, new UTF8Encoding(false)); token.ThrowIfCancellationRequested(); AtomicQueryFile.Commit(temporary, destination, token); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, token);
}
