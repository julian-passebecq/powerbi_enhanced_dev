using System.Diagnostics;
using System.Text.RegularExpressions;
using PbiBench.Dax.LanguageService;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.ModelAuthoring;

public sealed record DaxAuthoringObject(string Id, DaxScriptObjectKind Kind, string? Table, string Name, string Expression,
    string Description, string FormatStringExpression)
{
    public override string ToString() => Kind + " · " + (Table == null ? Name : "'" + Table + "'[" + Name + "]");
}
public sealed record DaxFunctionEdit(string? ObjectId, string Name, string Expression, string Description = "", bool IsHidden = false);
public sealed record DaxTextSearch(string Pattern, string Replacement = "", bool UseRegex = false, bool MatchCase = false,
    bool WholeWord = false, bool IncludeDescriptions = false);
public sealed record DaxTextMatch(string ObjectId, string ObjectPath, string Property, int Start, int Length, string Text, string Before, string After);
public sealed record DaxDependencyNode(string SymbolId, string Name, bool IsCycle, IReadOnlyList<DaxDependencyNode> Children);
public sealed record DaxExplanation(IReadOnlyList<string> Dependencies, IReadOnlyList<string> Callers,
    IReadOnlyList<string> Variables, string Expression, IReadOnlyList<DaxDiagnostic> Diagnostics)
{
    public IReadOnlyList<DaxDependencyNode> DependencyTree { get; init; } = Array.Empty<DaxDependencyNode>();
}

/// <summary>Own model authoring through existing TE2 setters, captured previews and native undo transactions.</summary>
public sealed class DaxAuthoringService
{
    private readonly TabularModelHandler handler;
    private readonly DaxLanguageService language = new();
    public DaxAuthoringService(TabularModelHandler handler) => this.handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public IReadOnlyList<DaxAuthoringObject> GetObjects() => Bindings().Where(binding => binding.Property == "Expression")
        .Select(binding => new DaxAuthoringObject(binding.Entry.ObjectKey, binding.Entry.Kind, binding.Entry.Table, binding.Entry.Name,
            binding.Get(), (binding.Object as IDescriptionObject)?.Description ?? "", FormatExpression(binding.Object)))
        .OrderBy(item => item.Kind).ThenBy(item => item.Table).ThenBy(item => item.Name).ToArray();
    public IReadOnlyList<DaxAuthoringObject> GetFunctions() => GetObjects().Where(item => item.Kind == DaxScriptObjectKind.Function).ToArray();
    public DaxAuthoringObject? ResolveDefinition(DaxSymbolLocation location)
    {
        var target = DaxMetadataSnapshotProvider.Resolve(handler, location);
        var binding = Bindings().FirstOrDefault(item => item.Property == "Expression" && ReferenceEquals(item.Object, target));
        return binding == null ? null : GetObjects().FirstOrDefault(item => item.Id == binding.Entry.ObjectKey);
    }

    public string ExportScript(IEnumerable<string>? selectedObjectIds = null)
    {
        var selected = selectedObjectIds == null ? null : new HashSet<string>(selectedObjectIds, StringComparer.OrdinalIgnoreCase);
        return DaxModelScript.Serialize(Bindings().Where(binding => selected == null || selected.Contains(binding.Entry.ObjectKey))
            .Where(binding => binding.Property == "Expression" || !string.IsNullOrEmpty(binding.Get()))
            .Select(binding => binding.Entry with { Expression = binding.Get() }));
    }

    public AuthoringPreview PreviewScript(string text, IEnumerable<string>? selectedKeys = null)
    {
        var parsed = DaxModelScript.Parse(text);
        var issues = parsed.Diagnostics.Select(diagnostic => new AuthoringIssue(diagnostic.Id, diagnostic.Message, AuthoringIssueSeverity.Error)).ToList();
        var selected = selectedKeys == null ? null : new HashSet<string>(selectedKeys, StringComparer.OrdinalIgnoreCase);
        var entries = parsed.Entries.Where(entry => selected == null || selected.Contains(entry.Key)).ToArray();
        if (selected != null && selected.Any(key => !parsed.Entries.Any(entry => Same(entry.Key, key)))) issues.Add(Error("DAXSCRIPT_SELECTION", "The selected script entries changed. Parse the current script again."));
        var edits = new List<AuthoringEdit>(); var snapshot = DaxMetadataSnapshotProvider.Capture(handler);
        var existing = Bindings().ToDictionary(binding => binding.Entry.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.OrderBy(entry => entry.Property != "Expression").ThenBy(entry => entry.Kind == DaxScriptObjectKind.Function ? 0 : entry.Kind == DaxScriptObjectKind.Table ? 1 : 2))
        {
            ValidateExpression(entry, snapshot, issues);
            if (existing.TryGetValue(entry.Key, out var binding))
            {
                AddExpressionEdit(edits, issues, binding, entry.Expression, "Update this listed DAX property. Other properties and unlisted objects stay unchanged.");
                continue;
            }
            if (entry.Property != "Expression")
            { issues.Add(Error("DAXSCRIPT_NEW_FORMAT", "Create the object first, then add its dynamic format expression in a separate preview.", entry.DisplayName)); continue; }
            AddCreation(entry, edits, issues);
        }
        var omittedCreates = parsed.Entries.Where(entry => entry.Property == "Expression" && !existing.ContainsKey(entry.Key) && !entries.Any(selectedEntry => Same(selectedEntry.Key, entry.Key))).ToArray();
        foreach (var entry in entries)
        {
            var tokens = DaxTokenizer.Tokenize(entry.Expression).Where(token => token.Kind != DaxTokenKind.Comment && token.Kind != DaxTokenKind.Whitespace).ToArray();
            foreach (var omitted in omittedCreates)
            {
                var used = tokens.Where((token, index) => Same(token.Value, omitted.Name) &&
                    (omitted.Kind == DaxScriptObjectKind.Function ? token.Kind == DaxTokenKind.Identifier && index + 1 < tokens.Length && tokens[index + 1].Text == "(" : token.Kind == DaxTokenKind.BracketIdentifier)).Any();
                if (used) issues.Add(Error("DAXSCRIPT_DEPENDENCY", "Select the new dependency " + omitted.DisplayName + " too, or create it first.", entry.DisplayName));
            }
        }
        if (entries.Length == 0) issues.Add(new AuthoringIssue("DAXSCRIPT_EMPTY", "Select at least one parsed object property.", AuthoringIssueSeverity.Information));
        return AuthoringPreview.Create(handler, "Apply DAX script", edits, issues);
    }

    public AuthoringPreview PreviewFunction(DaxFunctionEdit edit)
    {
        var issues = new List<AuthoringIssue>(); var edits = new List<AuthoringEdit>();
        ValidateFunction(edit.Name, edit.Expression, issues);
        var binding = edit.ObjectId == null ? null : Bindings().FirstOrDefault(item => item.Entry.ObjectKey == edit.ObjectId && item.Object is Function);
        if (edit.ObjectId != null && binding == null) issues.Add(Error("UDF_MISSING", "The function no longer exists. Refresh the function list."));
        if (binding == null && edit.ObjectId == null)
        {
            if (handler.Model.Functions.Any(function => Same(function.Name, edit.Name))) issues.Add(Error("UDF_DUPLICATE", "A function with this name already exists."));
            edits.Add(new AuthoringEdit(new AuthoringChange(edit.Name, "New function", "(absent)", edit.Expression + "\nDescription: " + edit.Description + "\nHidden: " + edit.IsHidden, "Create local function metadata; save/deploy remains separate."),
                () => { var function = handler.Model.AddFunction(edit.Name); function.Expression = edit.Expression; function.Description = edit.Description; function.IsHidden = edit.IsHidden; },
                () => handler.Model.Functions.Any(function => function.Name == edit.Name && function.Expression == edit.Expression && (function.Description ?? "") == edit.Description && function.IsHidden == edit.IsHidden)));
        }
        else if (binding?.Object is Function function)
        {
            if (function.Name != edit.Name) issues.Add(Error("UDF_RENAME", "Use Rename with callers first; its preview includes every affected expression."));
            AddExpressionEdit(edits, issues, binding, edit.Expression, "Update the function body without changing callers.");
            AddProperty(edits, binding.Entry.DisplayName, "Description", function.Description ?? "", edit.Description, () => function.Description = edit.Description, () => (function.Description ?? "") == edit.Description);
            AddProperty(edits, binding.Entry.DisplayName, "IsHidden", function.IsHidden.ToString(), edit.IsHidden.ToString(), () => function.IsHidden = edit.IsHidden, () => function.IsHidden == edit.IsHidden);
        }
        return AuthoringPreview.Create(handler, "Edit DAX function", edits, issues);
    }

    public AuthoringPreview PreviewFunctionRename(string objectId, string newName)
    {
        var binding = Bindings().FirstOrDefault(item => item.Entry.ObjectKey == objectId && item.Object is Function) ?? throw new ArgumentException("Select an existing function.", nameof(objectId));
        var function = (Function)binding.Object;
        var issues = new List<AuthoringIssue>(); var edits = new List<AuthoringEdit>(); ValidateFunction(newName, function.Expression, issues);
        if (handler.Model.Functions.Any(item => !ReferenceEquals(item, function) && Same(item.Name, newName))) issues.Add(Error("UDF_DUPLICATE", "A function with this name already exists."));
        var snapshot = DaxMetadataSnapshotProvider.Capture(handler);
        var symbolId = snapshot.Symbols.First(symbol => symbol.Kind == DaxSymbolKind.Function && Same(symbol.Name, function.Name)).Id;
        AddProperty(edits, function.Name, "Name", function.Name, newName,
            () => { var fixup = handler.Settings.AutoFixup; try { handler.Settings.AutoFixup = false; function.Name = newName; } finally { handler.Settings.AutoFixup = fixup; } },
            () => function.Name == newName);
        foreach (var caller in Bindings())
        {
            var text = caller.Get();
            var analysis = language.Analyze(Document(caller, text), snapshot);
            var spans = analysis.Tokens.Where(token => Same(token.Value, function.Name))
                .Where(token => language.FindDefinition(analysis, token.Span.Start)?.SymbolId == symbolId).Select(token => token.Span).Distinct().OrderByDescending(span => span.Start).ToArray();
            var after = text;
            foreach (var span in spans) after = after.Remove(span.Start, span.Length).Insert(span.Start, newName);
            AddExpressionEdit(edits, issues, caller, after, "Rename this resolved UDF reference. Strings, comments and other function names are preserved.");
        }
        return AuthoringPreview.Create(handler, "Rename function and callers", edits, issues);
    }

    public IReadOnlyList<DaxTextMatch> Search(DaxTextSearch search, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(search.Pattern) || search.Pattern.Length > 1024) throw new ArgumentException("Enter a search pattern from 1 to 1,024 characters.");
        var pattern = search.UseRegex ? search.Pattern : Regex.Escape(search.Pattern);
        if (search.WholeWord) pattern = @"(?<![\p{L}\p{N}_])(?:" + pattern + @")(?![\p{L}\p{N}_])";
        var regex = new Regex(pattern, RegexOptions.CultureInvariant | (search.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew(); var matches = new List<DaxTextMatch>();
        foreach (var binding in SearchBindings(search.IncludeDescriptions))
        {
            ct.ThrowIfCancellationRequested();
            if (watch.Elapsed > TimeSpan.FromSeconds(3)) throw new InvalidOperationException("Search exceeded three seconds. Narrow the pattern or object scope.");
            var text = binding.Get(); var found = regex.Matches(text).Cast<Match>().ToArray();
            var after = search.UseRegex ? regex.Replace(text, search.Replacement) : regex.Replace(text, _ => search.Replacement);
            foreach (var match in found)
            {
                if (matches.Count >= 10000) throw new InvalidOperationException("Search exceeds 10,000 matches. Narrow the pattern before replacing.");
                matches.Add(new DaxTextMatch(binding.Entry.ObjectKey, binding.Entry.DisplayName, binding.Property, match.Index, match.Length, match.Value, text, after));
            }
        }
        return matches;
    }

    public AuthoringPreview PreviewReplace(DaxTextSearch search, IEnumerable<string>? selectedObjectIds = null)
    {
        var selected = selectedObjectIds == null ? null : new HashSet<string>(selectedObjectIds, StringComparer.OrdinalIgnoreCase);
        var matches = Search(search).Where(match => selected == null || selected.Contains(match.ObjectId));
        var bindings = SearchBindings(search.IncludeDescriptions).ToDictionary(binding => binding.Entry.ObjectKey + ":" + binding.Property);
        var edits = new List<AuthoringEdit>(); var issues = new List<AuthoringIssue>(); var snapshot = DaxMetadataSnapshotProvider.Capture(handler);
        foreach (var group in matches.GroupBy(match => match.ObjectId + ":" + match.Property))
        {
            var match = group.First(); var binding = bindings[group.Key];
            if (binding.Property != "Description") ValidateExpression(binding.Entry with { Expression = match.After }, snapshot, issues);
            AddExpressionEdit(edits, issues, binding, match.After, $"Replace {group.Count()} reviewed match(es). Undo restores the complete batch.");
        }
        return AuthoringPreview.Create(handler, "Model-wide DAX find / replace", edits, issues);
    }

    public DaxExplanation Explain(string objectId, string? editedExpression = null)
    {
        var binding = Bindings().FirstOrDefault(item => item.Entry.ObjectKey == objectId && item.Property == "Expression") ?? throw new ArgumentException("Select a DAX object.");
        var snapshot = DaxMetadataSnapshotProvider.Capture(handler); var text = editedExpression ?? binding.Get();
        var analysis = language.Analyze(Document(binding, text), snapshot);
        var dependencies = analysis.Tokens.Select(token => language.FindDefinition(analysis, token.Span.Start))
            .Where(location => location != null && location.DocumentId == null).Select(location => location!.Name).Distinct().OrderBy(name => name).ToArray();
        var target = snapshot.Symbols.FirstOrDefault(symbol => symbol.Kind.ToString() == binding.Entry.Kind.ToString() && Same(symbol.Name, binding.Entry.Name) && Same(symbol.Table, binding.Entry.Table));
        var callers = new List<string>();
        if (target != null)
            foreach (var candidate in Bindings().Where(item => !ReferenceEquals(item.Object, binding.Object)))
            {
                var candidateAnalysis = language.Analyze(Document(candidate, candidate.Get()), snapshot);
                if (candidateAnalysis.Tokens.Any(token => language.FindDefinition(candidateAnalysis, token.Span.Start)?.SymbolId == target.Id)) callers.Add(candidate.Entry.DisplayName + " / " + candidate.Property);
            }
        var significant = analysis.Tokens.Where(token => token.Kind != DaxTokenKind.Comment && token.Kind != DaxTokenKind.Whitespace).ToArray();
        var variables = significant.Where((token, index) => token.Kind == DaxTokenKind.Keyword && Same(token.Text, "VAR") && index + 1 < significant.Length)
            .Select(token => significant[Array.IndexOf(significant, token) + 1].Value).Distinct().ToArray();
        var remaining = 250;
        return new DaxExplanation(dependencies, callers.Distinct().ToArray(), variables, text, analysis.Diagnostics)
        { DependencyTree = BuildTree(analysis, new HashSet<string>(StringComparer.Ordinal), 0) };

        IReadOnlyList<DaxDependencyNode> BuildTree(DaxAnalysis source, HashSet<string> path, int depth)
        {
            var nodes = new List<DaxDependencyNode>();
            var references = source.Tokens.Select(token => language.FindDefinition(source, token.Span.Start)).Where(location => location != null && location.DocumentId == null).Select(location => location!).GroupBy(location => location.SymbolId).Select(group => group.First());
            foreach (var reference in references)
            {
                if (remaining-- <= 0) { nodes.Add(new DaxDependencyNode("", "Additional dependencies omitted (250-node limit)", false, Array.Empty<DaxDependencyNode>())); break; }
                var cycle = path.Contains(reference.SymbolId); IReadOnlyList<DaxDependencyNode> children = Array.Empty<DaxDependencyNode>();
                var symbol = snapshot.Symbols.FirstOrDefault(item => item.Id == reference.SymbolId);
                if (!cycle && depth < 8 && !string.IsNullOrWhiteSpace(symbol?.Expression))
                {
                    var branch = new HashSet<string>(path, StringComparer.Ordinal) { reference.SymbolId };
                    var nested = language.Analyze(new DaxDocument(symbol!.Id, symbol.Expression!, Kind: symbol.Kind == DaxSymbolKind.Function ? DaxDocumentKind.Function : DaxDocumentKind.Expression, CurrentTable: symbol.Table), snapshot);
                    children = BuildTree(nested, branch, depth + 1);
                }
                else if (!cycle && depth >= 8 && !string.IsNullOrWhiteSpace(symbol?.Expression)) children = new[] { new DaxDependencyNode("", "Depth limit reached", false, Array.Empty<DaxDependencyNode>()) };
                nodes.Add(new DaxDependencyNode(reference.SymbolId, reference.Name + (cycle ? " (cycle)" : ""), cycle, children));
            }
            return nodes;
        }
    }

    public AuthoringPreview PreviewFormat(IEnumerable<string> selectedObjectIds)
    {
        var selected = new HashSet<string>(selectedObjectIds, StringComparer.OrdinalIgnoreCase); var edits = new List<AuthoringEdit>(); var issues = new List<AuthoringIssue>();
        var formatter = new LocalDaxFormatter();
        foreach (var binding in Bindings().Where(binding => selected.Contains(binding.Entry.ObjectKey)))
        {
            if (string.IsNullOrWhiteSpace(binding.Get())) continue;
            try { AddExpressionEdit(edits, issues, binding, formatter.Format(binding.Get()), "Conservative formatting preserves the DAX token stream."); }
            catch (FormatException ex) { issues.Add(Error("DAX_FORMAT", ex.Message, binding.Entry.DisplayName)); }
        }
        return AuthoringPreview.Create(handler, "Format selected DAX objects", edits, issues);
    }

    private void ValidateExpression(DaxScriptEntry entry, DaxMetadataSnapshot snapshot, List<AuthoringIssue> issues)
    {
        if (entry.Kind == DaxScriptObjectKind.Function) { ValidateFunction(entry.Name, entry.Expression, issues); return; }
        if (entry.Property == "FormatStringExpression" && entry.Kind == DaxScriptObjectKind.Measure && handler.CompatibilityLevel < 1601)
            issues.Add(Error("DAX_FORMAT_COMPAT", "Measure dynamic format expressions require compatibility level 1601 or later.", entry.DisplayName));
        if (string.IsNullOrWhiteSpace(entry.Expression) && entry.Property == "FormatStringExpression") return;
        var analysis = language.Analyze(new DaxDocument(entry.Key, entry.Expression, Kind: DaxDocumentKind.Expression, CurrentTable: entry.Table), snapshot);
        foreach (var diagnostic in analysis.Diagnostics) issues.Add(new AuthoringIssue(diagnostic.Id, diagnostic.Message,
            diagnostic.Severity == DaxDiagnosticSeverity.Error ? AuthoringIssueSeverity.Error : AuthoringIssueSeverity.Warning, entry.DisplayName));
    }
    private void ValidateFunction(string name, string expression, List<AuthoringIssue> issues)
    {
        if (handler.CompatibilityLevel < 1702) issues.Add(Error("UDF_COMPATIBILITY", "Function metadata requires compatibility level 1702 or later. This action does not upgrade the model."));
        if (!Regex.IsMatch(name ?? "", @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$") || DaxFunctionCatalog.BuiltIns.ContainsKey(name ?? "") || DaxTokenizer.Keywords.Any(keyword => Same(keyword, name)))
            issues.Add(Error("UDF_NAME", "Use a nonreserved function name beginning with a letter or underscore. Namespaces may use dots."));
        if (!DaxLanguageService.TryFunctionSignature(name ?? "", expression ?? "", out _)) issues.Add(Error("UDF_SYNTAX", "The function needs valid parameters and a nonempty body: (parameters) => expression."));
    }

    private void AddCreation(DaxScriptEntry entry, List<AuthoringEdit> edits, List<AuthoringIssue> issues)
    {
        var table = entry.Table == null ? null : handler.Model.Tables.FirstOrDefault(item => Same(item.Name, entry.Table));
        if (entry.Table != null && table == null) { issues.Add(Error("DAXSCRIPT_TABLE", "Create the containing table or calculation group first.", entry.DisplayName)); return; }
        Action create;
        switch (entry.Kind)
        {
            case DaxScriptObjectKind.Measure:
                if (handler.Model.AllMeasures.Any(item => Same(item.Name, entry.Name)) || table!.Columns.Any(item => Same(item.Name, entry.Name))) { issues.Add(Error("DAXSCRIPT_NAME", "A measure or column already uses this name.", entry.DisplayName)); return; }
                create = () => table!.AddMeasure(entry.Name, entry.Expression); break;
            case DaxScriptObjectKind.Column:
                if (table!.Columns.Any(item => Same(item.Name, entry.Name)) || table.Measures.Any(item => Same(item.Name, entry.Name))) { issues.Add(Error("DAXSCRIPT_KIND", "The existing object is not an editable calculated column.", entry.DisplayName)); return; }
                create = () => table.AddCalculatedColumn(entry.Name, entry.Expression); break;
            case DaxScriptObjectKind.Table:
                if (handler.Model.Tables.Any(item => Same(item.Name, entry.Name))) { issues.Add(Error("DAXSCRIPT_KIND", "The existing table is not a calculated table.", entry.DisplayName)); return; }
                create = () => handler.Model.AddCalculatedTable(entry.Name, entry.Expression);
                issues.Add(new AuthoringIssue("DAXSCRIPT_REFRESH", "The calculated table's schema/data require engine validation and refresh after reviewed save/deploy.", AuthoringIssueSeverity.Warning, entry.DisplayName)); break;
            case DaxScriptObjectKind.CalculationItem:
                if (table is not CalculationGroupTable group) { issues.Add(Error("DAXSCRIPT_GROUP", "Calculation items require an existing calculation group.", entry.DisplayName)); return; }
                create = () => group.AddCalculationItem(entry.Name, entry.Expression); break;
            case DaxScriptObjectKind.Function:
                create = () => { var function = handler.Model.AddFunction(entry.Name); function.Expression = entry.Expression; }; break;
            default: throw new ArgumentOutOfRangeException(nameof(entry.Kind));
        }
        edits.Add(new AuthoringEdit(new AuthoringChange(entry.DisplayName, "New " + entry.Kind, "(absent)", entry.Expression, "Create local metadata through TE2; remote save/deploy remains separate."), create,
            () => Bindings().Any(binding => Same(binding.Entry.Key, entry.Key) && binding.Get() == entry.Expression)));
    }

    private static void AddExpressionEdit(List<AuthoringEdit> edits, List<AuthoringIssue> issues, Binding binding, string after, string reason)
    {
        var before = binding.Get(); if (before == after) return;
        if (binding.Object is Measure measure && binding.Property == "FormatStringExpression" && !string.IsNullOrWhiteSpace(after) && !string.IsNullOrEmpty(measure.FormatString))
            AddProperty(edits, binding.Entry.DisplayName, "FormatString", measure.FormatString, "", () => measure.FormatString = "", () => string.IsNullOrEmpty(measure.FormatString));
        edits.Add(new AuthoringEdit(new AuthoringChange(binding.Entry.DisplayName, binding.Property, before, after, reason), () => binding.Set(after), () => binding.Get() == after));
    }
    private static void AddProperty(List<AuthoringEdit> edits, string path, string property, string before, string after, Action apply, Func<bool> validate)
    { if (before != after) edits.Add(new AuthoringEdit(new AuthoringChange(path, property, before, after, "Reviewed local metadata change."), apply, validate)); }

    private IEnumerable<Binding> Bindings()
    {
        foreach (var table in handler.Model.Tables)
        {
            if (table is CalculatedTable calculated) yield return BindingFor(calculated, DaxScriptObjectKind.Table, null, () => calculated.Expression, value => calculated.Expression = value);
            foreach (var column in table.Columns.OfType<CalculatedColumn>()) yield return BindingFor(column, DaxScriptObjectKind.Column, table.Name, () => column.Expression, value => column.Expression = value);
            foreach (var measure in table.Measures)
            {
                yield return BindingFor(measure, DaxScriptObjectKind.Measure, table.Name, () => measure.Expression, value => measure.Expression = value);
                if (handler.CompatibilityLevel >= 1601) yield return BindingFor(measure, DaxScriptObjectKind.Measure, table.Name, () => measure.FormatStringExpression, value => measure.FormatStringExpression = value, "FormatStringExpression");
            }
            if (table is CalculationGroupTable group)
                foreach (var item in group.CalculationItems)
                {
                    yield return BindingFor(item, DaxScriptObjectKind.CalculationItem, table.Name, () => item.Expression, value => item.Expression = value);
                    yield return BindingFor(item, DaxScriptObjectKind.CalculationItem, table.Name, () => item.FormatStringExpression, value => item.FormatStringExpression = value, "FormatStringExpression");
                }
        }
        foreach (var function in handler.Model.Functions) yield return BindingFor(function, DaxScriptObjectKind.Function, null, () => function.Expression, value => function.Expression = value);
    }
    private IEnumerable<Binding> SearchBindings(bool descriptions)
    {
        foreach (var binding in Bindings())
        {
            yield return binding;
            if (descriptions && binding.Property == "Expression" && binding.Object is IDescriptionObject described)
                yield return binding with { Property = "Description", Get = () => described.Description ?? "", Set = value => described.Description = value };
        }
    }
    private static Binding BindingFor(TabularNamedObject obj, DaxScriptObjectKind kind, string? table, Func<string?> get, Action<string> set, string property = "Expression") =>
        new(obj, new DaxScriptEntry(kind, table, obj.Name, get() ?? "", property), property, () => get() ?? "", set);
    private static string FormatExpression(TabularNamedObject obj) => obj switch { Measure measure => measure.FormatStringExpression ?? "", CalculationItem item => item.FormatStringExpression ?? "", _ => "" };
    private static DaxDocument Document(Binding binding, string text) => new(binding.Entry.Key, text, Kind: binding.Entry.Kind == DaxScriptObjectKind.Function ? DaxDocumentKind.Function : DaxDocumentKind.Expression, CurrentTable: binding.Entry.Table);
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static AuthoringIssue Error(string code, string message, string? path = null) => new(code, message, AuthoringIssueSeverity.Error, path);
    private sealed record Binding(TabularNamedObject Object, DaxScriptEntry Entry, string Property, Func<string> Get, Action<string> Set);
}
