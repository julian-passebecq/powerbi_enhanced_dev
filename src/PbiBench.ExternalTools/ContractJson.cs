using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PbiBench.ExternalTools;

/// <summary>Bounded, duplicate-free, exact-case JSON for local metadata contracts.</summary>
public static class ContractJson
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    private static JsonSerializerOptions Options => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32, WriteIndented = true };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static JsonDocument Document(string text, int maximumBytes = MaximumBytes)
    {
        if (text == null || Encoding.UTF8.GetByteCount(text) > maximumBytes) throw new InvalidDataException("JSON exceeds the contract size limit.");
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            Check(doc.RootElement); return doc;
        }
        catch (Exception error) when (error is JsonException || error is InvalidDataException)
        { doc?.Dispose(); throw new InvalidDataException("Invalid contract JSON: " + error.Message, error); }
    }
    private static void Check(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property: " + property.Name); Check(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Check(item);
    }
    public static T Parse<T>(string text, int maximumBytes = MaximumBytes)
    {
        using var doc = Document(text, maximumBytes);
        try { return JsonSerializer.Deserialize<T>(doc.RootElement.GetRawText(), Options) ?? throw new InvalidDataException("Empty JSON contract."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid contract JSON: " + error.Message, error); }
    }
    public static async Task<string> ReadAsync(string path, CancellationToken ct, int maximumBytes = MaximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        if (stream.Length > maximumBytes) throw new InvalidDataException("File exceeds the contract size limit.");
        using var buffer = new MemoryStream(); var bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false)) != 0)
        { if (buffer.Length + count > maximumBytes) throw new InvalidDataException("File exceeds the contract size limit."); buffer.Write(bytes, 0, count); }
        ct.ThrowIfCancellationRequested();
        var text = new UTF8Encoding(false, true).GetString(buffer.ToArray());
        return text.TrimStart('\uFEFF');
    }
    public static async Task WriteNewAsync(string path, string text, CancellationToken ct)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("JSON exceeds the contract size limit.");
        ct.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);
        await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
    }
}
