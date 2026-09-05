using System.CodeDom.Compiler;
using System.Globalization;
using System.IO;
using System.Text;
using PbiBench.Semantic;
using PbiBench.CSharp.LanguageService;
using TabularEditor;
using TabularEditor.Scripting;
using TabularEditor.TOMWrapper;
using TabularEditor.UI;

namespace PbiBench.ModelEditor;

public sealed class TrustedScriptTicket
{
    internal TrustedScriptTicket(TabularModelHandler handler, string source, IReadOnlyList<TabularNamedObject> selection, string fingerprint, string snapshotPath)
    { Handler = handler; Source = source; Selection = selection.ToArray(); Fingerprint = fingerprint; SnapshotPath = snapshotPath; }
    internal TabularModelHandler Handler { get; }
    internal string Source { get; }
    internal IReadOnlyList<TabularNamedObject> Selection { get; }
    internal string Fingerprint { get; }
    internal bool Consumed { get; set; }
    public string SnapshotPath { get; }
}
public sealed record TrustedScriptResult(bool Succeeded, IReadOnlyList<string> Diagnostics, string ConsoleOutput, string SnapshotPath, bool UndoAvailable);

/// <summary>Explicit unrestricted legacy compatibility. It is never called by Safe Preview and offers no security sandbox or external-side-effect rollback.</summary>
public static class TrustedScriptRunner
{
    /// <summary>Compile using the existing TE2 compiler; never invoke the generated delegate.</summary>
    public static IReadOnlyList<CSharpDiagnostic> Validate(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > 1024 * 1024) return new[] { new CSharpDiagnostic(1, 1, "SOURCE", "Enter a script up to 1 MiB.") };
        try
        {
            ScriptEngine.CompileScript(source, out var compilation);
            return compilation.Errors.Cast<CompilerError>().Select(e => new CSharpDiagnostic(e.Line, e.Column, e.ErrorNumber, e.ErrorText, e.IsWarning)).ToArray();
        }
        catch (Exception error) { return new[] { new CSharpDiagnostic(1, 1, "COMPILER", error.GetType().Name + ": " + error.Message) }; }
    }
    public static async Task<TrustedScriptTicket> PrepareAsync(TabularModelHandler handler, string source, IReadOnlyList<TabularNamedObject> selection, string snapshotDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(source) || source.Length > 1024 * 1024) throw new ArgumentException("Enter a legacy script no larger than 1 MB.");
        if (selection.Any(item => !ReferenceEquals(item.Model, handler.Model))) throw new InvalidOperationException("The selection belongs to another model.");
        if (handler.UpdateInProgress || handler.UndoManager.BatchDepth != 0) throw new InvalidOperationException("Finish the current model operation before preparing a trusted run.");
        var capturedSelection = selection.ToArray(); var fingerprint = new SemanticModelService(handler).Fingerprint(); var json = Microsoft.AnalysisServices.Tabular.JsonSerializer.SerializeDatabase(handler.Database);
        var directory = Path.GetFullPath(snapshotDirectory); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".bim");
        var bytes = new UTF8Encoding(false).GetBytes(json);
        try { using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true)) { await stream.WriteAsync(bytes, 0, bytes.Length, ct); await stream.FlushAsync(ct); } ct.ThrowIfCancellationRequested(); }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
        return new TrustedScriptTicket(handler, source, capturedSelection, fingerprint, path);
    }
    public static TrustedScriptResult Run(TrustedScriptTicket ticket, TabularModelHandler currentHandler, bool trustAcknowledged)
    {
        if (!trustAcknowledged) throw new InvalidOperationException("Explicitly acknowledge unrestricted file, network and process access before running a trusted legacy script.");
        if (ticket.Consumed || !ReferenceEquals(ticket.Handler, currentHandler) || new SemanticModelService(currentHandler).Fingerprint() != ticket.Fingerprint) throw new InvalidOperationException("The trusted run is stale or already consumed. Prepare a fresh snapshot.");
        if (!File.Exists(ticket.SnapshotPath)) throw new InvalidOperationException("The pre-run model snapshot is missing.");
        if (!currentHandler.UndoManager.Enabled || currentHandler.UndoManager.BatchDepth != 0 || currentHandler.UpdateInProgress) throw new InvalidOperationException("Finish the current model operation and enable Undo first.");
        ticket.Consumed = true; var diagnostics = new List<string>(); var output = new BoundedWriter(256 * 1024); var originalOut = Console.Out; var originalError = Console.Error;
        var success = false; var started = false;
        try
        {
            Console.SetOut(output); Console.SetError(output);
            var run = ScriptEngine.CompileScript(ticket.Source, out var compilation);
            diagnostics.AddRange(compilation.Errors.Cast<CompilerError>().Select(error => $"{(error.IsWarning ? "warning" : "error")} {error.ErrorNumber} ({error.Line},{error.Column}): {error.ErrorText}"));
            if (run == null || compilation.Errors.HasErrors) return new(false, diagnostics, output.ToString(), ticket.SnapshotPath, false);
            currentHandler.BeginUpdate("PbiBench trusted legacy script"); started = true; ScriptOutputForm.Reset(true); ScriptHelper.BeforeScriptExecution();
            run(currentHandler.Model, new UITreeSelection(ticket.Selection.Cast<ITabularNamedObject>()));
            currentHandler.EndUpdateAll(); started = false; success = true;
        }
        catch (Exception error)
        {
            diagnostics.Add(error.GetType().Name + ": " + error.Message);
            if (started)
            {
                try { currentHandler.EndUpdateAll(rollback: true); }
                catch (Exception recovery) { diagnostics.Add("Model rollback failed: " + recovery.GetType().Name + ". The pre-run BIM snapshot remains available."); }
            }
            diagnostics.Add("File, network and process effects are unrestricted and cannot be undone by PbiBench.");
        }
        finally
        {
            try { if (started || success) ScriptHelper.AfterScriptExecution(); }
            finally { Console.SetOut(originalOut); Console.SetError(originalError); }
        }
        return new(success, diagnostics, output.ToString(), ticket.SnapshotPath, currentHandler.UndoManager.CanUndo);
    }
    private sealed class BoundedWriter(int limit) : TextWriter
    {
        private readonly StringBuilder value = new(); private bool truncated;
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char character) { if (value.Length < limit) value.Append(character); else truncated = true; }
        public override void Write(string? text) { if (text == null) return; foreach (var character in text) Write(character); }
        public override string ToString() => value.ToString() + (truncated ? "\n[Console output truncated at 256 KiB]" : "");
    }
}
