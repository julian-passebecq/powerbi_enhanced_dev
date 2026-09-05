using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using PbiBench.Automation.Commands;
using PbiBench.Core.Commands;
using TabularEditor.TOMWrapper;

namespace PbiBench.Cli;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false); var exit = 4; var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(new Action(async () => { try { exit = await RunAsync(args); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } }));
        Dispatcher.Run(); return exit;
    }
    private static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource(); ConsoleCancelEventHandler canceled = (_, e) => { e.Cancel = true; cancellation.Cancel(); }; Console.CancelKeyPress += canceled;
        var json = args.Contains("--json"); var kind = CommandKind.Inspect;
        try
        {
            var input = await CliArguments.ParseAsync(args, cancellation.Token); json = input.Json;
            if (input.Help) { Console.WriteLine(CliArguments.HelpText); return 0; }
            if (input.Schema) { Console.WriteLine(CommandSchema.Export()); return 0; }
            var request = input.Request!; kind = request.Kind; TabularModelHandler? handler = null;
            try
            {
                if (kind is not (CommandKind.Query or CommandKind.Test or CommandKind.Diff or CommandKind.Refresh or CommandKind.Deploy))
                {
                    if (request.Target.ModelPath == null) throw new ArgumentException("Supply --model for this command.");
                    var source = CommandModelFiles.Read(request.Target.ModelPath, cancellation.Token);
                    handler = new TabularModelHandler(source.LoadPath);
                }
                var connection = input.ConnectionEnvironmentVariable == null ? null : Environment.GetEnvironmentVariable(input.ConnectionEnvironmentVariable);
                if (input.ConnectionEnvironmentVariable != null && string.IsNullOrWhiteSpace(connection)) throw new ArgumentException("The configured connection-string environment variable is empty.");
                var service = new SemanticCommandService(() => handler, _ => connection); CommandResult result;
                if (kind is CommandKind.Set or CommandKind.Script or CommandKind.Action or CommandKind.Refresh or CommandKind.Deploy)
                {
                    if (input.Apply && (kind is CommandKind.Set or CommandKind.Script or CommandKind.Action) && request.OutputPath == null) throw new ArgumentException("CLI local apply requires an explicit --output .bim destination.");
                    var prepared = await service.PrepareAsync(request, cancellation.Token);
                    var displayedReview = prepared.Review;
                    if (input.ReviewOutput != null)
                    {
                        var envelope = CommandReviewStore.Create(prepared.Request, prepared.Review);
                        await CommandReviewStore.SaveAsync(input.ReviewOutput, envelope, cancellation.Token);
                        displayedReview = prepared.Review with { Hash = envelope.ApprovalHash };
                    }
                    else if (input.Envelope != null && input.Envelope.Review.Hash == prepared.Review.Hash) displayedReview = prepared.Review with { Hash = input.Envelope.ApprovalHash };
                    if (!input.Apply) result = CommandResult.Preview(displayedReview);
                    else
                    {
                        if (input.ApprovalHash == null) throw new InvalidOperationException("Apply requires --approve with the exact preview hash.");
                        if (prepared.Review.IsRemote && input.Envelope == null) throw new InvalidOperationException("Remote apply requires --review with an unexpired saved review; prepare one using --review-out.");
                        if (input.Envelope != null) CommandReviewStore.Claim(input.Envelope, prepared.Request, prepared.Review, input.ApprovalHash,
                            Path.Combine(StateDirectory(), "CommandApprovals"), cancellation.Token);
                        result = await service.ApplyAsync(prepared, input.Envelope == null ? input.ApprovalHash : prepared.Review.Hash, Environment.UserName, cancellation.Token);
                    }
                }
                else
                {
                    if (input.Apply || input.ApprovalHash != null || input.Envelope != null || input.ReviewOutput != null) throw new ArgumentException("Read commands do not accept approval or apply options.");
                    result = await service.ExecuteReadAsync(request, cancellation.Token);
                }
                Print(result, json); return result.ExitCode;
            }
            finally { handler?.Dispose(); if (handler != null) TabularModelHandler.Cleanup(); }
        }
        catch (Exception error)
        {
            var exit = error is OperationCanceledException ? 5 : error is ArgumentException || error is InvalidDataException || error is JsonException || error is FormatException || error is OverflowException ? 2 : error is InvalidOperationException ? 3 : 4;
            var message = error is ArgumentException || error is InvalidDataException || error is InvalidOperationException ? SafeMessage(error.Message) : error is OperationCanceledException ? "Canceled." : error is FormatException || error is OverflowException ? "A numeric or date option has an invalid value." : "The command could not complete (" + error.GetType().Name + "). Check the model, files, endpoint and credentials.";
            Print(new(1, kind, exit == 5 ? CommandStatus.Canceled : exit == 2 || exit == 3 ? CommandStatus.Rejected : CommandStatus.Failed, exit, message), json); return exit;
        }
        finally { Console.CancelKeyPress -= canceled; }
    }
    private static void Print(CommandResult result, bool json)
    {
        if (json) Console.WriteLine(CommandJson.Serialize(result));
        else { Console.WriteLine(result.Message); if (result.Review != null) Console.WriteLine(CommandJson.Serialize(result.Review)); if (result.Data != null) Console.WriteLine(result.Data.Value.GetRawText()); if (result.Diagnostics != null) foreach (var issue in result.Diagnostics) Console.WriteLine(issue.Severity + " " + issue.Code + " " + issue.ObjectPath + ": " + issue.Message); }
        if (result.ExitCode != 0) Console.Error.WriteLine(result.Message);
    }
    private static string SafeMessage(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"(?i)(password|pwd|access_token|token|secret|accountkey|connectionstring)\s*=\s*(?:""[^""]*""|'[^']*'|[^;\s,]+)", "$1=[redacted]");
    private static string StateDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("PBIBENCH_CLI_STATE_DIRECTORY");
        var path = Path.GetFullPath(configured == null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PbiBench") : string.IsNullOrWhiteSpace(configured) ? throw new ArgumentException("PBIBENCH_CLI_STATE_DIRECTORY must name a state directory.") : configured);
        PbiBench.Workspace.WorkspaceDiskStore.RejectLinks(path); return path;
    }
}
