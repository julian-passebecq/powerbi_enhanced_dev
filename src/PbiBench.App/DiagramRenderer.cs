using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;
internal static class DiagramRenderer
{
    internal static void Render(Canvas canvas, ModelGraph graph, Action<Table> select)
    {
        const double nodeWidth = 245, rowHeight = 225;
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var rows = new int[3];
        foreach (var table in graph.Tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var column = table.Role == "Fact" ? 0 : table.Role == "Dimension" ? 1 : 2;
            positions[table.Name] = new Point(30 + column * 425, 35 + rows[column]++ * rowHeight);
        }
        canvas.Width = Math.Max(750, positions.Values.Select(p => p.X + nodeWidth + 40).DefaultIfEmpty(750).Max());
        canvas.Height = Math.Max(430, rows.Max() * rowHeight + 60);
        var edgeIndex = 0;
        foreach (var r in graph.Relationships)
        {
            var from = positions[r.FromTable]; var to = positions[r.ToTable];
            var leftToRight = from.X < to.X;
            var start = new Point(from.X + (leftToRight ? nodeWidth : 0), from.Y + 68 + edgeIndex % 4 * 17);
            var end = new Point(to.X + (leftToRight ? 0 : nodeWidth), to.Y + 68 + edgeIndex % 4 * 17);
            var sameColumn = from.X == to.X;
            if (sameColumn) { start.X = from.X + nodeWidth; end.X = to.X + nodeWidth; }
            var controlX = sameColumn ? start.X + 70 + edgeIndex % 4 * 18 : (start.X + end.X) / 2;
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new BezierSegment(new Point(controlX, start.Y), new Point(controlX, end.Y), end, true));
            var brush = r.IsActive ? Brushes.SlateGray : Brushes.DarkGray;
            var path = new System.Windows.Shapes.Path { Data = new PathGeometry(new[] { figure }), Stroke = brush, StrokeThickness = r.IsActive ? 2.2 : 1.6, ToolTip = $"{r.Name}\n{r.FromTable}[{r.FromColumn}] → {r.ToTable}[{r.ToColumn}]\n{r.FilterDirection} · {(r.IsActive ? "Active" : "Inactive")}" };
            if (!r.IsActive) path.StrokeDashArray = new DoubleCollection { 5, 4 };
            canvas.Children.Add(path);
            Label(canvas, r.FromCardinality == "Many" ? "*" : "1", start.X + (leftToRight || sameColumn ? 6 : -18), start.Y - 25);
            Label(canvas, r.ToCardinality == "Many" ? "*" : "1", end.X + (leftToRight && !sameColumn ? -18 : 6), end.Y - 25);
            Arrow(canvas, start, sameColumn || leftToRight ? -1 : 1, brush);
            if (r.FilterDirection == "BothDirections") Arrow(canvas, end, leftToRight && !sameColumn ? 1 : -1, brush);
            edgeIndex++;
        }
        foreach (var t in graph.Tables)
        {
            var p = positions[t.Name];
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = t.Name, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = $"{t.Role.ToUpperInvariant()} · {t.MeasureCount} measures", FontSize = 10, Margin = new Thickness(0, 5, 0, 12), Foreground = Brushes.SlateGray });
            foreach (var column in t.Columns.Take(4)) content.Children.Add(new TextBlock { Text = column, Margin = new Thickness(0, 2, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis });
            if (t.Columns.Count > 4) content.Children.Add(new TextBlock { Text = $"+ {t.Columns.Count - 4} columns", Foreground = Brushes.SlateGray });
            var button = new Button { Content = content, Width = nodeWidth, MinHeight = 155, Padding = new Thickness(15), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.White,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString(t.Role == "Fact" ? "#508AAB" : t.Role == "Dimension" ? "#D6B24D" : "#A7B4BA")!, BorderThickness = new Thickness(2), ToolTip = "Select " + t.Name + " in Model", Tag = t.Object };
            button.Click += (_, _) => select(t.Object);
            Canvas.SetLeft(button, p.X); Canvas.SetTop(button, p.Y); canvas.Children.Add(button);
        }
    }
    private static void Label(Canvas canvas, string text, double x, double y)
    {
        var label = new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.Bold, Background = Brushes.White, Padding = new Thickness(2) };
        Canvas.SetLeft(label, x); Canvas.SetTop(label, y); canvas.Children.Add(label);
    }
    private static void Arrow(Canvas canvas, Point tip, int direction, Brush color)
        => canvas.Children.Add(new Polygon { Fill = color, Points = new PointCollection { tip, new(tip.X - direction * 9, tip.Y - 5), new(tip.X - direction * 9, tip.Y + 5) } });
}
