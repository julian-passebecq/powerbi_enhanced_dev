#if NETFRAMEWORK
using System.Windows.Forms;
using PbiBench.ModelEditor;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;
using Xunit;

namespace PbiBench.Adapters.Tests;

[Collection("Native TE2")]
public sealed class TrustedScriptBoundaryTests
{
    [Fact]
    public Task TrustedExecutionRequiresExplicitAcknowledgmentSnapshotAndCurrentSession() => Sta(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "pbibench-trusted-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var editor = new Te2ModelEditor(() => true, Path.Combine(directory, "profile")); editor.Open(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", "pass1-demo.bim"));
            var handler = editor.Handler!; var before = new SemanticModelService(handler).Fingerprint();
            var ticket = Await(TrustedScriptRunner.PrepareAsync(handler, "Model.Tables[\"Sales\"].Description = \"trusted edit\"; System.Console.WriteLine(\"Captured output\");", Array.Empty<TabularNamedObject>(), Path.Combine(directory, "snapshots"), CancellationToken.None));
            Assert.True(File.Exists(ticket.SnapshotPath)); Assert.Throws<InvalidOperationException>(() => TrustedScriptRunner.Run(ticket, handler, false)); Assert.Equal(before, new SemanticModelService(handler).Fingerprint());
            var console = Console.Out; var errors = Console.Error; var result = TrustedScriptRunner.Run(ticket, handler, true); Assert.Same(console, Console.Out); Assert.Same(errors, Console.Error); Assert.True(result.Succeeded, string.Join(";", result.Diagnostics)); Assert.Contains("Captured output", result.ConsoleOutput); Assert.Equal("trusted edit", handler.Model.Tables["Sales"].Description);
            handler.UndoManager.Undo(); Assert.Equal(before, new SemanticModelService(handler).Fingerprint()); Assert.Throws<InvalidOperationException>(() => TrustedScriptRunner.Run(ticket, handler, true));
            var stale = Await(TrustedScriptRunner.PrepareAsync(handler, "Model.Tables[\"Sales\"].Description = \"other\";", Array.Empty<TabularNamedObject>(), Path.Combine(directory, "snapshots"), CancellationToken.None)); handler.Model.Tables["Sales"].Description = "intervening";
            Assert.Throws<InvalidOperationException>(() => TrustedScriptRunner.Run(stale, handler, true));
            var invalid = Await(TrustedScriptRunner.PrepareAsync(handler, "Model.DoesNotExist();", Array.Empty<TabularNamedObject>(), Path.Combine(directory, "snapshots"), CancellationToken.None)); var compilation = TrustedScriptRunner.Run(invalid, handler, true); Assert.False(compilation.Succeeded); Assert.Contains(compilation.Diagnostics, line => line.StartsWith("error CS", StringComparison.Ordinal)); Assert.Same(console, Console.Out); Assert.Same(errors, Console.Error);
        }
        finally { DeleteTestDirectory(directory); }
    });
    [Fact]
    public Task TrustedRuntimeFailureCapturesOutputAndRollsBackNativeModelEdits() => Sta(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "pbibench-trusted-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var editor = new Te2ModelEditor(() => true, Path.Combine(directory, "profile")); editor.Open(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", "pass1-demo.bim")); var handler = editor.Handler!; var before = new SemanticModelService(handler).Fingerprint();
            var ticket = Await(TrustedScriptRunner.PrepareAsync(handler, "Model.Tables[\"Sales\"].Description = \"temporary\"; System.Console.WriteLine(\"before failure\"); throw new System.InvalidOperationException(\"fixture failure\");", Array.Empty<TabularNamedObject>(), Path.Combine(directory, "snapshots"), CancellationToken.None));
            var console = Console.Out; var errors = Console.Error; var result = TrustedScriptRunner.Run(ticket, handler, true); Assert.Same(console, Console.Out); Assert.Same(errors, Console.Error); Assert.False(result.Succeeded); Assert.Contains("before failure", result.ConsoleOutput); Assert.Contains(result.Diagnostics, line => line.Contains("fixture failure")); Assert.Equal(before, new SemanticModelService(handler).Fingerprint());
        }
        finally { DeleteTestDirectory(directory); }
    });
    private static void DeleteTestDirectory(string directory)
    {
        var resolved = Path.GetFullPath(directory); var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("pbibench-trusted-test-", StringComparison.Ordinal) || Path.GetDirectoryName(resolved)?.TrimEnd(Path.DirectorySeparatorChar) != temporary.TrimEnd(Path.DirectorySeparatorChar)) throw new InvalidOperationException("Unexpected test cleanup directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
    private static T Await<T>(Task<T> task) { var deadline = DateTime.UtcNow.AddSeconds(30); while (!task.IsCompleted) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Snapshot preparation timed out."); Application.DoEvents(); Thread.Sleep(1); } return task.GetAwaiter().GetResult(); }
    private static async Task Sta(Action action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } }) { IsBackground = true }; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(90))) != completion.Task) throw new TimeoutException("Trusted scripting STA boundary timed out.");
        await completion.Task;
    }
}
#endif
