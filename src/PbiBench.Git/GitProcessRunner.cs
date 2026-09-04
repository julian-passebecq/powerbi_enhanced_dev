using System.Diagnostics;
using System.Text;

namespace PbiBench.Git;

public interface IGitProcessRunner
{
    Task<GitResult> RunAsync(string root, IReadOnlyList<string> arguments, CancellationToken ct = default);
}

/// <summary>Replaceable, read-only Git process transport. No shell or repository writes.</summary>
public sealed class GitProcessRunner : IGitProcessRunner
{
    public async Task<GitResult> RunAsync(string root, IReadOnlyList<string> arguments, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var command = string.Join(" ", new[] { "--no-optional-locks", "-c", "core.quotepath=false" }.Concat(arguments).Select(Quote));
        using var process = new Process { StartInfo = new ProcessStartInfo("git", command)
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        }, EnableRaisingEvents = true };
        process.StartInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult(true);
        if (!process.Start()) throw new InvalidOperationException("Git did not start.");
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
            return new GitResult(process.ExitCode, output, error);
        }
    }

    private static string Quote(string value)
    {
        if (value.IndexOf('\0') >= 0) throw new ArgumentException("Arguments cannot contain NUL.", nameof(value));
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
