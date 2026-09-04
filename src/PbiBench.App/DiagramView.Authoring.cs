using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public sealed partial class DiagramView
{
    private sealed record ColumnChoice(Column Column)
    {
        public override string ToString() => SemanticModelService.ObjectPath(Column) + " · " + Column.DataType;
    }
    private sealed record TableChoice(TableGroupEntry Entry)
    {
        public override string ToString() => Entry.Table.Name + " — " + (Entry.Issue != null ? "annotation needs repair" : Entry.Group ?? "(ungrouped)");
    }

    public void EditRelationship(SingleColumnRelationship relationship)
    {
        var handler = authoringHandler;
        if (handler == null) { ShowAuthoringError(new InvalidOperationException("Open a model before editing relationships.")); return; }
        if (!handler.Model.Relationships.Any(item => ReferenceEquals(item, relationship))) { ShowAuthoringError(new InvalidOperationException("This relationship belongs to a previous model session.")); return; }
        var captured = RelationshipAuthoringService.Capture(relationship);
        var dialog = Dialog("Relationship editor", 790, 650);
        var layout = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var fields = new StackPanel();
        fields.Children.Add(new TextBlock { Text = "Edit the relationship in the current model, then review each metadata change. Save and deploy remain separate operations.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        fields.Children.Add(new TextBlock { Text = "Relationship ID: " + relationship.ID, FontSize = 11, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var columns = handler.Model.Tables.OrderBy(table => table.Name).SelectMany(table => table.Columns.OrderBy(column => column.Name)).Select(column => new ColumnChoice(column)).ToArray();
        var from = new ComboBox { ItemsSource = columns, SelectedItem = columns.FirstOrDefault(item => item.Column == captured.FromColumn), MinHeight = 30 };
        var to = new ComboBox { ItemsSource = columns, SelectedItem = columns.FirstOrDefault(item => item.Column == captured.ToColumn), MinHeight = 30 };
        var fromCardinality = Choice(new[] { RelationshipEndCardinality.Many, RelationshipEndCardinality.One }, captured.FromCardinality);
        var toCardinality = Choice(new[] { RelationshipEndCardinality.One, RelationshipEndCardinality.Many }, captured.ToCardinality);
        var direction = Choice(Enum.GetValues(typeof(CrossFilteringBehavior)).Cast<CrossFilteringBehavior>(), captured.CrossFilteringBehavior);
        var security = Choice(Enum.GetValues(typeof(SecurityFilteringBehavior)).Cast<SecurityFilteringBehavior>(), captured.SecurityFilteringBehavior);
        var date = Choice(Enum.GetValues(typeof(DateTimeRelationshipBehavior)).Cast<DateTimeRelationshipBehavior>(), captured.JoinOnDateBehavior);
        var active = new CheckBox { Content = "Active relationship", IsChecked = captured.IsActive, Margin = new Thickness(0, 8, 0, 8) };
        var integrity = new CheckBox { Content = "Assume referential integrity (DirectQuery)", IsChecked = captured.RelyOnReferentialIntegrity, Margin = new Thickness(0, 8, 0, 8) };
        fields.Children.Add(Field("From column", from)); fields.Children.Add(Field("To column", to));
        fields.Children.Add(Field("From cardinality", fromCardinality)); fields.Children.Add(Field("To cardinality", toCardinality));
        fields.Children.Add(Field("Cross-filter direction", direction));
        fields.Children.Add(new TextBlock { Text = "OneDirection means To filters From. Inverting endpoints reverses that flow. Automatic is resolved by the server.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 2, 0, 8) });
        fields.Children.Add(Field("Security filtering", security)); fields.Children.Add(Field("Date joining", date)); fields.Children.Add(active); fields.Children.Add(integrity);
        var invert = Command("Invert endpoints", () =>
        {
            var old = from.SelectedItem; from.SelectedItem = to.SelectedItem; to.SelectedItem = old;
            var oldCardinality = fromCardinality.SelectedItem; fromCardinality.SelectedItem = toCardinality.SelectedItem; toCardinality.SelectedItem = oldCardinality;
        }, "Swap both endpoints and their cardinalities in this draft; preview before applying.");
        footer.Children.Add(invert);
        footer.Children.Add(Command("Preview changes…", () =>
        {
            try
            {
                if (from.SelectedItem is not ColumnChoice fromColumn || to.SelectedItem is not ColumnChoice toColumn) throw new InvalidOperationException("Choose both relationship columns.");
                if (fromCardinality.SelectedItem == null || toCardinality.SelectedItem == null || direction.SelectedItem == null || security.SelectedItem == null || date.SelectedItem == null)
                    throw new InvalidOperationException("Choose both cardinalities and each filter/date setting before previewing.");
                var request = new RelationshipDefinition(fromColumn.Column, toColumn.Column,
                    (RelationshipEndCardinality)fromCardinality.SelectedItem, (RelationshipEndCardinality)toCardinality.SelectedItem,
                    (CrossFilteringBehavior)direction.SelectedItem, active.IsChecked == true,
                    (SecurityFilteringBehavior)security.SelectedItem, (DateTimeRelationshipBehavior)date.SelectedItem, integrity.IsChecked == true);
                var preview = new RelationshipAuthoringService(handler).Preview(relationship, request);
                if (AuthoringReview.Show(dialog, preview, () => authoringHandler, () => metadataChanged?.Invoke())) dialog.Close();
            }
            catch (Exception ex) { ShowAuthoringError(ex, dialog); }
        }, "Review the exact fields, validation errors and semantic implications before applying locally."));
        footer.Children.Add(Command("Close", dialog.Close, "Close without applying the draft"));
        layout.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = layout; dialog.ShowDialog();
    }

    public void EditTableGroups(Table? focusedTable = null)
    {
        var handler = authoringHandler;
        if (handler == null) { ShowAuthoringError(new InvalidOperationException("Open a model before assigning table groups.")); return; }
        var service = new TableGroupService(handler);
        var entries = service.Read();
        var dialog = Dialog("Table groups", 720, 640);
        var layout = new DockPanel { Margin = new Thickness(18) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "Virtual groups organize PbiBench's diagram. Assignments are saved as PbiBench annotations on the tables and participate in TE2 Undo. Select one or more tables below.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        var names = entries.Where(entry => entry.Group != null).Select(entry => entry.Group!).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var name = new ComboBox { IsEditable = true, ItemsSource = names, MinHeight = 30, Text = focusedTable == null ? "" : TableGroupService.Read(focusedTable).Group ?? "" };
        heading.Children.Add(Field("Assign group", name));
        var choices = entries.OrderBy(entry => entry.Table.Name, StringComparer.OrdinalIgnoreCase).Select(entry => new TableChoice(entry)).ToArray();
        var tables = new ListBox { ItemsSource = choices, SelectionMode = SelectionMode.Extended, Margin = new Thickness(0, 10, 0, 10) };
        if (focusedTable != null) tables.SelectedItem = choices.FirstOrDefault(choice => ReferenceEquals(choice.Entry.Table, focusedTable));
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Command("Select all", tables.SelectAll, "Select every table for a bulk group assignment"));
        void Review(Func<AuthoringPreview> create)
        {
            try { if (AuthoringReview.Show(dialog, create(), () => authoringHandler, () => metadataChanged?.Invoke())) dialog.Close(); }
            catch (Exception ex) { ShowAuthoringError(ex, dialog); }
        }
        buttons.Children.Add(Command("Preview assignment…", () => Review(() => service.PreviewAssign(tables.SelectedItems.Cast<TableChoice>().Select(choice => choice.Entry.Table), name.Text)), "Review selected tables. A blank group removes their assignment."));
        buttons.Children.Add(Command("Close", dialog.Close, "Close without changing groups"));
        footer.Children.Add(buttons);
        var management = new StackPanel { Margin = new Thickness(0, 16, 0, 8) };
        var existing = new ComboBox { ItemsSource = names, SelectedIndex = names.Length > 0 ? 0 : -1, MinHeight = 30 };
        var renamed = new TextBox { MinHeight = 30, MaxLength = TableGroupService.MaximumGroupLength };
        management.Children.Add(Field("Existing group", existing)); management.Children.Add(Field("Rename to", renamed));
        var managementButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        managementButtons.Children.Add(Command("Preview rename…", () => Review(() => service.PreviewRename(existing.SelectedItem as string ?? "", renamed.Text)), "Review changes for every table in the selected group."));
        managementButtons.Children.Add(Command("Preview remove group…", () => Review(() => service.PreviewRemove(existing.SelectedItem as string ?? "")), "Review removing the assignment from every table in this group."));
        management.Children.Add(managementButtons); footer.Children.Add(management);
        if (entries.Any(entry => entry.Issue != null)) footer.Children.Add(new TextBlock { Text = string.Join("\n", entries.Where(entry => entry.Issue != null).Select(entry => entry.Table.Name + ": " + entry.Issue)), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed, MaxHeight = 100 });
        layout.Children.Add(tables); dialog.Content = layout; dialog.ShowDialog();
    }

    private Window Dialog(string title, double width, double height) => new()
    {
        Title = "PbiBench — " + title, Owner = Window.GetWindow(this), Width = width, Height = height,
        MinWidth = 520, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false, Background = Brushes.White
    };
    private static ComboBox Choice<T>(IEnumerable<T> values, T selected) => new() { ItemsSource = values.ToArray(), SelectedItem = selected, MinHeight = 30 };
    private static FrameworkElement Field(string label, Control control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) });
        Grid.SetColumn(control, 1); grid.Children.Add(control);
        System.Windows.Automation.AutomationProperties.SetName(control, label);
        return grid;
    }
    private void ShowAuthoringError(Exception exception, Window? owner = null)
    {
        var window = owner ?? Window.GetWindow(this);
        if (window == null) MessageBox.Show(exception.Message, "PbiBench", MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(window, exception.Message, "PbiBench", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
