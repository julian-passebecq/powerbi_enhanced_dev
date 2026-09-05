using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PbiBench.DesignSystem;

/// <summary>Original PbiBench vectors and shared light-first tokens. No fonts or upstream icon assets.</summary>
public static class PbiBenchTheme
{
    public static SolidColorBrush Background { get; } = Brush("#F5F6F8");
    public static SolidColorBrush Surface { get; } = Brush("#FFFFFF");
    public static SolidColorBrush Border { get; } = Brush("#DDE1E6");
    public static SolidColorBrush Text { get; } = Brush("#20252D");
    public static SolidColorBrush Secondary { get; } = Brush("#626B78");
    public static SolidColorBrush Accent { get; } = Brush("#315DA8");
    public static SolidColorBrush Brush(string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
    public static void Apply(Window window)
    {
        window.Background = Background; window.Foreground = Text; window.FontFamily = new("Segoe UI"); window.FontSize = 13; window.UseLayoutRounding = true;
        Install(window.Resources);
    }
    public static void Install(ResourceDictionary resources)
    {
        resources["PbiBench.Background"] = Background; resources["PbiBench.Surface"] = Surface;
        resources["PbiBench.Border"] = Border; resources["PbiBench.Text"] = Text; resources["PbiBench.Secondary"] = Secondary; resources["PbiBench.Accent"] = Accent;
        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 5, 12, 5)));
        button.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 8)));
        button.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32d));
        button.Setters.Add(new Setter(Control.BackgroundProperty, Surface)); button.Setters.Add(new Setter(Control.BorderBrushProperty, Border)); button.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        resources[typeof(Button)] = button;
        var text = new Style(typeof(TextBlock)); text.Setters.Add(new Setter(TextBlock.ForegroundProperty, Text)); resources[typeof(TextBlock)] = text;
        var grid = new Style(typeof(DataGrid)); grid.Setters.Add(new Setter(DataGrid.AutoGenerateColumnsProperty, false)); grid.Setters.Add(new Setter(DataGrid.IsReadOnlyProperty, true)); grid.Setters.Add(new Setter(DataGrid.CanUserAddRowsProperty, false));
        grid.Setters.Add(new Setter(DataGrid.MinRowHeightProperty, 32d)); grid.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty, DataGridGridLinesVisibility.Horizontal)); grid.Setters.Add(new Setter(Control.BorderBrushProperty, Border)); resources[typeof(DataGrid)] = grid;
    }
    private static readonly IReadOnlyDictionary<string, string> Vectors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Home"] = "M3,11 L12,3 21,11 M6,10 L6,21 10,21 10,15 14,15 14,21 18,21 18,10",
        ["Model"] = "M4,6 C4,2 20,2 20,6 C20,10 4,10 4,6 L4,18 C4,22 20,22 20,18 L20,6 M4,12 C4,16 20,16 20,12",
        ["DAX"] = "M8,5 L2,12 8,19 M16,5 L22,12 16,19 M14,3 L10,21",
        ["Automate"] = "M13,2 L4,14 11,14 10,22 20,9 13,9 Z",
        ["Report"] = "M3,3 L3,21 22,21 M7,17 L7,12 10,12 10,17 Z M13,17 L13,7 16,7 16,17 Z M19,17 L19,3 22,3 22,17 Z",
        ["Project"] = "M2,6 L2,20 22,20 22,7 11,7 9,4 2,4 Z M11,10 L8,13 11,16 M15,10 L18,13 15,16",
        ["Fabric"] = "M6,19 C0,19 0,10 6,10 C7,1 19,2 19,10 C25,10 25,19 19,19 Z",
        ["Tools"] = "M7,2 L7,8 M17,2 L17,8 M4,8 L20,8 20,13 15,17 15,21 9,21 9,17 4,13 Z",
        ["Theme"] = "M12,2 C-1,2 -1,22 12,22 C18,22 10,16 17,15 C27,16 23,2 12,2 Z M6,8 L7,8 M11,5 L12,5 M17,7 L18,7",
        ["Git"] = "M7,5 A2,2 0 1 1 3,5 A2,2 0 1 1 7,5 M5,7 L5,18 M7,20 A2,2 0 1 1 3,20 A2,2 0 1 1 7,20 M19,5 A2,2 0 1 1 15,5 A2,2 0 1 1 19,5 M17,7 C17,15 5,10 5,18",
        ["Quality"] = "M12,2 L21,6 20,15 12,22 4,15 3,6 Z M7,12 L10,15 17,8",
        ["External"] = "M13,3 L21,3 21,11 M21,3 L10,14 M9,5 L3,5 3,21 19,21 19,15",
        ["Settings"] = "M3,6 L21,6 M3,12 L21,12 M3,18 L21,18 M8,3 L8,9 M16,9 L16,15 M8,15 L8,21",
        ["About"] = "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M12,10 L12,18 M12,6 L12,7"
    };
    public static IReadOnlyList<string> IconNames => Vectors.Keys.ToArray();
    public static FrameworkElement Icon(string name, double size = 20)
    {
        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(Vectors.TryGetValue(name, out var data) ? data : Vectors["External"]),
            Stroke = Accent, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stretch = Stretch.Uniform };
        return new Viewbox { Width = size, Height = size, Child = path, Margin = new Thickness(0, 0, 12, 0) };
    }
    public static StackPanel Label(string icon, string text)
    { var panel = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(Icon(icon)); panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }); return panel; }
    public static Border Header(string module, string context)
    {
        var panel = new StackPanel(); var title = Label(module, "PbiBench / " + module); title.Margin = new Thickness(0, 0, 0, 8); panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = context, Foreground = Secondary, TextWrapping = TextWrapping.Wrap });
        return new Border { Background = Surface, BorderBrush = Border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16), Child = panel };
    }
}
