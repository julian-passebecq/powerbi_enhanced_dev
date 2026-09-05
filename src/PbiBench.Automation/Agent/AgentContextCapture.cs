using System.Text.Json;
using PbiBench.Core.Agent;
using PbiBench.Core.Domain;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Automation.Agent;

/// <summary>Explicit, bounded projections; never serializes a database, connection, source partition, credential, or local path.</summary>
public static class AgentContextCapture
{
    public static AgentContextDocument Capture(TabularModelHandler? handler, IReadOnlyList<TabularNamedObject> selection,
        AgentContextOptions options, AgentContextExtras? extras = null)
    {
        if (selection == null || options == null) throw new ArgumentNullException(nameof(selection)); extras ??= new();
        if (selection.Any(item => item == null || handler == null || !ReferenceEquals(item.Model, handler.Model))) throw new InvalidOperationException("Select objects from the current model before capturing context.");
        var sections = new List<object>();
        if (options.SelectedObjects) Add("selectedObjects", selection.Where(Supported).Select(Object), 200);
        if (options.Inventory) Add("inventory", handler == null ? Array.Empty<object>() : handler.Model.Tables.SelectMany(table => new TabularNamedObject[] { table }.Concat(table.Columns).Concat(table.Measures)).Select(Object), 1000);
        if (options.CurrentDax) Add("currentDax", new object[] { Clip(extras.CurrentDax ?? "", 32000) }, 1);
        if (options.BpaFindings) Add("bpaFindings", (extras.Findings ?? Array.Empty<AgentContextFinding>()).Select(item => new { rule = Clip(item.Rule), objectPath = Clip(item.ObjectPath), severity = Clip(item.Severity, 128), reason = Clip(item.Reason) }), 100);
        if (options.WorkspaceDiff)
        {
            var differences = extras.WorkspaceDiff ?? Array.Empty<AgentContextDiff>();
            var safe = differences.Where(SafeDiff).ToArray();
            Add("workspaceDiff", safe.Select(item => new { objectPath = Clip(item.ObjectPath), property = item.Property, before = Clip(item.Before), after = Clip(item.After) }), 100);
            sections.Add(new { name = "workspaceDiffExclusions", omitted = differences.Count - safe.Length, items = new[] { "Only semantic presentation/DAX properties are included; connection, partition, arbitrary annotation and file content are excluded." } });
        }
        if (options.TestResults) Add("testResults", (extras.TestResults ?? Array.Empty<AgentContextTest>()).Select(item => new { name = Clip(item.Name), outcome = Clip(item.Outcome, 128), evidence = Clip(item.Evidence) }), 100);
        if (options.Capabilities)
        {
            var allowed = new HashSet<string>(Enum.GetNames(typeof(ToolCapability)), StringComparer.Ordinal);
            Add("capabilities", (extras.Capabilities ?? Array.Empty<string>()).Where(allowed.Contains).Distinct(StringComparer.Ordinal).Cast<object>(), 16);
        }
        var capture = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new { formatVersion = 1, captureId = capture, capturedAt = DateTimeOffset.UtcNow, sections }, new JsonSerializerOptions { WriteIndented = true });
        return new(capture, handler == null ? "" : new SemanticModelService(handler).Fingerprint(), json);

        void Add(string name, IEnumerable<object> source, int limit)
        {
            var items = source.Take(limit + 1).ToArray();
            sections.Add(new { name, items = items.Take(limit).ToArray(), omitted = items.Length > limit ? "Additional items omitted; narrow the source selection." : "None" });
        }
    }
    private static bool Supported(TabularNamedObject item) => item is Table or Column or Measure;
    private static object Object(TabularNamedObject item) => new { kind = item is Measure ? "Measure" : item is Column ? "Column" : "Table",
        name = item.Name, table = (item as ITabularTableObject)?.Table.Name, dataType = (item as Column)?.DataType.ToString(), hidden = (item as IHideableObject)?.IsHidden };
    private static object Clip(string? value, int maximum = 2000) => new { text = value == null ? "" : value.Substring(0, Math.Min(value.Length, maximum)), truncated = value?.Length > maximum };
    private static readonly HashSet<string> DiffProperties = new(new[] { "Name", "Description", "DisplayFolder", "Expression", "FormatString", "FormatStringExpression", "IsHidden", "DataType", "SummarizeBy" }, StringComparer.OrdinalIgnoreCase);
    private static bool SafeDiff(AgentContextDiff item)
    {
        if (!DiffProperties.Contains(item.Property)) return false;
        // TOM JSON uses camelCase properties. A DAX-looking property in a partition or shared M expression is still source metadata.
        var segments = item.ObjectPath.Split('/');
        return !segments.Any(segment => new[] { "partitions", "dataSources", "expressions", "annotations", "extendedProperties", "credentials", "source" }.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }
}
