using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PbiBench.App;
public sealed record PreviewRow(string Object, string Property, string Before, string After, string Reason);
internal static class PreviewDialog
{
    public static bool Show(Window owner, string title, string detail, IReadOnlyList<PreviewRow> rows, bool canApply, string applyLabel)
    {
        var window = new Window { Owner = owner, Title = "Preview · " + title, Width = Math.Min(1150, SystemParameters.WorkArea.Width - 50), Height = Math.Min(720, SystemParameters.WorkArea.Height - 50), MinWidth = 700, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(18) };
        var heading = new TextBlock { Text = title + $" · {rows.Count} change(s)\n\n" + detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var cancel = new Button { Content = "Close preview", IsCancel = true, Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 10, 0) };
        var apply = new Button { Content = applyLabel, IsEnabled = canApply, Padding = new Thickness(18, 8, 18, 8) };
        apply.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(apply); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, EnableRowVirtualization = true };
        foreach (var property in new[] { "Object", "Property", "Before", "After", "Reason" })
        {
            var textStyle = new Style(typeof(TextBlock)); textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)); textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(5)));
            grid.Columns.Add(new DataGridTextColumn { Header = property, Binding = new Binding(property), Width = new DataGridLength(property == "Property" ? 0.6 : 1, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        }
        panel.Children.Add(grid); window.Content = panel;
        return window.ShowDialog() == true;
    }
}
