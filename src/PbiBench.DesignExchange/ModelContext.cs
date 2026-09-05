using System.Security.Cryptography;
using System.Text;
using PbiBench.AI.ContextExport;
using PbiBench.ExternalTools;

namespace PbiBench.DesignExchange;

public sealed record DesignModel(string Name, int CompatibilityLevel, IReadOnlyList<ContextObject> Objects, IReadOnlyList<ContextRelationship> Relationships);
public sealed record ModelContext(int ContractVersion, string ModelFingerprint, DesignModel Model)
{
    private static readonly string[] Kinds = { "Table", "Column", "Measure", "CalculationGroup", "CalculationItem", "Function" };
    public static ModelContext Create(ContextModel source)
    {
        // Reuse the reviewed metadata projection, deliberately excluding roles, samples and arbitrary evidence.
        var model = new DesignModel(source.Name, source.CompatibilityLevel,
            Array.AsReadOnly(source.Objects.Where(o => Kinds.Contains(o.Kind, StringComparer.Ordinal)).OrderBy(o => o.Id, StringComparer.Ordinal).Select(o => o with {
                Expression = o.Kind is "Measure" or "Column" or "CalculationItem" or "Function" ? o.Expression : null,
                FormatExpression = o.Kind is "Measure" or "CalculationItem" ? o.FormatExpression : null
            }).ToArray()), Array.AsReadOnly(source.Relationships.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray()));
        ValidateModel(model); return new(1, Fingerprint(model), model);
    }
    public static ModelContext Parse(string json)
    {
        var value = ContractJson.Parse<ModelContext>(json);
        if (value.ContractVersion != 1) throw new InvalidDataException("Unsupported model-context version.");
        ValidateModel(value.Model);
        if (value.ModelFingerprint != Fingerprint(value.Model)) throw new InvalidDataException("Model context fingerprint does not match its metadata.");
        return value;
    }
    public static async Task<ModelContext> LoadAsync(string path, CancellationToken ct) => Parse(await ContractJson.ReadAsync(path, ct).ConfigureAwait(false));
    public Task SaveAsync(string path, CancellationToken ct) => ContractJson.WriteNewAsync(path, ToJson(), ct);
    public string ToJson() { ValidateModel(Model); if (ContractVersion != 1 || ModelFingerprint != Fingerprint(Model)) throw new InvalidDataException("Invalid model context."); return ContractJson.Serialize(this); }
    private static string Fingerprint(DesignModel model)
    {
        using var sha = SHA256.Create();
        var normalized = model with { Objects = model.Objects.OrderBy(o => o.Id, StringComparer.Ordinal).ToArray(), Relationships = model.Relationships.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray() };
        return "sha256:" + string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(ContractJson.Serialize(normalized))).Select(b => b.ToString("x2")));
    }
    private static void ValidateModel(DesignModel? model)
    {
        if (model == null || !Text(model.Name, 512) || model.CompatibilityLevel < 1200 || model.CompatibilityLevel > 10000 ||
            model.Objects == null || model.Objects.Count > 50000 || model.Relationships == null || model.Relationships.Count > 10000)
            throw new InvalidDataException("Invalid model metadata/counts.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var objects = new Dictionary<string, ContextObject>(StringComparer.Ordinal);
        foreach (var obj in model.Objects)
        {
            if (obj == null || !Kinds.Contains(obj.Kind, StringComparer.Ordinal) || !Text(obj.Name, 512) || !Text(obj.Id, 2048) || !ids.Add(obj.Id) ||
                obj.Id != ContextModel.ObjectId(obj.Kind, obj.Table, obj.Name) || obj.Table != null && !Text(obj.Table, 512) ||
                obj.Kind is "Table" or "CalculationGroup" && (obj.Expression != null || obj.FormatExpression != null)) throw new InvalidDataException("Invalid or duplicate semantic object.");
            foreach (var value in new[] { obj.Description, obj.Expression, obj.FormatExpression, obj.FormatString, obj.DisplayFolder, obj.DataType, obj.StorageMode })
                if (value != null && value.Length > 65536) throw new InvalidDataException("Semantic metadata text exceeds limit.");
            objects.Add(obj.Id, obj);
        }
        foreach (var obj in model.Objects)
            if (obj.Kind is "Column" or "Measure" or "CalculationItem" or "CalculationGroup" &&
                (obj.Table == null || !objects.ContainsKey(ContextModel.ObjectId("Table", null, obj.Table)))) throw new InvalidDataException("Semantic object has no owning table.");
        foreach (var rel in model.Relationships)
            if (rel == null || !Text(rel.Id, 2048) || !ids.Add(rel.Id) || !Text(rel.Name, 512) ||
                !objects.TryGetValue(rel.FromColumnId ?? "", out var from) || from.Kind != "Column" || !objects.TryGetValue(rel.ToColumnId ?? "", out var to) || to.Kind != "Column" ||
                !Text(rel.FromCardinality, 32) || !Text(rel.ToCardinality, 32) || !Text(rel.FilterDirection, 32)) throw new InvalidDataException("Invalid relationship endpoints.");
    }
    internal static bool Text(string? text, int limit) => !string.IsNullOrWhiteSpace(text) && text!.Length <= limit && !text.Any(char.IsControl);
}
