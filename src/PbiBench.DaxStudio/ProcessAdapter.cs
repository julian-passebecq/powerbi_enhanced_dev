using System.Diagnostics;
using System.Text;

namespace PbiBench.DaxStudio;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
public sealed record ProcessLaunchRequest(string Executable, IReadOnlyList<string> Arguments)
{
    public string WindowsArguments => string.Join(" ", Arguments.Select(WindowsCommandLine.Quote));
}

public interface IProcessAdapter
{
    void Start(ProcessLaunchRequest request);
    Task<ProcessResult> RunAsync(ProcessLaunchRequest request, CancellationToken ct = default);
}

/// <summary>Quotes a single argument following the Windows CommandLineToArgvW / CRT rules.</summary>
public static class WindowsCommandLine
{
    public static string Quote(string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (value.IndexOf('\0') >= 0) throw new ArgumentException("Arguments cannot contain NUL.", nameof(value));
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') result.Append('\\', slashes * 2 + 1);
            else result.Append('\\', slashes);
            result.Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}

public sealed class SystemProcessAdapter : IProcessAdapter
{
    public void Start(ProcessLaunchRequest request)
    {
        using var process = Process.Start(new ProcessStartInfo(request.Executable, request.WindowsArguments) { UseShellExecute = false });
        if (process is null) throw new InvalidOperationException("DAX Studio did not start.");
    }

    public async Task<ProcessResult> RunAsync(ProcessLaunchRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = new ProcessStartInfo(request.Executable, request.WindowsArguments)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
          StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 }, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult(true);
        if (!process.Start()) throw new InvalidOperationException("The external tool did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using (ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            exited.TrySetCanceled();
        }))
        {
            if (process.HasExited) exited.TrySetResult(true);
            await exited.Task.ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new ProcessResult(process.ExitCode, output, error);
        }
    }
}
