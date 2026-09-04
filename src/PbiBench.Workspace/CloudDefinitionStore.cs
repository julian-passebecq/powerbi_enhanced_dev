using System.Text;
using System.Text.Json;
namespace PbiBench.Workspace;

public sealed record DefinitionPart(string Path, string Payload, string PayloadType);
public sealed record CloudDefinition(string? Format, IReadOnlyList<DefinitionPart> Parts);

public sealed class CloudDefinitionStore
{
    public string Save(string root, string workspaceId, string itemId, CloudDefinition definition, DateTimeOffset timestamp)
    {
        var dir = System.IO.Path.Combine(root, ".pbibench", "cloud-snapshots", Safe(workspaceId), Safe(itemId), timestamp.UtcDateTime.ToString("yyyyMMddTHHmmssZ"));
        Directory.CreateDirectory(dir);
        foreach (var part in definition.Parts)
        {
            var relative = part.Path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(dir, relative));
            if (!target.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Definition part escaped snapshot root: {part.Path}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!string.Equals(part.PayloadType, "InlineBase64", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Unsupported payload type: {part.PayloadType}");
            File.WriteAllBytes(target, Convert.FromBase64String(part.Payload));
        }
        File.WriteAllText(Path.Combine(dir, "_snapshot.json"), JsonSerializer.Serialize(new { definition.Format, SavedAt = timestamp }, new JsonSerializerOptions{WriteIndented=true}));
        return dir;
    }
    private static string Safe(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
