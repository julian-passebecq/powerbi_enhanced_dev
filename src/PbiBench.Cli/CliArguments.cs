using System.Globalization;
using System.IO;
using System.Text.Json;
using PbiBench.Core.Automation;
using PbiBench.Core.Commands;
using PbiBench.Core.Quality;
using PbiBench.Core.Refresh;

namespace PbiBench.Cli;

internal sealed record CliProfile(int Version = 1, string? ModelPath = null, string? Server = null, string? Database = null, string? ConnectionStringEnvironmentVariable = null, int RowLimit = 10000, int TimeoutSeconds = 60);
internal sealed record CliInput(CommandRequest? Request, bool Json, bool Apply, bool Help, bool Schema, string? ApprovalHash, string? ReviewOutput, CommandReviewEnvelope? Envelope, string? ConnectionEnvironmentVariable);
internal static class CliArguments
{
    public const string HelpText = "PbiBench semantic CLI\nCommands: inspect, list, get, set, script, action, bpa, query, test, refresh, validate, diff, deploy\n  --model PATH  --server ENDPOINT --database NAME_OR_ID\n  --json --non-interactive --profile FILE --request FILE\n  --kind Measure --name Revenue --table Sales --property Description --value TEXT\n  --query-file FILE --script-file FILE --language SafeCSharp|Dax\n  --recipe-file FILE --action FormatMeasures --tests FILE --refresh-profile FILE\n  --against PATH --output MODEL.bim --fail-on Error|Warning|Information|None\nPreview is the default for writes. Use --review-out FILE, then:\n  pbibench apply --review FILE --approve HASH --apply --json --non-interactive\nLocal apply requires --output .bim; remote reviews are single-use and expire after 30 minutes.\nUse --schema for the safe command contract. Exit codes: 0 success, 2 usage, 3 rejected/validation, 4 execution, 5 canceled, 6 unknown remote outcome.\n";
    public static async Task<CliInput> ParseAsync(string[] args, CancellationToken ct)
    {
        var flags = new HashSet<string>(new[] { "json", "non-interactive", "apply", "help", "schema", "no-policy", "resolve-conflicts" }, StringComparer.Ordinal);
        var valued = new HashSet<string>(new[] { "model", "server", "database", "profile", "request", "approve", "review", "review-out", "connection-env", "kind", "name", "table", "property", "value", "query", "query-file", "script-file", "language", "recipe-file", "action", "action-options", "tests", "refresh-profile", "refresh-type", "scope-table", "partition", "max-parallelism", "effective-date", "against", "output", "bpa-profile", "fail-on", "row-limit", "timeout", "select" }, StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal); var switches = new HashSet<string>(StringComparer.Ordinal); var selections = new List<CommandObject>(); string? command = null;
        for (var index = 0; index < args.Length; index++)
        {
            var item = args[index]; if (!item.StartsWith("--", StringComparison.Ordinal)) { if (command != null) throw new ArgumentException("Unexpected positional argument. Use --model for model paths."); command = item; continue; }
            var key = item.Substring(2);
            if (flags.Contains(key)) { if (!switches.Add(key)) throw new ArgumentException("Duplicate option: --" + key); continue; }
            if (!valued.Contains(key) || ++index >= args.Length) throw new ArgumentException("Unknown option or missing value: --" + key);
            if (key == "select") { selections.Add(JsonSerializer.Deserialize<CommandObject>(args[index], CommandJson.Options) ?? throw new ArgumentException("Invalid object selection JSON.")); continue; }
            if (values.ContainsKey(key)) throw new ArgumentException("Duplicate option: --" + key); values.Add(key, args[index]);
        }
        string? Get(string key) => values.TryGetValue(key, out var value) ? value : null;
        int Number(string key, int fallback) => Get(key) is { } value ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) : fallback;
        if (switches.Contains("help") || args.Length == 0) return new(null, switches.Contains("json"), false, true, false, null, null, null, null);
        if (switches.Contains("schema") || command == "capabilities") return new(null, true, false, false, true, null, null, null, null);
        if (Get("request") != null && Get("review") != null) throw new ArgumentException("Choose a request or a saved review, not both.");
        if (Get("review-out") != null && (switches.Contains("apply") || command == "apply")) throw new ArgumentException("Save a review during preview, then apply the saved review separately.");
        if (Get("query") != null && Get("query-file") != null) throw new ArgumentException("Choose query text or a query file, not both.");
        if (Get("refresh-profile") != null && (new[] { "refresh-type", "scope-table", "partition", "max-parallelism", "effective-date", "timeout" }.Any(values.ContainsKey) || switches.Contains("no-policy"))) throw new ArgumentException("A refresh profile cannot be combined with refresh request overrides.");
        var profile = Get("profile") is { } profilePath ? JsonSerializer.Deserialize<CliProfile>(ReadJson(profilePath), CommandJson.Options) ?? throw new ArgumentException("Invalid profile.") : new CliProfile();
        if (profile.Version != 1) throw new ArgumentException("Unsupported profile version.");
        var envelope = Get("review") is { } reviewPath ? CommandReviewStore.Load(reviewPath) : null;
        CommandRequest request;
        if (envelope != null || Get("request") != null)
        {
            request = envelope?.Request ?? CommandJson.ParseRequest(Read(Get("request")!));
            if (command != null && command != "apply" && !request.Kind.ToString().Equals(command, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The command does not match the supplied request/review.");
            var accepted = new[] { "review", "request", "profile", "approve", "review-out", "connection-env" };
            if (values.Keys.Any(key => !accepted.Contains(key)) || selections.Count > 0 || switches.Contains("no-policy") || switches.Contains("resolve-conflicts")) throw new ArgumentException("A request/review file cannot be combined with hidden command overrides.");
        }
        else
        {
            if (!Enum.TryParse<CommandKind>(command, true, out var kind) || !Enum.IsDefined(typeof(CommandKind), kind)) throw new ArgumentException("Choose a documented command. Use --help.");
            if (Get("name") != null) selections.Add(new(Get("kind") ?? throw new ArgumentException("Object selection requires --kind."), Get("name")!, Get("table")));
            var recipe = Get("recipe-file") is { } recipePath ? await RecipeFiles.LoadRecipeAsync(recipePath, ct) : null;
            var refresh = Get("refresh-profile") is { } refreshPath ? (await RefreshProfileStore.LoadAsync(refreshPath, ct)).Request : kind == CommandKind.Refresh
                ? new RefreshRequest { Kind = Enum.TryParse<RefreshKind>(Get("refresh-type") ?? "Full", true, out var refreshType) ? refreshType : throw new ArgumentException("Unknown refresh type."), Objects = new[] { new RefreshObject(Get("scope-table"), Get("partition")) }, MaxParallelism = Number("max-parallelism", 2), ApplyRefreshPolicy = switches.Contains("no-policy") ? false : null, EffectiveDate = Get("effective-date") is { } date ? DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null, TimeoutSeconds = Number("timeout", profile.TimeoutSeconds) } : null;
            request = new CommandRequest
            {
                Kind = kind, Target = new(Get("model") ?? profile.ModelPath, Get("server") ?? profile.Server, Get("database") ?? profile.Database), Selection = selections,
                ObjectKind = Get("kind"), Property = Get("property"), Value = Get("value"), Script = Get("script-file") is { } scriptPath ? Read(scriptPath) : null, ScriptLanguage = Get("language") ?? "SafeCSharp",
                Recipe = recipe, Action = Get("action"), ActionOptions = Get("action-options") is { } options ? JsonSerializer.Deserialize<Dictionary<string, string>>(options, CommandJson.Options) : null,
                Query = Get("query-file") is { } queryPath ? Read(queryPath) : Get("query"), Tests = Get("tests") is { } testsPath ? await SemanticTestArtifactStore.LoadAsync(testsPath, ct) : null, Refresh = refresh,
                ComparePath = Get("against"), OutputPath = Get("output"), BpaProfilePath = Get("bpa-profile"), FailOn = Get("fail-on") ?? "Error", RowLimit = Number("row-limit", profile.RowLimit), TimeoutSeconds = Number("timeout", profile.TimeoutSeconds), ResolveConflictsUsingSource = switches.Contains("resolve-conflicts")
            };
            var refreshKeys = new[] { "refresh-profile", "refresh-type", "scope-table", "partition", "max-parallelism", "effective-date" };
            if (kind != CommandKind.Refresh && (values.Keys.Any(refreshKeys.Contains) || switches.Contains("no-policy"))) throw new ArgumentException("Refresh options require the refresh command.");
        }
        CommandJson.Validate(request); var environment = Get("connection-env") ?? profile.ConnectionStringEnvironmentVariable;
        if (environment != null && (environment.Length > 128 || environment.Any(character => !char.IsLetterOrDigit(character) && character != '_'))) throw new ArgumentException("Specify a connection environment-variable name, never its value.");
        return new(request, switches.Contains("json"), switches.Contains("apply") || command == "apply", false, false, Get("approve"), Get("review-out"), envelope, environment);
    }
    private static string Read(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("CLI input files are limited to 16 MB."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static string ReadJson(string path) { var json = Read(path); CommandJson.RejectDuplicateFields(json); return json; }
}
