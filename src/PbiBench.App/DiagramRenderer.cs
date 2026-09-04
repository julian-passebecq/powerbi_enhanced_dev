using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

/// <summary>Deterministic presentation of the current model graph; it never changes model metadata.</summary>
internal static class DiagramRenderer
{
    internal const double NodeWidth = 238;
    internal const double NodeHeight = 184;

    internal static void Render(Canvas canvas, ModelGraph graph, Action<Table> select)
    {
        canvas.Children.Clear();
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var rows = new int[3];
        foreach (var table in graph.Tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var column = table.Role == "Dimension" ? 0 : table.Role == "Fact" ? 1 : 2;
            positions[table.Name] = new Point(40 + column * 365, 40 + rows[column]++ * 226);
        }
        // An empty role column does not create a blank leading viewport.
        var minimumX = positions.Values.Select(p => p.X).DefaultIfEmpty(40).Min();
        foreach (var name in positions.Keys.ToArray()) positions[name] = new Point(positions[name].X - minimumX + 40, positions[name].Y);
        canvas.Width = Math.Max(430, positions.Values.Select(p => p.X + NodeWidth + 125).DefaultIfEmpty(430).Max());
        canvas.Height = Math.Max(280, rows.Max() * 226 + 60);
        var edgeIndex = 0;
        foreach (var r in graph.Relationships)
        {
            if (!positions.TryGetValue(r.FromTable, out var from) || !positions.TryGetValue(r.ToTable, out var to)) continue;
            var leftToRight = from.X < to.X;
            var sameColumn = from.X == to.X;
            var start = new Point(from.X + (leftToRight || sameColumn ? NodeWidth : 0), from.Y + 72 + edgeIndex % 4 * 22);
            var end = new Point(to.X + (leftToRight && !sameColumn ? 0 : NodeWidth), to.Y + 72 + edgeIndex % 4 * 22);
            if (r.FromTable == r.ToTable) end.Y += 65;
            var controlX = sameColumn ? start.X + 80 + edgeIndex % 3 * 16 : (start.X + end.X) / 2;
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new BezierSegment(new Point(controlX, start.Y), new Point(controlX, end.Y), end, true));
            var brush = Color(r.IsActive ? "#637988" : "#9DAAB2");
            var path = new Path
            {
                Data = new PathGeometry(new[] { figure }), Stroke = brush, StrokeThickness = r.IsActive ? 2 : 1.5,
                ToolTip = $"{r.Name}\n{r.FromTable}[{r.FromColumn}] ({Cardinality(r.FromCardinality)}) — {r.ToTable}[{r.ToColumn}] ({Cardinality(r.ToCardinality)})\n{(r.FilterDirection == "BothDirections" ? "Filters in both directions" : "Filters from " + r.ToTable + " to " + r.FromTable)} · {(r.IsActive ? "Active" : "Inactive")}",
                Tag = r
            };
            if (!r.IsActive) path.StrokeDashArray = new DoubleCollection { 5, 4 };
            canvas.Children.Add(path);
            Label(canvas, Cardinality(r.FromCardinality), start.X + (leftToRight || sameColumn ? 10 : -25), start.Y - 28);
            Label(canvas, Cardinality(r.ToCardinality), end.X + (leftToRight && !sameColumn ? -25 : 10), end.Y - 28);
            // In a single-direction TOM relationship, the To side filters the From side.
            Arrow(canvas, start, sameColumn || leftToRight ? -1 : 1, brush);
            if (r.FilterDirection == "BothDirections") Arrow(canvas, end, leftToRight && !sameColumn ? 1 : -1, brush);
            edgeIndex++;
        }
        foreach (var table in graph.Tables)
        {
            var p = positions[table.Name];
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = table.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Color("#15232D") });
            content.Children.Add(new TextBlock { Text = $"{table.Role.ToUpperInvariant()} · {table.MeasureCount} measures", FontSize = 10, Margin = new Thickness(0, 5, 0, 10), Foreground = Color("#637988") });
            foreach (var column in table.Columns.Take(4)) content.Children.Add(new TextBlock { Text = column, Margin = new Thickness(0, 2, 0, 2), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            if (table.Columns.Count > 4) content.Children.Add(new TextBlock { Text = $"+ {table.Columns.Count - 4} columns", FontSize = 11, Foreground = Color("#637988") });
            var button = new Button
            {
                Content = content, Width = NodeWidth, Height = NodeHeight, Padding = new Thickness(13),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                Background = Brushes.White, BorderBrush = RoleBrush(table.Role), BorderThickness = new Thickness(2),
                ToolTip = $"{table.Name}\n{table.Columns.Count} columns · {table.MeasureCount} measures\n{table.Role} role inferred from relationship cardinality.\nSelect in Model", Tag = table.Object
            };
            System.Windows.Automation.AutomationProperties.SetName(button, "Select table " + table.Name);
            button.Click += (_, _) => select(table.Object);
            Canvas.SetLeft(button, p.X); Canvas.SetTop(button, p.Y); canvas.Children.Add(button);
        }
        if (graph.Tables.Count == 0)
        {
            var empty = new TextBlock { Text = "This model has no tables. Create a table in Model to begin.", Foreground = Color("#637988"), TextWrapping = TextWrapping.Wrap, Width = 350 };
            Canvas.SetLeft(empty, 40); Canvas.SetTop(empty, 50); canvas.Children.Add(empty);
        }
    }

    internal static Brush RoleBrush(string role) => Color(role == "Fact" ? "#173C52" : role == "Dimension" ? "#E7BE24" : "#A7B4BA");
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
