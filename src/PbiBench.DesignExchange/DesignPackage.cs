using PbiBench.ExternalTools;

namespace PbiBench.DesignExchange;

/// <summary>Immutable captured input texts; receivers re-read and validate, never trust a sender's validation flag.</summary>
public sealed record DesignPackage(ModelContext Model, DashboardValidation? Dashboard, ThemeValidation? Theme,
    string ModelJson, string? DashboardJson, string? ThemeJson)
{
    public bool IsValid => (Dashboard != null || Theme != null) && (Dashboard?.IsValid ?? true) && (Theme?.IsValid ?? true);
    public static async Task<DesignPackage> LoadAsync(string modelPath, string? specPath, string? themePath, CancellationToken ct)
    {
        var modelJson = await ContractJson.ReadAsync(modelPath, ct).ConfigureAwait(false); var model = ModelContext.Parse(modelJson);
        var specJson = specPath == null ? null : await ContractJson.ReadAsync(specPath, ct).ConfigureAwait(false);
        var themeJson = themePath == null ? null : await ContractJson.ReadAsync(themePath, ct, 1024 * 1024).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return new(model, specJson == null ? null : DashboardValidator.Validate(specJson, model), themeJson == null ? null : new ThemeValidator().Validate(themeJson), modelJson, specJson, themeJson);
    }
    public static string Prompt => "Use pbibench-model-context.json as authoritative model metadata. Do not invent tables, columns or measures. Return dashboard-spec.json contractVersion 1 using the supplied modelFingerprint, report {title,audience}, and pages [{id,title,canvas:{width,height},visuals:[{id,kind,region,bindings:{value:{kind,table,name}}}]}]. IDs must be unique. Use Measure or Column bindings; use top/middle/bottom/left/right/full regions or an explicit position {x,y,width,height}. Supported kinds: " + string.Join(", ", DashboardValidator.SupportedKinds) + ". Optionally return theme.json matching Power BI Desktop 2.156. No executable expressions or scripts in the dashboard spec. No prose inside JSON. Return the two files separately.";
}
