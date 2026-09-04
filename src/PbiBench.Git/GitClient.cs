namespace PbiBench.Git;

public sealed record GitResult(int ExitCode, string Stdout, string Stderr);

public sealed record GitChange(string Status, string Path, string? OriginalPath, bool IsSemantic)
{
    public bool IsConflict => Status == "UU" || Status == "AA" || Status == "DD" || Status == "AU"
        || Status == "UA" || Status == "DU" || Status == "UD";
}

public sealed record GitStatus(bool IsRepository, string? RepositoryRoot, string? Branch,
    IReadOnlyList<GitChange> Changes, IReadOnlyList<string> Warnings)
{
    public bool IsStatusKnown { get; init; } = true;
    public bool IsDirty => Changes.Count > 0;
    public IReadOnlyList<string> ChangedSemanticFiles => Changes.Where(change => change.IsSemantic)
        .Select(change => change.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public string Summary => !IsRepository ? "Git unavailable" : !IsStatusKnown ? "Git status unavailable"
        : (Branch ?? "Detached HEAD") + (IsDirty ? " · modified" : " · clean");
}

public sealed class GitClient
{
    private readonly IGitProcessRunner _process;

    public GitClient(IGitProcessRunner? processRunner = null) => _process = processRunner ?? new GitProcessRunner();

    public Task<GitResult> StatusAsync(string root, CancellationToken ct = default)
        => _process.RunAsync(root, new[] { "status", "--porcelain=v1", "--branch" }, ct);
    public Task<GitResult> DiffAsync(string root, CancellationToken ct = default)
        => _process.RunAsync(root, new[] { "diff", "--" }, ct);
    public Task<GitResult> DiffCachedAsync(string root, CancellationToken ct = default)
        => _process.RunAsync(root, new[] { "diff", "--cached", "--" }, ct);

    public async Task<GitStatus> GetStatusAsync(string root, IEnumerable<string>? semanticFolders = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        root = Path.GetFullPath(root);
        try
        {
            var location = await _process.RunAsync(root, new[] { "rev-parse", "--show-toplevel" }, ct).ConfigureAwait(false);
            if (location.ExitCode != 0)
                return new GitStatus(false, null, null, Array.Empty<GitChange>(), new[] { "No readable Git repository was found for this workspace." });
            var repositoryRoot = location.Stdout.TrimEnd('\r', '\n');
            var status = await _process.RunAsync(repositoryRoot,
                new[] { "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all" }, ct).ConfigureAwait(false);
            if (status.ExitCode != 0)
                return new GitStatus(true, repositoryRoot, null, Array.Empty<GitChange>(), new[] { "Git status failed. Check repository access and Git configuration." })
                    { IsStatusKnown = false };
            return ParseStatus(repositoryRoot, status.Stdout, semanticFolders);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new GitStatus(false, null, null, Array.Empty<GitChange>(), new[] { "Git could not start. Install Git for Windows and make git.exe available on PATH." });
        }
        catch (DirectoryNotFoundException)
        {
            return new GitStatus(false, null, null, Array.Empty<GitChange>(), new[] { "The workspace folder no longer exists." });
        }
    }

    /// <summary>Parses NUL-delimited porcelain v1, preserving spaces, Unicode and rename destinations.</summary>
    public static GitStatus ParseStatus(string repositoryRoot, string porcelain, IEnumerable<string>? semanticFolders = null)
    {
        var folders = (semanticFolders ?? Array.Empty<string>()).Select(folder => NormalizeRelative(repositoryRoot, folder)).ToArray();
        var entries = porcelain.Split('\0');
        var changes = new List<GitChange>();
        var warnings = new List<string>();
        string? branch = null;
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry.StartsWith("## ", StringComparison.Ordinal))
            {
                branch = ParseBranch(entry.Substring(3));
                continue;
            }
            if (entry.Length == 0) continue;
            if (entry.Length < 4 || entry[2] != ' ')
            {
                warnings.Add("Git returned an unrecognized status entry.");
                continue;
            }
            var status = entry.Substring(0, 2);
            var path = entry.Substring(3);
            string? originalPath = null;
            if (status.IndexOf('R') >= 0 || status.IndexOf('C') >= 0)
            {
                if (index + 1 < entries.Length && entries[index + 1].Length > 0) originalPath = entries[++index];
                else warnings.Add("Git returned a rename without its original path.");
            }
            changes.Add(new GitChange(status, path, originalPath,
                IsSemantic(path, folders) || (originalPath != null && IsSemantic(originalPath, folders))));
        }
        if (changes.Any(change => change.IsConflict)) warnings.Add("Unresolved Git conflicts exist. Resolve them before external model edits.");
        return new GitStatus(true, Path.GetFullPath(repositoryRoot), branch, changes.ToArray(), warnings.Distinct().ToArray());
    }

    private static string? ParseBranch(string value)
    {
        if (value.StartsWith("No commits yet on ", StringComparison.Ordinal)) value = value.Substring(18);
        else if (value.StartsWith("Initial commit on ", StringComparison.Ordinal)) value = value.Substring(18);
        if (value.StartsWith("HEAD (", StringComparison.Ordinal) || value.StartsWith("(no branch)", StringComparison.Ordinal)) return null;
        var upstream = value.IndexOf("...", StringComparison.Ordinal);
        if (upstream >= 0) value = value.Substring(0, upstream);
        var tracking = value.IndexOf(" [", StringComparison.Ordinal);
        return tracking >= 0 ? value.Substring(0, tracking) : value.TrimEnd('\r', '\n');
    }

    private static string NormalizeRelative(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(fullRoot, path));
        if (string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)) return "";
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? fullPath.Substring(fullRoot.Length + 1).Replace('\\', '/').TrimEnd('/')
            : fullPath.Replace('\\', '/').TrimEnd('/');
    }

    private static bool IsSemantic(string path, IReadOnlyList<string> semanticFolders)
    {
        var normalized = path.Replace('\\', '/');
        if (semanticFolders.Any(folder => folder.Length == 0 || string.Equals(normalized, folder, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))) return true;
        if (normalized.Split('/').Any(segment => segment.EndsWith(".SemanticModel", StringComparison.OrdinalIgnoreCase))) return true;
        var extension = Path.GetExtension(normalized);
        return string.Equals(extension, ".tmdl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bim", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".pbism", StringComparison.OrdinalIgnoreCase);
    }
}
