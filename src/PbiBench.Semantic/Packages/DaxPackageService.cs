using PbiBench.Core.Packages;
using PbiBench.Dax.LanguageService;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Packages;

/// <summary>Local DAX-only package ownership and lock metadata use the existing authoring/undo boundary.</summary>
public sealed class DaxPackageService
{
    private readonly TabularModelHandler handler;
    public DaxPackageService(TabularModelHandler handler) => this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
    public DaxPackageLock CaptureLock() => DaxPackageLock.Parse(handler.Model.GetAnnotation(DaxPackageLock.AnnotationName));
    public AuthoringPreview PreviewInstall(LocalDaxPackage package)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));
        var manifest = package.Manifest; var before = CaptureLock(); var prior = before.Packages.FirstOrDefault(item => Same(item.Id, manifest.Id));
        var issues = new List<AuthoringIssue>(); var edits = new List<AuthoringEdit>(); var authoring = new DaxAuthoringService(handler);
        foreach (var error in before.ValidateGraph()) Error("PACKAGE_LOCK", error);
        if (handler.CompatibilityLevel < 1702) Error("PACKAGE_COMPATIBILITY", "DAX UDF packages require compatibility level 1702 or later; this preview does not upgrade the model.");
        foreach (var error in before.ValidateDependencies(manifest)) Error("PACKAGE_DEPENDENCY", error);
        foreach (var dependency in manifest.Dependencies) { var installed = before.Packages.FirstOrDefault(item => Same(item.Id, dependency.Id)); if (installed != null) ValidateOwned(installed, issues); }
        if (prior != null && new Version(manifest.Version) < new Version(prior.Version)) Error("PACKAGE_DOWNGRADE", "Downgrades are not supported by this prototype. Review and remove the installed package first.");
        if (prior != null && prior.Version == manifest.Version && prior.ContentHash != package.ContentHash) Error("PACKAGE_REPUBLISHED", "This installed version has a different package hash. Publish a new version instead of replacing the locked content.");
        if (prior != null) ValidateOwned(prior, issues);
        foreach (var dependent in before.Packages.Where(item => !Same(item.Id, manifest.Id)).SelectMany(item => item.Dependencies.Select(dependency => new { item.Id, Dependency = dependency })))
            if (Same(dependent.Dependency.Id, manifest.Id) && (dependent.Dependency.Version != manifest.Version || !Same(dependent.Dependency.Sha256, package.ContentHash))) Error("PACKAGE_PINNED", dependent.Id + " pins the installed version/hash. Remove or update that dependent explicitly before changing this package.");
        var allNames = new HashSet<string>(manifest.Functions.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
        var knownFunctions = authoring.GetFunctions();
        var snapshot = DaxMetadataSnapshotProvider.Capture(handler);
        var metadata = new DaxMetadataSnapshot(snapshot.Symbols.Where(item => item.Kind != DaxSymbolKind.Function || !allNames.Contains(item.Name)).Concat(manifest.Functions.Select(item => new DaxSymbol("package:" + item.Name, item.Name, DaxSymbolKind.Function, Expression: package.Functions[item.Name], Description: item.Description))), handler.CompatibilityLevel);
        foreach (var item in manifest.Functions)
        {
            var function = handler.Model.Functions.FirstOrDefault(found => Same(found.Name, item.Name)); var expression = package.Functions[item.Name];
            var owned = prior?.Functions.Any(found => Same(found.Name, item.Name)) == true;
            if (function != null && !owned) { Error("PACKAGE_COLLISION", "Existing function " + item.Name + " is not owned by this package. It will not be overwritten."); continue; }
            var authoringId = function == null ? null : knownFunctions.First(found => Same(found.Name, item.Name)).Id;
            issues.AddRange(authoring.PreviewFunction(new(authoringId, item.Name, expression, item.Description, item.IsHidden)).Issues);
            var analysis = new DaxLanguageService().Analyze(new(item.Name, expression, Kind: DaxDocumentKind.Function), metadata);
            issues.AddRange(analysis.Diagnostics.Select(itemDiagnostic => new AuthoringIssue(itemDiagnostic.Id, itemDiagnostic.Message, itemDiagnostic.Severity == DaxDiagnosticSeverity.Error ? AuthoringIssueSeverity.Error : AuthoringIssueSeverity.Warning, item.Name)));
            var significant = analysis.Tokens.Where(token => token.Kind is not (DaxTokenKind.Comment or DaxTokenKind.Whitespace)).ToArray();
            if (significant.Any(token => token.Kind != DaxTokenKind.String && (token.Text == ";" || token.Kind == DaxTokenKind.Keyword && (Same(token.Value, "EVALUATE") || Same(token.Value, "DEFINE")))))
                Error("PACKAGE_EXPRESSION", item.Name + " must contain one UDF expression, not a query, script or statement list.");
            for (var index = 0; index + 1 < significant.Length; index++)
                if (significant[index].Kind == DaxTokenKind.Identifier && significant[index + 1].Text == "(")
                {
                    if (!DaxFunctionCatalog.BuiltIns.ContainsKey(significant[index].Value) && !metadata.Symbols.Any(symbol => symbol.Kind == DaxSymbolKind.Function && Same(symbol.Name, significant[index].Value)))
                        Error("PACKAGE_UNKNOWN_CALL", item.Name + " calls an unknown function: " + significant[index].Value + ". The bounded prototype requires catalog or captured model/package functions.");
                    var provider = before.Packages.FirstOrDefault(locked => !Same(locked.Id, manifest.Id) && locked.Functions.Any(ownedFunction => Same(ownedFunction.Name, significant[index].Value)));
                    if (provider != null && !manifest.Dependencies.Any(dependency => Same(dependency.Id, provider.Id))) Error("PACKAGE_UNDECLARED", item.Name + " calls " + provider.Id + " without a manifest dependency pin.");
                }
            var old = function == null ? "(absent)" : Describe(function.Expression, function.Description ?? "", function.IsHidden); var next = Describe(expression, item.Description, item.IsHidden); if (old == next) continue;
            edits.Add(new(new(item.Name, function == null ? "Install function" : "Update function", old, next, "Reviewed DAX metadata only. Package " + manifest.Id + " " + manifest.Version + " · License " + manifest.License),
                () => { var target = function ?? handler.Model.AddFunction(item.Name); target.Expression = expression; target.Description = item.Description; target.IsHidden = item.IsHidden; },
                () => handler.Model.Functions.Any(target => target.Name == item.Name && Describe(target.Expression, target.Description ?? "", target.IsHidden) == next)));
        }
        var removed = prior?.Functions.Where(item => !allNames.Contains(item.Name)).Select(item => item.Name).ToArray() ?? Array.Empty<string>();
        ValidateCallers(removed, new HashSet<string>((prior?.Functions.Select(item => item.Name) ?? Array.Empty<string>()).Concat(allNames), StringComparer.OrdinalIgnoreCase), manifest.Functions.Select(item => (item.Name, Expression: package.Functions[item.Name])), issues);
        AddDeletions(removed, edits);
        var replacement = new DaxLockedPackage(manifest.Id, manifest.Version, manifest.License, package.ContentHash, manifest.Dependencies,
            Array.AsReadOnly(manifest.Functions.Select(item => new DaxLockedFunction(item.Name, DaxPackageLock.FunctionHash(package.Functions[item.Name], item.Description, item.IsHidden))).ToArray()));
        var after = new DaxPackageLock(before.Packages.Where(item => !Same(item.Id, manifest.Id)).Concat(new[] { replacement }));
        foreach (var error in after.ValidateGraph()) Error("PACKAGE_LOCK", error);
        AddLock(after, edits); issues.Add(new("PACKAGE_LOCAL", "Only captured .dax bodies and the exact model lock are installed. No files, feed, executable or installer script are run. Export the lock JSON explicitly for Git.", AuthoringIssueSeverity.Information));
        return AuthoringPreview.Create(handler, "Prototype · " + (prior == null ? "install " : "update ") + manifest.Id + " " + manifest.Version, edits, issues);
        void Error(string code, string message) => issues.Add(new(code, message, AuthoringIssueSeverity.Error));
    }
    public AuthoringPreview PreviewRemove(string id)
    {
        var before = CaptureLock(); var installed = before.Packages.FirstOrDefault(item => Same(item.Id, id)) ?? throw new ArgumentException("Select an installed local package.");
        var issues = new List<AuthoringIssue>(); var edits = new List<AuthoringEdit>(); ValidateOwned(installed, issues);
        foreach (var error in before.ValidateGraph()) issues.Add(new("PACKAGE_LOCK", error, AuthoringIssueSeverity.Error));
        foreach (var dependent in before.Packages.Where(item => item.Dependencies.Any(dependency => Same(dependency.Id, id)))) issues.Add(new("PACKAGE_DEPENDENCY", dependent.Id + " depends on this package; remove or update it explicitly first.", AuthoringIssueSeverity.Error));
        var removed = installed.Functions.Select(item => item.Name).ToArray(); ValidateCallers(removed, new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase), Array.Empty<(string Name, string Expression)>(), issues);
        AddDeletions(removed, edits); AddLock(new DaxPackageLock(before.Packages.Where(item => !Same(item.Id, id))), edits);
        return AuthoringPreview.Create(handler, "Prototype · remove " + installed.Id, edits, issues);
    }
    private void ValidateOwned(DaxLockedPackage package, ICollection<AuthoringIssue> issues)
    {
        foreach (var item in package.Functions)
        {
            var function = handler.Model.Functions.FirstOrDefault(found => Same(found.Name, item.Name));
            if (function == null || DaxPackageLock.FunctionHash(function.Expression, function.Description ?? "", function.IsHidden) != item.DefinitionHash) issues.Add(new("PACKAGE_LOCAL_EDIT", "Owned function " + item.Name + " was edited, renamed or deleted since installation. Preserve or reconcile it before package changes.", AuthoringIssueSeverity.Error));
        }
    }
    private void ValidateCallers(IReadOnlyList<string> removed, HashSet<string> replaced, IEnumerable<(string Name, string Expression)> next, ICollection<AuthoringIssue> issues)
    {
        if (removed.Count == 0) return;
        var callers = DaxModelScript.Parse(new DaxAuthoringService(handler).ExportScript()).Entries.Where(entry => entry.Kind != DaxScriptObjectKind.Function || !replaced.Contains(entry.Name)).Select(entry => (Name: entry.DisplayName, entry.Expression)).Concat(next);
        foreach (var caller in callers)
        {
            var tokens = DaxTokenizer.Tokenize(caller.Expression).Where(token => token.Kind is not (DaxTokenKind.Comment or DaxTokenKind.Whitespace)).ToArray();
            for (var index = 0; index + 1 < tokens.Length; index++)
                if (tokens[index].Kind == DaxTokenKind.Identifier && tokens[index + 1].Text == "(" && removed.Contains(tokens[index].Value, StringComparer.OrdinalIgnoreCase)) issues.Add(new("PACKAGE_CALLER", caller.Name + " still calls removed function " + tokens[index].Value + ". Update that caller first.", AuthoringIssueSeverity.Error));
        }
    }
    private void AddDeletions(IEnumerable<string> names, ICollection<AuthoringEdit> edits)
    {
        var removed = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        // Native Undo appends restored members; reverse deletion preserves original collection order.
        foreach (var function in handler.Model.Functions.Where(item => removed.Contains(item.Name)).Reverse().ToArray())
        { var name = function.Name; edits.Add(new(new(name, "Remove function", Describe(function.Expression, function.Description ?? "", function.IsHidden), "(absent)", "Remove only the unchanged package-owned function; reject external callers."), () => function.Delete(), () => !handler.Model.Functions.Any(item => item.Name == name))); }
    }
    private void AddLock(DaxPackageLock after, ICollection<AuthoringEdit> edits)
    {
        var before = handler.Model.GetAnnotation(DaxPackageLock.AnnotationName); var next = after.ToJson(); if (before == next) return;
        edits.Add(new(new("Model", DaxPackageLock.AnnotationName, before ?? "(absent)", next, "Version, license, dependency and content/definition hashes are changed in the same native Undo batch."),
            () => handler.Model.SetAnnotation(DaxPackageLock.AnnotationName, next), () => handler.Model.GetAnnotation(DaxPackageLock.AnnotationName) == next));
    }
    private static string Describe(string expression, string description, bool hidden) => expression + "\nDescription: " + description + "\nHidden: " + hidden;
    private static bool Same(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
