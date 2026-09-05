using System.Text.Json.Serialization;
using PbiBench.ExternalTools;

namespace PbiBench.DesignExchange;

public sealed record DesignReport(string Title, string Audience);
public sealed record DesignCanvas([property: JsonRequired] double Width, [property: JsonRequired] double Height);
public sealed record DesignPosition([property: JsonRequired] double X, [property: JsonRequired] double Y,
    [property: JsonRequired] double Width, [property: JsonRequired] double Height);
public sealed record DesignBinding(string Kind, string Table, string Name);
public sealed record DesignVisual(string Id, string Kind, IReadOnlyDictionary<string, DesignBinding> Bindings,
    string? Purpose = null, string? Region = null, DesignPosition? Position = null);
public sealed record DesignPage(string Id, string Title, DesignCanvas Canvas, IReadOnlyList<DesignVisual> Visuals, string? Archetype = null);
public sealed record DashboardSpec(int ContractVersion, DesignReport Report, IReadOnlyList<DesignPage> Pages,
    string? ModelFingerprint = null, bool Unbound = false);
public sealed record DesignDiagnostic(string Severity, string Location, string Message);
public sealed record BindingValidity(string Page, string Visual, string Role, string Field, string Status);
public sealed record DashboardValidation(DashboardSpec? Spec, IReadOnlyList<DesignDiagnostic> Diagnostics, IReadOnlyList<BindingValidity> Bindings)
{ public bool IsValid => Spec != null && Diagnostics.All(d => d.Severity != "Error"); }

public static class DashboardValidator
{
    public static IReadOnlyList<string> SupportedKinds { get; } = Array.AsReadOnly(new[] { "card", "kpi", "line", "area", "clusteredColumn", "stackedColumn", "bar", "donut", "table", "matrix", "slicer", "scatter", "combo", "waterfall", "text" });
    public static DashboardValidation Validate(string json, ModelContext model)
    {
        var diagnostics = new List<DesignDiagnostic>(); var bindings = new List<BindingValidity>(); DashboardSpec spec;
        void Error(string at, string message) => diagnostics.Add(new("Error", at, message));
        try { spec = ContractJson.Parse<DashboardSpec>(json); }
        catch (InvalidDataException error) { Error("/", error.Message); return new(null, diagnostics.AsReadOnly(), bindings.AsReadOnly()); }
        if (spec.ContractVersion != 1) Error("/contractVersion", "Only dashboard-spec contract v1 is supported.");
        if (spec.Unbound == (spec.ModelFingerprint != null)) Error("/modelFingerprint", "Provide a fingerprint or explicit unbound: true, exclusively.");
        if (!spec.Unbound && spec.ModelFingerprint != model.ModelFingerprint) Error("/modelFingerprint", "Model fingerprint mismatch. Export current model context and regenerate the design, or explicitly use unbound mode.");
        if (spec.Unbound) diagnostics.Add(new("Warning", "/unbound", "Unbound design intent. Bindings are unverified; this cannot authorize report changes."));
        if (spec.Report == null || !ModelContext.Text(spec.Report.Title, 240) || !ModelContext.Text(spec.Report.Audience, 512)) Error("/report", "A bounded report title and audience are required.");
        if (spec.Pages == null || spec.Pages.Count is < 1 or > 32) { Error("/pages", "Provide 1–32 pages."); return new(spec, diagnostics.AsReadOnly(), bindings.AsReadOnly()); }
        var pageIds = new HashSet<string>(StringComparer.Ordinal); var visualIds = new HashSet<string>(StringComparer.Ordinal); var count = 0;
        var fields = new HashSet<string>(model.Model.Objects.Where(o => o.Kind is "Column" or "Measure").Select(o => o.Id), StringComparer.Ordinal);
        foreach (var page in spec.Pages)
        {
            if (page == null) { Error("/pages", "Null page."); continue; }
            var at = "/pages/" + page.Id;
            if (!Id(page.Id) || !pageIds.Add(page.Id)) Error(at, "Page IDs must be valid and unique.");
            if (!ModelContext.Text(page.Title, 240) || page.Archetype != null && !ModelContext.Text(page.Archetype, 120)) Error(at, "Invalid page title/archetype.");
            var canvasValid = page.Canvas != null && Size(page.Canvas.Width) && Size(page.Canvas.Height);
            if (!canvasValid) Error(at + "/canvas", "Canvas dimensions must be finite and within 1–8192.");
            if (page.Visuals == null || page.Visuals.Count > 100 || (count += page.Visuals.Count) > 1000) { Error(at, "Limit: 100 visuals per page, 1,000 per design."); continue; }
            foreach (var visual in page.Visuals)
            {
                if (visual == null) { Error(at, "Null visual."); continue; }
                var vat = at + "/visuals/" + visual.Id;
                if (!Id(visual.Id) || !visualIds.Add(visual.Id)) Error(vat, "Visual IDs must be unique across the design.");
                if (!ModelContext.Text(visual.Kind, 80)) Error(vat, "Visual kind is required.");
                else if (!SupportedKinds.Contains(visual.Kind, StringComparer.Ordinal)) diagnostics.Add(new("Warning", vat, "Unsupported design kind: " + visual.Kind + ". Shown as a placeholder only."));
                if (visual.Purpose != null && !ModelContext.Text(visual.Purpose, 512)) Error(vat, "Purpose exceeds the text limit.");
                if (visual.Position == null && visual.Region == null) Error(vat, "Provide position or region layout intent.");
                if (visual.Region != null && !new[] { "top", "middle", "bottom", "left", "right", "full" }.Contains(visual.Region, StringComparer.Ordinal)) Error(vat, "Unsupported layout region.");
                if (visual.Position is { } pos && (!Coordinate(pos.X) || !Coordinate(pos.Y) || !Size(pos.Width) || !Size(pos.Height) ||
                    canvasValid && (pos.X + pos.Width > page.Canvas!.Width || pos.Y + pos.Height > page.Canvas!.Height))) Error(vat + "/position", "Position must be finite, bounded and contained in the canvas.");
                if (visual.Bindings == null || visual.Bindings.Count > 12) { Error(vat, "A bindings object with at most 12 entries is required."); continue; }
                if (visual.Kind != "text" && visual.Bindings.Count == 0) Error(vat, "Data visuals require semantic bindings.");
                foreach (var pair in visual.Bindings)
                {
                    var binding = pair.Value; var bat = vat + "/bindings/" + pair.Key;
                    if (!Id(pair.Key) || binding == null || binding.Kind is not ("Measure" or "Column") || !ModelContext.Text(binding.Table, 512) || !ModelContext.Text(binding.Name, 512)) { Error(bat, "Invalid semantic binding role, kind or name."); continue; }
                    var exists = fields.Contains(PbiBench.AI.ContextExport.ContextModel.ObjectId(binding.Kind, binding.Table, binding.Name));
                    var state = spec.Unbound ? "Unverified · unbound" : exists ? "Valid" : "Invalid · missing object or kind mismatch";
                    bindings.Add(new(page.Id, visual.Id, pair.Key, binding.Table + "[" + binding.Name + "] (" + binding.Kind + ")", state));
                    if (!exists && !spec.Unbound) Error(bat, state);
                }
            }
        }
        return new(spec, diagnostics.AsReadOnly(), bindings.AsReadOnly());
    }
    private static bool Id(string? id) => ModelContext.Text(id, 80) && id!.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');
    private static bool Coordinate(double number) => !double.IsNaN(number) && !double.IsInfinity(number) && number >= 0 && number <= 8192;
    private static bool Size(double number) => Coordinate(number) && number >= 1;
}
