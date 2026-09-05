using System.Text;
using System.Text.Json;

namespace PbiBench.Pbir;

public sealed class ReportFileChange
{
    private readonly byte[]? before, after;
    internal ReportFileChange(string path, byte[]? before, byte[]? after)
    { Path = path; this.before = before?.ToArray(); this.after = after?.ToArray(); }
    public string Path { get; }
    public string? BeforeHash => before == null ? null : Disk.Hash(before);
    public string? AfterHash => after == null ? null : Disk.Hash(after);
    public string BeforeText => before == null ? "(file does not exist)" : Encoding.UTF8.GetString(before);
    public string AfterText => after == null ? "(delete file)" : Encoding.UTF8.GetString(after);
    public string Operation => before == null ? "Create" : after == null ? "Delete" : "Update";
    public string ExactDiff => "--- " + Path + " · " + (BeforeHash ?? "absent") + "\n+++ " + Path + " · " + (AfterHash ?? "absent") + "\n" +
        (before == null ? "" : string.Join("\n", BeforeText.Split('\n').Select(line => "-" + line))) + "\n" +
        (after == null ? "" : string.Join("\n", AfterText.Split('\n').Select(line => "+" + line)));
    internal byte[]? BeforeBytes => before?.ToArray();
    internal byte[]? AfterBytes => after?.ToArray();
}
public sealed class ReportChangePlan
{
    internal ReportChangePlan(ReportIndex source, string title, IEnumerable<ReportFileChange> changes, IReadOnlyList<ReportIssue> validation, IEnumerable<ReportIndex>? readDependencies = null)
    { Source = source; Title = title; Changes = Array.AsReadOnly(changes.ToArray()); Validation = Array.AsReadOnly(validation.ToArray()); ReadDependencies = Array.AsReadOnly((readDependencies ?? Array.Empty<ReportIndex>()).ToArray()); }
    internal ReportIndex Source { get; }
    internal IReadOnlyList<ReportIndex> ReadDependencies { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; }
    public string Root => Source.Root;
    public IReadOnlyList<ReportFileChange> Changes { get; }
    public IReadOnlyList<ReportIssue> Validation { get; }
    public bool CanApply => Changes.Count > 0 && !Validation.Any(i => i.Severity == "Error");
    public ApprovedReportChangePlan Approve(Guid reviewedPlanId) => Id == reviewedPlanId && CanApply ? new(this) : throw new InvalidOperationException("Review the exact valid plan before approving it.");
}
public sealed class ApprovedReportChangePlan
{
    internal ApprovedReportChangePlan(ReportChangePlan plan) => Plan = plan;
    internal ReportChangePlan Plan { get; }
    internal bool Consumed { get; set; }
}
public sealed record ReportApplyResult(string BackupManifest, IReadOnlyList<ReportIssue> Validation);

/// <summary>Local file transactions: complete preflight, durable backup, per-file atomic replace, validation, guarded rollback.</summary>
public sealed class ReportChangeEngine(ReportValidator validator)
{
    internal ReportChangePlan Prepare(ReportIndex source, string title, IEnumerable<ReportFileChange> changes, IEnumerable<ReportIndex>? readDependencies = null)
    {
        var rows = changes.Where(c => c.BeforeHash != c.AfterHash).ToArray();
        if (rows.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Length) throw new InvalidDataException("Duplicate target file.");
        var files = source.Files.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            CheckTarget(source.Root, row.Path);
            if (row.AfterBytes is { } bytes) files[row.Path] = new(row.Path, bytes); else files.Remove(row.Path);
        }
        var projected = new ReportIndex(source.Root, source.ProjectFile, source.SemanticModelPath, files, source.Resources);
        return new(source, title, rows, validator.Validate(projected), readDependencies);
    }
    public Task<ReportApplyResult> ApplyAsync(ApprovedReportChangePlan approval, CancellationToken ct) => Task.Run(async () =>
    {
        var plan = approval.Plan;
        lock (approval) { if (approval.Consumed) throw new InvalidOperationException("This approval was already used."); approval.Consumed = true; }
        if (!plan.CanApply) throw new InvalidOperationException("The plan has validation errors.");
        var control = Disk.Resolve(plan.Root, ".pbibench"); Directory.CreateDirectory(control);
        using var lease = new FileStream(Disk.Resolve(plan.Root, ".pbibench/report-write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await AssertFresh(plan.Source, ct).ConfigureAwait(false);
        foreach (var dependency in plan.ReadDependencies) await AssertFresh(dependency, ct).ConfigureAwait(false);
        foreach (var change in plan.Changes) AssertHash(CheckTarget(plan.Root, change.Path), change.BeforeHash);
        ct.ThrowIfCancellationRequested();
        var backupRoot = Disk.Resolve(plan.Root, ".pbibench/report-backups/" + plan.Id.ToString("N")); Directory.CreateDirectory(backupRoot);
        var manifest = Path.Combine(backupRoot, "manifest.json");
        var backup = new Backup(1, plan.Root, plan.Title, plan.Changes.Select(c => new BackupEntry(c.Path,
            c.BeforeBytes == null ? null : Convert.ToBase64String(c.BeforeBytes), c.AfterBytes == null ? null : Convert.ToBase64String(c.AfterBytes), c.BeforeHash, c.AfterHash)).ToArray());
        Atomic(manifest, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true })));
        // Once the durable journal exists, cancellation waits for completion/rollback of this small local transaction.
        var applied = new List<ReportFileChange>();
        try
        {
            foreach (var change in plan.Changes)
            {
                var target = CheckTarget(plan.Root, change.Path); AssertHash(target, change.BeforeHash);
                Atomic(target, change.AfterBytes, change.BeforeHash); applied.Add(change);
            }
            var after = await ReportIndex.OpenAsync(plan.Root, CancellationToken.None).ConfigureAwait(false);
            foreach (var file in plan.Changes) AssertHash(CheckTarget(plan.Root, file.Path), file.AfterHash);
            var validation = validator.Validate(after);
            if (validation.Any(i => i.Severity == "Error")) throw new InvalidDataException("Validation after apply failed: " + validation.First(i => i.Severity == "Error").Message);
            return new ReportApplyResult(manifest, validation);
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            foreach (var change in applied.AsEnumerable().Reverse())
                try { var target = CheckTarget(plan.Root, change.Path); AssertHash(target, change.AfterHash); Atomic(target, change.BeforeBytes, change.AfterHash); }
                catch (Exception rollback) { failures.Add(rollback); }
            if (failures.Count > 1) throw new AggregateException("Rollback encountered a changed/locked file. Review the durable backup: " + manifest, failures);
            throw;
        }
    }, ct);
    public async Task<ReportChangePlan> PreviewRestoreAsync(string reportRoot, string manifest, CancellationToken ct)
    {
        var source = await ReportIndex.OpenAsync(reportRoot, ct).ConfigureAwait(false);
        var expectedRoot = Disk.Resolve(source.Root, ".pbibench/report-backups"); var full = Path.GetFullPath(manifest);
        if (!full.StartsWith(expectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select a backup belonging to this report.");
        Disk.CheckLinks(full); ct.ThrowIfCancellationRequested();
        if (new FileInfo(full).Length > 192 * 1024 * 1024) throw new InvalidDataException("Backup is too large.");
        var backup = JsonSerializer.Deserialize<Backup>(await File.ReadAllTextAsync(full, ct).ConfigureAwait(false)) ?? throw new InvalidDataException("Invalid backup.");
        if (backup.Version != 1 || !string.Equals(source.Root, backup.Root, StringComparison.OrdinalIgnoreCase) || backup.Entries.Length is < 1 or > 20000) throw new InvalidDataException("Backup version/root is invalid.");
        var rows = backup.Entries.Select(entry =>
        {
            CheckTarget(source.Root, entry.Path); var before = entry.Before == null ? null : Convert.FromBase64String(entry.Before); var after = entry.After == null ? null : Convert.FromBase64String(entry.After);
            if ((before == null ? null : Disk.Hash(before)) != entry.BeforeHash || (after == null ? null : Disk.Hash(after)) != entry.AfterHash) throw new InvalidDataException("Backup content hash mismatch.");
            var current = source.Files.TryGetValue(entry.Path, out var file) ? file : null;
            if (current?.Hash != entry.AfterHash && current?.Hash != entry.BeforeHash) throw new InvalidOperationException("Restore rejected: file changed after the reviewed transaction: " + entry.Path);
            return new ReportFileChange(entry.Path, current?.Bytes(), before);
        }).ToArray();
        return Prepare(source, "Restore · " + backup.Title, rows);
    }
    private static async Task AssertFresh(ReportIndex before, CancellationToken ct)
    {
        var current = await ReportIndex.OpenAsync(before.Root, ct).ConfigureAwait(false);
        if (current.Files.Count != before.Files.Count || before.Files.Any(p => !current.Files.TryGetValue(p.Key, out var file) || file.Hash != p.Value.Hash))
            throw new InvalidOperationException("Report changed on disk. Refresh and preview again; no report files were written.");
    }
    private static string CheckTarget(string root, string path)
    {
        if (!path.StartsWith("definition/", StringComparison.Ordinal) || !path.EndsWith(".json", StringComparison.Ordinal) || path.Split('/').Any(p => p is "." or ".." or ".pbi" or ".pbibench") || path.Contains('\\')) throw new InvalidDataException("Only canonical enhanced PBIR definition JSON can be changed.");
        return Disk.Resolve(root, path);
    }
    private static void AssertHash(string path, string? hash)
    { if ((File.Exists(path) ? Disk.Hash(Disk.Read(path)) : null) != hash) throw new InvalidOperationException("Stale file; preview again: " + Path.GetFileName(path)); }
    private static void Atomic(string path, byte[]? bytes, string? expectedHash = null)
    {
        Disk.CheckLinks(path);
        if (bytes == null) { File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp = path + ".pbibench-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            for (var attempt = 0; ; attempt++)
            {
                // Windows scanners can briefly deny delete-sharing. Retry only those failures,
                // retaining the exact reviewed precondition at every attempt.
                Disk.CheckLinks(path); AssertHash(path, expectedHash);
                try { if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); break; }
                catch (IOException error) when (attempt < 4 && (error.HResult & 0xffff) is 32 or 33 or 1175 or 1176)
                { Thread.Sleep(25 * (attempt + 1)); }
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private sealed record Backup(int Version, string Root, string Title, BackupEntry[] Entries);
    private sealed record BackupEntry(string Path, string? Before, string? After, string? BeforeHash, string? AfterHash);
}
