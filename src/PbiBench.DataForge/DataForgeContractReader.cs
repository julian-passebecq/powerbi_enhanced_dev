using System.Text.Json;
namespace PbiBench.DataForge;
public sealed record DataForgeContract(string Root,JsonDocument? Project,JsonDocument? SemanticModel,JsonDocument? Kpis,JsonDocument? Truth,IReadOnlyList<string> DataFiles);
public sealed class DataForgeContractReader
{
    public async Task<DataForgeContract> ReadAsync(string root,CancellationToken ct=default)
    {
        root=Path.GetFullPath(root); if(!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        async Task<JsonDocument?> Read(string name){var p=Directory.EnumerateFiles(root,name,SearchOption.AllDirectories).FirstOrDefault();if(p is null)return null;await using var s=File.OpenRead(p);return await JsonDocument.ParseAsync(s,cancellationToken:ct);}
        var data=Directory.EnumerateFiles(root,"*.*",SearchOption.AllDirectories).Where(p=>p.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".parquet",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase)).ToArray();
        return new(root,await Read("project.json"),await Read("semantic_model.json"),await Read("kpi_catalog.json"),await Read("truth_manifest.json"),data);
    }
}
