using System.Text.Json.Nodes;

namespace PbiBench.Pbir;

/// <summary>Explicitly tested format lanes; every referenced file must also pass the pinned offline validator.</summary>
internal static class PbirWritePolicy
{
    internal static readonly JsonObject Policy = Load();
    private static JsonObject Load()
    {
        using var stream = typeof(PbirWritePolicy).Assembly.GetManifestResourceStream("PbiBench.Pbir.WritePolicy")!;
        using var reader = new StreamReader(stream);
        return Disk.Parse(reader.ReadToEnd());
    }
    internal static bool Supports(ReportIndex report)
    {
        if (!report.Files.TryGetValue("definition.pbir", out var definition) || definition.ParseError != null ||
            !report.Files.TryGetValue("definition/version.json", out var metadata) || metadata.ParseError != null) return false;
        return Policy["supportedContracts"]!.AsArray().OfType<JsonObject>().Any(lane =>
            lane["definitionVersion"]!.ToString() == report.Version &&
            lane["definitionSchemas"]!.AsArray().Any(s => s!.ToString() == definition.Schema) &&
            lane["metadataVersion"]!.ToString() == metadata.Json()["version"]?.ToString() &&
            lane["metadataSchema"]!.ToString() == metadata.Schema);
    }
}
