using System.Text;
using System.Text.Json;
using PbiBench.Core.Queries;

namespace PbiBench.Core.Automation;

public enum MacroMode { SafeScript, Recipe, TrustedLegacy }
public sealed record ScriptMacro(string Id, string Name, MacroMode Mode, string Source, ActionRecipe? Recipe = null)
{
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public bool Favorite { get; init; }
    public MacroContextRule? Context { get; init; }
}
public sealed record MacroLibrary(IReadOnlyList<ScriptMacro> Macros, int Version = 1);

public static class RecipeFiles
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, MaxDepth = 20, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static async Task SaveRecipeAsync(string path, ActionRecipe recipe, CancellationToken ct)
    { ActionRecipeRules.Validate(recipe); await WriteAsync(path, JsonSerializer.Serialize(recipe, Json), ct).ConfigureAwait(false); }
    public static async Task<ActionRecipe> LoadRecipeAsync(string path, CancellationToken ct)
    { var recipe = JsonSerializer.Deserialize<ActionRecipe>(await ReadAsync(path, ct).ConfigureAwait(false), Json) ?? throw new InvalidDataException("Recipe is empty."); ActionRecipeRules.Validate(recipe); return recipe; }
    public static async Task SaveLibraryAsync(string path, MacroLibrary library, CancellationToken ct)
    { Validate(library); await WriteAsync(path, JsonSerializer.Serialize(library, Json), ct).ConfigureAwait(false); }
    public static async Task<MacroLibrary> LoadLibraryAsync(string path, CancellationToken ct)
    { var library = JsonSerializer.Deserialize<MacroLibrary>(await ReadAsync(path, ct).ConfigureAwait(false), Json) ?? throw new InvalidDataException("Macro library is empty."); Validate(library); return library; }
    private static void Validate(MacroLibrary library)
    {
        if (library.Version != 1 || library.Macros == null || library.Macros.Count > 256) throw new InvalidDataException("Unsupported macro library version or more than 256 entries.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var macro in library.Macros)
        {
            if (macro == null || !Guid.TryParse(macro.Id, out _) || !ids.Add(macro.Id) || string.IsNullOrWhiteSpace(macro.Name) || macro.Name.Length > 128 || !Enum.IsDefined(typeof(MacroMode), macro.Mode) || macro.Source == null || macro.Source.Length > 262144) throw new InvalidDataException("Invalid macro id, name, mode or source.");
            if (macro.Mode == MacroMode.Recipe) { if (macro.Recipe == null) throw new InvalidDataException("A typed macro requires a recipe."); ActionRecipeRules.Validate(macro.Recipe); }
            else if (macro.Recipe != null) throw new InvalidDataException("Script macros cannot contain a hidden recipe.");
            if (macro.Tags == null || macro.Tags.Count > 16 || macro.Tags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 64)) throw new InvalidDataException("Macros support at most 16 tags of 64 characters.");
            macro.Context?.Validate();
        }
    }
    private static async Task<string> ReadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Recipe and macro files are limited to 4 MB.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true); var text = await reader.ReadToEndAsync().ConfigureAwait(false); ct.ThrowIfCancellationRequested(); return text;
    }
    private static async Task WriteAsync(string path, string json, CancellationToken ct)
    {
        var bytes = new UTF8Encoding(false).GetBytes(json); if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Recipe and macro files are limited to 4 MB.");
        var destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true)) { await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false); await stream.FlushAsync(ct).ConfigureAwait(false); } AtomicQueryFile.Commit(temporary, destination, ct); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
