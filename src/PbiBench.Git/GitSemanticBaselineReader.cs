namespace PbiBench.Git;

public sealed record GitSemanticFile(string Path, string Content);
public sealed record GitSemanticBaseline(string Commit, IReadOnlyList<GitSemanticFile> Files);
/// <summary>Reads verified HEAD blobs without checkout, staging, filters or repository writes.</summary>
public sealed class GitSemanticBaselineReader
{
    private readonly IGitProcessRunner process;
    public GitSemanticBaselineReader(IGitProcessRunner? process = null) => this.process = process ?? new GitProcessRunner();
    public async Task<GitSemanticBaseline> ReadAsync(string repositoryRoot, string definitionDirectory, bool isBim, CancellationToken ct)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar); definitionDirectory = Path.GetFullPath(definitionDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!definitionDirectory.Equals(repositoryRoot, StringComparison.OrdinalIgnoreCase) && !definitionDirectory.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The definition is outside this repository.");
        var relative = definitionDirectory.Length == repositoryRoot.Length ? "" : definitionDirectory.Substring(repositoryRoot.Length + 1).Replace('\\', '/') + "/";
        var revision = await process.RunAsync(repositoryRoot, new[] { "rev-parse", "--verify", "HEAD^{commit}" }, ct).ConfigureAwait(false);
        var commit = revision.Stdout.Trim(); if (revision.ExitCode != 0 || commit.Length is not (40 or 64) || !commit.All(Uri.IsHexDigit)) throw new InvalidOperationException("Git HEAD has no readable commit baseline.");
        var tree = await process.RunAsync(repositoryRoot, new[] { "ls-tree", "-r", "-z", commit, "--", relative.Length == 0 ? "." : relative.TrimEnd('/') }, ct).ConfigureAwait(false);
        if (tree.ExitCode != 0) throw new InvalidOperationException("Git could not read the semantic baseline tree.");
        var files = new List<GitSemanticFile>(); long total = 0;
        foreach (var entry in tree.Stdout.Split('\0').Where(text => text.Length > 0))
        {
            ct.ThrowIfCancellationRequested(); var tab = entry.IndexOf('\t'); if (tab < 0) throw new InvalidDataException("Unexpected Git tree entry."); var fields = entry.Substring(0, tab).Split(' '); var path = entry.Substring(tab + 1);
            if (fields.Length != 3 || !path.StartsWith(relative, StringComparison.Ordinal)) continue; path = path.Substring(relative.Length);
            if (!(isBim ? path == "model.bim" : path.EndsWith(".tmdl", StringComparison.OrdinalIgnoreCase))) continue;
            if (fields[0] != "100644" && fields[0] != "100755" || fields[1] != "blob" || fields[2].Length is not (40 or 64) || !fields[2].All(Uri.IsHexDigit) || path.Split('/').Any(segment => segment is "" or "." or ".." or ".git" or ".pbibench" or ".pbi")) throw new InvalidDataException("Linked or invalid semantic files are not accepted from Git.");
            if (files.Count >= 10000) throw new InvalidDataException("Git semantic baselines are limited to 10,000 files.");
            var size = await process.RunAsync(repositoryRoot, new[] { "cat-file", "-s", fields[2] }, ct).ConfigureAwait(false);
            if (size.ExitCode != 0 || !long.TryParse(size.Stdout.Trim(), out var bytes) || bytes < 0 || bytes > 16 * 1024 * 1024 || (total += bytes) > 64 * 1024 * 1024) throw new InvalidDataException("Git semantic metadata is oversized or unreadable.");
            var blob = await process.RunAsync(repositoryRoot, new[] { "cat-file", "blob", fields[2] }, ct).ConfigureAwait(false); if (blob.ExitCode != 0) throw new InvalidDataException("A Git semantic blob could not be read.");
            files.Add(new(path, blob.Stdout));
        }
        if (files.Count == 0) throw new InvalidDataException("HEAD contains no semantic definition for this folder."); return new(commit, files.AsReadOnly());
    }
}
