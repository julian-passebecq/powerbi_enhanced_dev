using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PbiBench.Core.Queries;

namespace PbiBench.Core.Quality;

public static class SemanticTestArtifactStore
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Serialize(SemanticTestArtifact artifact)
    {
        Validate(artifact); var json = JsonSerializer.Serialize(artifact, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("The semantic test artifact exceeds 16 MB.");
        return json;
    }
    public static SemanticTestArtifact Deserialize(string json)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("The semantic test artifact exceeds 16 MB.");
        try
        {
            var artifact = JsonSerializer.Deserialize<SemanticTestArtifact>(json, Options) ?? throw new InvalidDataException("The semantic test artifact is empty.");
            Validate(artifact); return artifact;
        }
        catch (JsonException) { throw new InvalidDataException("The semantic test artifact is invalid or contains unsupported fields."); }
    }
    public static void Validate(SemanticTestArtifact artifact)
    {
        if (artifact == null || artifact.FormatVersion != SemanticTestArtifact.CurrentVersion) throw new InvalidDataException("Unsupported semantic test artifact version.");
        if (artifact.Tests == null || artifact.Tests.Count > 200) throw new InvalidDataException("An artifact can contain at most 200 tests.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var test in artifact.Tests)
        {
            SemanticTestService.Validate(test);
            if (!ids.Add(test.Id)) throw new InvalidDataException("Semantic test ids must be unique within an artifact.");
        }
    }
    public static Task<SemanticTestArtifact> LoadAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("The semantic test artifact exceeds 16 MB.");
        var artifact = Deserialize(File.ReadAllText(path)); cancellationToken.ThrowIfCancellationRequested(); return artifact;
    }, cancellationToken);
    public static Task SaveAsync(string path, SemanticTestArtifact artifact, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested(); Save(path, Serialize(artifact), cancellationToken);
    }, cancellationToken);
    public static Task SaveReportAsync(string path, SemanticTestReport report, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (report.FormatVersion != 1 || report.Results.Count > 200) throw new InvalidDataException("Unsupported or excessive semantic test report.");
        var json = JsonSerializer.Serialize(report, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("The semantic test report exceeds 16 MB.");
        Save(path, json, cancellationToken);
    }, cancellationToken);
    private static void Save(string path, string json, CancellationToken token)
    {
        var destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json, new UTF8Encoding(false)); token.ThrowIfCancellationRequested(); AtomicQueryFile.Commit(temporary, destination, token); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
