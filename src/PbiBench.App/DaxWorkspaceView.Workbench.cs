using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PbiBench.Dax.LanguageService;
using PbiBench.Semantic;

namespace PbiBench.App;

public sealed partial class DaxWorkspaceView
{
    private readonly ListBox modelObjects = new() { DisplayMemberPath = "QualifiedName" };
    private readonly TextBlock contextHelp = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly WrapPanel companionBar = new();
    private readonly DispatcherTimer helpTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DaxLanguageService contextLanguage = new();
    private string? guideFunction;
    private int lastCaret = -1;
    private DaxAnalysis? lastAnalysis;
    private Action? refreshExplorer;
    private FrameworkElement BuildWorkbench(FrameworkElement editor)
    {
        var panel = new DockPanel(); DockPanel.SetDock(companionBar, Dock.Top); panel.Children.Add(companionBar);
        companionBar.Children.Add(Button("Format DAX", () => ActiveEditor.Text = new LocalDaxFormatter().Format(ActiveEditor.Text)));
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(185), MinWidth = 130 }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(220), MinWidth = 150 }); panel.Children.Add(grid);
        var explorer = new DockPanel { Margin = new Thickness(0, 0, 6, 0) }; var filter = new TextBox { ToolTip = "Find model object", Margin = new Thickness(4), Padding = new Thickness(5) };
        DockPanel.SetDock(filter, Dock.Top); explorer.Children.Add(filter); explorer.Children.Add(modelObjects); grid.Children.Add(explorer);
        void RefreshObjects() => modelObjects.ItemsSource = metadata().Symbols.Where(s => s.Kind is DaxSymbolKind.Table or DaxSymbolKind.Measure or DaxSymbolKind.Column or DaxSymbolKind.Function)
            .Where(s => s.QualifiedName.IndexOf(filter.Text, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(s => s.Table).ThenBy(s => s.Name).Take(2000).ToArray();
        refreshExplorer = RefreshObjects;
        filter.TextChanged += (_, _) => RefreshObjects(); Loaded += (_, _) => { RefreshObjects(); helpTimer.Start(); }; Unloaded += (_, _) => helpTimer.Stop();
        modelObjects.MouseDoubleClick += (_, _) => { if (modelObjects.SelectedItem is DaxSymbol symbol) navigate(new(symbol.Id, symbol.Name, symbol.Kind, null, null, symbol.Expression, symbol.Description), false); };
        modelObjects.SelectionChanged += (_, _) => { if (modelObjects.SelectedItem is DaxSymbol symbol) contextHelp.Text = symbol.QualifiedName + "\n\n" + symbol.Description + "\n\n" + symbol.Expression + "\n\nDouble-click to open its existing model editor."; };
        Grid.SetColumn(editor, 1); grid.Children.Add(editor);
        var context = new DockPanel(); Grid.SetColumn(context, 2); grid.Children.Add(context);
        var heading = new TextBlock { Text = "Context / Help", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8) }; DockPanel.SetDock(heading, Dock.Top); context.Children.Add(heading);
        var guide = Button("Open in DAX Guide ↗", () => { if (guideFunction != null) Process.Start(new ProcessStartInfo("https://dax.guide/" + Uri.EscapeDataString(guideFunction.ToLowerInvariant()) + "/") { UseShellExecute = true }); });
        DockPanel.SetDock(guide, Dock.Bottom); context.Children.Add(guide); context.Children.Add(new ScrollViewer { Content = contextHelp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        helpTimer.Tick += (_, _) =>
        {
            if (disposed || tabs.Count == 0) return;
            var analysis = ActiveEditor.LatestAnalysis; var caret = ActiveEditor.CaretOffset;
            if (analysis == null || ReferenceEquals(lastAnalysis, analysis) && caret == lastCaret) return;
            lastAnalysis = analysis; lastCaret = caret; guideFunction = null;
            var signature = contextLanguage.GetSignatureHelp(analysis, caret);
            if (signature != null) { guideFunction = signature.Signature.Name; contextHelp.Text = signature.Signature.Label + "\n\n" + signature.Signature.Description + "\n\nParameter " + (signature.ActiveParameter + 1) + "\nOriginal local help; full reference opens in your browser."; }
            else if (contextLanguage.FindDefinition(analysis, caret) is { } definition) contextHelp.Text = definition.Name + "\n\n" + definition.Description + "\n\n" + definition.Expression;
            else contextHelp.Text = "Place the caret in a function call for its signature and parameter hints. Select a model object to inspect its definition.";
            guide.IsEnabled = guideFunction != null;
        };
        return panel;
    }
    public void AddWorkbenchCommand(string title, Action action, Func<(bool Enabled, string Reason)>? applicability = null)
    {
        var button = Button(title, action); companionBar.Children.Add(button);
        void Refresh() { var state = applicability?.Invoke() ?? (true, ""); button.IsEnabled = state.Item1; button.ToolTip = state.Item2; }
        button.Loaded += (_, _) => Refresh(); helpTimer.Tick += (_, _) => Refresh();
    }
}
