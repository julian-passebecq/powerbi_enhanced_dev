using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PbiBench.DesignExchange;
using PbiBench.DesignSystem;

namespace PbiBench.ReportStudio;

/// <summary>Read-only design intent renderer. Does not construct PBIR actions, evaluate expressions or load remote assets.</summary>
public sealed class DesignPreviewView : UserControl
{
    private readonly Canvas canvas = new() { Background = Brushes.White, ClipToBounds = true };
    private readonly ListBox pages = new() { DisplayMemberPath = "Title", MinWidth = 180 };
    private readonly ScaleTransform scale = new(.75, .75);
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16, 0, 0, 0) };
    private readonly DesignPackage package;
    public int VisualCount => canvas.Children.Count;
    public DesignPackage Package => package;
    public DesignPreviewView(DesignPackage package)
    {
        if (!package.IsValid) throw new InvalidOperationException("Only a validated design can be previewed.");
        this.package = package;
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = package.Dashboard?.Spec?.Report.Title ?? "Theme preview", FontSize = 26, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Design Preview · Proposed layout · " + package.Model.Model.Name +
            (package.Dashboard?.Spec?.Unbound == true ? " · Unbound" : " · Validated bindings") + "\n" +
            (package.Dashboard?.Spec?.Report.Audience is { } audience ? "Audience: " + audience + "\n" : "") +
            "This is design intent, not a Desktop rendering. PBIR generation and apply are unavailable here.", TextWrapping = TextWrapping.Wrap, Foreground = PbiBenchTheme.Secondary, Margin = new Thickness(0, 8, 0, 12) });
        if (package.Theme is { } theme)
        {
            header.Children.Add(new TextBlock { Text = "Theme: " + theme.Name + " · " + theme.SchemaVersion + " · Schema valid\nRecognized visual styles: " + string.Join(", ", theme.VisualStyleFamilies), TextWrapping = TextWrapping.Wrap });
            var colors = new WrapPanel { Margin = new Thickness(0, 8, 0, 12) }; header.Children.Add(colors);
            foreach (var color in theme.DataColors)
            {
                Brush fill = Brushes.Transparent;
                // Only local color values are rendered. Theme image URLs, fonts and expressions are never resolved.
                if (color.StartsWith("#", StringComparison.Ordinal) && color.Length is 4 or 7 or 9)
                { try { fill = PbiBenchTheme.Brush(color); } catch (FormatException) { } }
                colors.Children.Add(new Border { Width = 28, Height = 28, Background = fill, BorderBrush = PbiBenchTheme.Border, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), ToolTip = color });
            }
        }
        var evidence = new TabControl { Height = 165, Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(evidence, Dock.Bottom); root.Children.Add(evidence);
        evidence.Items.Add(new TabItem { Header = "Binding validity", Content = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, ItemsSource = package.Dashboard?.Bindings } });
        evidence.Items.Add(new TabItem { Header = "Diagnostics", Content = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, ItemsSource = (package.Dashboard?.Diagnostics ?? Array.Empty<DesignDiagnostic>()).Concat(package.Theme?.Diagnostics ?? Array.Empty<DesignDiagnostic>()).ToArray() } });
        var zoom = new WrapPanel(); var zoomLabel = new TextBlock { Text = "75%", Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
        void ZoomButton(string label, Func<double> factor)
        {
            var button = new Button { Content = label }; button.Click += (_, _) => { scale.ScaleX = scale.ScaleY = Math.Max(.1, Math.Min(2, factor())); zoomLabel.Text = Math.Round(scale.ScaleX * 100) + "%"; }; zoom.Children.Add(button);
        }
        ZoomButton("−", () => scale.ScaleX - .125); ZoomButton("+", () => scale.ScaleX + .125); ZoomButton("100%", () => 1);
        ZoomButton("Fit page", () => Math.Min(Math.Max(1, viewport.ViewportWidth - 24) / canvas.Width, Math.Max(1, viewport.ViewportHeight - 24) / canvas.Height));
        zoom.Children.Add(zoomLabel); header.Children.Add(zoom);
        DockPanel.SetDock(pages, Dock.Left); root.Children.Add(pages);
        canvas.LayoutTransform = scale; canvas.HorizontalAlignment = HorizontalAlignment.Left; canvas.VerticalAlignment = VerticalAlignment.Top;
        viewport.Content = new Border { BorderBrush = PbiBenchTheme.Border, BorderThickness = new Thickness(1), Child = canvas, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }; root.Children.Add(viewport);
        pages.ItemsSource = package.Dashboard?.Spec?.Pages; pages.SelectionChanged += (_, _) => Render(pages.SelectedItem as DesignPage); pages.SelectedIndex = 0;
        if (package.Dashboard == null) Render(null);
    }
    public void SelectPage(int index) => pages.SelectedIndex = index;
    private void Render(DesignPage? page)
    {
        canvas.Children.Clear(); canvas.Width = page?.Canvas.Width ?? 1280; canvas.Height = page?.Canvas.Height ?? 720;
        if (page == null) return;
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var visual in page.Visuals)
        {
            var region = visual.Region ?? "middle"; groups.TryGetValue(region, out var offset); groups[region] = offset + 1;
            var position = visual.Position ?? new DesignPosition(16 + offset % 3 * (canvas.Width - 32) / 3,
                16 + (region == "top" ? 0 : region == "bottom" ? 2 : 1) * (canvas.Height - 32) / 3, (canvas.Width - 56) / 3, (canvas.Height - 56) / 3);
            var supported = DashboardValidator.SupportedKinds.Contains(visual.Kind, StringComparer.Ordinal);
            var text = new StackPanel(); text.Children.Add(new TextBlock { Text = visual.Id, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = visual.Kind + (supported ? " · proposed" : " · UNSUPPORTED"), Margin = new Thickness(0, 8, 0, 4) });
            text.Children.Add(new TextBlock { Text = visual.Purpose ?? "", TextWrapping = TextWrapping.Wrap, Foreground = PbiBenchTheme.Secondary });
            text.Children.Add(new TextBlock { Text = string.Join("\n", package.Dashboard!.Bindings.Where(b => b.Visual == visual.Id).Select(b => b.Field + " · " + b.Status)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontSize = 11 });
            var card = new Border { Width = position.Width, Height = position.Height, Child = text, Padding = new Thickness(12), ClipToBounds = true,
                Background = PbiBenchTheme.Background, BorderBrush = supported ? PbiBenchTheme.Border : Brushes.DarkOrange, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ToolTip = visual.Id + " · " + (visual.Region ?? "explicit position") };
            Canvas.SetLeft(card, position.X); Canvas.SetTop(card, position.Y); canvas.Children.Add(card);
        }
    }
}
