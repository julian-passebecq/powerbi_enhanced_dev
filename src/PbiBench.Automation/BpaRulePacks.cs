using System.Text.Json;

namespace PbiBench.Automation;

public sealed record BpaRuleDefinition(string Id, string Title, string Category, FindingSeverity Severity,
    string Risk, string Applicability, string Reference);
public sealed record BpaRulePack(string Id, string Name, string Category, string Version, string Origin,
    IReadOnlyList<BpaRuleDefinition> Rules);
public sealed record BpaWorkspaceContext(bool HasPbip, bool IsGitRepository, bool HasConflicts, bool HasSemanticChanges);

/// <summary>Original PbiBench policies. Rule descriptions are requirements, never executable fix text.</summary>
public static class BpaRulePacks
{
    public const string Version = "1.0.0";
    public static IReadOnlyList<BpaRulePack> BuiltIn { get; } = Array.AsReadOnly(new[]
    {
        Pack("Naming", Rule("PBIBENCH002", "Measure needs a description", "Naming", FindingSeverity.Information, "SAFE", "Measures without descriptions", "https://learn.microsoft.com/en-us/power-bi/guidance/star-schema"),
            Rule("PBIBENCH006", "Object name has surrounding whitespace", "Naming", FindingSeverity.Warning, "REVIEW", "Tables, columns and measures", "https://learn.microsoft.com/en-us/analysis-services/tabular-models/rename-a-table-or-column-ssas-tabular")),
        Pack("Formatting", Rule("PBIBENCH003", "Measure has no display folder", "Formatting", FindingSeverity.Information, "SAFE", "Measures without folders", "https://learn.microsoft.com/en-us/power-bi/transform-model/desktop-measures"),
            Rule("PBIBENCH007", "Review the measure's general number format", "Formatting", FindingSeverity.Information, "MANUAL", "Measures with no static or dynamic format", "https://learn.microsoft.com/en-us/power-bi/create-reports/desktop-dynamic-format-strings")),
        Pack("Modeling", Rule("PBIBENCH004", "Key column allows implicit aggregation", "Modeling", FindingSeverity.Warning, "REVIEW", "Explicit keys and relationship one-side keys", "https://learn.microsoft.com/en-us/power-bi/guidance/star-schema"),
            Rule("PBIBENCH005", "Review bidirectional filtering", "Modeling", FindingSeverity.Warning, "REVIEW", "Active bidirectional relationships", "https://learn.microsoft.com/en-us/power-bi/guidance/relationships-bidirectional-filtering")),
        Pack("Performance", Rule("PBIBENCH008", "Review calculated-column storage and refresh cost", "Performance", FindingSeverity.Information, "BENCHMARK", "Calculated columns in stored models", "https://learn.microsoft.com/en-us/power-bi/guidance/import-modeling-data-reduction")),
        Pack("Security", Rule("PBIBENCH009", "Review bidirectional security propagation", "Security", FindingSeverity.Warning, "REVIEW", "Relationships that propagate security both ways", "https://learn.microsoft.com/en-us/power-bi/enterprise/service-admin-rls"),
            Rule("PBIBENCH010", "Review a role with no row filters", "Security", FindingSeverity.Information, "MANUAL", "Roles with no nonempty table filter", "https://learn.microsoft.com/en-us/power-bi/enterprise/service-admin-rls")),
        Pack("DAX", Rule("PBIBENCH001", "Measure has no expression", "DAX", FindingSeverity.Error, "MANUAL", "Measures with empty DAX", "https://learn.microsoft.com/en-us/dax/dax-overview"),
            Rule("PBIBENCH011", "Review division-by-zero behavior", "DAX", FindingSeverity.Information, "REVIEW", "Measure expressions containing the division operator", "https://learn.microsoft.com/en-us/dax/best-practices/dax-divide-function-operator")),
        Pack("Direct Lake", Rule("PBIBENCH012", "Review calculated columns on a Direct Lake table", "Direct Lake", FindingSeverity.Warning, "MANUAL", "Direct Lake partitions and calculated columns in one table", "https://learn.microsoft.com/en-us/fabric/fundamentals/direct-lake-overview"),
            Rule("PBIBENCH013", "Review mixed storage behavior for Direct Lake", "Direct Lake", FindingSeverity.Information, "REVIEW", "Models combining Direct Lake with another partition mode", "https://learn.microsoft.com/en-us/fabric/fundamentals/direct-lake-overview")),
        Pack("PBIP/Git", Rule("PBIBENCH014", "Check PBIP repository access", "PBIP/Git", FindingSeverity.Information, "MANUAL", "Detected PBIP workspace without a readable Git repository", "https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-overview"),
            Rule("PBIBENCH015", "Resolve Git conflicts before publishing", "PBIP/Git", FindingSeverity.Error, "MANUAL", "Observed unmerged Git paths", "https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-overview"),
            Rule("PBIBENCH016", "Review semantic changes before committing", "PBIP/Git", FindingSeverity.Information, "REVIEW", "Observed changed semantic files", "https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-overview"))
    });
    public static IEnumerable<BpaRuleDefinition> Rules => BuiltIn.SelectMany(pack => pack.Rules);
    public static BpaRuleDefinition Get(string id) => Rules.FirstOrDefault(rule => rule.Id == id)
        ?? throw new ArgumentException("Unknown PbiBench rule: " + id, nameof(id));
    public static BpaRulePack PackFor(string id) => BuiltIn.Single(pack => pack.Rules.Any(rule => rule.Id == id));
    private static BpaRuleDefinition Rule(string id, string title, string category, FindingSeverity severity, string risk, string applicability, string reference)
        => new(id, title, category, severity, risk, applicability, reference);
    private static BpaRulePack Pack(string category, params BpaRuleDefinition[] rules) => new("pbibench." + category.ToLowerInvariant().Replace("/", "-").Replace(" ", "-"),
        "PbiBench " + category, category, Version, "Original PbiBench policies; public Microsoft behavior references", Array.AsReadOnly(rules));
}

public sealed class BpaRuleProfile
{
    public int Version { get; set; } = 1;
    public Dictionary<string, bool> Enabled { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, FindingSeverity> Severities { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> Suppressions { get; set; } = new(StringComparer.Ordinal);
    public bool IsEnabled(string id) => !Enabled.TryGetValue(id, out var enabled) || enabled;
    public void Validate()
    {
        if (Version != 1 || Enabled == null || Severities == null || Suppressions == null || Enabled.Count > 1000 || Severities.Count > 1000 || Suppressions.Count > 10000)
            throw new InvalidDataException("Unsupported or oversized BPA profile.");
        foreach (var id in Enabled.Keys.Concat(Severities.Keys)) BpaRulePacks.Get(id);
        if (Severities.Values.Any(value => !Enum.IsDefined(typeof(FindingSeverity), value)) || Suppressions.Any(value => value == null || value.Length > 4096))
            throw new InvalidDataException("Invalid BPA profile values.");
    }
    public static async Task<BpaRuleProfile> LoadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (file.Length > 1024 * 1024) throw new InvalidDataException("BPA profile exceeds 1 MiB.");
        using var memory = new MemoryStream(); await file.CopyToAsync(memory, 4096, ct).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<BpaRuleProfile>(memory.ToArray()) ?? throw new InvalidDataException("Empty BPA profile."); value.Validate(); return value;
    }
    public async Task SaveAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Validate(); var content = JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true });
        if (content.Length > 1024 * 1024) throw new InvalidDataException("BPA profile exceeds 1 MiB.");
        var destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true)) await file.WriteAsync(content, 0, content.Length, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
