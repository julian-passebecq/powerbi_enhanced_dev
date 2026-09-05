using System.Windows;
using System.Windows.Controls;
using PbiBench.Automation;

namespace PbiBench.App;

/// <summary>Visible original policy catalog; configuration contains data and cannot execute rule code.</summary>
public sealed class BpaRulesView : UserControl
{
    private readonly BpaRuleProfile profile;
    private readonly Action changed;
    private readonly DataGrid grid = new() { IsReadOnly = true, AutoGenerateColumns = true, SelectionMode = DataGridSelectionMode.Extended };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly ComboBox category = new() { Width = 140 };
    private readonly ComboBox severity = new() { Width = 120, ItemsSource = Enum.GetValues(typeof(FindingSeverity)), SelectedIndex = 1 };
    private readonly TextBox search = new() { Width = 180 };

    public BpaRulesView(BpaRuleProfile profile, Action changed)
    {
        this.profile = profile; this.changed = changed;
        var root = new DockPanel { Margin = new Thickness(8) }; var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "8 original PbiBench packs · version " + BpaRulePacks.Version, FontSize = 19, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = "Rules expose provenance, applicability and fix risk. Performance suggestions require a benchmark. User/community executable rules remain in the native TE2 BPA experience.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 10) });
        category.ItemsSource = new[] { "All packs" }.Concat(BpaRulePacks.BuiltIn.Select(pack => pack.Category)); category.SelectedIndex = 0;
        var bar = new WrapPanel(); bar.Children.Add(category); bar.Children.Add(search);
        AddButton("Enable selected", () => UpdateEnabled(true)); AddButton("Disable selected", () => UpdateEnabled(false));
        bar.Children.Add(severity); AddButton("Set severity", () => { foreach (var row in Selected()) profile.Severities[row.Id] = (FindingSeverity)severity.SelectedItem; Commit(); });
        AddButton("Restore defaults", () => { profile.Enabled.Clear(); profile.Severities.Clear(); Commit(); });
        AddButton("Clear stored suppressions", () => { profile.Suppressions.Clear(); Commit(); }); top.Children.Add(bar);
        DockPanel.SetDock(details, Dock.Bottom); root.Children.Add(details); root.Children.Add(grid); Content = root;
        category.SelectionChanged += (_, _) => Refresh(); search.TextChanged += (_, _) => Refresh();
        grid.SelectionChanged += (_, _) => { if (grid.SelectedItem is RuleRow row) { var rule = BpaRulePacks.Get(row.Id); details.Text = rule.Title + "\n" + rule.Applicability + "\n" + row.Risk + " · " + BpaRulePacks.PackFor(row.Id).Origin + "\n" + rule.Reference; } }; Refresh();
        void AddButton(string text, Action action) { var button = new Button { Content = text }; button.Click += (_, _) => action(); bar.Children.Add(button); }
    }
    public void Refresh() => grid.ItemsSource = BpaRulePacks.Rules.Where(rule => (category.SelectedIndex == 0 || Convert.ToString(category.SelectedItem) == rule.Category) &&
        (rule.Title + " " + rule.Id).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).Select(rule => new RuleRow(rule.Id, rule.Title, rule.Category, BpaRulePacks.Version,
            profile.IsEnabled(rule.Id), profile.Severities.TryGetValue(rule.Id, out var value) ? value : rule.Severity, rule.Risk)).ToArray();
    private RuleRow[] Selected() => grid.SelectedItems.Cast<RuleRow>().ToArray();
    private void UpdateEnabled(bool enabled) { foreach (var row in Selected()) profile.Enabled[row.Id] = enabled; Commit(); }
    private void Commit() { profile.Validate(); Refresh(); changed(); }
    private sealed record RuleRow(string Id, string Rule, string Pack, string Version, bool Enabled, FindingSeverity Severity, string Risk);
}
