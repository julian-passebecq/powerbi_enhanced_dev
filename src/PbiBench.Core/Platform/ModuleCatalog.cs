using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PbiBench.Core.Platform;

public sealed record CatalogModule(string Id, string DisplayName, string Kind, string Lifecycle, string Version,
    string EntryPoint, IReadOnlyList<string> TargetFrameworks, IReadOnlyList<string> OwnerProjects, string UpdateLane,
    IReadOnlyList<string> UpstreamDependencies, IReadOnlyList<string> Contracts, IReadOnlyList<string> DependsOnModules,
    IReadOnlyList<string> ForbiddenDependencies, IReadOnlyList<string> ProtectingTests);
public sealed record CatalogProject(string Path, string ModuleId, IReadOnlyList<string> References);

/// <summary>Offline ownership and update contracts. No module loading or execution is performed.</summary>
public sealed record ModuleCatalog(int SchemaVersion, string ProductVersion, string BaselineCommit, IReadOnlyList<CatalogModule> Modules)
{
    public IReadOnlyList<CatalogProject> ProjectGraph { get; init; } = Array.Empty<CatalogProject>();
    public static IReadOnlyList<string> Lifecycles { get; } = Array.AsReadOnly(new[] { "Active", "Selective", "Independent", "Incubating", "OnDemand", "Later" });
    public static IReadOnlyList<string> Kinds { get; } = Array.AsReadOnly(new[] { "InProcess", "SeparateProcess", "ExternalProcess", "Library", "Lab" });
    public static string LifecycleLabel(string value) => value == "OnDemand" ? "On demand" : value;
    public static ModuleCatalog Parse(string json)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > 256 * 1024) throw new InvalidDataException("Module catalog exceeds 256 KiB.");
        ModuleCatalog value;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 }); RejectDuplicates(doc.RootElement);
            value = JsonSerializer.Deserialize<ModuleCatalog>(json, new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16
            }) ?? throw new InvalidDataException("Empty module catalog.");
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid module catalog JSON.", error); }
        if (value.SchemaVersion != 1 || !Match(value.ProductVersion, @"^\d+\.\d+\.\d+$") || !Match(value.BaselineCommit, "^[0-9a-f]{40}$") ||
            value.Modules == null || value.Modules.Count is < 1 or > 64) throw new InvalidDataException("Invalid module catalog header.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in value.Modules)
        {
            if (m == null || !Match(m.Id, "^[a-z][a-z0-9-]{0,63}$") || !ids.Add(m.Id) || !Text(m.DisplayName, 80) ||
                !Kinds.Contains(m.Kind, StringComparer.Ordinal) || !Lifecycles.Contains(m.Lifecycle, StringComparer.Ordinal) || !Match(m.Version, @"^\d+\.\d+\.\d+$") ||
                !Text(m.EntryPoint, 240) || !Text(m.UpdateLane, 80) || !List(m.TargetFrameworks, 1, 8, 64) ||
                m.TargetFrameworks.Any(f => !new[] { "net48", "net10.0", "net10.0-windows", "external", "planned" }.Contains(f, StringComparer.Ordinal)) ||
                !List(m.OwnerProjects, 1, 16, 160) || m.OwnerProjects.Any(p => !Path(p, "src/", ".csproj") && !(m.Lifecycle == "Later" && p == "planned")) ||
                !List(m.ProtectingTests, m.Lifecycle == "Later" ? 0 : 1, 32, 240) || m.ProtectingTests.Any(p => !Path(p, "tests/", ".cs")) ||
                !List(m.UpstreamDependencies, 0, 32, 160) || !List(m.Contracts, 1, 16, 240) || !List(m.DependsOnModules, 0, 32, 64) ||
                !List(m.ForbiddenDependencies, 0, 32, 160) || m.Lifecycle != "Later" && m.TargetFrameworks.Contains("planned"))
                throw new InvalidDataException("Invalid or incomplete module metadata.");
        }
        var modules = value.Modules.ToDictionary(m => m.Id, StringComparer.Ordinal);
        if (value.ProjectGraph == null || value.ProjectGraph.Count > 64 || value.ProjectGraph.Any(p => p == null) || value.ProjectGraph.Select(p => p.Path).Distinct(StringComparer.Ordinal).Count() != value.ProjectGraph.Count)
            throw new InvalidDataException("Invalid project-reference graph.");
        foreach (var project in value.ProjectGraph)
            if (!Path(project.Path, "src/", ".csproj") || !modules.ContainsKey(project.ModuleId) || project.References == null ||
                project.References.Any(p => !Path(p, "src/", ".csproj")) || project.References.Distinct(StringComparer.Ordinal).Count() != project.References.Count ||
                project.References.Any(p => !value.ProjectGraph.Any(target => target.Path == p))) throw new InvalidDataException("Invalid project-reference graph entry.");
        var completed = new HashSet<string>(StringComparer.Ordinal); var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (!modules.TryGetValue(id, out var m)) throw new InvalidDataException("Unknown module dependency: " + id);
            if (completed.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidDataException("Module dependency cycle: " + id);
            foreach (var dependency in m.DependsOnModules) Visit(dependency);
            visiting.Remove(id); completed.Add(id);
        }
        foreach (var m in value.Modules) Visit(m.Id);
        string Runtime(string framework) => framework == "net10.0-windows" ? "net10.0" : framework;
        foreach (var m in value.Modules.Where(m => m.Lifecycle != "Later"))
            foreach (var dependency in m.DependsOnModules.Select(id => modules[id]).Where(d => d.Kind is not ("SeparateProcess" or "ExternalProcess") && d.Lifecycle != "Later"))
                if (!m.TargetFrameworks.Select(Runtime).Intersect(dependency.TargetFrameworks.Select(Runtime), StringComparer.Ordinal).Any())
                    throw new InvalidDataException("Incompatible module runtimes: " + m.Id + " -> " + dependency.Id);
        IEnumerable<CatalogModule> Closure(CatalogModule m)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(m.Id);
            while (pending.Count > 0) { var id = pending.Pop(); if (!seen.Add(id)) continue; yield return modules[id]; foreach (var dependency in modules[id].DependsOnModules) pending.Push(dependency); }
        }
        foreach (var m in value.Modules)
        {
            var closure = Closure(m).ToArray();
            if (closure.Any(d => m.ForbiddenDependencies.Contains(d.Id, StringComparer.Ordinal) ||
                d.OwnerProjects.Select(System.IO.Path.GetFileNameWithoutExtension).Concat(d.UpstreamDependencies).Any(p => m.ForbiddenDependencies.Contains(p!, StringComparer.Ordinal))))
                throw new InvalidDataException("Forbidden dependency in module: " + m.Id);
        }
        IReadOnlyList<string> Copy(IReadOnlyList<string> items) => Array.AsReadOnly(items.ToArray());
        return value with { ProjectGraph = Array.AsReadOnly(value.ProjectGraph.Select(p => p with { References = Array.AsReadOnly(p.References.ToArray()) }).ToArray()), Modules = Array.AsReadOnly(value.Modules.Select(m => m with {
            TargetFrameworks = Copy(m.TargetFrameworks), OwnerProjects = Copy(m.OwnerProjects), UpstreamDependencies = Copy(m.UpstreamDependencies),
            Contracts = Copy(m.Contracts), DependsOnModules = Copy(m.DependsOnModules), ForbiddenDependencies = Copy(m.ForbiddenDependencies), ProtectingTests = Copy(m.ProtectingTests)
        }).ToArray()) };
    }
    public static ModuleCatalog Bundled()
    {
        using var stream = typeof(ModuleCatalog).Assembly.GetManifestResourceStream("PbiBench.module_catalog.json") ?? throw new InvalidDataException("Bundled module catalog is missing.");
        using var reader = new StreamReader(stream); return Parse(reader.ReadToEnd());
    }
    private static bool Path(string value, string prefix, string suffix) => value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(suffix, StringComparison.Ordinal) && !value.Contains("..") && !value.Contains('\\') && !value.Contains(':');
    private static bool Text(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text!.Length <= max && !text.Any(char.IsControl);
    private static bool Match(string? text, string pattern) => text != null && text.Length <= 80 && Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static bool List(IReadOnlyList<string>? list, int min, int max, int length) => list != null && list.Count >= min && list.Count <= max && list.All(s => Text(s, length)) && list.Distinct(StringComparer.Ordinal).Count() == list.Count;
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate module field."); RejectDuplicates(p.Value); } }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}
