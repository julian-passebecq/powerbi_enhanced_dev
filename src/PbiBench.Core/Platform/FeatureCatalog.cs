using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PbiBench.Core.Platform;

public sealed record FeatureComparisonBaseline(string Product, string VerifiedVersion, string VerifiedDate, string SourceUrl);
public sealed record FeatureComparison(string Comparison, string Capability, string? SourceUrl);
public sealed record CatalogFeature(string Id, string Name, string Status, string Lifecycle, string UiLocation, string Summary,
    string Implementation, IReadOnlyList<string> ProvenanceIds, IReadOnlyList<string> ModuleIds, IReadOnlyList<string> Limitations, FeatureComparison Te3);
public enum FeatureMapFilter { All, Core, Companions, Labs, Te3Gaps }

/// <summary>Offline product metadata. The provenance ledger remains authoritative for sources, pins and licenses.</summary>
public sealed record FeatureCatalog(int SchemaVersion, string ProductVersion, string BaselineCommit,
    FeatureComparisonBaseline Comparison, IReadOnlyList<CatalogFeature> Features)
{
    // Closed, case-sensitive JSON enum vocabularies; display labels are also the serialized values.
    public static IReadOnlyList<string> Statuses { get; } = Array.AsReadOnly(new[] { "Core", "Companion", "External", "Utility", "Labs", "Future", "Gap" });
    public static IReadOnlyList<string> Lifecycles => ModuleCatalog.Lifecycles;
    public static IReadOnlyList<string> Comparisons { get; } = Array.AsReadOnly(new[] { "Comparable", "Partial", "Gap", "Different", "No direct equivalent", "N/A" });
    public const string ComparisonNotice = "Public capability comparison only; comparable does not mean equal depth. Gaps are informational. Every lifecycle allows future development within its module and update lane.";

    public static FeatureCatalog Parse(string json, ProvenanceCatalog provenance, ModuleCatalog? modules = null)
    {
        modules ??= ModuleCatalog.Bundled();
        if (json == null || Encoding.UTF8.GetByteCount(json) > 256 * 1024) throw new InvalidDataException("Feature catalog exceeds 256 KiB.");
        FeatureCatalog value;
        try
        {
            using var document = JsonDocument.Parse(json); RejectDuplicates(document.RootElement);
            value = JsonSerializer.Deserialize<FeatureCatalog>(json, new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? throw new InvalidDataException("Empty feature catalog.");
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid feature catalog JSON.", error); }
        if (value.SchemaVersion != 2 || !Match(value.ProductVersion, @"^\d+\.\d+\.\d+$") || value.ProductVersion != provenance.ProductVersion || value.ProductVersion != modules.ProductVersion || value.BaselineCommit != modules.BaselineCommit ||
            !Match(value.BaselineCommit, "^[0-9a-f]{40}$") || value.BaselineCommit != provenance.BaselineCommit ||
            value.Features == null || value.Features.Count is < 1 or > 64) throw new InvalidDataException("Invalid feature catalog header or provenance version mismatch.");
        var baseline = value.Comparison;
        if (baseline == null || baseline.Product != "Tabular Editor 3" || !Match(baseline.VerifiedVersion, @"^\d+\.\d+\.\d+$") ||
            !DateTime.TryParseExact(baseline.VerifiedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || !OfficialUrl(baseline.SourceUrl))
            throw new InvalidDataException("Invalid public comparison baseline.");
        var known = new HashSet<string>(provenance.Components.Select(c => c.Id), StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in value.Features)
        {
            if (feature == null || !Match(feature.Id, "^[a-z][a-z0-9-]{0,63}$") || !ids.Add(feature.Id) || !Text(feature.Name, 64) ||
                !Statuses.Contains(feature.Status, StringComparer.Ordinal) || !Lifecycles.Contains(feature.Lifecycle, StringComparer.Ordinal) ||
                feature.ModuleIds == null || feature.ModuleIds.Count is < 1 or > 8 || feature.ModuleIds.Any(id => !modules.Modules.Any(m => m.Id == id)) || feature.ModuleIds.Distinct(StringComparer.Ordinal).Count() != feature.ModuleIds.Count ||
                !Text(feature.UiLocation, 160) || !Text(feature.Summary, 240) || !Text(feature.Implementation, 120) ||
                feature.ProvenanceIds == null || feature.ProvenanceIds.Count > 16 || feature.ProvenanceIds.Any(id => !known.Contains(id)) ||
                feature.ProvenanceIds.Distinct(StringComparer.Ordinal).Count() != feature.ProvenanceIds.Count ||
                feature.ProvenanceIds.Count == 0 && feature.Status is not ("Gap" or "Future") ||
                feature.Limitations == null || feature.Limitations.Count is < 1 or > 8 || feature.Limitations.Any(s => !Text(s, 512)))
                throw new InvalidDataException("Invalid, duplicate or unlinked feature row.");
            var comparison = feature.Te3;
            if (comparison == null || !Comparisons.Contains(comparison.Comparison, StringComparer.Ordinal) || !Text(comparison.Capability, 80) ||
                (comparison.SourceUrl != null ? !OfficialUrl(comparison.SourceUrl) : comparison.Comparison != "N/A"))
                throw new InvalidDataException("Invalid TE3 comparison or non-official evidence URL.");
        }
        return value with { Features = Array.AsReadOnly(value.Features.Select(f => f with {
            ProvenanceIds = Array.AsReadOnly(f.ProvenanceIds.ToArray()), ModuleIds = Array.AsReadOnly(f.ModuleIds.ToArray()), Limitations = Array.AsReadOnly(f.Limitations.ToArray()) }).ToArray()) };
    }
    public static FeatureCatalog Bundled(ProvenanceCatalog? provenance = null)
    {
        using var stream = typeof(FeatureCatalog).Assembly.GetManifestResourceStream("PbiBench.feature_catalog.json") ?? throw new InvalidDataException("Bundled feature catalog is missing.");
        using var reader = new StreamReader(stream); return Parse(reader.ReadToEnd(), provenance ?? ProvenanceCatalog.Bundled());
    }
    public IReadOnlyList<FeatureMapRow> Rows(ProvenanceCatalog provenance, FeatureMapFilter filter = FeatureMapFilter.All, ModuleCatalog? modules = null)
    {
        var owners = (modules ?? ModuleCatalog.Bundled()).Modules.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var components = provenance.Components.ToDictionary(c => c.Id, StringComparer.Ordinal);
        return Array.AsReadOnly(Features.Where(f => filter switch {
            FeatureMapFilter.All => true, FeatureMapFilter.Core => f.Status == "Core",
            FeatureMapFilter.Companions => f.Status is "Companion" or "External",
            FeatureMapFilter.Labs => f.Status is "Labs" or "Future",
            FeatureMapFilter.Te3Gaps => f.Te3.Comparison is "Partial" or "Gap", _ => throw new ArgumentOutOfRangeException(nameof(filter))
        }).Select(f => new FeatureMapRow(f, Array.AsReadOnly(f.ProvenanceIds.Select(id => components[id]).ToArray()), Array.AsReadOnly(f.ModuleIds.Select(id => owners[id]).ToArray()))).ToArray());
    }
    public string ToMarkdown(ProvenanceCatalog provenance, ModuleCatalog? modules = null)
    {
        var text = new StringBuilder(); void Line(string line = "") => text.Append(line).Append('\n');
        Line("# PbiBench " + ProductVersion + " Feature Catalog"); Line();
        Line("Generated from feature_catalog.json, module_catalog.json and provenance.json. Do not edit this file directly.");
        Line("Regenerate: `dotnet run --project scripts/FeatureCatalogGenerator -- <repository-root>`."); Line();
        Line("Baseline: `" + BaselineCommit + "`. Detailed sources, licenses and pins below are joined from the provenance ledger.");
        Line("Comparison: " + Comparison.Product + " " + Comparison.VerifiedVersion + ", verified " + Comparison.VerifiedDate + ". [Official version reference](" + Comparison.SourceUrl + ")."); Line();
        Line(ComparisonNotice); Line("Comparisons are PbiBench assessments of official public documentation, not TE3 provenance or hands-on parity tests. Edition and connection limitations may apply. No TE3 binaries, code or assets are used."); Line();
        Line("## Overview"); Line(); Line("| Feature | Status | Lifecycle | Origin | TE3 comparison |"); Line("| --- | --- | --- | --- | --- |");
        var rows = Rows(provenance, modules: modules);
        foreach (var row in rows) Line("| " + string.Join(" | ", new[] { row.Name, row.Status, row.Lifecycle, row.Origin, row.Te3 }.Select(Escape)) + " |");
        Line();
        foreach (var row in rows)
        {
            var f = row.Feature;
            Line("## " + Escape(f.Name)); Line();
            Line("- ID: `" + f.Id + "`"); Line("- Status: " + f.Status + "; lifecycle: " + row.Lifecycle);
            foreach (var m in row.Modules) Line("- Module: " + Escape(m.Id) + " " + m.Version + " · " + m.Kind + " · " + string.Join(", ", m.TargetFrameworks) + " · update lane: " + Escape(m.UpdateLane) + " · entry: " + Escape(m.EntryPoint));
            Line("- Purpose: " + Escape(f.Summary)); Line("- UI location: " + Escape(f.UiLocation));
            Line("- PbiBench implementation: " + Escape(f.Implementation)); Line("- Origin summary: " + Escape(row.Origin));
            Line("- TE3 public comparison: " + Escape(row.Te3) + "; verified " + Comparison.VerifiedDate + "." + (f.Te3.SourceUrl == null ? "" : " [Official capability reference](" + f.Te3.SourceUrl + ")."));
            Line("- Known limitations: " + string.Join(" ", f.Limitations.Select(Escape))); Line();
            if (row.Components.Count == 0) { Line("No implementation provenance is claimed for this gap/future area."); Line(); }
            foreach (var c in row.Components)
            {
                Line("### Component: " + c.Id); Line(); Line("- Feature / owner: " + Escape(c.Feature) + " / " + Escape(c.OwnerProject));
                Line("- Source type: " + Escape(c.SourceType)); Line("- Upstream: " + Escape(c.Upstream)); Line("- Pin: " + Escape(c.Pin));
                Line("- License: " + Escape(c.License)); Line("- Adapter: `" + c.LocalAdapter + "`"); Line("- Update lane: " + Escape(c.UpdateLane));
                Line("- Local patches: " + (c.LocalPatches.Count == 0 ? "None" : string.Join(", ", c.LocalPatches.Select(p => "`" + p + "`"))));
                Line("- Protecting tests: " + string.Join(", ", c.ProtectingTests.Select(p => "`" + p + "`"))); Line();
            }
        }
        return text.ToString().TrimEnd('\n') + "\n";
    }
    private static bool Text(string? text, int maximum) => !string.IsNullOrWhiteSpace(text) && text!.Length <= maximum && !text.Any(char.IsControl);
    private static bool Match(string? value, string pattern) => value != null && value.Length <= 80 && Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static bool OfficialUrl(string? value) => value != null && value.Length <= 256 &&
        value.StartsWith("https://docs.tabulareditor.com/", StringComparison.Ordinal) && !value.Contains('\\') && !value.Any(char.IsWhiteSpace) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "docs.tabulareditor.com" && uri.IsDefaultPort && uri.UserInfo == "" && uri.AbsolutePath.StartsWith("/en/", StringComparison.Ordinal) && uri.AbsolutePath.EndsWith(".html", StringComparison.Ordinal) && uri.Query == "";
    private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("|", "\\|").Replace("[", "\\[").Replace("]", "\\]").Replace("\r\n", " ").Replace("\n", " ");
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate catalog field."); RejectDuplicates(p.Value); } }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}

public sealed record FeatureMapRow(CatalogFeature Feature, IReadOnlyList<ProvenanceComponent> Components, IReadOnlyList<CatalogModule> Modules)
{
    public string Name => Feature.Name;
    public string Status => Feature.Status;
    public string Lifecycle => ModuleCatalog.LifecycleLabel(Feature.Lifecycle);
    public string Implementation => Feature.Implementation;
    public string Te3 => Feature.Te3.Comparison + " · " + Feature.Te3.Capability;
    public string Origin
    {
        get
        {
            if (Components.Count == 0) return "Not implemented";
            var te2 = Components.FirstOrDefault(c => c.SourceType is "te2-mit" or "te2-backed");
            if (te2 != null) return "TE2 " + te2.Pin.Split(' ')[0] + " MIT + PbiBench";
            if (Components.Any(c => c.SourceType == "original-plus-te2-data")) return "TE2 data + PbiBench";
            var external = Components.FirstOrDefault(c => c.SourceType == "external-process-bridge");
            if (external != null) return "External: " + external.Upstream.Replace(" external executable", "");
            if (Components.Any(c => c.SourceType == "original-public-api-adapter" || c.SourceType == "third-party-package" && c.Upstream.StartsWith("Microsoft", StringComparison.Ordinal))) return "Microsoft APIs + PbiBench";
            return "PbiBench original";
        }
    }
    public string Detail => Feature.Summary + "\nLocation: " + Feature.UiLocation + "\n" + string.Join(" ", Feature.Limitations) +
        "\n" + string.Join("\n", Modules.Select(m => "Module: " + m.Id + " · version: " + m.Version + " · " + m.Kind + " · runtime: " + string.Join(", ", m.TargetFrameworks) + " · update lane: " + m.UpdateLane + " · entry: " + m.EntryPoint)) +
        "\n" + (Components.Count == 0 ? "No implementation provenance claimed." : string.Join("\n", Components.Select(c => c.Id + " · " + c.OwnerProject + " · update lane: " + c.UpdateLane)));
}
