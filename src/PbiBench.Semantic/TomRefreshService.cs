using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AnalysisServices;
using PbiBench.Core.Domain;
using PbiBench.Core.Queries;
using PbiBench.Core.Refresh;
using TOM = Microsoft.AnalysisServices.Tabular;

namespace PbiBench.Semantic;

public sealed class TomRefreshService
{
    private readonly IRefreshSessionFactory sessions;
    public TomRefreshService() : this(new TomRefreshSessionFactory()) { }
    public TomRefreshService(IRefreshSessionFactory sessions) { this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions)); }
    /// <summary>Read-only metadata capture through a private connection; no native model handler is created.</summary>
    public async Task<RefreshMetadataSnapshot> CaptureAsync(RefreshConnection connection, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (connection == null || string.IsNullOrWhiteSpace(connection.Server) || string.IsNullOrWhiteSpace(connection.DatabaseId) || timeoutSeconds < 1 || timeoutSeconds > 3600)
            throw new ArgumentException("An explicit refresh target and timeout from 1 to 3600 seconds are required.");
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested(); using var session = sessions.Create();
                var gate = new object(); Task? cancellation = null; var complete = false;
                var registration = linked.Token.Register(() => { lock (gate) { if (!complete) cancellation ??= Task.Run(() => { try { session.Cancel(); } catch { } }); } });
                try { session.Open(connection, timeoutSeconds); linked.Token.ThrowIfCancellationRequested(); var snapshot = session.CaptureMetadata(); linked.Token.ThrowIfCancellationRequested(); return snapshot; }
                finally { registration.Dispose(); lock (gate) complete = true; cancellation?.GetAwaiter().GetResult(); }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) when (linked.IsCancellationRequested) { throw new OperationCanceledException("Refresh metadata capture was canceled or timed out.", linked.Token); }
        catch (Exception) { throw new InvalidOperationException("Could not capture the explicit refresh target. Verify its endpoint, database and authentication."); }
    }
    public async Task<RefreshRunResult> ExecuteAsync(RefreshPlan plan, ApprovedChangePlan approval, RefreshConnection connection,
        IProgress<RefreshProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); plan.ClaimExecution(approval, connection);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(plan.Request.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        return await Task.Run(() => Execute(plan, connection, progress, linked.Token), CancellationToken.None).ConfigureAwait(false);
    }
    private RefreshRunResult Execute(RefreshPlan plan, RefreshConnection connection, IProgress<RefreshProgress>? progress, CancellationToken token)
    {
        var watch = Stopwatch.StartNew(); var started = DateTimeOffset.UtcNow; var runId = Guid.NewGuid(); var submitted = false;
        using var session = sessions.Create(); var gate = new object(); Task? cancellation = null; var complete = false;
        var registration = token.Register(() => { lock (gate) { if (!complete) cancellation ??= Task.Run(() => { try { session.Cancel(); } catch { } }); } });
        RefreshRunResult Result(RefreshOutcome outcome, string message, IReadOnlyList<string>? details = null) => new(runId, plan.ChangePlan.Id, outcome, started, watch.Elapsed.TotalMilliseconds, message, details ?? Array.Empty<string>(), submitted);
        void Report(string stage, string message) { try { progress?.Report(new(stage, message)); } catch { /* An observer does not own the remote command outcome. */ } }
        try
        {
            token.ThrowIfCancellationRequested(); Report("Connecting", "Opening an independent refresh session."); session.Open(connection, plan.Request.TimeoutSeconds);
            token.ThrowIfCancellationRequested(); Report("Validating", "Verifying target metadata against the reviewed plan."); var current = session.CaptureMetadata();
            token.ThrowIfCancellationRequested();
            if (current.Server != plan.Metadata.Server || current.DatabaseId != plan.Metadata.DatabaseId || current.DatabaseName != plan.Metadata.DatabaseName || current.Fingerprint != plan.Metadata.Fingerprint || !current.IsConnected || current.HasUnsavedChanges)
                return Result(RefreshOutcome.Failed, "Target metadata changed or does not match the reviewed model. No refresh command was submitted. Reload metadata and preview again.");
            Report("Executing", "Executing the exact approved TMSL. The engine does not expose a reliable percentage through this connection.");
            token.ThrowIfCancellationRequested(); submitted = true; var response = session.Execute(plan.Tmsl);
            if (response == null) return Result(RefreshOutcome.OutcomeUnknown, "The server did not return a processing result. Inspect the target before retrying.");
            if (response.HasErrors || response.Errors.Count != 0) return Result(RefreshOutcome.Failed, "The server returned processing errors. Inspect the details and target state before retrying.", response.Errors.Take(30).Select(message => RefreshMessages.Redact(message, connection)).ToArray());
            var warnings = response.Warnings.Take(30).Select(message => RefreshMessages.Redact(message, connection)).ToList(); if (token.IsCancellationRequested) warnings.Add("Cancellation arrived after the server reported success; the completed refresh was not undone.");
            Report("Completed", "The server acknowledged the refresh command.");
            return Result(warnings.Count > 0 ? RefreshOutcome.SucceededWithWarnings : RefreshOutcome.Succeeded, "The server acknowledged successful refresh processing. Refresh model metadata before making further changes.", warnings);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            return Result(submitted ? RefreshOutcome.OutcomeUnknown : RefreshOutcome.CanceledBeforeExecution,
                submitted ? "Cancellation or timeout was requested after submission. Completion/rollback is unconfirmed; inspect the target before retrying." : "Canceled or timed out before a refresh command was submitted.");
        }
        catch (Exception)
        {
            return Result(submitted ? RefreshOutcome.OutcomeUnknown : RefreshOutcome.Failed,
                submitted ? "The connection failed after submission. Completion/rollback is unconfirmed; inspect the target before retrying." : "Could not open or validate the independent target connection. No refresh command was submitted.");
        }
        finally
        {
            registration.Dispose(); lock (gate) complete = true; cancellation?.GetAwaiter().GetResult();
        }
    }
}

public sealed class TomRefreshSessionFactory : IRefreshSessionFactory
{
    public IRefreshSession Create() => new TomRefreshSession();
}
internal sealed class TomRefreshSession : IRefreshSession
{
    private readonly TOM.Server server = new();
    private RefreshConnection? connection;
    public void Open(RefreshConnection target, int timeoutSeconds)
    {
        new QueryRequest(target.Server, target.DatabaseId, "EVALUATE ROW(\"Validation\", 1)", 1, Math.Min(timeoutSeconds, 3600)).Validate();
        connection = target;
        var request = new QueryRequest(target.Server, target.DatabaseId, "", 1, timeoutSeconds) { ConnectionString = target.ConnectionString };
        server.Connect(TomQuerySession.BuildConnectionString(request));
    }
    public RefreshMetadataSnapshot CaptureMetadata()
    {
        if (connection == null || !server.Connected) throw new InvalidOperationException("A private refresh connection is required.");
        var matches = server.Databases.Cast<TOM.Database>().Where(database => database.ID == connection.DatabaseId || database.Name == connection.DatabaseId).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("The requested database is absent or its identifier is ambiguous.");
        var database = matches[0];
        return RefreshMetadataProvider.Capture(database, connection.Server);
    }
    public RefreshEngineResponse Execute(string approvedTmsl)
    {
        if (connection == null || !server.Connected) throw new InvalidOperationException("A private refresh connection is required.");
        XmlaResultCollection results;
        try { results = server.Execute(new XElement("Statement", approvedTmsl).ToString(SaveOptions.DisableFormatting)); }
        catch (OperationException error) when (error.Results != null && error.Results.ContainsErrors)
        { results = error.Results; }
        if (results == null) throw new InvalidOperationException("The server returned no processing response.");
        var messages = results.Cast<XmlaResult>().SelectMany(result => result.Messages.Cast<XmlaMessage>()).ToArray();
        return new(results.ContainsErrors, messages.OfType<XmlaError>().Take(30).Select(error => RefreshMessages.Redact(error.Description, connection)).ToArray(),
            messages.Where(message => !(message is XmlaError)).Take(30).Select(message => RefreshMessages.Redact(message.Description, connection)).ToArray());
    }
    public void Cancel() { if (server.Connected) server.CancelCommand(); }
    public void Dispose() => server.Dispose();
}
internal static class RefreshMessages
{
    internal static string Redact(string? text, RefreshConnection? connection)
    {
        var message = text ?? "The server returned a processing message.";
        if (!string.IsNullOrEmpty(connection?.ConnectionString))
        {
            message = message.Replace(connection!.ConnectionString!, "[connection]");
            var options = new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString };
            foreach (string key in options.Keys)
                if (Regex.IsMatch(key, "password|pwd|token|secret|key", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                { var value = Convert.ToString(options[key]); if (!string.IsNullOrEmpty(value)) message = message.Replace(value, "[redacted]"); }
        }
        message = Regex.Replace(message, @"(?i)(password|pwd|access_token|token|secret|accountkey)\s*=\s*(?:""[^""]*""|'[^']*'|[^;\s,]+)", "$1=[redacted]");
        return message.Length > 4000 ? message.Substring(0, 4000) + "…" : message;
    }
}
