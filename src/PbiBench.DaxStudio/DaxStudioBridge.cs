using System.Text;

namespace PbiBench.DaxStudio;

/// <summary>Launches the standalone specialist tool; never executes a query merely by opening it.</summary>
public sealed class DaxStudioBridge
{
    private readonly string? _daxStudioExe;
    private readonly string? _dscmdExe;
    private readonly IProcessAdapter _process;

    public DaxStudioBridge(string? daxStudioExe = null, string? dscmdExe = null, IProcessAdapter? processAdapter = null)
    {
        _daxStudioExe = daxStudioExe;
        _dscmdExe = dscmdExe;
        _process = processAdapter ?? new SystemProcessAdapter();
    }

    // Official GUI arguments: https://daxstudio.org/docs/features/startup-parameters/
    public ProcessLaunchRequest CreateLaunchRequest(string? server, string? database, string? queryFile)
    {
        var executable = DaxStudioLocator.Discover(_daxStudioExe)
            ?? throw new FileNotFoundException("DAX Studio was not found. Install DAX Studio or configure the full path to DaxStudio.exe in PbiBench.", _daxStudioExe);
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(server))
        {
            args.Add("--server"); args.Add(server!);
            if (!string.IsNullOrWhiteSpace(database)) { args.Add("--database"); args.Add(database!); }
        }
        if (!string.IsNullOrWhiteSpace(queryFile))
        {
            var fullPath = Path.GetFullPath(queryFile!);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("The DAX query file does not exist.", fullPath);
            args.Add("--file"); args.Add(fullPath);
        }
        return new ProcessLaunchRequest(executable, args.ToArray());
    }

    public void Open(string? server, string? database, string? queryFile)
        => _process.Start(CreateLaunchRequest(server, database, queryFile));

    /// <summary>Saves the current text as a new, retained scratch query before opening DAX Studio.</summary>
    public async Task<string> OpenQueryAsync(string queryText, string? server = null, string? database = null,
        string? scratchDirectory = null, CancellationToken ct = default)
    {
        if (queryText is null) throw new ArgumentNullException(nameof(queryText));
        ct.ThrowIfCancellationRequested();
        var request = CreateLaunchRequest(server, database, null);
        var directory = Path.GetFullPath(scratchDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PbiBench", "Scratch"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "query-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".dax");
        var content = new UTF8Encoding(false).GetBytes(queryText);
        using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true))
            await stream.WriteAsync(content, 0, content.Length, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        _process.Start(new ProcessLaunchRequest(request.Executable, request.Arguments.Concat(new[] { "--file", file }).ToArray()));
        return file;
    }

    // Explicit CLI calls retained for compatibility. Opening the GUI never invokes these.
    public Task<ProcessResult> BenchmarkAsync(string outputCsv, string server, string database, string queryFile, CancellationToken ct = default)
        => RunDscmdAsync(new[] { "BENCHMARK", Path.GetFullPath(outputCsv), "--server", server, "--database", database, "--file", Path.GetFullPath(queryFile) }, ct);

    public Task<ProcessResult> QueryCsvAsync(string outputCsv, string server, string database, string queryFile, CancellationToken ct = default)
        => RunDscmdAsync(new[] { "CSV", Path.GetFullPath(outputCsv), "--server", server, "--database", database, "--file", Path.GetFullPath(queryFile) }, ct);

    public Task<ProcessResult> VpaxAsync(string outputVpax, string server, string database, CancellationToken ct = default)
        => RunDscmdAsync(new[] { "VPAX", Path.GetFullPath(outputVpax), "--server", server, "--database", database }, ct);

    private Task<ProcessResult> RunDscmdAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var executable = DaxStudioLocator.DiscoverCommandLine(_dscmdExe, _daxStudioExe)
            ?? throw new FileNotFoundException("dscmd.exe was not found. Configure its path or install the DAX Studio command-line tool.", _dscmdExe);
        return _process.RunAsync(new ProcessLaunchRequest(executable, arguments), ct);
    }
}
