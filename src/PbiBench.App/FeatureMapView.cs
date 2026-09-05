using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PbiBench.Core.Platform;

namespace PbiBench.App;

/// <summary>Read-only, offline product map. Detailed ownership is joined from the existing provenance ledger.</summary>
public sealed class FeatureMapView : UserControl
{
    private readonly FeatureCatalog catalog;
    private readonly ProvenanceCatalog provenance;
    private readonly DataGrid grid = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, EnableRowVirtualization = true };
    private readonly TextBox detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        BorderThickness = new Thickness(0), MinHeight = 85, MaxHeight = 150, Padding = new Thickness(5) };
    private readonly TextBlock count = new() { Margin = new Thickness(10, 6, 0, 6) };
    private readonly Dictionary<FeatureMapFilter, RadioButton> filters = new();
    public event EventHandler? DocumentationRequested;
    internal IReadOnlyList<FeatureMapRow> VisibleRows => (IReadOnlyList<FeatureMapRow>)grid.ItemsSource;
    internal string SelectedDetail => detail.Text;
    internal DataGrid FeatureGrid => grid;
    internal FeatureMapFilter CurrentFilter { get; private set; }

    public FeatureMapView(FeatureCatalog catalog, ProvenanceCatalog provenance)
    {
        this.catalog = catalog; this.provenance = provenance;
        var root = new DockPanel { Margin = new Thickness(14) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Feature Map", FontSize = 24, Margin = new Thickness(2, 0, 0, 5) });
        header.Children.Add(Note("PbiBench " + catalog.ProductVersion + " · Offline product catalog · TE3 " + catalog.Comparison.VerifiedVersion + " comparison verified " + catalog.Comparison.VerifiedDate));
        var toolbar = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) }; header.Children.Add(toolbar);
        foreach (var pair in new[] { (FeatureMapFilter.All, "All"), (FeatureMapFilter.Core, "Core"), (FeatureMapFilter.Companions, "Companions"), (FeatureMapFilter.Labs, "Labs"), (FeatureMapFilter.Te3Gaps, "TE3 gaps") })
        {
            var button = new RadioButton { Content = pair.Item2, Margin = new Thickness(4, 6, 12, 6), Padding = new Thickness(2), GroupName = "FeatureMapFilter" };
            button.Checked += (_, _) => ApplyFilter(pair.Item1); filters.Add(pair.Item1, button); toolbar.Children.Add(button);
        }
        toolbar.Children.Add(count);
        var documentation = new Button { Content = "Open detailed catalog", Margin = new Thickness(16, 0, 0, 0), Padding = new Thickness(9, 5, 9, 5) };
        documentation.Click += (_, _) => DocumentationRequested?.Invoke(this, EventArgs.Empty); toolbar.Children.Add(documentation);
        header.Children.Add(Note("Companions includes external tools. Labs includes incubating and future work. TE3 gaps includes Partial and Gap comparisons."));

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(new TextBlock { Text = "Selected feature", FontWeight = FontWeights.SemiBold }); footer.Children.Add(detail);
        footer.Children.Add(Note(FeatureCatalog.ComparisonNotice));
        AddColumn("Feature", nameof(FeatureMapRow.Name), 1.25, 180, bold: true);
        AddColumn("Status", nameof(FeatureMapRow.Status), .6, 82, bold: true);
        AddColumn("Origin", nameof(FeatureMapRow.Origin), 1.05, 155);
        AddColumn("Our implementation", nameof(FeatureMapRow.Implementation), 1.8, 220);
        AddColumn("TE3 comparable capability", nameof(FeatureMapRow.Te3), 1.45, 200);
        AddColumn("Lifecycle", nameof(FeatureMapRow.Lifecycle), .9, 130);
        grid.SelectionChanged += (_, _) => detail.Text = grid.SelectedItem is FeatureMapRow row ? row.Detail +
            (row.Feature.Te3.SourceUrl == null ? "" : "\nTE3 public reference: " + row.Feature.Te3.SourceUrl) : "";
        root.Children.Add(grid); SelectFilter(FeatureMapFilter.All);
    }
    internal void SelectFilter(FeatureMapFilter filter) => filters[filter].IsChecked = true;
    private void ApplyFilter(FeatureMapFilter filter)
    {
        var selectedId = (grid.SelectedItem as FeatureMapRow)?.Feature.Id;
        CurrentFilter = filter; var rows = catalog.Rows(provenance, filter); grid.ItemsSource = rows;
        grid.SelectedItem = rows.FirstOrDefault(r => r.Feature.Id == selectedId) ?? rows.FirstOrDefault();
        count.Text = rows.Count + " / " + catalog.Features.Count + " areas";
    }
    private void AddColumn(string header, string property, double width, double minimum, bool bold = false)
    {
        var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(5, 7, 5, 7)));
        if (bold) style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = new DataGridLength(width, DataGridLengthUnitType.Star), MinWidth = minimum, ElementStyle = style });
    }
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 2, 5) };
}

public sealed class FeatureMapWindow : Window
{
    internal FeatureMapView Map { get; }
    internal TabControl Pages { get; } = new();
    public FeatureMapWindow()
    {
        var provenance = ProvenanceCatalog.Bundled(); var catalog = FeatureCatalog.Bundled(provenance);
        Title = "PbiBench " + catalog.ProductVersion + " · Feature Map / Provenance";
        Width = 1360; Height = 850; MinWidth = 960; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Map = new(catalog, provenance);
        Map.DocumentationRequested += (_, _) =>
        {
            try
            {
                var text = ReadDetailedCatalog(AppDomain.CurrentDomain.BaseDirectory);
                new Window { Owner = this, Title = "PbiBench · Detailed feature catalog", Width = 1050, Height = 760, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new TextBox { Text = text, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 13,
                        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12) } }.ShowDialog();
            }
            catch (IOException error) { MessageBox.Show(this, error.Message, "Detailed catalog", MessageBoxButton.OK, MessageBoxImage.Information); }
        };
        Pages.Items.Add(new TabItem { Header = "Feature Map", Content = Map });
        Pages.Items.Add(new TabItem { Header = "Provenance / About", Content = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false,
            ItemsSource = provenance.Components.Select(c => new { c.Feature, c.OwnerProject, c.SourceType, c.Pin, c.License, c.UpdateLane,
                Patches = string.Join("; ", c.LocalPatches), Tests = string.Join("; ", c.ProtectingTests) }).ToArray() } });
        Pages.SelectedIndex = 0; Content = Pages;
    }
    internal static string ReadDetailedCatalog(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "docs", "architecture", "FEATURE_CATALOG.md");
        using var stream = File.OpenRead(path); if (stream.Length > 1024 * 1024) throw new InvalidDataException("Detailed catalog exceeds 1 MiB.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
