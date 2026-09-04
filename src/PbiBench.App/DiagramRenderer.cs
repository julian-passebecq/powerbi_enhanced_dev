using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public enum DiagramColumnDisplay { All, Keys, None }
internal enum DiagramRelationshipAction { Select, Edit, Invert, ToggleActive }
internal enum DiagramTableAction { Related, Filtering, Group }

/// <summary>Deterministic presentation of the current model graph; it never changes model metadata.</summary>
internal static class DiagramRenderer
{
    internal const double NodeWidth = 238;
    internal const double NodeHeight = 184;

    internal static void Render(Canvas canvas, ModelGraph graph, Action<Table> select)
        => Render(canvas, graph, select, DiagramColumnDisplay.All, false, null, null);

    internal static void Render(Canvas canvas, ModelGraph graph, Action<Table> select, DiagramColumnDisplay columns, bool groupTables,
        Action<GraphRelationship, DiagramRelationshipAction>? relationshipAction, Action<Table, DiagramTableAction>? tableAction)
    {
        canvas.Children.Clear();
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var nodeHeight = columns == DiagramColumnDisplay.None ? 104 : columns == DiagramColumnDisplay.Keys ? 222 : 286;
        var y = 40d;
        var groups = graph.Tables.GroupBy(t => groupTables ? t.Group ?? "(ungrouped)" : "", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var group in groups)
        {
            var rows = new int[3];
            if (groupTables)
            {
                var heading = new TextBlock { Text = group.Key, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Color("#637988"), IsHitTestVisible = false };
                Canvas.SetLeft(heading, 40); Canvas.SetTop(heading, y); canvas.Children.Add(heading); y += 34;
            }
            foreach (var table in group.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var column = table.Role == "Dimension" ? 0 : table.Role == "Fact" ? 1 : 2;
                positions[table.Name] = new Point(40 + column * 365, y + rows[column]++ * (nodeHeight + 42));
            }
            y += rows.Max() * (nodeHeight + 42) + (groupTables ? 24 : 0);
        }
        // An empty role column does not create a blank leading viewport.
        var minimumX = positions.Values.Select(p => p.X).DefaultIfEmpty(40).Min();
        foreach (var name in positions.Keys.ToArray()) positions[name] = new Point(positions[name].X - minimumX + 40, positions[name].Y);
        canvas.Width = Math.Max(430, positions.Values.Select(p => p.X + NodeWidth + 125).DefaultIfEmpty(430).Max());
        canvas.Height = Math.Max(280, y + 20);
        var edgeIndex = 0;
        foreach (var r in graph.Relationships)
        {
            if (!positions.TryGetValue(r.FromTable, out var from) || !positions.TryGetValue(r.ToTable, out var to)) continue;
            var leftToRight = from.X < to.X;
            var sameColumn = from.X == to.X;
            var endpointY = Math.Min(nodeHeight - 22, 72 + edgeIndex % 4 * 22);
            var start = new Point(from.X + (leftToRight || sameColumn ? NodeWidth : 0), from.Y + endpointY);
            var end = new Point(to.X + (leftToRight && !sameColumn ? 0 : NodeWidth), to.Y + endpointY);
            if (r.FromTable == r.ToTable) end.Y += 65;
            var controlX = sameColumn ? start.X + 80 + edgeIndex % 3 * 16 : (start.X + end.X) / 2;
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new BezierSegment(new Point(controlX, start.Y), new Point(controlX, end.Y), end, true));
            var brush = Color(r.IsActive ? "#637988" : "#9DAAB2");
            var path = new System.Windows.Shapes.Path
            {
                Data = new PathGeometry(new[] { figure }), Stroke = brush, StrokeThickness = r.IsActive ? 2 : 1.5,
                ToolTip = $"{r.Name}\n{r.FromTable}[{r.FromColumn}] ({Cardinality(r.FromCardinality)}) — {r.ToTable}[{r.ToColumn}] ({Cardinality(r.ToCardinality)})\n{FilterLabel(r)} · {(r.IsActive ? "Active" : "Inactive")}\nSecurity: {r.SecurityFilterDirection ?? "unknown"}\nClick to inspect; double-click to edit; right-click for actions.",
                Tag = r
            };
            if (!r.IsActive) path.StrokeDashArray = new DoubleCollection { 5, 4 };
            if (relationshipAction != null && r.Object != null)
            {
                var hit = new System.Windows.Shapes.Path { Data = path.Data, Stroke = Brushes.Transparent, StrokeThickness = 16, ToolTip = path.ToolTip, Cursor = System.Windows.Input.Cursors.Hand };
                void Click(object sender, System.Windows.Input.MouseButtonEventArgs args)
                {
                    if (args.ChangedButton != System.Windows.Input.MouseButton.Left) return;
                    relationshipAction(r, args.ClickCount > 1 ? DiagramRelationshipAction.Edit : DiagramRelationshipAction.Select); args.Handled = true;
                }
                path.MouseDown += Click; hit.MouseDown += Click;
                path.ContextMenu = RelationshipMenu(r, relationshipAction); hit.ContextMenu = RelationshipMenu(r, relationshipAction);
                canvas.Children.Add(hit);
            }
            canvas.Children.Add(path);
            Label(canvas, Cardinality(r.FromCardinality), start.X + (leftToRight || sameColumn ? 10 : -25), start.Y - 28);
            Label(canvas, Cardinality(r.ToCardinality), end.X + (leftToRight && !sameColumn ? -25 : 10), end.Y - 28);
            // In a single-direction TOM relationship, the To side filters the From side.
            if (r.FilterDirection != "Automatic") Arrow(canvas, start, sameColumn || leftToRight ? -1 : 1, brush);
            if (r.FilterDirection == "BothDirections") Arrow(canvas, end, leftToRight && !sameColumn ? 1 : -1, brush);
            edgeIndex++;
        }
        foreach (var table in graph.Tables)
        {
            var p = positions[table.Name];
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = table.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Color("#15232D") });
            content.Children.Add(new TextBlock { Text = $"{table.Role.ToUpperInvariant()} · {table.MeasureCount} measures" + (table.Group == null ? "" : " · " + table.Group), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 10, Margin = new Thickness(0, 5, 0, 10), Foreground = Color("#637988") });
            if (columns != DiagramColumnDisplay.None)
            {
                var fields = new StackPanel();
                var metadata = table.ColumnMetadata ?? table.Columns.Select(name => new GraphColumn(name, "Unknown", false,
                    graph.Relationships.Any(r => (r.FromTable == table.Name && r.FromColumn == name) || (r.ToTable == table.Name && r.ToColumn == name)), false)).ToArray();
                var visible = metadata.Where(column => columns != DiagramColumnDisplay.Keys || column.IsKey || column.IsRelationshipKey).ToArray();
                foreach (var column in visible)
                    fields.Children.Add(new TextBlock { Text = (column.IsKey ? "⚿ " : column.IsRelationshipKey ? "↔ " : "") + TypeIcon(column.DataType) + "  " + column.Name,
                        ToolTip = $"{column.Name} · {column.DataType}" + (column.IsKey ? " · key metadata" : "") + (column.IsRelationshipKey ? " · relationship column" : "") + (column.IsHidden ? " · hidden" : ""),
                        Opacity = column.IsHidden ? 0.55 : 1, Margin = new Thickness(0, 2, 0, 2), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                if (visible.Length == 0) fields.Children.Add(new TextBlock { Text = "No key columns", FontSize = 11, Foreground = Color("#637988") });
                content.Children.Add(new ScrollViewer { Content = fields, MaxHeight = nodeHeight - 93, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            }
            var button = new Button
            {
                Content = content, Width = NodeWidth, Height = nodeHeight, Padding = new Thickness(13),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                Background = Brushes.White, BorderBrush = RoleBrush(table.Role), BorderThickness = new Thickness(2),
                ToolTip = $"{table.Name}\n{table.Columns.Count} columns · {table.MeasureCount} measures\n{table.Role} role inferred from relationship cardinality.\nSelect in Model", Tag = table.Object
            };
            System.Windows.Automation.AutomationProperties.SetName(button, "Select table " + table.Name);
            button.Click += (_, _) => select(table.Object);
            if (tableAction != null)
            {
                var menu = new ContextMenu();
                menu.Items.Add(Menu("Show related tables", () => tableAction(table.Object, DiagramTableAction.Related)));
                menu.Items.Add(Menu("Show tables filtering this table", () => tableAction(table.Object, DiagramTableAction.Filtering)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Menu("Assign table group…", () => tableAction(table.Object, DiagramTableAction.Group)));
                button.ContextMenu = menu;
            }
            Canvas.SetLeft(button, p.X); Canvas.SetTop(button, p.Y); canvas.Children.Add(button);
        }
        if (graph.Tables.Count == 0)
        {
            var empty = new TextBlock { Text = "This model has no tables. Create a table in Model to begin.", Foreground = Color("#637988"), TextWrapping = TextWrapping.Wrap, Width = 350 };
            Canvas.SetLeft(empty, 40); Canvas.SetTop(empty, 50); canvas.Children.Add(empty);
        }
    }

    internal static Brush RoleBrush(string role) => Color(role == "Fact" ? "#173C52" : role == "Dimension" ? "#E7BE24" : "#A7B4BA");
    private static string TypeIcon(string type) => type switch { "String" => "Abc", "Int64" => "123", "Double" or "Decimal" => "1.2", "DateTime" => "▦", "Boolean" => "T/F", "Binary" => "01", _ => "?" };
    private static string FilterLabel(GraphRelationship r) => r.FilterDirection == "BothDirections" ? "Filters in both directions" : r.FilterDirection == "Automatic" ? "Automatic: engine chooses filter direction" : "Filters from " + r.ToTable + " to " + r.FromTable;
    private static ContextMenu RelationshipMenu(GraphRelationship relationship, Action<GraphRelationship, DiagramRelationshipAction> action)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Inspect relationship", () => action(relationship, DiagramRelationshipAction.Select)));
        menu.Items.Add(Menu("Edit relationship…", () => action(relationship, DiagramRelationshipAction.Edit)));
        menu.Items.Add(Menu("Preview invert endpoints…", () => action(relationship, DiagramRelationshipAction.Invert)));
        menu.Items.Add(Menu(relationship.IsActive ? "Preview deactivate…" : "Preview activate…", () => action(relationship, DiagramRelationshipAction.ToggleActive)));
        return menu;
    }
    private static MenuItem Menu(string label, Action action)
    {
        var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); return item;
    }
    private static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    private static string Cardinality(string value) => value == "Many" ? "*" : value == "One" ? "1" : "?";
    private static void Label(Canvas canvas, string text, double x, double y)
    {
        var label = new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.Bold, Background = Color("#FAFAF7"), Padding = new Thickness(2), IsHitTestVisible = false };
        Canvas.SetLeft(label, x); Canvas.SetTop(label, y); canvas.Children.Add(label);
    }
    private static void Arrow(Canvas canvas, Point tip, int direction, Brush color)
        => canvas.Children.Add(new Polygon { Fill = color, IsHitTestVisible = false, Points = new PointCollection { tip, new(tip.X - direction * 9, tip.Y - 5), new(tip.X - direction * 9, tip.Y + 5) } });
}
