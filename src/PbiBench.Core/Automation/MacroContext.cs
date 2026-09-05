namespace PbiBench.Core.Automation;

public sealed record MacroContextRule(IReadOnlyList<string> AllowedSelectionKinds, int MinSelectedCount = 0, int MaxSelectedCount = 10000, bool RequiresConnectedModel = false)
{
    public void Validate()
    {
        if (AllowedSelectionKinds == null || AllowedSelectionKinds.Count > 4 || AllowedSelectionKinds.Distinct(StringComparer.Ordinal).Count() != AllowedSelectionKinds.Count ||
            AllowedSelectionKinds.Any(k => k is not ("Model" or "Table" or "Column" or "Measure")) || MinSelectedCount < 0 || MaxSelectedCount > 10000 || MaxSelectedCount < MinSelectedCount)
            throw new InvalidDataException("Macro context supports Model/Table/Column/Measure and selection counts from 0 to 10,000.");
    }
}
public sealed record MacroSelectionContext(bool HasModel, bool IsConnected, IReadOnlyList<string> SelectedKinds);
public sealed record MacroAvailability(bool Enabled, string Reason);
public static class MacroContextRules
{
    public static MacroAvailability Evaluate(MacroContextRule? rule, MacroSelectionContext context)
    {
        if (!context.HasModel) return new(false, "Open a semantic model first.");
        if (rule == null) return new(true, "Available; no additional context rules.");
        rule.Validate();
        if (rule.RequiresConnectedModel && !context.IsConnected) return new(false, "Requires a connected model.");
        if (context.SelectedKinds.Count < rule.MinSelectedCount || context.SelectedKinds.Count > rule.MaxSelectedCount)
            return new(false, "Select " + rule.MinSelectedCount + "–" + rule.MaxSelectedCount + " objects; currently " + context.SelectedKinds.Count + ".");
        if (rule.AllowedSelectionKinds.Count > 0 && context.SelectedKinds.Any(k => !rule.AllowedSelectionKinds.Contains(k, StringComparer.Ordinal)))
            return new(false, "Allowed selection: " + string.Join(", ", rule.AllowedSelectionKinds) + ".");
        return new(true, "Available in the current model selection.");
    }
}
