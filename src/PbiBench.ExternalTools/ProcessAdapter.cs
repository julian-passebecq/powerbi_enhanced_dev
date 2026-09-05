using System.Diagnostics;
using System.Text;

namespace PbiBench.ExternalTools;

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

public interface IFocusProcessAdapter { void StartOrFocus(ProcessLaunchRequest request); }

public sealed class SystemProcessAdapter : IProcessAdapter, IFocusProcessAdapter
{
    private readonly Dictionary<string, Process> children = new(StringComparer.Ordinal);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr handle, int command);
    public void StartOrFocus(ProcessLaunchRequest request)
    {
        var key = Path.GetFullPath(request.Executable).ToUpperInvariant() + "\n" + request.WindowsArguments;
        if (children.TryGetValue(key, out var child))
        {
            if (!child.HasExited)
            {
                child.Refresh();
                if (child.MainWindowHandle != IntPtr.Zero) { ShowWindowAsync(child.MainWindowHandle, 9); SetForegroundWindow(child.MainWindowHandle); }
                return;
            }
            child.Dispose(); children.Remove(key);
        }
        foreach (var ended in children.Where(p => p.Value.HasExited).Select(p => p.Key).ToArray()) { children[ended].Dispose(); children.Remove(ended); }
        children.Add(key, Process.Start(new ProcessStartInfo(request.Executable, request.WindowsArguments) { UseShellExecute = false }) ?? throw new InvalidOperationException("The module did not start."));
    }
    public void Start(ProcessLaunchRequest request)
    {
        using var process = Process.Start(new ProcessStartInfo(request.Executable, request.WindowsArguments) { UseShellExecute = false });
        if (process is null) throw new InvalidOperationException("The external tool did not start.");
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
