namespace PbiBench.ExternalTools;

public static class ExecutableDiscovery
{
    public static string? ExistingExecutable(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            return File.Exists(fullPath) && string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase) ? fullPath : null;
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (PathTooLongException) { return null; }
    }
    public static string? Find(string executable, string? configuredPath, IEnumerable<string?> directories)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)) return ExistingExecutable(configuredPath!);
        foreach (var directory in directories.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            try { var found = ExistingExecutable(Path.Combine(directory!.Trim().Trim('"'), executable)); if (found != null) return found; }
            catch (ArgumentException) { } catch (NotSupportedException) { }
        }
        return null;
    }
}
