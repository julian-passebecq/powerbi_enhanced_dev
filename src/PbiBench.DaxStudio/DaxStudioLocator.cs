namespace PbiBench.DaxStudio;

public static class DaxStudioLocator
{
    public static string? Discover(string? configuredPath = null) => Find("DaxStudio.exe", configuredPath, null);

    public static string? DiscoverCommandLine(string? configuredPath = null, string? daxStudioPath = null)
        => Find("dscmd.exe", configuredPath, string.IsNullOrWhiteSpace(daxStudioPath) ? null : Path.GetDirectoryName(daxStudioPath));

    private static string? Find(string executable, string? configuredPath, string? siblingDirectory)
    {
        // An explicit invalid setting must never silently launch a different install.
        if (!string.IsNullOrWhiteSpace(configuredPath)) return ExistingExecutable(configuredPath!);
        var directories = new List<string?>
        {
            siblingDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DAX Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "DAX Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DAX Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DAX Studio")
        };
        directories.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        foreach (var directory in directories.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            try
            {
                var result = ExistingExecutable(Path.Combine(directory!.Trim().Trim('"'), executable));
                if (result != null) return result;
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }
        return null;
    }

    private static string? ExistingExecutable(string path)
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
}
