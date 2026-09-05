namespace PbiBench.Pbir;

/// <summary>One cache per immutable index and semantic catalog. Selection, search and paint never rebuild lineage.</summary>
public sealed class ReportViewSnapshot
{
    public ReportIndex Report { get; }
    public IReadOnlyList<ReportUsage> Usages { get; }
    public IReadOnlyList<ReportIssue> Issues { get; }
    private readonly Dictionary<string, IReadOnlyList<ReportUsage>> byFile;
    public ReportViewSnapshot(ReportIndex report, LocalSemanticCatalog catalog, IReadOnlyList<ReportIssue> issues)
    {
        Report = report; Issues = Array.AsReadOnly(issues.ToArray());
        Usages = ReportLineage.Build(report, catalog.Fields, catalog.Complete);
        byFile = Usages.GroupBy(u => u.File).ToDictionary(g => g.Key, g => (IReadOnlyList<ReportUsage>)Array.AsReadOnly(g.ToArray()));
    }
    public IReadOnlyList<ReportUsage> ForFile(string file) => byFile.TryGetValue(file, out var rows) ? rows : Array.Empty<ReportUsage>();
    public string Badges(string file)
    {
        var result = new List<string>(); var errors = Issues.Count(i => i.File == file && i.Severity == "Error");
        if (errors > 0) result.Add("schema " + errors);
        var rows = ForFile(file); if (rows.Any(u => u.Status == "Broken reference")) result.Add("broken");
        if (rows.Any(u => !u.Status.StartsWith("Resolved", StringComparison.Ordinal) && u.Status != "Broken reference")) result.Add("unverified");
        if (Report.Pages.SelectMany(p => p.Visuals).Any(v => v.File == file && v.Hidden)) result.Add("hidden");
        return result.Count == 0 ? "" : " [" + string.Join(" · ", result) + "]";
    }
    public bool Matches(ReportPage page, ReportVisual? visual, string search)
    {
        var values = new[] { page.Id, page.Name, visual?.Type, visual?.Title, visual?.Id }
            .Concat(ForFile(visual?.File ?? page.File).Select(u => u.Table + "[" + u.Name + "]"));
        return string.IsNullOrWhiteSpace(search) || values.Any(v => v?.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
    }
    public ReportOccurrenceImpact Impact(SemanticField field) => ReportOccurrenceImpact.From(Usages.Where(u => ReportImpact.Matches(u, field)));
}
