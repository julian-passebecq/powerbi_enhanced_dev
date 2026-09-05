namespace PbiBench.ExternalTools;

/// <summary>Versioned path/ID-only handoff. Never pass connection strings, queries, access tokens or credentials to companions.</summary>
public sealed record ToolContext(string? Server = null, string? Database = null, string? ProjectDirectory = null,
    string? ProjectFile = null, string? ReportFile = null, string? PageId = null, string? VisualId = null,
    string? ProjectContextFile = null, string? ModelContextFile = null, string? DashboardSpecFile = null, string? ThemeFile = null);
public sealed record ToolApplicability(bool Enabled, string Reason, IReadOnlyList<string> Arguments);
public static class ExternalToolContext
{
    public static ToolApplicability Evaluate(ExternalToolStatus status, ToolContext context)
    {
        ToolApplicability Disabled(string reason) => new(false, reason, Array.Empty<string>());
        ToolApplicability Enabled(params string[] args) => new(true, "Ready for current context", Array.AsReadOnly(args));
        if (status.Path == null || !File.Exists(status.Path)) return Disabled(status.Diagnostic ?? status.Tool.Name + " is missing; configure its executable path.");
        string? Existing(string? path, string extension) => path != null && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? Path.GetFullPath(path) : null;
        var pbip = Existing(context.ProjectFile, ".pbip"); var pbir = Existing(context.ReportFile, ".pbir");
        switch (status.Tool.Id)
        {
            case "bravo":
                if (!Endpoint(context.Server) || string.IsNullOrWhiteSpace(context.Database) || context.Database!.Any(char.IsControl) || context.Database!.Length > 512)
                    return Disabled("Bravo requires a live server and database. Offline BIM/TMDL/PBIP metadata has no compatible engine connection.");
                return Enabled("--server=" + context.Server, "--database=" + context.Database);
            case "powerbi":
                return (pbip ?? pbir) != null ? Enabled(pbip ?? pbir!) : Disabled("Open a known PBIP project or definition.pbir report first.");
            case "report-studio":
                var path = pbir ?? pbip;
                var design = context.ModelContextFile != null || context.DashboardSpecFile != null || context.ThemeFile != null;
                if (path == null && !design) return Disabled("Select a PBIP or PBIR report in the current project first.");
                var args = new List<string> { "--contract-version", "1" };
                if (path != null) { args.Add("--report"); args.Add(path); }
                if (design)
                {
                    if (Existing(context.ModelContextFile, ".json") == null || (context.DashboardSpecFile == null && context.ThemeFile == null)) return Disabled("Design Preview requires model context and a dashboard spec or theme.");
                    foreach (var pair in new[] { ("--model-context", context.ModelContextFile), ("--dashboard-spec", context.DashboardSpecFile), ("--theme", context.ThemeFile) })
                        if (pair.Item2 != null) { var file = Existing(pair.Item2, ".json"); if (file == null) return Disabled("Design input file is missing."); args.Add(pair.Item1); args.Add(file); }
                }
                if (context.ProjectContextFile != null) { var file = Existing(context.ProjectContextFile, ".json"); if (file == null) return Disabled("Project context file is missing."); args.Add("--project-context"); args.Add(file); }
                foreach (var pair in new[] { ("--page", context.PageId), ("--visual", context.VisualId) })
                    if (pair.Item2 != null) { if (pair.Item2.Length > 50 || pair.Item2.Any(char.IsControl)) return Disabled("Invalid report object ID."); args.Add(pair.Item1); args.Add(pair.Item2); }
                return Enabled(args.ToArray());
            case "fabric-toolbox":
                if (context.ProjectContextFile == null) return Enabled();
                var project = Existing(context.ProjectContextFile, ".json");
                return project == null ? Disabled("Project context file is missing.") : Enabled("--contract-version", "1", "--project-context", project);
            case "vscode":
                return context.ProjectDirectory != null && Directory.Exists(context.ProjectDirectory) ? Enabled(Path.GetFullPath(context.ProjectDirectory)) : Disabled("Open a project folder first.");
            default: return Enabled();
        }
    }
    private static bool Endpoint(string? server) => !string.IsNullOrWhiteSpace(server) && server!.Length <= 2048 && !server.Any(char.IsControl) && !server.Contains(';') && !server.Contains('=') && !server.Contains('@') &&
        (server.StartsWith("powerbi://", StringComparison.OrdinalIgnoreCase) || server.StartsWith("asazure://", StringComparison.OrdinalIgnoreCase) || !server.Contains("://"));
}
