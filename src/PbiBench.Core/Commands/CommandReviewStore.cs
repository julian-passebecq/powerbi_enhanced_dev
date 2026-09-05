using System.Text;
using System.Text.Json;
using PbiBench.Core.Queries;

namespace PbiBench.Core.Commands;

public sealed record CommandReviewEnvelope(int Version, Guid ReviewId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    CommandRequest Request, CommandReview Review, string ApprovalHash);
public static class CommandReviewStore
{
    public static CommandReviewEnvelope Create(CommandRequest request, CommandReview review)
    { var now = DateTimeOffset.UtcNow; var envelope = new CommandReviewEnvelope(1, Guid.NewGuid(), now, now.AddMinutes(30), CommandJson.ParseRequest(CommandJson.Serialize(request)), review, ""); return envelope with { ApprovalHash = ApprovalHash(envelope) }; }
    private static string ApprovalHash(CommandReviewEnvelope envelope) => CommandJson.Hash(new { policy = "pbibench-command-envelope-v1", envelope.Version, envelope.ReviewId, envelope.CreatedAt, envelope.ExpiresAt, envelope.Request, envelope.Review });
    public static async Task SaveAsync(string path, CommandReviewEnvelope envelope, CancellationToken ct)
    {
        Validate(envelope); var text = CommandJson.Serialize(envelope); var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { var bytes = Encoding.UTF8.GetBytes(text); using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true)) await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false); AtomicQueryFile.Commit(temporary, full, ct); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static CommandReviewEnvelope Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("The review file exceeds 32 MB.");
        CommandReviewEnvelope envelope;
        try { using var reader = new StreamReader(stream); var json = reader.ReadToEnd(); CommandJson.RejectDuplicateFields(json); envelope = JsonSerializer.Deserialize<CommandReviewEnvelope>(json, CommandJson.Options) ?? throw new InvalidDataException("The review is empty."); }
        catch (JsonException) { throw new InvalidDataException("The saved review is invalid or contains unsupported fields."); }
        Validate(envelope); return envelope;
    }
    public static void Validate(CommandReviewEnvelope envelope)
    {
        var now = DateTimeOffset.UtcNow;
        if (envelope == null || envelope.Version != 1 || envelope.ReviewId == Guid.Empty || envelope.Request == null || envelope.Review == null || envelope.Review.Version != 1 || envelope.Review.Hash == null || envelope.Review.Hash.Length != 64 || envelope.Request.Kind != envelope.Review.Kind || envelope.CreatedAt > now.AddSeconds(5) || envelope.ExpiresAt <= now || envelope.ExpiresAt <= envelope.CreatedAt || envelope.ExpiresAt - envelope.CreatedAt > TimeSpan.FromMinutes(30)) throw new InvalidOperationException("The review is invalid, mismatched or expired. Prepare a new review.");
        CommandJson.Validate(envelope.Request);
        if (envelope.ApprovalHash != ApprovalHash(envelope)) throw new InvalidOperationException("The saved review identity, lifetime or content was modified. Prepare a new review.");
    }
    /// <summary>Claims the saved review once, before any remote operation. Uncertain attempts remain consumed.</summary>
    public static void Claim(CommandReviewEnvelope envelope, CommandRequest currentRequest, CommandReview currentReview, string approvedHash, string journalDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Validate(envelope);
        if (!currentReview.CanApply || currentReview.Hash != envelope.Review.Hash || envelope.ApprovalHash != approvedHash || CommandJson.Hash(envelope.Request) != CommandJson.Hash(currentRequest)) throw new InvalidOperationException("The requested operation no longer matches the approved review. Prepare and review it again.");
        var root = Path.GetFullPath(journalDirectory); Directory.CreateDirectory(root); var path = Path.Combine(root, envelope.ReviewId.ToString("N") + ".claimed.json");
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var bytes = Encoding.UTF8.GetBytes(CommandJson.Serialize(new { version = 1, envelope.ReviewId, hash = approvedHash, claimedAt = DateTimeOffset.UtcNow })); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
        }
        catch (IOException) when (File.Exists(path)) { throw new InvalidOperationException("This saved review was already claimed. Prepare a new review before retrying."); }
    }
}
