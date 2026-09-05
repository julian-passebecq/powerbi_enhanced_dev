using System.Text;
using System.Text.Json;

namespace PbiBench.Pbir;

public sealed record ReportOccurrenceImpact(int Occurrences, int Files, int Pages, int Visuals)
{
    public static ReportOccurrenceImpact From(IEnumerable<ReportUsage> usages)
    {
        var rows = usages.ToArray();
        return new(rows.Length, rows.Select(u => u.ReportRoot + "/" + u.File).Distinct().Count(),
            rows.Where(u => u.File.StartsWith("definition/pages/", StringComparison.Ordinal)).Select(u => u.ReportRoot + "/" + u.File.Split('/')[2]).Distinct().Count(),
            rows.Where(u => u.Visual != "(page/report)").Select(u => u.ReportRoot + "/" + u.File).Distinct().Count());
    }
    public override string ToString() => $"{Occurrences} occurrences · {Files} files · {Pages} pages · {Visuals} visuals";
}
public static class ReportImpact
{
    public static bool Matches(ReportUsage usage, SemanticField field) =>
        string.Equals(usage.Table, field.Table, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(usage.Name, field.Name, StringComparison.OrdinalIgnoreCase) && usage.Kind == field.Kind;
    public static IReadOnlyList<ReportUsage> Find(IEnumerable<ReportIndex> reports, IEnumerable<SemanticField> fields)
    {
        var selected = fields.ToArray();
        return Array.AsReadOnly(reports.SelectMany(r => ReportLineage.Build(r)).Where(u => selected.Any(f => Matches(u, f))).ToArray());
    }
}
public sealed class ReportImpactHandoff
{
    public int Version => 1;
    public string Operation { get; }
    public SemanticField Before { get; }
    public SemanticField? After { get; }
    public IReadOnlyList<ReportUsage> Usages { get; }
    public IReadOnlyList<ReportImpactFile> Files { get; }
    public string Recovery => "Apply TOM and PBIR separately with their own reviewed plans and recovery. This handoff authorizes no writes and is not an atomic cross-layer transaction.";
    public ReportImpactHandoff(string operation, SemanticField before, SemanticField? after, IEnumerable<ReportIndex> reports)
    {
        Operation = operation; Before = before; After = after; var indexes = reports.ToArray();
        Usages = ReportImpact.Find(indexes, new[] { before });
        Files = Array.AsReadOnly(Usages.Select(u => new ReportImpactFile(u.ReportRoot, u.File, indexes.Single(r => r.Root == u.ReportRoot).Files[u.File].Hash)).Distinct().ToArray());
    }
    public Task SaveAsync(string destination, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested(); Disk.CheckLinks(destination);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.Write(bytes, 0, bytes.Length);
    }, ct);
}
public sealed record ReportImpactFile(string ReportRoot, string File, string Hash);
