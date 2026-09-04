namespace PbiBench.Workspace;

public sealed record PbipInventory(
    string Root,
    IReadOnlyList<string> PbipFiles,
    IReadOnlyList<string> PbirFiles,
    IReadOnlyList<string> TmdlFiles,
    IReadOnlyList<string> DaxQueryFiles,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<string> SemanticModelFolders { get; init; } = Array.Empty<string>();
    public bool HasTmdl => TmdlFiles.Count > 0;
    public bool HasPbir => PbirFiles.Count > 0 || HasEnhancedPbir;
    public bool HasEnhancedPbir { get; init; }
}

public sealed class PbipWorkspaceScanner
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".pbi", ".pbibench", "node_modules", "bin", "obj" };

    /// <summary>Finds the closest PBIP workspace above an active model file or folder.</summary>
    public PbipInventory? Detect(string activeFileOrFolder, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(activeFileOrFolder)) return null;
        var path = Path.GetFullPath(activeFileOrFolder);
        var directory = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path).Directory;
        while (directory != null)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (directory.Exists && directory.EnumerateFiles("*.pbip", SearchOption.TopDirectoryOnly).Any())
                    return Scan(directory.FullName, ct);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            directory = directory.Parent;
        }
        return null;
    }

    public Task<PbipInventory?> DetectAsync(string activeFileOrFolder, CancellationToken ct = default)
        => Task.Run(() => Detect(activeFileOrFolder, ct), ct);

    public Task<PbipInventory> ScanAsync(string rootPath, CancellationToken ct = default)
        => Task.Run(() => Scan(rootPath, ct), ct);

    public PbipInventory Scan(string rootPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var warnings = new List<string>();
        var files = EnumerateSafely(root, warnings, ct);
        var pbip = WithExtension(files, ".pbip");
        var pbir = WithExtension(files, ".pbir");
        var tmdl = WithExtension(files, ".tmdl");
        var dax = WithExtension(files, ".dax");
        var semanticFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (string.Equals(name, "definition.pbism", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "model.bim", StringComparison.OrdinalIgnoreCase))
                semanticFolders.Add(Path.GetDirectoryName(file)!);
            var parent = new FileInfo(file).Directory;
            while (parent != null && IsWithin(root, parent.FullName))
            {
                if (parent.Name.EndsWith(".SemanticModel", StringComparison.OrdinalIgnoreCase))
                { semanticFolders.Add(parent.FullName); break; }
                parent = parent.Parent;
            }
        }
        if (pbip.Count == 0) warnings.Add("No .pbip file found.");
        if (files.Any(file => string.Equals(Path.GetFileName(file), "unappliedChanges.json", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("unappliedChanges.json detected: external semantic expression edits may be lost when pending Desktop changes are applied.");
        if (root.Length > 120 || files.Any(file => file.Length >= 240))
            warnings.Add("Workspace paths are long; nested PBIP paths can approach Windows path limits.");
        if (root.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
            warnings.Add("Workspace appears under OneDrive; concurrent sync can cause file churn/conflicts.");
        return new PbipInventory(root, pbip, pbir, tmdl, dax, warnings.ToArray())
        {
            SemanticModelFolders = semanticFolders.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            HasEnhancedPbir = files.Any(file => string.Equals(Path.GetFileName(file), "report.json", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "definition", StringComparison.OrdinalIgnoreCase))
        };
    }

    private static bool IsWithin(string root, string path) => string.Equals(root, path, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> WithExtension(IEnumerable<string> files, string extension)
        => files.Where(file => string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyList<string> EnumerateSafely(string root, List<string> warnings, CancellationToken ct)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        warnings.Add("Skipped linked path outside the workspace scan: " + entry);
                        continue;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        var directoryName = Path.GetFileName(entry);
                        if (string.Equals(directoryName, ".pbi", StringComparison.OrdinalIgnoreCase))
                        {
                            // Desktop stores pending Power Query edits here. Inspect the marker without reading cache data.
                            var marker = Path.Combine(entry, "unappliedChanges.json");
                            if (File.Exists(marker)) files.Add(marker);
                        }
                        else if (!ExcludedDirectories.Contains(directoryName)) pending.Push(entry);
                    }
                    else files.Add(entry);
                }
            }
            catch (UnauthorizedAccessException) { warnings.Add("Cannot read folder: " + directory); }
            catch (IOException) { warnings.Add("Could not fully scan folder: " + directory); }
        }
        return files;
    }
}
