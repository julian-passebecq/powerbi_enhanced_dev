namespace PbiBench.ExternalTools;

public sealed record ProductComponent(string Id, string Version, string Path);
public sealed record ComponentsManifest(int ContractVersion, string ProductVersion, int ExternalToolsContractVersion,
    int PbirContractVersion, IReadOnlyList<ProductComponent> Components)
{
    public static ComponentsManifest Parse(string json)
    {
        var value = ContractJson.Parse<ComponentsManifest>(json, 64 * 1024);
        if (value.ContractVersion != 1 || value.ExternalToolsContractVersion != 1 || value.PbirContractVersion != 1 ||
            !Version.TryParse(value.ProductVersion, out _) || value.Components == null || value.Components.Count != 3)
            throw new InvalidDataException("Unsupported components manifest.");
        var expected = new[] { "semantic-ide", "report-studio", "fabric-toolbox" };
        if (!value.Components.Select(c => c?.Id).OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x))) throw new InvalidDataException("Invalid component IDs.");
        foreach (var item in value.Components)
            if (!Version.TryParse(item.Version, out _) || string.IsNullOrEmpty(item.Path) || item.Path.Length > 240 ||
                item.Path.Any(char.IsControl) || item.Path.Contains(':') || item.Path.Contains('\\') || item.Path.StartsWith("/", StringComparison.Ordinal) ||
                item.Path.Split('/').Any(p => p is "" or "." or "..") || !item.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid component version/path.");
        return value;
    }
    public static string? Find(string baseDirectory)
    {
        var directory = new DirectoryInfo(baseDirectory);
        for (var i = 0; directory != null && i < 8; i++, directory = directory.Parent)
        { var file = System.IO.Path.Combine(directory.FullName, "components.json"); if (File.Exists(file)) return file; }
        return null;
    }
    public static ComponentsManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 64 * 1024) throw new InvalidDataException("Components manifest exceeds 64 KiB.");
        using var reader = new StreamReader(stream); return Parse(reader.ReadToEnd());
    }
    public string Resolve(string manifestPath, string componentId)
    {
        var root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(manifestPath))!;
        var relative = Components.Single(c => c.Id == componentId).Path;
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        if (!path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Component path escapes its package.");
        return path;
    }
}
