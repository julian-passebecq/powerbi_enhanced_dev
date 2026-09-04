using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

/// <summary>Model navigation and previewed authoring over the existing TE2 session. Selection is handed back to the hosted editor.</summary>
public sealed partial class DiagramView : UserControl
{
    private readonly ScrollViewer viewport;
    private readonly TextBox search;
    private readonly TextBlock zoomLabel;
    private readonly TextBlock summary;
    private readonly ScaleTransform scale = new(1, 1);
    private ModelGraph? graph;
    private Action<Table>? select;
    private Point? panOrigin;
    private Point panOffset;
    private List<Button> searchMatches = new();
    private int searchIndex = -1;
    private bool autoFit = true;
    private readonly ComboBox columnDisplay = new() { Width = 105, MinHeight = 28, Margin = new Thickness(6, 0, 4, 0) };
    private readonly ComboBox groupFilter = new() { Width = 175, MinHeight = 28, Margin = new Thickness(6, 0, 4, 0) };
    private readonly CheckBox arrangeGroups = new() { Content = "Group layout", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
    private bool updatingTools;
    private HashSet<string>? visibleTables;
    private TabularModelHandler? authoringHandler;
    private Action? metadataChanged;
    private Action<SingleColumnRelationship>? selectRelationship;
    private SingleColumnRelationship? selectedRelationship;
    private sealed record GroupChoice(string Label, string? Group, bool All = false) { public override string ToString() => Label; }

    public Canvas Canvas { get; } = new() { Background = new SolidColorBrush(Color.FromRgb(250, 250, 247)) };
    public double Zoom => scale.ScaleX;
    public string? SelectedTableName { get; private set; }
    public int SearchMatchCount => searchMatches.Count;
    public DiagramColumnDisplay ColumnDisplay => columnDisplay.SelectedItem is DiagramColumnDisplay value ? value : DiagramColumnDisplay.Keys;

    public DiagramView()
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        tools.Children.Add(Command("−", () => SetZoom(Zoom / 1.2), "Zoom out"));
        zoomLabel = new TextBlock { Width = 50, Text = "100%", VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        tools.Children.Add(zoomLabel);
        tools.Children.Add(Command("+", () => SetZoom(Zoom * 1.2), "Zoom in"));
        tools.Children.Add(Command("Fit", Fit, "Fit the complete model in view"));
        tools.Children.Add(Command("Auto layout", AutoLayout, "Arrange tables by inferred role and fit"));
        tools.Children.Add(new TextBlock { Text = "Find table", Margin = new Thickness(12, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        search = new TextBox { Width = 165, MinHeight = 28, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Search by table name. Enter selects the next match in Model." };
        System.Windows.Automation.AutomationProperties.SetName(search, "Find table in diagram");
        search.TextChanged += (_, _) => ApplySearch();
        search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SelectNextMatch(); e.Handled = true; } else if (e.Key == Key.Escape) { Search(string.Empty); e.Handled = true; } };
        tools.Children.Add(search);
        tools.Children.Add(Command("Next", SelectNextMatch, "Select the next matching table"));
        columnDisplay.ItemsSource = Enum.GetValues(typeof(DiagramColumnDisplay)); columnDisplay.SelectedItem = DiagramColumnDisplay.Keys;
        columnDisplay.ToolTip = "Show all columns, key/relationship columns, or table headers only";
        System.Windows.Automation.AutomationProperties.SetName(columnDisplay, "Diagram column display");
        columnDisplay.SelectionChanged += (_, _) => { if (!updatingTools) { RenderCurrent(); Fit(); } };
        tools.Children.Add(columnDisplay);
        groupFilter.ToolTip = "Focus a PbiBench virtual table group";
        System.Windows.Automation.AutomationProperties.SetName(groupFilter, "Diagram table group");
        groupFilter.SelectionChanged += (_, _) => { if (!updatingTools) { visibleTables = null; RenderCurrent(); Fit(); } };
        tools.Children.Add(groupFilter); tools.Children.Add(arrangeGroups);
        arrangeGroups.Click += (_, _) => { RenderCurrent(); Fit(); };
        tools.Children.Add(Command("All tables", ShowAllTables, "Clear related-table and group filters"));
        tools.Children.Add(Command("Table groups…", () => EditTableGroups(), "Assign, rename or remove virtual table groups with a local change preview"));
        layout.Children.Add(tools);
        Canvas.LayoutTransform = scale;
        viewport = new ScrollViewer { Content = Canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Canvas.Background, Focusable = true, CanContentScroll = false };
        viewport.PreviewMouseWheel += OnWheel;
        viewport.PreviewMouseDown += OnPanStart;
        viewport.PreviewMouseMove += OnPanMove;
        viewport.PreviewMouseUp += OnPanEnd;
        viewport.LostMouseCapture += (_, _) => EndPan();
        viewport.SizeChanged += (_, _) => { if (autoFit && graph != null) Fit(); };
        Grid.SetRow(viewport, 1); layout.Children.Add(viewport);
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        summary = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(99, 121, 136)), TextWrapping = TextWrapping.Wrap };
        footer.Children.Add(summary);
        footer.Children.Add(new TextBlock
        {
            Text = "Navy: fact · Gold: dimension · Solid: active · Dashed: inactive · Arrows: filter direction · ⚿ key · ↔ relationship column\nCtrl + wheel to zoom · Drag empty space or middle-drag to pan · Click a table or line to inspect · Right-click for related tables and previewed edits",
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(99, 121, 136)), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        Content = layout;
        Loaded += (_, _) => { if (autoFit) Fit(); };
    }

    public void Render(ModelGraph modelGraph, Action<Table> onSelect)
    {
        if (modelGraph == null) throw new ArgumentNullException(nameof(modelGraph));
        if (onSelect == null) throw new ArgumentNullException(nameof(onSelect));
        var structureChanged = graph == null || !graph.Tables.Select(t => t.Name).SequenceEqual(modelGraph.Tables.Select(t => t.Name));
        graph = modelGraph; select = onSelect;
        var previousGroup = groupFilter.SelectedItem as GroupChoice;
        updatingTools = true;
        var choices = new[] { new GroupChoice("All groups", null, true), new GroupChoice("(ungrouped)", null) }
            .Concat(graph.Tables.Select(table => table.Group).Where(group => group != null).Distinct(StringComparer.Ordinal).OrderBy(group => group, StringComparer.OrdinalIgnoreCase).Select(group => new GroupChoice(group!, group))).ToArray();
        groupFilter.ItemsSource = choices;
        groupFilter.SelectedItem = choices.FirstOrDefault(choice => choice.Group == previousGroup?.Group && choice.All == previousGroup?.All) ?? choices[0];
        updatingTools = false;
        RenderCurrent();
        if (structureChanged) autoFit = true;
        if (autoFit) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Fit));
    }

    /// <summary>Retains the model objects and only recomputes their visual arrangement.</summary>
    public void AutoLayout()
    {
        if (graph == null || select == null) return;
        RenderCurrent(); Fit();
    }

    public void ConfigureAuthoring(TabularModelHandler? handler, Action changed, Action<SingleColumnRelationship> selected)
    {
        if (!ReferenceEquals(authoringHandler, handler)) { selectedRelationship = null; visibleTables = null; }
        authoringHandler = handler; metadataChanged = changed; selectRelationship = selected;
    }

    public void SetColumnDisplay(DiagramColumnDisplay display) => columnDisplay.SelectedItem = display;

    public void ShowAllTables()
    {
        visibleTables = null;
        if (groupFilter.Items.Count > 0) groupFilter.SelectedIndex = 0;
        RenderCurrent(); Fit();
    }

    public void ShowRelatedTables(string tableName, bool filteringOnly = false)
    {
        if (graph == null) return;
        updatingTools = true; if (groupFilter.Items.Count > 0) groupFilter.SelectedIndex = 0; updatingTools = false;
        visibleTables = new HashSet<string>(graph.RelatedTables(tableName, filteringOnly), StringComparer.Ordinal) { tableName };
        RenderCurrent(); Fit();
        if (filteringOnly && graph.Relationships.Any(item => item.FilterDirection == "Automatic" && (item.FromTable == tableName || item.ToTable == tableName)))
            summary.Text += " · Automatic filter direction is engine-defined and omitted from filtering neighbours.";
    }

    private void RenderCurrent()
    {
        if (graph == null || select == null) return;
        var group = groupFilter.SelectedItem as GroupChoice;
        var tables = graph.Tables.Where(table => (group == null || group.All || table.Group == group.Group) && (visibleTables == null || visibleTables.Contains(table.Name))).ToArray();
        var names = new HashSet<string>(tables.Select(table => table.Name), StringComparer.Ordinal);
        var filtered = new ModelGraph(tables, graph.Relationships.Where(item => names.Contains(item.FromTable) && names.Contains(item.ToTable)).ToArray());
        DiagramRenderer.Render(Canvas, filtered, OnSelected, ColumnDisplay, arrangeGroups.IsChecked == true, OnRelationshipAction, OnTableAction);
        UpdateRelationshipAppearance(); ApplySearch();
        if (tables.Length != graph.Tables.Count) summary.Text += $" · {tables.Length} tables shown";
    }

    private void OnTableAction(Table table, DiagramTableAction action)
    {
        if (action == DiagramTableAction.Group) EditTableGroups(table);
        else ShowRelatedTables(table.Name, action == DiagramTableAction.Filtering);
    }

    private void OnRelationshipAction(GraphRelationship edge, DiagramRelationshipAction action)
    {
        var relationship = edge.Object; if (relationship == null) return;
        SelectedTableName = null; selectedRelationship = relationship; UpdateNodeAppearance(); UpdateRelationshipAppearance(); selectRelationship?.Invoke(relationship);
        if (action == DiagramRelationshipAction.Select) return;
        if (action == DiagramRelationshipAction.Edit) { EditRelationship(relationship); return; }
        if (authoringHandler == null) return;
        try
        {
            var service = new RelationshipAuthoringService(authoringHandler);
            var preview = action == DiagramRelationshipAction.Invert ? service.PreviewInvert(relationship) : service.PreviewActive(relationship, !relationship.IsActive);
            AuthoringReview.Show(this, preview, () => authoringHandler, () => metadataChanged?.Invoke());
        }
        catch (Exception ex) { ShowAuthoringError(ex); }
    }

    private void UpdateRelationshipAppearance()
    {
        foreach (var path in Canvas.Children.OfType<System.Windows.Shapes.Path>())
            if (path.Tag is GraphRelationship edge) path.StrokeThickness = ReferenceEquals(edge.Object, selectedRelationship) && selectedRelationship != null ? 4 : edge.IsActive ? 2 : 1.5;
    }

    public void Fit()
    {
        autoFit = true;
        if (viewport.ViewportWidth <= 1 || viewport.ViewportHeight <= 1 || double.IsNaN(Canvas.Width) || double.IsNaN(Canvas.Height)) return;
        ApplyZoom(Math.Min(1, Math.Min(Math.Max(1, viewport.ViewportWidth - 22) / Canvas.Width, Math.Max(1, viewport.ViewportHeight - 22) / Canvas.Height)));
        viewport.ScrollToHorizontalOffset(0); viewport.ScrollToVerticalOffset(0);
    }

    public void SetZoom(double factor)
    {
        if (double.IsNaN(factor) || double.IsInfinity(factor)) throw new ArgumentOutOfRangeException(nameof(factor));
        ZoomAt(factor, new Point(viewport.ViewportWidth / 2, viewport.ViewportHeight / 2));
    }

    public int Search(string text)
    {
        search.Text = text ?? string.Empty;
        if (searchMatches.Count > 0) Center(searchMatches[0]);
        return searchMatches.Count;
    }

    /// <summary>Highlights a selection made elsewhere without firing the selection callback again.</summary>
    public void SelectTable(string? name)
    {
        SelectedTableName = name;
        if (name != null) { selectedRelationship = null; UpdateRelationshipAppearance(); }
        UpdateNodeAppearance();
    }

    public void SelectRelationship(SingleColumnRelationship? relationship)
    {
        selectedRelationship = relationship;
        if (relationship != null) SelectedTableName = null;
        UpdateNodeAppearance(); UpdateRelationshipAppearance();
    }

    private void OnSelected(Table table)
    {
        SelectedTableName = table.Name; selectedRelationship = null; UpdateNodeAppearance(); UpdateRelationshipAppearance(); select?.Invoke(table);
    }

    private void ApplySearch()
    {
        var query = search.Text.Trim(); searchIndex = -1;
        searchMatches = Canvas.Children.OfType<Button>().Where(b => b.Tag is Table t && query.Length > 0 && t.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        UpdateNodeAppearance();
        summary.Text = graph == null ? "Open a model to view its tables and relationships." : $"{graph.Tables.Count} tables · {graph.Relationships.Count} relationships" +
            (query.Length > 0 ? $" · {searchMatches.Count} matching tables" : string.Empty);
    }

    private void UpdateNodeAppearance()
    {
        foreach (var button in Canvas.Children.OfType<Button>())
        {
            if (!(button.Tag is Table table)) continue;
            var selected = string.Equals(table.Name, SelectedTableName, StringComparison.Ordinal);
            var match = searchMatches.Contains(button);
            button.Opacity = search.Text.Trim().Length > 0 && !match && !selected ? 0.38 : 1;
            button.Background = selected ? new SolidColorBrush(Color.FromRgb(232, 240, 244)) : match ? new SolidColorBrush(Color.FromRgb(255, 249, 219)) : Brushes.White;
            button.BorderThickness = new Thickness(selected ? 3 : 2);
        }
    }

    private void SelectNextMatch()
    {
        if (searchMatches.Count == 0) return;
        searchIndex = (searchIndex + 1) % searchMatches.Count;
        var node = searchMatches[searchIndex]; Center(node);
        if (node.Tag is Table table) OnSelected(table);
    }

    private void Center(Button node)
    {
        autoFit = false;
        viewport.ScrollToHorizontalOffset((System.Windows.Controls.Canvas.GetLeft(node) + node.Width / 2) * Zoom - viewport.ViewportWidth / 2);
        viewport.ScrollToVerticalOffset((System.Windows.Controls.Canvas.GetTop(node) + node.Height / 2) * Zoom - viewport.ViewportHeight / 2);
    }

    private void ApplyZoom(double factor)
    {
        var value = Math.Max(0.1, Math.Min(2.5, factor)); scale.ScaleX = value; scale.ScaleY = value;
        zoomLabel.Text = value.ToString("P0");
    }

    private void ZoomAt(double factor, Point anchor)
    {
        autoFit = false;
        var x = (viewport.HorizontalOffset + anchor.X) / Zoom;
        var y = (viewport.VerticalOffset + anchor.Y) / Zoom;
        ApplyZoom(factor); viewport.UpdateLayout();
        viewport.ScrollToHorizontalOffset(x * Zoom - anchor.X); viewport.ScrollToVerticalOffset(y * Zoom - anchor.Y);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ZoomAt(Zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), e.GetPosition(viewport)); e.Handled = true;
    }

    private void OnPanStart(object sender, MouseButtonEventArgs e)
    {
        // Preserve table-button activation and scrollbar interactions.
        if (e.ChangedButton != MouseButton.Middle && !(e.ChangedButton == MouseButton.Left && ReferenceEquals(e.OriginalSource, Canvas))) return;
        autoFit = false; panOrigin = e.GetPosition(viewport); panOffset = new Point(viewport.HorizontalOffset, viewport.VerticalOffset);
        viewport.CaptureMouse(); viewport.Cursor = Cursors.Hand; e.Handled = true;
    }

    private void OnPanMove(object sender, MouseEventArgs e)
    {
        if (!panOrigin.HasValue) return;
        var position = e.GetPosition(viewport);
        viewport.ScrollToHorizontalOffset(panOffset.X + panOrigin.Value.X - position.X);
        viewport.ScrollToVerticalOffset(panOffset.Y + panOrigin.Value.Y - position.Y); e.Handled = true;
    }

    private void OnPanEnd(object sender, MouseButtonEventArgs e)
    {
        if (!panOrigin.HasValue) return;
        viewport.ReleaseMouseCapture(); EndPan(); e.Handled = true;
    }

    private void EndPan() { panOrigin = null; viewport.Cursor = Cursors.Arrow; }
    private static Button Command(string label, Action action, string tooltip)
    {
        var button = new Button { Content = label, MinHeight = 28, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 4, 0), ToolTip = tooltip };
        button.Click += (_, _) => action(); return button;
    }
}
