using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PbiBench.Core.Abstractions;
using PbiBench.Core.Fabric;

namespace PbiBench.Fabric;

public sealed record FabricReportSnapshot(string Directory, string DefinitionFile, string ManifestFile, string Format, int PartCount);
public sealed record ReportSnapshotPart(string Path, string Sha256, int Bytes);
/// <summary>Explicit read-only getDefinition flow. Authentication belongs to the caller; no report update API exists here.</summary>
public sealed class FabricReportSnapshotService(HttpClient http, IAccessTokenProvider tokens)
{
    public const int MaximumPartBytes = 4 * 1024 * 1024;
    public const int MaximumTotalBytes = 32 * 1024 * 1024;
    public const int MaximumParts = 2048;
    public async Task<FabricReportSnapshot> GetSnapshotAsync(FabricItem item, string newDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Kind != "Report") throw new ArgumentException("Select a Fabric Report.");
        var workspace = FabricSchemaRules.Id(item.WorkspaceId); var report = FabricSchemaRules.Id(item.Id);
        var destination = Path.GetFullPath(newDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        CheckLinks(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a NEW snapshot directory. Existing files are never overwritten.");
        var parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("A snapshot parent directory is required.");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Select an existing snapshot parent directory.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(10)); var ct = deadline.Token;
        var uri = new Uri(FabricApiClient.BaseUri, "workspaces/" + workspace + "/reports/" + report + "/getDefinition");
        var response = await FabricHttp.SendAsync(http, tokens, HttpMethod.Post, uri, FabricAudience.Fabric, null, ct).ConfigureAwait(false);
        try
        {
            if ((int)response.StatusCode == 202)
            {
                string? operationId = null;
                if (response.Headers.TryGetValues("x-ms-operation-id", out var values))
                { var ids = values.ToArray(); if (ids.Length != 1) throw new InvalidDataException("Ambiguous Fabric operation identity."); operationId = FabricSchemaRules.Id(ids[0]); }
                if (response.Headers.Location == null)
                { if (operationId == null) throw new InvalidDataException("Fabric omitted its operation identity."); response.Headers.Location = new Uri(FabricApiClient.BaseUri, "operations/" + operationId); }
                var location = LongRunningOperationPoller.OperationUri(response.Headers.Location);
                if (operationId != null && Guid.Parse(location.AbsolutePath.Split('/')[3]) != Guid.Parse(operationId)) throw new InvalidDataException("Fabric operation header identities disagree.");
                response = await new LongRunningOperationPoller(http, token => tokens.GetAccessTokenAsync(EntraPublicClientTokenProvider.Scopes(FabricAudience.Fabric), token)).WaitAsync(response, ct).ConfigureAwait(false);
            }
            if ((int)response.StatusCode != 200) throw new FabricApiException("Report definition returned an unexpected HTTP status.", (int)response.StatusCode);
            using var document = await FabricHttp.ReadJsonAsync(response.Content, ct, 48 * 1024 * 1024).ConfigureAwait(false);
            CheckDuplicateProperties(document.RootElement);
            if (!document.RootElement.TryGetProperty("definition", out var definition) || !definition.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() is < 1 or > MaximumParts)
                throw new InvalidDataException("Missing or oversized report definition parts.");
            var decoded = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase); var total = 0;
            foreach (var part in parts.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested(); var path = NormalizePartPath(Required(part, "path"));
                if (Required(part, "payloadType") != "InlineBase64") throw new InvalidDataException("Only InlineBase64 report payloads are supported.");
                var payload = Required(part, "payload");
                if (payload.Length > ((MaximumPartBytes + 2) / 3) * 4 || payload.Any(char.IsWhiteSpace)) throw new InvalidDataException("Report part exceeds the strict Base64 payload bound.");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(payload); } catch (FormatException) { throw new InvalidDataException("Invalid InlineBase64 report part."); }
                if (bytes.Length > MaximumPartBytes || Convert.ToBase64String(bytes) != payload || (total += bytes.Length) > MaximumTotalBytes) throw new InvalidDataException("Report payload exceeds snapshot bounds or has noncanonical Base64.");
                if (decoded.ContainsKey(path)) throw new InvalidDataException("Duplicate normalized report part path.");
                CheckDefinitionCredentials(path, bytes); decoded.Add(path, bytes);
            }
            // Reject file/directory collisions before writing anything.
            foreach (var path in decoded.Keys) if (decoded.Keys.Any(other => other.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Report part file/directory collision.");
            if (!decoded.ContainsKey("definition.pbir")) throw new InvalidDataException("Report definition omitted definition.pbir.");
            var enhanced = decoded.ContainsKey("definition/report.json"); var legacy = decoded.ContainsKey("report.json");
            if (enhanced == legacy) throw new InvalidDataException("Expected one PBIR or PBIR-Legacy definition.");
            var format = enhanced ? "PBIR" : "PBIR-Legacy";
            if (definition.TryGetProperty("format", out var declared) && declared.ValueKind != JsonValueKind.Null && declared.GetString() != format) throw new InvalidDataException("Report definition format disagrees with its parts.");
            var manifest = JsonSerializer.Serialize(new { version = 1, workspaceId = workspace, reportId = report, retrievedAt = DateTimeOffset.UtcNow, format,
                parts = decoded.Select(p => new ReportSnapshotPart(p.Key, Hash(p.Value), p.Value.Length)).ToArray() }, new JsonSerializerOptions { WriteIndented = true });
            await WriteNewAsync(parent, destination, decoded, manifest, ct).ConfigureAwait(false);
            return new(destination, Path.Combine(destination, "definition.pbir"), Path.Combine(destination, "pbibench-fabric-snapshot.json"), format, decoded.Count);
        }
        finally { response.Dispose(); }
    }
    public static string NormalizePartPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith("\\", StringComparison.Ordinal) || value.Contains(':')) throw new InvalidDataException("Absolute/device report paths are forbidden.");
        var parts = value.Replace('\\', '/').Split('/');
        foreach (var part in parts)
        {
            var stem = part.Split('.')[0].TrimEnd(' ');
            if (part.Length is < 1 or > 128 || part is "." or ".." || part.EndsWith(" ", StringComparison.Ordinal) || part.EndsWith(".", StringComparison.Ordinal) || part.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c)) ||
                Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ||
                new[] { ".pbi", ".pbibench", ".git" }.Contains(part, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe report part path.");
        }
        var path = string.Join("/", parts);
        if (!(path is "definition.pbir" or "report.json" or ".platform" or "semanticModelDiagramLayout.json" || path.StartsWith("definition/", StringComparison.Ordinal) || path.StartsWith("StaticResources/", StringComparison.Ordinal)))
            throw new InvalidDataException("Unsupported public report definition part path.");
        return path;
    }
    private static string Required(JsonElement obj, string name) => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException("Report part omitted " + name + ".");
    private static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static void CheckDuplicateProperties(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        { var keys = new HashSet<string>(StringComparer.Ordinal); foreach (var p in node.EnumerateObject()) { if (!keys.Add(p.Name)) throw new InvalidDataException("Duplicate JSON property in report response."); CheckDuplicateProperties(p.Value); } }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) CheckDuplicateProperties(item);
    }
    private static void CheckDefinitionCredentials(string path, byte[] bytes)
    {
        if (!(path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path == "definition.pbir" || path == ".platform")) return;
        using var doc = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')); CheckDuplicateProperties(doc.RootElement);
        void Visit(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object) foreach (var property in node.EnumerateObject())
            {
                if (new[] { "accessToken", "refreshToken", "clientSecret", "password", "pwd", "credentials", "authorization" }.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Report part contains credential fields; no snapshot was saved.");
                if (property.Name.Equals("connectionString", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String &&
                    Regex.IsMatch(property.Value.GetString()!, @"(?:^|;)\s*(password|pwd|access\s*token|token|client\s*secret|accountkey|sharedaccesssignature)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new InvalidDataException("Report part contains connection credentials; no snapshot was saved.");
                Visit(property.Value);
            }
            else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) Visit(item);
        }
        Visit(doc.RootElement);
    }
    private static void CheckLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Snapshot paths cannot use reparse points.");
    }
    private static async Task WriteNewAsync(string parent, string destination, IReadOnlyDictionary<string, byte[]> parts, string manifest, CancellationToken ct)
    {
        var staging = Path.Combine(parent, ".pbibench-snapshot-" + Guid.NewGuid().ToString("N")); CheckLinks(staging);
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var pair in parts.Concat(new[] { new KeyValuePair<string, byte[]>("pbibench-fabric-snapshot.json", Encoding.UTF8.GetBytes(manifest)) }))
            {
                ct.ThrowIfCancellationRequested(); var path = Path.Combine(staging, pair.Key.Replace('/', Path.DirectorySeparatorChar)); CheckLinks(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); CheckLinks(path);
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
                await stream.WriteAsync(pair.Value, 0, pair.Value.Length, ct).ConfigureAwait(false); stream.Flush(true);
            }
            ct.ThrowIfCancellationRequested(); CheckLinks(destination); CheckLinks(staging);
            // Directory.Move fails if destination appeared during the request. It never merges or overwrites.
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                CheckLinks(staging);
                // Only delete our generated staging directory after checking every descendant without following links.
                var pending = new Stack<string>(); pending.Push(staging);
                while (pending.Count > 0) { var dir = pending.Pop(); foreach (var entry in Directory.EnumerateFileSystemEntries(dir)) { CheckLinks(entry); if (Directory.Exists(entry)) pending.Push(entry); } }
                Directory.Delete(staging, true);
            }
        }
    }
}
