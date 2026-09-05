using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PbiBench.Pbir;

public sealed class ReportFile
{
    private readonly byte[] bytes;
    public string Path { get; }
    public string Hash { get; }
    public string Text { get; }
    public string? Schema { get; }
    public string? ParseError { get; }
    public ReportFile(string path, byte[] contents)
    {
        Path = path; bytes = (byte[])contents.Clone(); Hash = Disk.Hash(bytes);
        Text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        try { Schema = Json()["$schema"]?.GetValue<string>(); }
        catch (Exception error) when (error is JsonException || error is InvalidOperationException || error is InvalidDataException) { ParseError = error.Message; }
    }
    public JsonObject Json() => Disk.Parse(Text);
    public byte[] Bytes() => (byte[])bytes.Clone();
}
public sealed record ReportVisual(string Id, string PageId, string File, string Type, string Title,
    double X, double Y, double Width, double Height, double Z, bool Hidden);
public sealed record ReportPage(string Id, string Name, string File, double Width, double Height, IReadOnlyList<ReportVisual> Visuals);

/// <summary>Immutable local snapshot. Does not load a semantic engine, authenticate, or modify files.</summary>
public sealed class ReportIndex
{
    public string Root { get; }
    public string? ProjectFile { get; }
    public string? SemanticModelPath { get; }
    public IReadOnlyDictionary<string, ReportFile> Files { get; }
    public IReadOnlyList<string> Resources { get; }
    public IReadOnlyList<ReportPage> Pages { get; }
    public string Name => System.IO.Path.GetFileName(Root);
    public bool Enhanced => Files.ContainsKey("definition/report.json");
    public string Version => Files.TryGetValue("definition.pbir", out var file) && file.ParseError == null ? file.Json()["version"]?.ToString() ?? "Unknown" : "Unknown";
    internal ReportIndex(string root, string? projectFile, string? semanticModelPath, IDictionary<string, ReportFile> files, IEnumerable<string> resources)
    {
        Root = root; ProjectFile = projectFile; SemanticModelPath = semanticModelPath;
        Files = new ReadOnlyDictionary<string, ReportFile>(new Dictionary<string, ReportFile>(files, StringComparer.OrdinalIgnoreCase));
        Resources = Array.AsReadOnly(resources.ToArray());
        var pages = new List<ReportPage>();
        foreach (var file in Files.Values.Where(f => f.Path.StartsWith("definition/pages/", StringComparison.Ordinal) && f.Path.EndsWith("/page.json", StringComparison.Ordinal) && f.ParseError == null))
        {
            var json = file.Json(); var prefix = file.Path.Substring(0, file.Path.Length - "page.json".Length);
            var id = json["name"]?.ToString() ?? prefix.Split('/')[2];
            var visuals = Files.Values.Where(f => f.Path.StartsWith(prefix + "visuals/", StringComparison.Ordinal) && f.Path.EndsWith("/visual.json", StringComparison.Ordinal) && f.ParseError == null).Select(f =>
            {
                var v = f.Json(); var p = v["position"] as JsonObject;
                return new ReportVisual(v["name"]?.ToString() ?? "(unnamed)", id, f.Path, (v["visual"] as JsonObject)?["visualType"]?.ToString() ?? "group", Title(v),
                    Number(p, "x"), Number(p, "y"), Number(p, "width", 100), Number(p, "height", 80), Number(p, "z"), v["isHidden"]?.ToString() == "true");
            }).OrderBy(v => v.Z).ToArray();
            pages.Add(new(id, json["displayName"]?.ToString() ?? id, file.Path, Number(json, "width", 1280), Number(json, "height", 720), Array.AsReadOnly(visuals)));
        }
        var order = Files.TryGetValue("definition/pages/pages.json", out var metadata) && metadata.ParseError == null
            ? (metadata.Json()["pageOrder"] as JsonArray)?.Select(n => n?.ToString()).ToList() : null;
        Pages = Array.AsReadOnly(pages.OrderBy(p => order?.Contains(p.Id) == true ? order.IndexOf(p.Id) : int.MaxValue).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public static string Title(JsonObject visual)
    {
        var node = At(visual, "visual", "visualContainerObjects", "title") as JsonArray;
        var value = At(node?.FirstOrDefault(), "properties", "text", "expr", "Literal", "Value")?.ToString();
        return value != null && value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'' ? value.Substring(1, value.Length - 2).Replace("''", "'") : value ?? "";
    }
    internal static JsonNode? At(JsonNode? node, params string[] path) { foreach (var key in path) node = (node as JsonObject)?[key]; return node; }
    private static double Number(JsonObject? node, string key, double fallback = 0) => double.TryParse(node?[key]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && !double.IsNaN(value) && !double.IsInfinity(value) ? value : fallback;
    public static Task<ReportIndex> OpenAsync(string input, CancellationToken ct) => Task.Run(() => Open(input, ct), ct);
    private static ReportIndex Open(string input, CancellationToken ct)
    {
        var full = System.IO.Path.GetFullPath(input); string? project = null;
        if (File.Exists(full) && full.EndsWith(".pbip", StringComparison.OrdinalIgnoreCase))
        {
            project = full; var p = Disk.Parse(Disk.ReadText(full));
            var paths = (p["artifacts"] as JsonArray)?.Select(a => At(a, "report", "path")?.ToString()).Where(a => a != null).ToArray();
            if (paths == null || paths.Length != 1) throw new InvalidDataException("Choose a report folder when a PBIP has no single report artifact.");
            full = Disk.Resolve(System.IO.Path.GetDirectoryName(full)!, paths[0]!);
        }
        else if (File.Exists(full) && full.EndsWith(".pbir", StringComparison.OrdinalIgnoreCase)) full = System.IO.Path.GetDirectoryName(full)!;
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Choose a PBIP, definition.pbir, report folder or project root.");
        Disk.CheckLinks(full);
        if (!File.Exists(System.IO.Path.Combine(full, "definition.pbir")))
        {
            var reports = Discover(full, ct);
            if (reports.Count != 1) throw new InvalidDataException("Select one report; this folder contains " + reports.Count + " reports.");
            return Open(reports[0], ct);
        }
        var files = new Dictionary<string, ReportFile>(StringComparer.OrdinalIgnoreCase); var resources = new List<string>(); long total = 0;
        foreach (var path in Disk.Enumerate(full, ct))
        {
            var relative = path.Substring(full.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
            if (relative == "definition.pbir" || relative.StartsWith("definition/", StringComparison.Ordinal) && relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Disk.Read(path); total += bytes.Length;
                if (total > 64 * 1024 * 1024) throw new InvalidDataException("Report definitions exceed 64 MiB.");
                files.Add(relative, new(relative, bytes));
            }
            else if (relative.StartsWith("StaticResources/", StringComparison.Ordinal) || relative.StartsWith("CustomVisuals/", StringComparison.Ordinal)) resources.Add(relative);
        }
        string? model = null;
        if (files.TryGetValue("definition.pbir", out var definition) && definition.ParseError == null)
        {
            var relative = At(definition.Json(), "datasetReference", "byPath", "path")?.ToString();
            if (relative != null)
            {
                var parent = System.IO.Path.GetDirectoryName(full)!;
                var candidate = Disk.Resolve(parent, System.IO.Path.GetFileName(full) + "/" + relative);
                if (Directory.Exists(candidate)) model = candidate;
            }
        }
        return new(full, project, model, files, resources);
    }
    public static IReadOnlyList<string> Discover(string root, CancellationToken ct) => Array.AsReadOnly(Disk.Enumerate(System.IO.Path.GetFullPath(root), ct).Where(p => System.IO.Path.GetFileName(p).Equals("definition.pbir", StringComparison.OrdinalIgnoreCase)).ToArray());
}

internal static class Disk
{
    public static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    public static byte[] Read(string path) { CheckLinks(path); if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Definition exceeds 4 MiB: " + System.IO.Path.GetFileName(path)); return File.ReadAllBytes(path); }
    public static string ReadText(string path) => new UTF8Encoding(false, true).GetString(Read(path)).TrimStart('\uFEFF');
    public static JsonObject Parse(string text)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
        void Check(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate JSON property: " + p.Name); Check(p.Value); } }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Check(item);
        }
        Check(doc.RootElement); return JsonNode.Parse(text)?.AsObject() ?? throw new InvalidDataException("Expected a JSON object.");
    }
    public static string Resolve(string root, string relative)
    {
        root = System.IO.Path.GetFullPath(root).TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(p => p.EndsWith(" ") || p.EndsWith(".") && p != ".." && p != ".")) throw new InvalidDataException("Invalid relative project path.");
        var result = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        if (!result.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path leaves the selected project root.");
        CheckLinks(result); return result;
    }
    public static void CheckLinks(string path)
    {
        for (var current = System.IO.Path.GetFullPath(path); current != null; current = System.IO.Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files/folders are not supported for report editing.");
    }
    public static IEnumerable<string> Enumerate(string root, CancellationToken ct)
    {
        CheckLinks(root); var pending = new Stack<string>(); pending.Push(root); var count = 0;
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested(); var directory = pending.Pop(); CheckLinks(directory);
            foreach (var child in Directory.EnumerateDirectories(directory))
                if (!new[] { ".pbi", ".git", ".pbibench", "bin", "obj", "node_modules" }.Contains(System.IO.Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) { CheckLinks(child); pending.Push(child); }
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                ct.ThrowIfCancellationRequested(); if (++count > 20000) throw new InvalidDataException("Project exceeds 20,000 files. Choose a narrower report folder.");
                CheckLinks(file); yield return file;
            }
        }
    }
}
