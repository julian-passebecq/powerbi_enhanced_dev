using System.Globalization;
using System.Text.Json;
using PbiBench.Core.Automation;
using PbiBench.Core.Commands;
using PbiBench.Core.Quality;
using PbiBench.Core.Queries;
using PbiBench.Core.Workspaces;
using PbiBench.Dax.LanguageService;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using PbiBench.Semantic.Workspaces;
using TabularEditor.TOMWrapper;

namespace PbiBench.Automation.Commands;

/// <summary>One shared command surface for the owner-thread GUI model and the STA CLI host.</summary>
public sealed class SemanticCommandService
{
    private readonly Func<TabularModelHandler?> currentHandler;
    private readonly Func<CommandTarget, string?> connectionString;
    private readonly IDaxQueryService queries;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    public SemanticCommandService(Func<TabularModelHandler?> currentHandler, Func<CommandTarget, string?>? connectionString = null, IDaxQueryService? queries = null)
    { this.currentHandler = currentHandler ?? throw new ArgumentNullException(nameof(currentHandler)); this.connectionString = connectionString ?? (_ => null); this.queries = queries ?? new TomDaxQueryService(); }
    private TabularModelHandler Model()
    { if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Native model commands must run on their owning thread."); return currentHandler() ?? throw new InvalidOperationException("This command requires a loaded model."); }
    private static CommandRequest Freeze(CommandRequest request) => CommandJson.ParseRequest(CommandJson.Serialize(request));
    public async Task<PreparedCommand> PrepareAsync(CommandRequest request, CancellationToken ct = default)
    {
        request = Freeze(request); ct.ThrowIfCancellationRequested();
        if (request.Kind is CommandKind.Refresh or CommandKind.Deploy) return await ConnectedCommandOperations.PrepareAsync(request, connectionString, ct);
        if (request.Kind != CommandKind.Set && request.Kind != CommandKind.Script && request.Kind != CommandKind.Action) throw new ArgumentException("This is a read command; use ExecuteReadAsync.");
        var handler = Model(); var selection = Resolve(handler, request.Selection); var original = new SemanticModelService(handler).Fingerprint();
        var source = request.Target.ModelPath == null ? null : CommandModelFiles.Read(request.Target.ModelPath, ct);
        if (source != null && (handler.IsConnected || string.IsNullOrWhiteSpace(handler.Source) || !Path.GetFullPath(handler.Source).TrimEnd(Path.DirectorySeparatorChar).Equals(source.LoadPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("The command source must match the loaded model owner. Reload the requested model before reviewing changes.");
        if (source != null && handler.HasUnsavedChanges) throw new InvalidOperationException("Source-bound commands require a freshly loaded model without unsaved edits. Save or reload before preparing this disk command.");
        var beforeHash = source?.ContentHash ?? original; var output = CommandModelFiles.PrepareOutput(request.OutputPath);
        var target = output?.Path ?? (handler.IsConnected ? "Loaded model " + handler.Database.ID : "Loaded local model " + handler.Database.ID);
        beforeHash = CommandJson.Hash(new { source = beforeHash, output = output?.BeforeHash });
        AuthoringPreview? native = null; ChangePreview? gallery = null; AutomationService? automation = null;
        if (request.Kind == CommandKind.Action && request.Action != null)
        {
            if (!Enum.TryParse<AutomationActionId>(request.Action, true, out var action) || !Enum.IsDefined(typeof(AutomationActionId), action)) throw new ArgumentException("Unknown typed gallery action.");
            automation = new AutomationService(handler); gallery = automation.Preview(action, selection, Options(request.ActionOptions));
        }
        else if (request.Kind == CommandKind.Script && request.ScriptLanguage == "Dax")
        { if (string.IsNullOrWhiteSpace(request.Script)) throw new ArgumentException("Supply the DAX model script."); native = new DaxAuthoringService(handler).PreviewScript(request.Script!); }
        else
        {
            var script = new ScriptPreviewService(handler);
            PreparedScriptPreview prepared;
            if (request.Kind == CommandKind.Set)
            {
                if (selection.Count == 0 || request.Property == null || request.Value == null || !ActionRecipeRules.Properties.Contains(request.Property)) throw new ArgumentException("Set requires explicit objects, an allowed property and a value.");
                var steps = selection.Select(item => new RecipeStep(RecipeTargetFor(item), RecipeOperation.SetProperty, request.Property, RecipeValue.Literal(request.Value))).ToArray();
                prepared = script.PrepareRecipe(new ActionRecipe("Typed property edit", steps), selection);
            }
            else if (request.Kind == CommandKind.Action && request.Recipe != null) prepared = script.PrepareRecipe(request.Recipe, selection);
            else if (request.Kind == CommandKind.Script && !string.IsNullOrWhiteSpace(request.Script)) prepared = script.PrepareScript(request.Script!, selection);
            else throw new ArgumentException("Supply a typed recipe, gallery action or safe script.");
            var computed = await Task.Run(() => script.Compute(prepared, ct), ct);
            if (!ReferenceEquals(Model(), handler)) throw new InvalidOperationException("The model owner changed during preview."); native = script.Materialize(computed);
        }
        var rows = native != null ? native.Changes.Select(change => new CommandChange(change.ObjectPath, change.Property, change.Before, change.After, change.Reason)).ToArray()
            : gallery!.Changes.Select(change => new CommandChange(change.ObjectPath, change.Property, change.Before, change.After, change.Reason)).ToArray();
        var issues = native != null ? native.Issues.Select(issue => new CommandDiagnostic(issue.Code, issue.Message, issue.Severity.ToString(), issue.ObjectPath)).ToArray()
            : gallery!.Notices.Select(message => new CommandDiagnostic("ACTION_NOTICE", message, "Information")).ToArray();
        if (output != null) rows = rows.Concat(new[] { new CommandChange(output.Path, "Persist BIM", output.BeforeHash, "Serialize the approved edited model", "A recovery copy is kept before replacing an existing destination.") }).ToArray();
        var review = CommandJson.Review(request, target, beforeHash, false, native?.CanApply ?? gallery!.CanApply, rows, issues);
        return new PreparedCommand(request, review, async (_, token) =>
        {
            token.ThrowIfCancellationRequested(); if (!ReferenceEquals(Model(), handler) || new SemanticModelService(handler).Fingerprint() != original) throw new InvalidOperationException("The model changed after review.");
            if (source != null && CommandModelFiles.Read(request.Target.ModelPath!, token).ContentHash != source.ContentHash) throw new InvalidOperationException("The source files changed after review.");
            if (output != null) CommandModelFiles.VerifyOutput(output);
            var undoSteps = handler.UndoManager.UndoSteps;
            if (native != null) native.Apply(handler); else automation!.Apply(gallery!);
            var appliedFingerprint = new SemanticModelService(handler).Fingerprint();
            try
            {
                string? backup = null;
                if (output != null)
                {
                    var json = Microsoft.AnalysisServices.Tabular.JsonSerializer.SerializeDatabase(handler.Database);
                    backup = await Task.Run(() => CommandModelFiles.WriteOutput(output, json, token), token);
                }
                return CommandResult.Success(request.Kind, new { changedProperties = rows.Length, outputPath = output?.Path, backupPath = backup, persisted = output != null }, output == null ? "Approved changes applied to the loaded model. Save remains a separate reviewed operation." : "Approved model changes written to the reviewed BIM destination.");
            }
            catch
            {
                if (ReferenceEquals(Model(), handler) && handler.UndoManager.UndoSteps == undoSteps + 1 && new SemanticModelService(handler).Fingerprint() == appliedFingerprint) handler.UndoManager.Undo();
                throw;
            }
        });
    }
    public Task<CommandResult> ApplyAsync(PreparedCommand prepared, string approvalHash, string actor, CancellationToken ct = default)
        => prepared.ApplyAsync(approvalHash, actor, ct);

    public async Task<CommandResult> ExecuteReadAsync(CommandRequest request, CancellationToken ct = default)
    {
        request = Freeze(request); ct.ThrowIfCancellationRequested();
        if (request.Kind == CommandKind.Query)
        {
            var result = await queries.ExecuteAsync(Query(request, request.Query ?? throw new ArgumentException("A DAX query is required.")), ct);
            return CommandResult.Success(request.Kind, new { result.Id, result.Server, result.Database, result.StartedAt, elapsedMilliseconds = result.Elapsed.TotalMilliseconds, result.DocumentRevision,
                result.Warnings, resultSets = result.Results.Select(set => new { set.Index, set.Name, set.Columns, set.IsTruncated, rows = set.Rows.Select(row => row.Select(SemanticValue.From).ToArray()).ToArray() }).ToArray() });
        }
        if (request.Kind == CommandKind.Test)
        {
            if (request.Tests == null || request.Tests.Tests == null || request.Tests.Tests.Count == 0) throw new ArgumentException("A nonempty semantic test artifact is required.");
            SemanticTestArtifactStore.Deserialize(SemanticTestArtifactStore.Serialize(request.Tests));
            var service = new SemanticTestService(queries); var results = new List<SemanticTestResult>();
            foreach (var test in request.Tests.Tests) { ct.ThrowIfCancellationRequested(); results.Add(await service.RunAsync(test, Query(request, test.Query), ct)); }
            var report = new SemanticTestReport(1, results); return new(1, request.Kind, report.Passed ? CommandStatus.Succeeded : CommandStatus.Failed, report.Passed ? 0 : 3, report.Passed ? "All engine assertions passed." : "One or more engine assertions failed or could not run.", CommandJson.Element(report));
        }
        if (request.Kind == CommandKind.Diff)
        {
            if (request.Target.ModelPath == null || request.ComparePath == null) throw new ArgumentException("Diff requires a source model and a comparison path.");
            var left = CommandModelFiles.Read(request.Target.ModelPath, ct); var right = CommandModelFiles.Read(request.ComparePath, ct);
            return CommandResult.Success(request.Kind, new { beforeHash = left.Snapshot.Hash, afterHash = right.Snapshot.Hash, changes = WorkspaceSemanticDiff.Between(left.Snapshot, right.Snapshot).Select(change => new CommandChange(change.ObjectPath, change.Property, WorkspaceSemanticDiff.DisplayValue(change.Property, change.Baseline), WorkspaceSemanticDiff.DisplayValue(change.Property, change.Disk), change.Kind.ToString())).ToArray() });
        }
        var handler = Model(); var objects = Objects(handler).ToArray();
        if (request.Kind == CommandKind.Inspect) return CommandResult.Success(request.Kind, new { model = handler.Database.Name, databaseId = handler.Database.ID, compatibilityLevel = handler.CompatibilityLevel, source = request.Target.ModelPath ?? "Loaded model", hasUnsavedChanges = handler.HasUnsavedChanges, counts = objects.GroupBy(Kind).ToDictionary(group => group.Key, group => group.Count()), objects = objects.Select(Project).ToArray() });
        if (request.Kind == CommandKind.List) return CommandResult.Success(request.Kind, objects.Where(item => request.ObjectKind == null || Kind(item).Equals(request.ObjectKind.TrimEnd('s'), StringComparison.OrdinalIgnoreCase)).Select(Project).ToArray());
        if (request.Kind == CommandKind.Get)
        {
            var selected = Resolve(handler, request.Selection); if (selected.Count == 0) throw new ArgumentException("Get requires an explicit object.");
            return CommandResult.Success(request.Kind, selected.Select(item => request.Property == null ? (object)Project(item) : new { kind = Kind(item), item.Name, property = request.Property, value = Property(item, request.Property) }).ToArray());
        }
        if (request.Kind == CommandKind.Bpa || request.Kind == CommandKind.Validate)
        {
            var profile = request.BpaProfilePath == null ? new BpaRuleProfile() : await BpaRuleProfile.LoadAsync(request.BpaProfilePath, ct);
            if (!ReferenceEquals(Model(), handler)) throw new InvalidOperationException("The model changed while loading the BPA profile.");
            var findings = new BpaService(handler, new AutomationService(handler)).Scan(profile);
            var diagnostics = findings.Select(finding => new CommandDiagnostic(finding.RuleId, finding.Reason, finding.Severity.ToString(), finding.ObjectPath)).ToList();
            if (request.Kind == CommandKind.Validate)
            {
                var codec = new TmdlWorkspaceCodec(); var snapshot = codec.CaptureLoaded(handler); var serialized = codec.Serialize(snapshot, false, ct);
                var roundTrip = codec.Parse(new PbiBench.Workspace.WorkspaceDiskSnapshot("validation", serialized), ct);
                if (snapshot.Hash != roundTrip.Hash) diagnostics.Add(new("TMDL_ROUNDTRIP", "TMDL round-trip changed semantic metadata."));
                var metadata = DaxMetadataSnapshotProvider.Capture(handler); var language = new DaxLanguageService();
                foreach (var symbol in metadata.Symbols.Where(symbol => !string.IsNullOrWhiteSpace(symbol.Expression)))
                {
                    ct.ThrowIfCancellationRequested(); var analysis = language.Analyze(new DaxDocument(symbol.Id, symbol.Expression!, Kind: symbol.Kind == DaxSymbolKind.Function ? DaxDocumentKind.Function : DaxDocumentKind.Expression, CurrentTable: symbol.Table), metadata, ct);
                    diagnostics.AddRange(analysis.Diagnostics.Select(issue => new CommandDiagnostic(issue.Id, issue.Message, issue.Severity.ToString(), symbol.QualifiedName)));
                }
            }
            var failed = diagnostics.Any(item => Fails(item.Severity, request.FailOn));
            return new(1, request.Kind, failed ? CommandStatus.Failed : CommandStatus.Succeeded, failed ? 3 : 0,
                request.Kind == CommandKind.Validate ? "Offline TOM/TMDL, DAX and BPA validation completed; engine validation was not run." : "Versioned PbiBench BPA scan completed.",
                CommandJson.Element(new { rulePacks = BpaRulePacks.BuiltIn, findings = findings.Select(finding => new { finding.RuleId, finding.Rule, finding.Severity, finding.ObjectPath, finding.Reason, finding.Risk, finding.Pack, finding.Version, canPreviewFix = finding.FixPreview?.CanApply == true }).ToArray() }), Diagnostics: diagnostics);
        }
        throw new ArgumentException("This command requires PrepareAsync and explicit review.");
    }
    private QueryRequest Query(CommandRequest request, string text) => new(request.Target.Server ?? throw new ArgumentException("An explicit server is required."), request.Target.Database ?? throw new ArgumentException("An explicit database is required."), text, request.RowLimit, request.TimeoutSeconds) { ConnectionString = connectionString(request.Target) };
    private static bool Fails(string severity, string threshold) => threshold != "None" && Rank(severity) >= Rank(threshold);
    private static int Rank(string severity) => severity == "Error" ? 3 : severity == "Warning" ? 2 : 1;
    private static RecipeTarget RecipeTargetFor(TabularNamedObject item) => item is Measure ? new(RecipeScope.Measure, ((Measure)item).Table.Name, item.Name) : item is Column ? new(RecipeScope.Column, ((Column)item).Table.Name, item.Name) : item is Table ? new(RecipeScope.Table, Name: item.Name) : throw new ArgumentException("Typed property edits support tables, columns and measures. Use dedicated authoring tools for other object kinds.");
    private static AutomationOptions Options(IReadOnlyDictionary<string, string>? input)
    {
        var options = new AutomationOptions(); if (input == null) return options;
        foreach (var pair in input) switch (pair.Key) { case "measureTableName": options.MeasureTableName = pair.Value; break; case "displayFolder": options.DisplayFolder = pair.Value; break; case "measurePrefix": options.MeasurePrefix = pair.Value; break; case "descriptionTemplate": options.DescriptionTemplate = pair.Value; break; case "allMeasuresWhenSelectionEmpty": options.AllMeasuresWhenSelectionEmpty = bool.Parse(pair.Value); break; default: throw new ArgumentException("Unknown typed action option: " + pair.Key); }
        return options;
    }
    private static IEnumerable<TabularNamedObject> Objects(TabularModelHandler handler) => handler.Model.Tables.SelectMany(table => new TabularNamedObject[] { table }.Concat(table.Columns).Concat(table.Measures).Concat(table.Partitions).Concat(table.Hierarchies).Concat(table is CalculationGroupTable group ? group.CalculationItems : Enumerable.Empty<CalculationItem>())).Concat(handler.Model.Relationships).Concat(handler.Model.Functions).Concat(handler.Model.Perspectives).Concat(handler.Model.Cultures).Concat(handler.Model.Roles);
    private static string Kind(TabularNamedObject item) => item switch { Table => "Table", Column => "Column", Measure => "Measure", CalculationItem => "CalculationItem", Function => "Function", Relationship => "Relationship", Partition => "Partition", Hierarchy => "Hierarchy", Perspective => "Perspective", Culture => "Culture", _ => item.ObjectTypeName };
    private static IReadOnlyList<TabularNamedObject> Resolve(TabularModelHandler handler, IReadOnlyList<CommandObject> selectors) => selectors.Select(selector => Objects(handler).SingleOrDefault(item => Kind(item).Equals(selector.Kind.TrimEnd('s'), StringComparison.OrdinalIgnoreCase) && item.Name.Equals(selector.Name, StringComparison.OrdinalIgnoreCase) && (item is ITabularTableObject child ? child.Table.Name.Equals(selector.Table, StringComparison.OrdinalIgnoreCase) : selector.Table == null)) ?? throw new ArgumentException("The selected model object does not exist: " + selector.Kind + " / " + selector.Name)).Distinct().ToArray();
    private static object Project(TabularNamedObject item) => new { kind = Kind(item), item.Name, table = (item as ITabularTableObject)?.Table.Name, path = SemanticModelService.ObjectPath(item), description = (item as IDescriptionObject)?.Description, expression = item is Partition ? null : (item as IExpressionObject)?.Expression, hidden = (item as IHideableObject)?.IsHidden };
    private static object? Property(TabularNamedObject item, string property) => property switch
    {
        "Name" => item.Name, "Description" => (item as IDescriptionObject)?.Description, "Expression" => item is Partition ? throw new ArgumentException("Source partition expressions are not part of safe get.") : (item as IExpressionObject)?.Expression,
        "IsHidden" => (item as IHideableObject)?.IsHidden, "DisplayFolder" => item is Measure measure ? measure.DisplayFolder : (item as Column)?.DisplayFolder,
        "FormatString" => (item as Measure)?.FormatString, "DataType" => item is Column column ? column.DataType.ToString() : (item as Measure)?.DataType.ToString(),
        "SummarizeBy" => (item as Column)?.SummarizeBy.ToString(), _ => throw new ArgumentException("This property is not on the safe get allowlist.")
    };
}
