using System.Globalization;
using System.Text.Json;
using PbiBench.Core.Domain;

namespace PbiBench.Core.Refresh;

public static class RefreshPlanner
{
    public static RefreshPlan Build(RefreshMetadataSnapshot metadata, RefreshRequest request)
    {
        if (metadata == null || request == null) throw new ArgumentNullException(metadata == null ? nameof(metadata) : nameof(request));
        if (metadata.Tables == null || metadata.Tables.Count > 100000 || metadata.Tables.Any(table => table == null || table.Partitions == null)) throw new InvalidDataException("Invalid refresh metadata snapshot.");
        if (request.Objects == null || request.Objects.Count == 0 || request.Objects.Count > 10000 || request.Objects.Any(o => o == null)) throw new InvalidDataException("Select between one and 10,000 refresh objects.");
        if (request.SourceOverrides == null || request.SourceOverrides.Count > 1000 || request.SourceOverrides.Any(o => o == null)) throw new InvalidDataException("Too many or invalid source overrides.");
        metadata = metadata with { Tables = Array.AsReadOnly(metadata.Tables.Select(t => t with { Partitions = Array.AsReadOnly(t.Partitions.ToArray()) }).ToArray()) };
        request = request with { Objects = Array.AsReadOnly(request.Objects.ToArray()), SourceOverrides = Array.AsReadOnly(request.SourceOverrides.ToArray()) };
        var issues = new List<RefreshIssue>();
        void Error(string code, string message) => issues.Add(new(code, message, RefreshIssueSeverity.Error));
        void Warn(string code, string message) => issues.Add(new(code, message, RefreshIssueSeverity.Warning));
        if (!Enum.IsDefined(typeof(RefreshKind), request.Kind)) Error("TYPE", "Choose a documented refresh type.");
        if (request.MaxParallelism < 1 || request.MaxParallelism > 256) Error("PARALLELISM", "Choose maximum parallelism from 1 to 256.");
        if (request.TimeoutSeconds < 1 || request.TimeoutSeconds > 86400) Error("TIMEOUT", "Choose a timeout from 1 to 86,400 seconds.");
        if (!metadata.IsConnected || string.IsNullOrWhiteSpace(metadata.Server)) Error("OFFLINE", "Connect to the target model before executing refresh. TMSL can still be reviewed and exported.");
        if (metadata.HasUnsavedChanges) Error("UNSAVED", "The editor has unsaved metadata edits. Save or discard them before previewing a connected refresh; this tool never deploys them implicitly.");
        if (string.IsNullOrWhiteSpace(metadata.DatabaseId) || string.IsNullOrWhiteSpace(metadata.DatabaseName) || string.IsNullOrWhiteSpace(metadata.Fingerprint)) Error("TARGET", "A database identity, name and metadata fingerprint are required.");
        if (metadata.CompatibilityLevel < 1200) Error("COMPATIBILITY", "TMSL refresh requires model compatibility level 1200 or later.");
        var tables = metadata.Tables.ToDictionary(table => table.Name, StringComparer.Ordinal); var selected = new List<(string Table, RefreshPartitionMetadata Partition)>();
        var objectKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in request.Objects)
        {
            if (obj.Table == null && obj.Partition != null) { Error("SCOPE", "A partition requires its containing table."); continue; }
            var key = JsonSerializer.Serialize(obj); if (!objectKeys.Add(key)) Error("DUPLICATE", "Duplicate refresh scopes are not allowed.");
            if (obj.Table == null)
            {
                if (request.Objects.Count != 1) Error("OVERLAP", "Entire-model refresh cannot be combined with table or partition scopes.");
                selected.AddRange(metadata.Tables.SelectMany(t => t.Partitions.Select(p => (t.Name, p)))); continue;
            }
            if (!tables.TryGetValue(obj.Table, out var table)) { Error("TABLE", "Selected table no longer exists: " + obj.Table); continue; }
            if (obj.Partition == null)
            {
                if (request.Objects.Any(other => other.Table == obj.Table && other.Partition != null)) Error("OVERLAP", "A table and its partitions cannot both be selected: " + obj.Table);
                selected.AddRange(table.Partitions.Select(p => (table.Name, p)));
            }
            else
            {
                var partition = table.Partitions.FirstOrDefault(p => p.Name == obj.Partition);
                if (partition == null) Error("PARTITION", "Selected partition no longer exists: " + obj); else selected.Add((table.Name, partition));
            }
        }
        if (request.Kind == RefreshKind.Add && (request.Objects.Any(o => o.Partition == null) || selected.Any(p => p.Partition.SourceKind != RefreshSourceKind.M && p.Partition.SourceKind != RefreshSourceKind.Query))) Error("ADD_SCOPE", "Add is supported here only for explicit regular M/native-query partitions, not calculated/entity/push partitions or whole tables/models.");
        if (request.Kind == RefreshKind.Defragment && request.Objects.Any(o => o.Partition != null)) Error("DEFRAGMENT_SCOPE", "Defragment targets a table or model, not a partition.");
        if (request.Kind == RefreshKind.Add) Warn("APPEND", "Add appends rows. Overlapping source rows can create duplicates; no automatic deduplication is performed.");
        if (request.Kind == RefreshKind.ClearValues) Warn("CLEAR", "ClearValues removes stored values from the selected objects and their dependents. Data must be processed again before it can be queried.");
        if (request.Kind == RefreshKind.DataOnly) Warn("RECALCULATE", "DataOnly loads data and clears dependents. A subsequent Calculate/Full refresh may be required before dependent calculations are usable.");
        var directLake = selected.Any(p => p.Partition.Mode == "DirectLake");
        if (directLake)
        {
            Warn("DIRECT_LAKE", "Direct Lake refresh frames the Delta metadata snapshot and may evict resident segments. It does not import an entire source copy; capacity/source guardrails still apply.");
            if (request.Kind != RefreshKind.Full && request.Kind != RefreshKind.Automatic && request.Kind != RefreshKind.Calculate) Error("DIRECT_LAKE_TYPE", "This workflow supports Full, Automatic or Calculate for scopes containing Direct Lake; other processing types require endpoint-specific validation.");
        }
        if (selected.Any(p => p.Partition.Mode == "DirectQuery"))
        {
            Warn("DIRECT_QUERY", "DirectQuery source data is read at query time; refresh does not create a full imported copy.");
            if (request.Kind == RefreshKind.Add || request.Kind == RefreshKind.DataOnly || request.Kind == RefreshKind.Defragment) Error("DIRECT_QUERY_TYPE", "The selected refresh type is not supported here for DirectQuery partitions.");
        }
        if ((request.ApplyRefreshPolicy.HasValue || request.EffectiveDate.HasValue) && !metadata.IsPowerBi) Error("POLICY_ENDPOINT", "Incremental policy overrides are supported here only for a Power BI/Fabric XMLA model.");
        var hasPolicy = request.Objects.Any(o => o.Table == null ? tables.Values.Any(t => t.HasRefreshPolicy) : tables.TryGetValue(o.Table, out var t) && t.HasRefreshPolicy);
        if (request.EffectiveDate.HasValue && (!hasPolicy || request.ApplyRefreshPolicy == false)) Error("EFFECTIVE_DATE", "An effective date requires an applicable incremental refresh policy with policy application enabled.");
        if (request.EffectiveDate.HasValue && request.EffectiveDate.Value.TimeOfDay != TimeSpan.Zero) Error("DATE_TIME", "Effective date is a calendar date; omit the time component.");
        if (request.EffectiveDate.HasValue && request.Kind != RefreshKind.Full && request.Kind != RefreshKind.DataOnly && request.Kind != RefreshKind.Automatic) Error("DATE_REFRESH_TYPE", "Effective date does not affect this processing type. Use Full, DataOnly or Automatic with an applicable policy.");
        if (hasPolicy && request.ApplyRefreshPolicy != false && (request.Kind == RefreshKind.Full || request.Kind == RefreshKind.DataOnly || request.Kind == RefreshKind.Automatic))
            Warn("POLICY", "Applying the incremental policy can create, merge or remove partitions, including historical partitions outside the rolling window. Full does not mean all historical partitions are reloaded when policy application is enabled.");
        if (hasPolicy && request.ApplyRefreshPolicy == false) Warn("POLICY_DISABLED", "The refresh policy is bypassed for this run. A full table refresh preserves partition definitions and reloads all existing partitions, which may be expensive.");
        var overrides = new List<object>(); var overrideKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in request.SourceOverrides)
        {
            var match = selected.FirstOrDefault(p => p.Table == source.Table && p.Partition.Name == source.Partition);
            if (match.Partition == null) { Error("OVERRIDE_SCOPE", "A source override must target a selected partition: " + source.Table + " / " + source.Partition); continue; }
            if (!overrideKeys.Add(JsonSerializer.Serialize(new[] { source.Table, source.Partition }))) Error("OVERRIDE_DUPLICATE", "Only one source override is allowed per partition.");
            if (source.SourceKind != match.Partition.SourceKind || source.SourceKind != RefreshSourceKind.M && source.SourceKind != RefreshSourceKind.Query) Error("OVERRIDE_KIND", "Overrides must retain the existing M or native Query source type. Calculated/entity and source-type conversions are not supported.");
            if (match.Partition.Mode != "Import") Error("OVERRIDE_MODE", "Development source overrides are restricted to Import partitions.");
            if (string.IsNullOrWhiteSpace(source.Expression) || source.Expression.Length > 1000000) Error("OVERRIDE_TEXT", "An override requires source text of at most one million characters.");
            if (request.Kind != RefreshKind.Full && request.Kind != RefreshKind.DataOnly && request.Kind != RefreshKind.Add) Error("OVERRIDE_REFRESH_TYPE", "Source overrides require Full, DataOnly or Add so the selected data is actually read.");
            if (tables[source.Table].HasRefreshPolicy && request.ApplyRefreshPolicy != false) Error("OVERRIDE_POLICY", "Disable policy application when overriding a managed partition; policy-created partitions cannot be silently rebound.");
            var content = new Dictionary<string, object?> { ["type"] = source.SourceKind == RefreshSourceKind.M ? "m" : "query" };
            content[source.SourceKind == RefreshSourceKind.M ? "expression" : "query"] = source.Expression;
            if (source.SourceKind == RefreshSourceKind.Query)
            {
                if (string.IsNullOrWhiteSpace(match.Partition.DataSource)) Error("OVERRIDE_DATASOURCE", "A native query override requires an existing data-source binding.");
                content["dataSource"] = match.Partition.DataSource;
            }
            overrides.Add(new { originalObject = new { database = metadata.DatabaseName, table = source.Table, partition = source.Partition }, source = content });
        }
        if (overrides.Count > 0) Warn("SOURCE_OVERRIDE", "Development overrides change the data loaded for this run without persisting source metadata. Review the exact M/native query. Existing source credentials, gateway and privacy bindings must support it; the loaded development data remains until a later refresh replaces it.");
        // TMSL references existing objects by database name; the connection and preflight bind the distinct stable ID.
        var refresh = new Dictionary<string, object?> { ["type"] = TypeName(request.Kind), ["objects"] = request.Objects.Select(o => Object(metadata.DatabaseName, o)).ToArray() };
        if (request.ApplyRefreshPolicy.HasValue) refresh["applyRefreshPolicy"] = request.ApplyRefreshPolicy.Value;
        if (request.EffectiveDate.HasValue) refresh["effectiveDate"] = request.EffectiveDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (overrides.Count > 0) refresh["overrides"] = new[] { new { partitions = overrides } };
        var tmsl = JsonSerializer.Serialize(new { sequence = new { maxParallelism = request.MaxParallelism, operations = new[] { new { refresh } } } }, new JsonSerializerOptions { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(tmsl) > 16 * 1024 * 1024) Error("SIZE", "The generated refresh command exceeds 16 MB.");
        var changes = request.Objects.Select(o => new PlannedChange(o.ToString(), TypeName(request.Kind), "Existing processed state", Effect(request.Kind), Array.AsReadOnly(issues.Select(i => i.Message).ToArray()))).ToArray();
        var changePlan = new ChangePlan(Guid.NewGuid(), DateTimeOffset.UtcNow, ApprovalLevel.RemoteModelWrite,
            new ResourceRef("xmla", null, null, metadata.DatabaseId, "SemanticModel", metadata.Server + " / " + metadata.DatabaseName), Array.AsReadOnly(changes),
            "Verify the captured metadata fingerprint on a separate connection before execution.", "A successful refresh changes engine data. No local Undo is available. A lost response or cancellation does not establish rollback; inspect the target before retrying.");
        return new(metadata, request, tmsl, Array.AsReadOnly(issues.ToArray()), changePlan);
    }
    private static Dictionary<string, string> Object(string database, RefreshObject scope)
    { var result = new Dictionary<string, string> { ["database"] = database }; if (scope.Table != null) result["table"] = scope.Table; if (scope.Partition != null) result["partition"] = scope.Partition; return result; }
    public static string TypeName(RefreshKind kind) => kind switch { RefreshKind.Full => "full", RefreshKind.ClearValues => "clearValues", RefreshKind.Calculate => "calculate", RefreshKind.DataOnly => "dataOnly", RefreshKind.Automatic => "automatic", RefreshKind.Add => "add", RefreshKind.Defragment => "defragment", _ => "invalid" };
    public static string Effect(RefreshKind kind) => kind switch
    {
        RefreshKind.Full => "Load source data and recalculate dependents, subject to incremental policy and storage mode.", RefreshKind.ClearValues => "Remove stored values from selected objects and dependents.",
        RefreshKind.Calculate => "Recalculate dependent objects as needed.", RefreshKind.DataOnly => "Load source data and clear dependent calculations.", RefreshKind.Automatic => "Refresh objects that need processing and their dependents.",
        RefreshKind.Add => "Append source rows and recalculate dependents.", RefreshKind.Defragment => "Remove unused dictionary values from the selected tables.", _ => "Unsupported refresh type."
    };
}
