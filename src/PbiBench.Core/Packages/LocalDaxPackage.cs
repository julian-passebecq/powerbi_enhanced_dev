using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PbiBench.Core.Packages;

public sealed record DaxPackageDependency(string Id, string Version, string Sha256);
public sealed record DaxPackageFunction(string Name, string Path, string Sha256, string Description, bool IsHidden);
public sealed record DaxPackageManifest(int SchemaVersion, string Id, string Version, string License, string Description,
    IReadOnlyList<DaxPackageDependency> Dependencies, IReadOnlyList<DaxPackageFunction> Functions);
public sealed class LocalDaxPackage
{
    internal LocalDaxPackage(string directory, DaxPackageManifest manifest, IDictionary<string, string> functions, string hash)
    { Directory = directory; Manifest = manifest; Functions = new ReadOnlyDictionary<string, string>(functions); ContentHash = hash; }
    public string Directory { get; }
    public DaxPackageManifest Manifest { get; }
    public IReadOnlyDictionary<string, string> Functions { get; }
    public string ContentHash { get; }
    public string Prototype => "PbiBench local DAX package prototype; no remote feed or installer code";
}
public sealed record DaxLockedFunction(string Name, string DefinitionHash);
public sealed record DaxLockedPackage(string Id, string Version, string License, string ContentHash,
    IReadOnlyList<DaxPackageDependency> Dependencies, IReadOnlyList<DaxLockedFunction> Functions);
public sealed class DaxPackageLock
{
    public const string AnnotationName = "PbiBench.PackageLock.v1";
    public DaxPackageLock(IEnumerable<DaxLockedPackage>? packages = null)
    {
        var items = (packages ?? Array.Empty<DaxLockedPackage>()).ToArray(); if (items.Length > 256) throw new ArgumentException("At most 256 local packages may be locked.");
        foreach (var item in items)
        {
            PackageRules.Id(item.Id); PackageRules.Version(item.Version); PackageRules.Hash(item.ContentHash); PackageRules.Text(item.License, 256, true);
            if (item.Dependencies == null || item.Functions == null || item.Dependencies.Count > 256 || item.Functions.Count > 512) throw new ArgumentException("Invalid package lock entries.");
            foreach (var dependency in item.Dependencies) { PackageRules.Id(dependency.Id); PackageRules.Version(dependency.Version); PackageRules.Hash(dependency.Sha256); }
            foreach (var function in item.Functions) { PackageRules.Function(function.Name, item.Id); PackageRules.Hash(function.DefinitionHash); }
            PackageRules.Unique(item.Dependencies.Select(dependency => dependency.Id)); PackageRules.Unique(item.Functions.Select(function => function.Name));
        }
        PackageRules.Unique(items.Select(item => item.Id)); PackageRules.Unique(items.SelectMany(item => item.Functions).Select(function => function.Name));
        Packages = Array.AsReadOnly(items.Select(item => item with { Dependencies = Array.AsReadOnly(item.Dependencies.ToArray()), Functions = Array.AsReadOnly(item.Functions.ToArray()) }).ToArray());
    }
    public IReadOnlyList<DaxLockedPackage> Packages { get; }
    public string ToJson() => JsonSerializer.Serialize(new { schemaVersion = 1, packages = Packages }, PackageRules.JsonOptions);
    public static DaxPackageLock Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        if (json!.Length > 4 * 1024 * 1024) throw new ArgumentException("Package locks are limited to 4 MiB.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 }); var root = document.RootElement;
        PackageRules.Properties(root, "schemaVersion", "packages"); if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new ArgumentException("Unsupported package lock schema version.");
        var packages = root.GetProperty("packages").EnumerateArray().Select(item =>
        {
            PackageRules.Properties(item, "id", "version", "license", "contentHash", "dependencies", "functions");
            var dependencies = item.GetProperty("dependencies").EnumerateArray().Select(PackageRules.Dependency).ToArray();
            var functions = item.GetProperty("functions").EnumerateArray().Select(function => { PackageRules.Properties(function, "name", "definitionHash"); return new DaxLockedFunction(function.GetProperty("name").GetString()!, function.GetProperty("definitionHash").GetString()!); }).ToArray();
            return new DaxLockedPackage(item.GetProperty("id").GetString()!, item.GetProperty("version").GetString()!, item.GetProperty("license").GetString()!, item.GetProperty("contentHash").GetString()!, dependencies, functions);
        }).ToArray();
        return new(packages);
    }
    public IReadOnlyList<string> ValidateDependencies(DaxPackageManifest manifest)
    {
        var errors = new List<string>();
        foreach (var dependency in manifest.Dependencies)
        {
            var found = Packages.FirstOrDefault(package => string.Equals(package.Id, dependency.Id, StringComparison.OrdinalIgnoreCase));
            if (found == null || found.Version != dependency.Version || !string.Equals(found.ContentHash, dependency.Sha256, StringComparison.OrdinalIgnoreCase)) errors.Add("Dependency " + dependency.Id + " requires installed version " + dependency.Version + " with SHA-256 " + dependency.Sha256 + ".");
        }
        return errors.AsReadOnly();
    }
    public IReadOnlyList<string> ValidateGraph()
    {
        var issues = new List<string>(); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in Packages) Visit(package);
        return issues.AsReadOnly();
        void Visit(DaxLockedPackage package)
        {
            if (active.Contains(package.Id)) { issues.Add("Package dependency cycle includes " + package.Id + "."); return; }
            if (!visited.Add(package.Id)) return; active.Add(package.Id);
            foreach (var dependency in package.Dependencies)
            {
                var target = Packages.FirstOrDefault(item => string.Equals(item.Id, dependency.Id, StringComparison.OrdinalIgnoreCase));
                if (target == null || target.Version != dependency.Version || !string.Equals(target.ContentHash, dependency.Sha256, StringComparison.OrdinalIgnoreCase)) issues.Add("Locked dependency " + dependency.Id + " is missing or does not match its exact version/hash.");
                else Visit(target);
            }
            active.Remove(package.Id);
        }
    }
    public static string FunctionHash(string expression, string description, bool isHidden) => PackageRules.Digest(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { expression, description, isHidden })));
}
public sealed class LocalDaxPackageReader
{
    public Task<LocalDaxPackage> ReadAsync(string folder, CancellationToken ct = default) => Task.Run(() => Read(folder, ct), ct);
    private static LocalDaxPackage Read(string folder, CancellationToken ct)
    {
        folder = Path.GetFullPath(folder); RejectLinks(folder); if (!System.IO.Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing local package folder.");
        var manifestBytes = ReadBounded(Path.Combine(folder, "pbibench.package.json"), 256 * 1024, ct); var manifest = ParseManifest(Decode(manifestBytes));
        var bodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var digest = new StringBuilder(PackageRules.Digest(manifestBytes)); var total = manifestBytes.Length;
        foreach (var function in manifest.Functions.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested(); var bytes = ReadBounded(SafePath(folder, function.Path), 256 * 1024, ct); total += bytes.Length; if (total > 8 * 1024 * 1024) throw new InvalidDataException("Local package contents exceed 8 MiB.");
            var actual = PackageRules.Digest(bytes); if (!string.Equals(actual, function.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 mismatch for " + function.Path + ". The package was not loaded.");
            var body = Decode(bytes); PackageRules.Text(body, 262144, true); bodies.Add(function.Name, body); digest.Append('\n').Append(function.Path).Append(':').Append(actual);
        }
        // The read package is an immutable snapshot; subsequent disk changes cannot alter a reviewed plan.
        return new(folder, manifest, bodies, PackageRules.Digest(Encoding.UTF8.GetBytes(digest.ToString())));
    }
    public static DaxPackageManifest ParseManifest(string json)
    {
        if (json == null || json.Length > 256 * 1024) throw new ArgumentException("Package manifests are limited to 256 KiB.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 }); var root = document.RootElement;
        PackageRules.Properties(root, "schemaVersion", "id", "version", "license", "description", "dependencies", "functions");
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new ArgumentException("Only PbiBench package manifest schemaVersion 1 is supported.");
        var id = root.GetProperty("id").GetString()!; var version = root.GetProperty("version").GetString()!; var license = root.GetProperty("license").GetString()!; var description = root.GetProperty("description").GetString()!;
        PackageRules.Id(id); PackageRules.Version(version); PackageRules.Text(license, 256, true); PackageRules.Text(description, 8192, false);
        var dependencies = root.GetProperty("dependencies").EnumerateArray().Select(PackageRules.Dependency).ToArray();
        var functions = root.GetProperty("functions").EnumerateArray().Select(item =>
        {
            PackageRules.Properties(item, "name", "path", "sha256", "description", "isHidden"); var name = item.GetProperty("name").GetString()!; var path = item.GetProperty("path").GetString()!; var hash = item.GetProperty("sha256").GetString()!; var comment = item.GetProperty("description").GetString()!;
            PackageRules.Function(name, id); ValidateRelative(path); PackageRules.Hash(hash); PackageRules.Text(comment, 8192, false);
            return new DaxPackageFunction(name, path, hash.ToLowerInvariant(), comment, item.GetProperty("isHidden").GetBoolean());
        }).ToArray();
        if (dependencies.Length > 256 || functions.Length is < 1 or > 512) throw new ArgumentException("A package must contain 1–512 functions and at most 256 dependencies.");
        if (dependencies.Any(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A package cannot depend on itself.");
        PackageRules.Unique(dependencies.Select(item => item.Id)); PackageRules.Unique(functions.Select(item => item.Name)); PackageRules.Unique(functions.Select(item => item.Path));
        return new(1, id, version, license, description, Array.AsReadOnly(dependencies), Array.AsReadOnly(functions));
    }
    private static string Decode(byte[] bytes) { var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0; return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset); }
    private static byte[] ReadBounded(string path, int limit, CancellationToken ct)
    {
        RejectLinks(path); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); if (stream.Length > limit) throw new InvalidDataException("An expected package file exceeds its size bound.");
        using var memory = new MemoryStream(); var buffer = new byte[8192]; int count; while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) { ct.ThrowIfCancellationRequested(); if (memory.Length + count > limit) throw new InvalidDataException("A package file grew beyond its size bound."); memory.Write(buffer, 0, count); } ct.ThrowIfCancellationRequested(); return memory.ToArray();
    }
    private static string SafePath(string folder, string relative) { ValidateRelative(relative); var path = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar))); if (!path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Package path escaped its folder."); RejectLinks(path); return path; }
    private static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || !path.EndsWith(".dax", StringComparison.OrdinalIgnoreCase) || path.Contains('\\') || Path.IsPathRooted(path) || path.Contains(':') || path.Split('/').Any(part => part is "" or "." or ".." || part.StartsWith(".", StringComparison.Ordinal) || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))) throw new ArgumentException("Functions require ordinary relative .dax paths without traversal, hidden directories or Windows device names.");
    }
    private static void RejectLinks(string path) { for (string? current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)) if ((File.Exists(current) || System.IO.Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked package paths are unsupported."); }
}
internal static class PackageRules
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    internal static string Digest(byte[] bytes) { using var algorithm = SHA256.Create(); return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    internal static void Id(string text) { if (text == null || text.Length > 128 || !Regex.IsMatch(text, @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)) throw new ArgumentException("Use a lowercase dotted package ID, for example contoso.math."); }
    internal static void Function(string text, string id) { if (text == null || text.Length > 256 || !text.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(text, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$", RegexOptions.CultureInvariant)) throw new ArgumentException("Each function must be in the package's dotted namespace."); }
    internal static void Version(string text) { if (text == null || text.Length > 32 || !Regex.IsMatch(text, @"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})$", RegexOptions.CultureInvariant)) throw new ArgumentException("The prototype requires an exact major.minor.patch version; ranges and prereleases are unsupported."); }
    internal static void Hash(string text) { if (text == null || !Regex.IsMatch(text, @"^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Expected an exact SHA-256 hash."); }
    internal static void Text(string text, int maximum, bool required) { if (text == null || text.Length > maximum || required && string.IsNullOrWhiteSpace(text) || text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) throw new ArgumentException("Missing, invalid or oversized package text."); }
    internal static void Unique(IEnumerable<string> values) { if (values.GroupBy(item => item, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1)) throw new ArgumentException("Duplicate package names, paths or dependencies are unsupported."); }
    internal static void Properties(JsonElement element, params string[] allowed) { if (element.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a package JSON object."); var names = element.EnumerateObject().Select(item => item.Name).ToArray(); if (names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(name => !allowed.Contains(name, StringComparer.Ordinal)) || allowed.Any(name => !names.Contains(name, StringComparer.Ordinal))) throw new ArgumentException("Package JSON has missing, duplicate or unsupported fields."); }
    internal static DaxPackageDependency Dependency(JsonElement item) { Properties(item, "id", "version", "sha256"); var id = item.GetProperty("id").GetString()!; var version = item.GetProperty("version").GetString()!; var hash = item.GetProperty("sha256").GetString()!; Id(id); Version(version); Hash(hash); return new(id, version, hash.ToLowerInvariant()); }
}
