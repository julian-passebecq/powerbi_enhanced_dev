using System.Text.Json;

namespace PbiBench.Core.Platform;

public sealed record ProvenanceComponent(string Id, string Feature, string OwnerProject, string SourceType, string Upstream,
    string Pin, string LocalAdapter, IReadOnlyList<string> LocalPatches, string License, string UpdateLane, IReadOnlyList<string> ProtectingTests);
public sealed record ProvenanceCatalog(int SchemaVersion, string ProductVersion, string BaselineCommit, IReadOnlyList<ProvenanceComponent> Components)
{
    public static ProvenanceCatalog Parse(string json)
    {
        if (json.Length > 1024 * 1024) throw new InvalidDataException("Provenance exceeds 1 MiB.");
        var value = JsonSerializer.Deserialize<ProvenanceCatalog>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty provenance.");
        if (value.SchemaVersion != 1 || string.IsNullOrWhiteSpace(value.ProductVersion) || !System.Text.RegularExpressions.Regex.IsMatch(value.BaselineCommit ?? "", "^[0-9a-f]{40}$") || value.Components == null || value.Components.Count == 0 || value.Components.Count > 512) throw new InvalidDataException("Invalid provenance header.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in value.Components)
            if (c == null || !ids.Add(c.Id) || new[] { c.Id, c.Feature, c.OwnerProject, c.SourceType, c.Upstream, c.Pin, c.LocalAdapter, c.License, c.UpdateLane }.Any(string.IsNullOrWhiteSpace) || c.LocalPatches == null || c.ProtectingTests == null || c.ProtectingTests.Count == 0) throw new InvalidDataException("Incomplete or duplicate provenance component.");
        return value;
    }
    public static ProvenanceCatalog Bundled()
    {
        using var stream = typeof(ProvenanceCatalog).Assembly.GetManifestResourceStream("PbiBench.provenance.json") ?? throw new InvalidDataException("Bundled provenance is missing.");
        using var reader = new StreamReader(stream); return Parse(reader.ReadToEnd());
    }
}
