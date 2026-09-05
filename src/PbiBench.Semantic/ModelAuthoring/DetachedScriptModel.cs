using PbiBench.Core.Automation;
using PbiBench.Dax.LanguageService;
using TOM = Microsoft.AnalysisServices.Tabular;

namespace PbiBench.Semantic.ModelAuthoring;

internal sealed class DetachedScriptObject
{
    public DetachedScriptObject(TOM.NamedMetadataObject obj, TOM.Table table, bool created = false)
    { Object = obj; Table = table; Id = created ? "new:" + Guid.NewGuid().ToString("N") : ScriptPreviewService.Key(Target); }
    public string Id { get; }
    public TOM.NamedMetadataObject Object { get; }
    public TOM.Table Table { get; }
    public bool Deleted { get; set; }
    public string Name => Object.Name;
    public string? TableName => Kind == RecipeScope.Table ? null : Table.Name;
    public RecipeScope Kind => Object is TOM.Measure ? RecipeScope.Measure : Object is TOM.Column ? RecipeScope.Column : RecipeScope.Table;
    public RecipeTarget Target => new(Kind, TableName, Name);
    public Dictionary<string, string> Values()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal) { ["Name"] = Name };
        switch (Object)
        {
            case TOM.Table table: result["Description"] = table.Description ?? ""; result["IsHidden"] = table.IsHidden ? "true" : "false"; break;
            case TOM.Measure measure:
                result["Description"] = measure.Description ?? ""; result["IsHidden"] = measure.IsHidden ? "true" : "false"; result["DisplayFolder"] = measure.DisplayFolder ?? ""; result["Expression"] = measure.Expression ?? "";
                result["FormatString"] = measure.FormatString ?? ""; result["FormatStringExpression"] = measure.FormatStringDefinition?.Expression ?? ""; result["Annotation:Format"] = System.Text.Json.JsonSerializer.Serialize(measure.Annotations.Find("Format")?.Value); break;
            case TOM.Column column:
                result["Description"] = column.Description ?? ""; result["IsHidden"] = column.IsHidden ? "true" : "false"; result["DisplayFolder"] = column.DisplayFolder ?? ""; result["FormatString"] = column.FormatString ?? ""; result["SummarizeBy"] = column.SummarizeBy.ToString();
                result["Annotation:Format"] = System.Text.Json.JsonSerializer.Serialize(column.Annotations.Find("Format")?.Value); if (column is TOM.CalculatedColumn calculated) result["Expression"] = calculated.Expression ?? ""; break;
        }
        return result;
    }
}
internal sealed record DetachedExpression(string Id, RecipeTarget Target, Func<string?> CurrentTable, Func<string> Get, Action<string> Set, bool Auxiliary, bool Function = false, string Property = "Expression");

internal sealed class DetachedScriptModel
{
    private readonly TOM.Database database;
    private readonly DaxLanguageService language = new();
    public List<DetachedScriptObject> Objects { get; } = new();
    public List<DetachedExpression> Expressions { get; } = new();
    public DetachedScriptModel(TOM.Database database)
    {
        this.database = database;
        foreach (var table in database.Model.Tables)
        {
            var tableObject = new DetachedScriptObject(table, table); Objects.Add(tableObject);
            Expressions.Add(new("aux:table-detail:" + table.Name, tableObject.Target, () => table.Name, () => table.DefaultDetailRowsDefinition?.Expression ?? "", value => { table.DefaultDetailRowsDefinition ??= new TOM.DetailRowsDefinition(); table.DefaultDetailRowsDefinition.Expression = value; }, true, Property: "DefaultDetailRowsExpression"));
            foreach (var column in table.Columns)
            {
                var item = new DetachedScriptObject(column, table); Objects.Add(item);
                if (column is TOM.CalculatedColumn calculated) Expressions.Add(new(item.Id + ":Expression", item.Target, () => table.Name, () => calculated.Expression ?? "", value => calculated.Expression = value, false));
            }
            foreach (var measure in table.Measures) AddMeasureObject(new DetachedScriptObject(measure, table));
            if (table.Partitions.FirstOrDefault()?.Source is TOM.CalculatedPartitionSource calculatedSource)
                Expressions.Add(new("aux:table:" + table.Name, tableObject.Target, () => null, () => calculatedSource.Expression ?? "", value => calculatedSource.Expression = value, true));
            if (table.CalculationGroup != null) foreach (var item in table.CalculationGroup.CalculationItems)
            {
                var target = new RecipeTarget(RecipeScope.Measure, table.Name, item.Name);
                Expressions.Add(new("aux:item:" + table.Name + ":" + item.Name, target, () => table.Name, () => item.Expression ?? "", value => item.Expression = value, true));
                Expressions.Add(new("aux:item-format:" + table.Name + ":" + item.Name, target, () => table.Name, () => item.FormatStringDefinition?.Expression ?? "", value => { item.FormatStringDefinition ??= new TOM.FormatStringDefinition(); item.FormatStringDefinition.Expression = value; }, true, Property: "FormatStringExpression"));
            }
        }
        foreach (var function in database.Model.Functions) Expressions.Add(new("aux:function:" + function.Name, new(RecipeScope.Measure, "Functions", function.Name), () => null, () => function.Expression ?? "", value => function.Expression = value, true, true));
        foreach (var role in database.Model.Roles) foreach (var permission in role.TablePermissions)
            Expressions.Add(new("aux:role:" + role.Name + ":" + permission.Table.Name, new(RecipeScope.Table, role.Name, permission.Table.Name), () => permission.Table.Name, () => permission.FilterExpression ?? "", value => permission.FilterExpression = value, true));
    }
    private void AddMeasureObject(DetachedScriptObject item)
    {
        Objects.Add(item); var measure = (TOM.Measure)item.Object;
        Expressions.Add(new("aux:measure-detail:" + item.Table.Name + ":" + measure.Name, item.Target, () => item.Table.Name, () => measure.DetailRowsDefinition?.Expression ?? "", value => { measure.DetailRowsDefinition ??= new TOM.DetailRowsDefinition(); measure.DetailRowsDefinition.Expression = value; }, true, Property: "DetailRowsExpression"));
        Expressions.Add(new(item.Id + ":Expression", item.Target, () => item.Table.Name, () => measure.Expression ?? "", value => measure.Expression = value, false));
        Expressions.Add(new(item.Id + ":FormatStringExpression", item.Target, () => item.Table.Name, () => measure.FormatStringDefinition?.Expression ?? "", value => { measure.FormatStringDefinition ??= new TOM.FormatStringDefinition(); measure.FormatStringDefinition.Expression = value; }, false));
    }
    public IEnumerable<DetachedScriptObject> Resolve(RecipeTarget target, HashSet<string> selected) => Objects.Where(item => !item.Deleted).Where(item => target.Scope switch
    {
        RecipeScope.AllMeasures => item.Kind == RecipeScope.Measure, RecipeScope.AllColumns => item.Kind == RecipeScope.Column, RecipeScope.AllTables => item.Kind == RecipeScope.Table,
        RecipeScope.SelectedMeasures => item.Kind == RecipeScope.Measure && selected.Contains(item.Id), RecipeScope.SelectedColumns => item.Kind == RecipeScope.Column && selected.Contains(item.Id), RecipeScope.SelectedTables => item.Kind == RecipeScope.Table && selected.Contains(item.Id),
        RecipeScope.Measure or RecipeScope.Column => item.Kind == target.Scope && Same(item.TableName, target.Table) && Same(item.Name, target.Name),
        RecipeScope.Table => item.Kind == RecipeScope.Table && Same(item.Name, target.Name), _ => false
    });
    public void Set(DetachedScriptObject item, string property, string value, CancellationToken ct)
    {
        if (!item.Values().ContainsKey(property)) throw new InvalidOperationException("The property " + property + " is unsupported on " + item.Kind + ".");
        if (value.Length > 262144) throw new InvalidOperationException("Property values are limited to 256 KiB.");
        if (property == "Name") { Rename(item, value, ct); return; }
        if (property == "Expression" || property == "FormatStringExpression") ValidateExpression(value, item.TableName, property == "FormatStringExpression");
        if (property == "FormatStringExpression" && database.CompatibilityLevel < 1601) throw new InvalidOperationException("Measure dynamic formats require compatibility 1601; this script does not upgrade the model.");
        switch (item.Object)
        {
            case TOM.Table table:
                if (property == "Description") table.Description = value; else if (property == "IsHidden") table.IsHidden = Boolean(value); break;
            case TOM.Measure measure:
                switch (property)
                {
                    case "Description": measure.Description = value; break; case "IsHidden": measure.IsHidden = Boolean(value); break; case "DisplayFolder": measure.DisplayFolder = value; break;
                    case "Expression": measure.Expression = value; break;
                    case "FormatString": if ((measure.FormatString ?? "") != value) { measure.FormatString = value; if (measure.Annotations.ContainsName("Format")) measure.Annotations.Remove("Format"); } if (!string.IsNullOrWhiteSpace(value)) measure.FormatStringDefinition = null; break;
                    case "FormatStringExpression": if (string.IsNullOrWhiteSpace(value)) measure.FormatStringDefinition = null; else { if (!string.IsNullOrEmpty(measure.FormatString)) { measure.FormatString = ""; if (measure.Annotations.ContainsName("Format")) measure.Annotations.Remove("Format"); } measure.FormatStringDefinition ??= new TOM.FormatStringDefinition(); measure.FormatStringDefinition.Expression = value; } break;
                }
                break;
            case TOM.Column column:
                switch (property)
                {
                    case "Description": column.Description = value; break; case "IsHidden": column.IsHidden = Boolean(value); break; case "DisplayFolder": column.DisplayFolder = value; break; case "FormatString": if ((column.FormatString ?? "") != value) { column.FormatString = value; if (column.Annotations.ContainsName("Format")) column.Annotations.Remove("Format"); } break;
                    case "Expression": ((TOM.CalculatedColumn)column).Expression = value; break;
                    case "SummarizeBy": if (!Enum.TryParse<TOM.AggregateFunction>(value, out var aggregate) || !Enum.IsDefined(typeof(TOM.AggregateFunction), aggregate)) throw new InvalidOperationException("Invalid aggregation value."); column.SummarizeBy = aggregate; break;
                }
                break;
        }
    }
    public void CreateMeasure(DetachedScriptObject tableObject, string name, string expression, string folder)
    {
        ValidateName(name); EnsureAvailable(RecipeScope.Measure, tableObject.Table, name, null); ValidateExpression(expression, tableObject.Name, false);
        var measure = new TOM.Measure { Name = name, Expression = expression, DisplayFolder = folder }; tableObject.Table.Measures.Add(measure); AddMeasureObject(new DetachedScriptObject(measure, tableObject.Table, true));
    }
    public void DeleteMeasure(DetachedScriptObject item, CancellationToken ct)
    {
        var metadata = Metadata();
        foreach (var expression in Expressions.Where(expression => !expression.Id.StartsWith(item.Id + ":", StringComparison.Ordinal)))
        {
            ct.ThrowIfCancellationRequested(); var analysis = Analyze(expression, metadata);
            if (analysis.Tokens.Any(token => language.FindDefinition(analysis, token.Span.Start)?.SymbolId == item.Id)) throw new InvalidOperationException("The measure has a DAX caller: " + ScriptPreviewService.Display(expression.Target) + ". Update or delete callers first.");
        }
        item.Table.Measures.Remove((TOM.Measure)item.Object); item.Deleted = true; Expressions.RemoveAll(expression => expression.Id.StartsWith(item.Id + ":", StringComparison.Ordinal));
    }
    private void Rename(DetachedScriptObject item, string name, CancellationToken ct)
    {
        ValidateName(name); EnsureAvailable(item.Kind, item.Table, name, item); if (item.Name == name) return;
        var metadata = Metadata();
        foreach (var expression in Expressions)
        {
            ct.ThrowIfCancellationRequested(); var before = expression.Get(); var analysis = Analyze(expression, metadata); var after = before;
            var spans = analysis.Tokens.Where(token => token.Kind is DaxTokenKind.Identifier or DaxTokenKind.QuotedIdentifier or DaxTokenKind.BracketIdentifier or DaxTokenKind.Keyword)
                .Where(token => language.FindDefinition(analysis, token.Span.Start)?.SymbolId == item.Id).Select(token => token.Span).Distinct().OrderByDescending(span => span.Start).ToArray();
            foreach (var span in spans) after = after.Remove(span.Start, span.Length).Insert(span.Start, item.Kind == RecipeScope.Table ? DaxSymbol.QuoteTable(name) : DaxSymbol.QuoteMember(name));
            if (before != after) expression.Set(after);
        }
        item.Object.Name = name;
    }
    private void EnsureAvailable(RecipeScope kind, TOM.Table table, string name, DetachedScriptObject? except)
    {
        if (Objects.Any(other => !other.Deleted && !ReferenceEquals(other, except) && Same(other.Name, name) && (kind == RecipeScope.Table ? other.Kind == RecipeScope.Table : other.Kind == RecipeScope.Measure || other.Kind == RecipeScope.Column && ReferenceEquals(other.Table, table)))) throw new InvalidOperationException("The name already exists in this model scope: " + name);
    }
    private DaxMetadataSnapshot Metadata()
    {
        var symbols = Objects.Where(item => !item.Deleted).Select(item => new DaxSymbol(item.Id, item.Name, item.Kind == RecipeScope.Table ? DaxSymbolKind.Table : item.Kind == RecipeScope.Column ? DaxSymbolKind.Column : DaxSymbolKind.Measure, item.TableName,
            item.Object is TOM.Measure measure ? measure.Expression : item.Object is TOM.CalculatedColumn calculated ? calculated.Expression : null)).ToList();
        symbols.AddRange(database.Model.Functions.Select(function => new DaxSymbol("aux:function:" + function.Name, function.Name, DaxSymbolKind.Function, Expression: function.Expression)));
        return new DaxMetadataSnapshot(symbols, database.CompatibilityLevel);
    }
    private DaxAnalysis Analyze(DetachedExpression expression, DaxMetadataSnapshot metadata) => language.Analyze(new DaxDocument(expression.Id, expression.Get(), Kind: expression.Function ? DaxDocumentKind.Function : DaxDocumentKind.Expression, CurrentTable: expression.CurrentTable()), metadata);
    private void ValidateExpression(string expression, string? table, bool emptyAllowed)
    {
        if (string.IsNullOrWhiteSpace(expression)) { if (emptyAllowed) return; throw new InvalidOperationException("DAX expressions cannot be empty."); }
        var analysis = language.Analyze(new DaxDocument("script-expression", expression, Kind: DaxDocumentKind.Expression, CurrentTable: table), Metadata());
        var errors = analysis.Diagnostics.Where(diagnostic => diagnostic.Severity == DaxDiagnosticSeverity.Error).ToArray(); if (errors.Length > 0) throw new InvalidOperationException(string.Join("; ", errors.Select(error => error.Message)));
    }
    private static void ValidateName(string name) { if (string.IsNullOrWhiteSpace(name) || name.Length > 512 || name.Any(char.IsControl)) throw new InvalidOperationException("Enter a nonblank model name without control characters (maximum 512)."); }
    private static bool Boolean(string value) => value is "true" or "false" ? value == "true" : throw new InvalidOperationException("Boolean model properties require true or false.");
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
