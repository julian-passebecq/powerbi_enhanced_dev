namespace PbiBench.ExternalTools;

public sealed record ModuleHandoff(string? Report = null, string? Page = null, string? Visual = null,
    string? ProjectContext = null, string? ModelContext = null, string? DashboardSpec = null, string? Theme = null)
{
    public static ModuleHandoff Parse(IReadOnlyList<string> args, bool reportModule)
    {
        if (args.Count == 0) return new();
        if (reportModule && args.Count == 1 && !args[0].StartsWith("-", StringComparison.Ordinal)) return new(Report: Path.GetFullPath(args[0]));
        var allowed = reportModule ? new[] { "--contract-version", "--report", "--page", "--visual", "--project-context", "--model-context", "--dashboard-spec", "--theme" } : new[] { "--contract-version", "--project-context" };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i += 2)
        {
            if (!allowed.Contains(args[i], StringComparer.Ordinal) || i + 1 >= args.Count || values.ContainsKey(args[i]) || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].Any(char.IsControl)) throw new InvalidDataException("Invalid or duplicate module handoff argument.");
            values.Add(args[i], args[i + 1]);
        }
        if (!values.TryGetValue("--contract-version", out var version) || version != "1") throw new InvalidDataException("Only module handoff contract v1 is supported.");
        string? Value(string key) => values.TryGetValue(key, out var value) ? value : null;
        foreach (var id in new[] { Value("--page"), Value("--visual") }) if (id != null && id.Length > 50) throw new InvalidDataException("Report object ID exceeds the handoff limit.");
        foreach (var file in new[] { "--report", "--project-context", "--model-context", "--dashboard-spec", "--theme" })
            if (Value(file) is { } path && (!Path.IsPathRooted(path) || path.Length > 32767)) throw new InvalidDataException("Handoff paths must be absolute local paths.");
        if ((Value("--dashboard-spec") != null || Value("--theme") != null) && Value("--model-context") == null) throw new InvalidDataException("Design handoff requires model context.");
        if (Value("--model-context") != null && Value("--dashboard-spec") == null && Value("--theme") == null) throw new InvalidDataException("Choose a dashboard spec or theme for Design Preview.");
        return new(Value("--report"), Value("--page"), Value("--visual"), Value("--project-context"), Value("--model-context"), Value("--dashboard-spec"), Value("--theme"));
    }
}
