namespace PbiBench.ExternalTools;

/// <summary>Local process handoff only. This DTO has no credential, token, connection string or runtime object.</summary>
public sealed record ProjectContext([property: System.Text.Json.Serialization.JsonRequired] int ContractVersion = 1, string? PbipRoot = null, string? SemanticModelPath = null,
    string? ReportPath = null, string? ModelFingerprint = null, string? ReportFingerprint = null,
    string? FabricWorkspaceId = null, string? FabricItemId = null, string? GitBranch = null,
    string? GitStatus = null, string Source = "Disk")
{
    public void Validate()
    {
        if (ContractVersion != 1 || Source is not ("Disk" or "Loaded" or "Live")) throw new InvalidDataException("Unsupported project context contract/source.");
        foreach (var path in new[] { PbipRoot, SemanticModelPath, ReportPath })
            if (path != null && (path.Length > 32767 || path.Any(char.IsControl) || !Path.IsPathRooted(path))) throw new InvalidDataException("Project context requires absolute local paths.");
        foreach (var id in new[] { FabricWorkspaceId, FabricItemId })
            if (id != null && !Guid.TryParse(id, out _)) throw new InvalidDataException("Invalid Fabric selection ID.");
        foreach (var value in new[] { ModelFingerprint, ReportFingerprint, GitBranch, GitStatus })
            if (value != null && (value.Length > 512 || value.Any(char.IsControl))) throw new InvalidDataException("Invalid project context metadata.");
    }
    public static async Task<ProjectContext> LoadAsync(string path, CancellationToken ct)
    { var value = ContractJson.Parse<ProjectContext>(await ContractJson.ReadAsync(path, ct, 128 * 1024).ConfigureAwait(false)); value.Validate(); return value; }
    public Task SaveAsync(string path, CancellationToken ct) { Validate(); return ContractJson.WriteNewAsync(path, ContractJson.Serialize(this), ct); }
}
