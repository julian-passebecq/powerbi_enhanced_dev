using System.Windows;
using System.Windows.Controls;

namespace PbiBench.App;

public sealed partial class DiagramView
{
    private readonly ComboBox semanticMode = new() { ItemsSource = new[] { "Model", "Dependencies", "Report Usage", "Issues" }, SelectedIndex = 0, MinWidth = 130, Margin = new Thickness(8, 0, 8, 0) };
    private readonly DataGrid semanticRows = new() { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock semanticNotice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5) };
    private readonly DockPanel semanticPanel = new() { Visibility = Visibility.Collapsed };
    public event Action<string>? SemanticModeRequested;
    public event Action<object>? SemanticRowActivated;
    public string SemanticMode => (string)semanticMode.SelectedItem;
    private void InitializeSemanticModes(Grid layout, WrapPanel toolbar)
    {
        toolbar.Children.Insert(0, new TextBlock { Text = "Semantic View", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); toolbar.Children.Insert(1, semanticMode);
        toolbar.Children.Add(Command("Focus selected", () => { if (SelectedTableName != null) ShowRelatedTables(SelectedTableName); }, "Show the selected table and its immediate relationships"));
        DockPanel.SetDock(semanticNotice, Dock.Top); semanticPanel.Children.Add(semanticNotice); semanticPanel.Children.Add(semanticRows); Grid.SetRow(semanticPanel, 1); layout.Children.Add(semanticPanel);
        semanticMode.SelectionChanged += (_, _) => { var model = SemanticMode == "Model"; viewport.Visibility = model ? Visibility.Visible : Visibility.Collapsed; semanticPanel.Visibility = semanticRows.Visibility = model ? Visibility.Collapsed : Visibility.Visible; SemanticModeRequested?.Invoke(SemanticMode); };
        semanticRows.MouseDoubleClick += (_, _) => { if (semanticRows.SelectedItem is { } item) SemanticRowActivated?.Invoke(item); };
    }
    public void ShowSemanticMode(string mode) => semanticMode.SelectedItem = mode;
    public void SetSemanticRows(System.Collections.IEnumerable rows, string notice) { semanticRows.ItemsSource = rows; semanticNotice.Text = notice; }
}
