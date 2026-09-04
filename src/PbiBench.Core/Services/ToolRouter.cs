using PbiBench.Core.Domain;
namespace PbiBench.Core.Services;

public sealed class ToolRouter
{
    public string SelectAdapter(string intent, IReadOnlyCollection<AdapterCapability> available)
    {
        static bool Has(AdapterCapability c, ToolCapability cap) => c.IsConnected && (c.Capabilities & cap) == cap;
        var connected = available.Where(x => x.IsConnected).ToArray();
        return intent switch
        {
            "edit-single-measure-live" => connected.FirstOrDefault(x => x.AdapterId.IndexOf("xmla", StringComparison.OrdinalIgnoreCase) >= 0 && Has(x, ToolCapability.WriteMetadata))?.AdapterId
                ?? "local-pbip",
            "pull-cloud-model-to-git" => connected.FirstOrDefault(x => x.AdapterId == "fabric-rest" && Has(x, ToolCapability.GetDefinition))?.AdapterId
                ?? "unavailable",
            "advanced-dax-performance" => connected.FirstOrDefault(x => x.AdapterId == "daxstudio")?.AdapterId
                ?? connected.FirstOrDefault(x => Has(x, ToolCapability.QueryDax))?.AdapterId
                ?? "unavailable",
            "tenant-estate-inventory" => connected.FirstOrDefault(x => Has(x, ToolCapability.AdminInventory))?.AdapterId ?? "unavailable",
            _ => connected.FirstOrDefault()?.AdapterId ?? "unavailable"
        };
    }
}
