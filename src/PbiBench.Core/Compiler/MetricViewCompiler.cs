using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PbiBench.Core.Compiler;

/// <summary>Original, bounded importer for the explicitly documented flat Metric View YAML subset.</summary>
public sealed class MetricViewCompiler
{
    private static readonly Regex Identifier = new(@"^(?:source\.)?([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Aggregate = new(@"^(SUM|AVG|MIN|MAX)\s*\(\s*((?:source\.)?[A-Za-z_][A-Za-z0-9_]*)\s*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public SemanticCompilation Compile(string yaml, string name = "Imported metric view")
    {
        if (yaml == null || yaml.Length > 1024 * 1024) throw new ArgumentException("Metric View YAML is limited to 1 MiB of text.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 512 || name.Any(char.IsControl)) throw new ArgumentException("Enter an intent name of at most 512 characters.");
        var diagnostics = new List<CompilerDiagnostic>(); var top = new Dictionary<string, string>(StringComparer.Ordinal);
        var sections = new Dictionary<string, List<Entry>>(StringComparer.Ordinal) { ["fields"] = new(), ["dimensions"] = new(), ["measures"] = new(), ["joins"] = new() };
        var lines = yaml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'); if (lines.Length > 10000) throw new ArgumentException("Metric View YAML is limited to 10,000 lines.");
        string? section = null; Entry? current = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var raw = lines[index]; var line = index + 1;
            if (raw.Any(c => char.IsControl(c) && c != '\t') || raw.Contains('\t')) { Error("YAML_CONTROL", "Tabs and control characters are unsupported; use spaces for indentation.", line); continue; }
            var text = StripComment(raw).TrimEnd(); if (string.IsNullOrWhiteSpace(text)) continue;
            var indent = text.Length - text.TrimStart(' ').Length; text = text.TrimStart(' ');
            var isItem = indent == 2 && text.StartsWith("- ", StringComparison.Ordinal); if (isItem) text = text.Substring(2);
            var colon = text.IndexOf(':');
            if (colon < 1 || !Regex.IsMatch(text.Substring(0, colon), @"^[a-z_]+$")) { Error("YAML_SHAPE", "Expected an unquoted property key followed by ':'. YAML directives, tags, aliases, flow collections and multiline plain scalars are unsupported.", line); continue; }
            var key = text.Substring(0, colon); var value = text.Substring(colon + 1).Trim();
            if (value is "|" or "|-")
            {
                var block = new List<string>(); int? blockIndent = null;
                while (index + 1 < lines.Length)
                {
                    var next = lines[index + 1]; if (string.IsNullOrWhiteSpace(next)) { index++; block.Add(""); continue; }
                    var spaces = next.Length - next.TrimStart(' ').Length; if (spaces <= indent) break;
                    blockIndent ??= spaces; if (spaces < blockIndent.Value || next.Contains('\t')) { Error("YAML_BLOCK", "Block scalar indentation must remain consistent.", index + 2); break; }
                    index++; block.Add(next.Substring(blockIndent.Value));
                }
                var literal = string.Join("\n", block).TrimEnd('\n');
                value = JsonSerializer.Serialize(value == "|" && block.Count > 0 ? literal + "\n" : literal);
            }
            string parsed;
            try { parsed = Scalar(value); } catch (ArgumentException error) { Error("YAML_SCALAR", error.Message, line); continue; }
            if (indent == 0)
            {
                current = null; section = null;
                if (top.ContainsKey(key)) { Error("YAML_DUPLICATE", "Duplicate top-level key: " + key, line); continue; }
                top.Add(key, parsed);
                if (sections.ContainsKey(key)) { section = key; if (parsed.Length > 0) Error("YAML_SEQUENCE", "Use an indented block sequence for " + key + ".", line); }
                else if (key is not ("version" or "source" or "comment")) Error("UNSUPPORTED_" + key.ToUpperInvariant(), "The prototype preserves but cannot translate '" + key + "'. Metadata proposals are blocked to avoid dropping its semantics.", line);
            }
            else if (section != null && isItem)
            { current = new Entry(line); sections[section].Add(current); Set(current, key, parsed, line); }
            else if (section != null && current != null && indent == 4) Set(current, key, parsed, line);
            else Error("YAML_NESTING", "Supported layout: top-level keys, two-space '- name' items, and four-space item properties. Nested YAML is preserved in the original source but cannot be translated.", line);
        }
        var version = Get(top, "version"); var source = Get(top, "source");
        if (version is not ("0.1" or "1.1")) Error("METRIC_VERSION", "Only the documented 0.1 and 1.1 format versions are recognized.");
        if (!Regex.IsMatch(source, @"^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$")) Error("METRIC_SOURCE", "Select a simple catalog.schema.table source. SQL queries, quoted identifiers and metric-view composition require manual review.");
        if (top.ContainsKey("fields") && top.ContainsKey("dimensions")) Error("METRIC_FIELDS", "Choose fields or its dimensions synonym, not both.");
        var dimensions = sections["fields"].Concat(sections["dimensions"]).Select(entry =>
        {
            Validate(entry, "name", "expr", "comment", "display_name"); var expression = Required(entry, "expr"); var match = Identifier.Match(expression); var column = match.Success ? match.Groups[1].Value : null;
            if (column == null) Error("DIMENSION_SQL", "Dimension SQL transformations and joined references are preserved as intent and cannot produce metadata proposals.", entry.Line);
            return new SemanticDimensionIntent(Required(entry, "name"), expression, Get(entry.Values, "comment"), column, entry.Line);
        }).ToArray();
        var measures = sections["measures"].Select(entry =>
        {
            Validate(entry, "name", "expr", "comment", "display_name"); var expression = Required(entry, "expr"); var match = Aggregate.Match(expression); var aggregate = match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
            var column = match.Success ? Identifier.Match(match.Groups[2].Value).Groups[1].Value : null;
            if (Regex.IsMatch(expression, @"^COUNT\s*\(\s*(?:\*|1)\s*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) aggregate = "COUNTROWS";
            if (aggregate == null) Error("MEASURE_SQL", "Only direct-column SUM, AVG, MIN, MAX and COUNT(*)/COUNT(1) have prototype proposals. Filters, DISTINCT, windows, arithmetic and SQL functions require manual translation.", entry.Line);
            return new SemanticMeasureIntent(Required(entry, "name"), expression, Get(entry.Values, "comment"), aggregate, column, entry.Line);
        }).ToArray();
        var joins = sections["joins"].Select(entry =>
        {
            Validate(entry, "name", "source", "on", "cardinality"); Error("JOIN_REVIEW", "Join intent is exported for review; join grain, null behavior and dynamic joins are not assumed equivalent to tabular relationships.", entry.Line);
            return new SemanticJoinIntent(Required(entry, "name"), Required(entry, "source"), Required(entry, "on"), entry.Values.TryGetValue("cardinality", out var cardinality) ? cardinality : "many_to_one", entry.Line);
        }).ToArray();
        if (dimensions.Length + measures.Length == 0) Error("METRIC_EMPTY", "Define at least one field or measure.");
        foreach (var measure in measures.Where(item => item.SourceColumn != null))
            if (dimensions.Any(field => string.Equals(field.Name, measure.SourceColumn, StringComparison.OrdinalIgnoreCase) && !string.Equals(field.Name, field.SourceColumn, StringComparison.OrdinalIgnoreCase))) Error("FIELD_ALIAS", "Measure " + measure.Name + " references a field alias. Resolve alias lineage explicitly before proposing metadata.", measure.Line);
        foreach (var dimension in dimensions.Where(item => item.SourceColumn != null))
            if (dimensions.Any(field => !ReferenceEquals(field, dimension) && string.Equals(field.Name, dimension.SourceColumn, StringComparison.OrdinalIgnoreCase))) Error("FIELD_ALIAS", "Field " + dimension.Name + " may reference another field alias. Resolve its lineage manually.", dimension.Line);
        foreach (var duplicate in dimensions.Select(item => item.Name).Concat(measures.Select(item => item.Name)).GroupBy(item => item, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)) Error("METRIC_DUPLICATE", "Duplicate field/measure name: " + duplicate.Key);
        diagnostics.Add(new("PROTOTYPE_REVIEW", "The IR preserves the original YAML. Proposed aggregates require explicit source-table mapping and data validation; no source query, table, relationship or partition is created.", CompilerSeverity.Warning));
        return new(new SemanticIntent(name, version, source, Get(top, "comment"), Array.AsReadOnly(dimensions), Array.AsReadOnly(measures), Array.AsReadOnly(joins), yaml), diagnostics);
        void Error(string code, string message, int line = 0) => diagnostics.Add(new(code, message, CompilerSeverity.Error, line));
        void Set(Entry entry, string key, string value, int line) { if (entry.Values.ContainsKey(key)) Error("YAML_DUPLICATE", "Duplicate item property: " + key, line); else entry.Values.Add(key, value); }
        string Required(Entry entry, string key) { var value = Get(entry.Values, key); if (string.IsNullOrWhiteSpace(value) || value.Length > (key == "expr" ? 262144 : 512) || key == "name" && value.Any(char.IsControl)) Error("METRIC_REQUIRED", "Missing or oversized " + key + ".", entry.Line); return value; }
        void Validate(Entry entry, params string[] allowed) { foreach (var key in entry.Values.Keys.Except(allowed)) Error("METRIC_PROPERTY", "Unsupported item property: " + key + ". Its semantics must be translated manually.", entry.Line); if (entry.Values.ContainsKey("display_name")) diagnostics.Add(new("DISPLAY_NAME", "display_name is retained in the source; object names use the explicit name property.", CompilerSeverity.Information, entry.Line)); }
    }
    private static string Get(IDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value : "";
    private static string Scalar(string text)
    {
        if (text.Length == 0) return "";
        if (text[0] == '"') { try { return JsonSerializer.Deserialize<string>(text) ?? throw new ArgumentException("A scalar string is required."); } catch (JsonException) { throw new ArgumentException("Use a complete JSON-compatible double-quoted scalar."); } }
        if (text[0] == '\'') { if (text.Length < 2 || text[text.Length - 1] != '\'') throw new ArgumentException("Unclosed single-quoted scalar."); var body = text.Substring(1, text.Length - 2); if (body.Replace("''", "").Contains('\'')) throw new ArgumentException("Escape a single quote by doubling it."); return body.Replace("''", "'"); }
        if (text[0] is '&' or '*' or '!' or '[' or '{' or '|' or '>' || text == "---" || text == "...") throw new ArgumentException("YAML aliases, anchors, tags, flow collections and unsupported block modifiers are rejected.");
        return text;
    }
    private static string StripComment(string text)
    {
        char quote = '\0'; for (var index = 0; index < text.Length; index++) { var c = text[index]; if (quote == '"' && c == '\\') { index++; continue; } if (c is '\'' or '"') { if (quote == c) { if (c == '\'' && index + 1 < text.Length && text[index + 1] == '\'') index++; else quote = '\0'; } else if (quote == '\0') quote = c; } else if (quote == '\0' && c == '#' && (index == 0 || char.IsWhiteSpace(text[index - 1]))) return text.Substring(0, index); } return text;
    }
    private sealed class Entry { public Entry(int line) => Line = line; public int Line { get; } public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal); }
}
