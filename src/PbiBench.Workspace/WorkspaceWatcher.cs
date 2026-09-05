namespace PbiBench.Workspace;

/// <summary>Events invalidate snapshots. They never reload a model or write files.</summary>
public sealed class WorkspaceWatcher : IDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly FileSystemWatcher directories;
    private readonly Timer timer;
    private long sequence; private int disposed; private string? lastChange;
    public WorkspaceWatcher(string directory, int debounceMilliseconds = 350)
    {
        WorkspaceDiskStore.RejectLinks(directory); timer = new Timer(_ => { if (Volatile.Read(ref disposed) == 0) Changed?.Invoke(this, EventArgs.Empty); }, null, Timeout.Infinite, Timeout.Infinite);
        watcher = new FileSystemWatcher(directory) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        // Windows can report a directory LastWrite notification during read-only traversal.
        // Directory-name notifications preserve moves/deletions (including dotted folder names)
        // without interpreting those directory timestamp notifications as model-file edits.
        directories = new FileSystemWatcher(directory) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.DirectoryName };
        watcher.Changed += FileChanged; watcher.Created += FileChanged; watcher.Deleted += FileChanged;
        watcher.Renamed += (_, e) => { Invalidate(e.FullPath, e.ChangeType, false); Invalidate(e.OldFullPath, e.ChangeType, false); };
        directories.Created += DirectoryChanged; directories.Deleted += DirectoryChanged;
        directories.Renamed += (_, e) => { Invalidate(e.FullPath, e.ChangeType, true); Invalidate(e.OldFullPath, e.ChangeType, true); };
        watcher.Error += WatcherError; directories.Error += WatcherError;
        DebounceMilliseconds = Math.Max(50, Math.Min(5000, debounceMilliseconds)); watcher.EnableRaisingEvents = true; directories.EnableRaisingEvents = true;
    }
    public int DebounceMilliseconds { get; }
    public long Sequence => Interlocked.Read(ref sequence);
    public string? LastChange => Volatile.Read(ref lastChange);
    public event EventHandler? Changed;
    private void FileChanged(object sender, FileSystemEventArgs args) => Invalidate(args.FullPath, args.ChangeType, false);
    private void DirectoryChanged(object sender, FileSystemEventArgs args) => Invalidate(args.FullPath, args.ChangeType, true);
    private void WatcherError(object sender, ErrorEventArgs args) => Invalidate(null, WatcherChangeTypes.All, false);
    private void Invalidate(string? path, WatcherChangeTypes changeType, bool isDirectory)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        if (path != null && (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => new[] { ".pbibench", ".pbi", ".git", "DAXQueries" }.Contains(part, StringComparer.OrdinalIgnoreCase)) ||
            !isDirectory && (!path.EndsWith(".tmdl", StringComparison.OrdinalIgnoreCase) && !string.Equals(Path.GetFileName(path), "model.bim", StringComparison.OrdinalIgnoreCase) || Directory.Exists(path)))) return;
        Volatile.Write(ref lastChange, path == null ? "Watcher notification overflow or error" : changeType + ": " + path);
        Interlocked.Increment(ref sequence); try { timer.Change(DebounceMilliseconds, Timeout.Infinite); } catch (ObjectDisposedException) { }
    }
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; watcher.Dispose(); directories.Dispose(); timer.Dispose(); }
}
