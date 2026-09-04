using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PbiBench.Core.DataExploration;
using PbiBench.Core.Queries;

namespace PbiBench.App;

public sealed class PivotLabView : UserControl, IDisposable
{
    private readonly Func<DataModelSchema> schema;
    private readonly DataQueryView query;
    private readonly Func<string> artifactDirectory;
    private readonly ListBox fields = new() { MinHeight = 100 };
    private readonly ListBox rows = new();
    private readonly ListBox columns = new();
    private readonly ListBox values = new();
    private readonly ListBox filters = new();
    private readonly List<DataFilter> filterValues = new();
    private readonly CheckBox rowTotals = new() { Content = "Row totals", IsChecked = true };
    private readonly CheckBox columnTotals = new() { Content = "Column totals", IsChecked = true };
    private readonly CheckBox auto = new() { Content = "Auto refresh" };
    private readonly ComboBox aggregation = new() { ItemsSource = Enum.GetValues(typeof(PivotAggregation)), SelectedItem = PivotAggregation.Sum, MinWidth = 90 };
    private readonly TextBox name = new() { Text = "Pivot", MinWidth = 120 };
    private readonly TextBox rowLimit = new() { Text = "1000", Width = 65, ToolTip = "Maximum displayed pivot rows" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 7) };
    private readonly DataGrid matrix = new() { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private PivotQueryPlan? plan;
    private bool loading;
    private bool disposed;
    private Point dragStart;
    public PivotLayout Layout => new()
    {
        Name = name.Text, Rows = rows.Items.Cast<Field>().Select(f => new PivotAxisField(f.Table, f.Name, f.Descending)).ToArray(),
        Columns = columns.Items.Cast<Field>().Select(f => new PivotAxisField(f.Table, f.Name, f.Descending)).ToArray(),
        Values = values.Items.Cast<ValueField>().Select(f => f.Value).ToArray(), Filters = filterValues.ToArray(),
        IncludeRowTotals = rowTotals.IsChecked == true, IncludeColumnTotals = columnTotals.IsChecked == true, AutoRefresh = auto.IsChecked == true,
        RowLimit = int.TryParse(rowLimit.Text, out var limit) ? limit : throw new InvalidOperationException("Choose a numeric pivot row limit.")
    };

    public PivotLabView(Func<DataModelSchema> schema, DataQueryView query, Func<string> artifactDirectory)
    {
        this.schema = schema; this.query = query; this.artifactDirectory = artifactDirectory;
        matrix.AutoGeneratingColumn += (_, e) =>
        {
            if (matrix.ItemsSource is DataView view && view.Table.Columns.Contains(e.PropertyName)) e.Column.Header = view.Table.Columns[e.PropertyName].Caption;
        };
        var root = new DockPanel();
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var actions = new WrapPanel(); top.Children.Add(actions); actions.Children.Add(name);
        actions.Children.Add(Action("Run pivot", async () => { BuildPlan(); await query.RunAsync(); }));
        actions.Children.Add(Action("Cancel", () => { query.Cancel(); return Task.CompletedTask; }));
        actions.Children.Add(Action("Generate DAX", () => { BuildPlan(); return Task.CompletedTask; }));
        actions.Children.Add(Action("Save layout…", SaveLayoutAsync)); actions.Children.Add(Action("Open layout…", OpenLayoutAsync)); actions.Children.Add(Action("Save regression test…", SaveTestAsync));
        actions.Children.Add(rowTotals); actions.Children.Add(columnTotals); actions.Children.Add(auto); top.Children.Add(status);
        actions.Children.Add(new TextBlock { Text = "Rows", Margin = new Thickness(5) }); actions.Children.Add(rowLimit);
        var split = new Grid(); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) }); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); split.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(split);
        var fieldPanel = new DockPanel(); split.Children.Add(fieldPanel);
        var instructions = new TextBlock { Text = "Drag fields to Rows, Columns, Values or Filters.\nSelect a column aggregation below.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 5, 8) }; DockPanel.SetDock(instructions, Dock.Top); fieldPanel.Children.Add(instructions);
        DockPanel.SetDock(aggregation, Dock.Top); fieldPanel.Children.Add(aggregation); fieldPanel.Children.Add(fields);
        var right = new Grid(); Grid.SetColumn(right, 2); split.Children.Add(right); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(135) }); right.RowDefinitions.Add(new RowDefinition());
        var zones = new Grid(); for (var i = 0; i < 4; i++) zones.ColumnDefinitions.Add(new ColumnDefinition()); right.Children.Add(zones);
        AddZone(zones, 0, "Rows", rows, field => AddAxis(rows, field)); AddZone(zones, 1, "Columns", columns, field => AddAxis(columns, field));
        AddZone(zones, 2, "Values", values, AddValue); AddZone(zones, 3, "Filters", filters, AddFilter);
        var outputs = new TabControl(); Grid.SetRow(outputs, 1); right.Children.Add(outputs);
        outputs.Items.Add(new TabItem { Header = "Pivot", Content = matrix }); outputs.Items.Add(new TabItem { Header = "Query / results", Content = query });
        fields.ItemsSource = schema().Tables.SelectMany(t => t.Columns.Select(c => new Field(t.Name, c.Name, false)).Concat(t.Measures.Select(m => new Field(t.Name, m.Name, true)))).ToArray();
        fields.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(fields);
        fields.PreviewMouseMove += (_, e) =>
        {
            var delta = e.GetPosition(fields) - dragStart;
            if (e.LeftButton == MouseButtonState.Pressed && fields.SelectedItem is Field field && (Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance)) DragDrop.DoDragDrop(fields, new DataObject(typeof(Field), field), DragDropEffects.Copy);
        };
        query.RefreshRequested += (_, _) => BuildPlan();
        query.Completed += (_, _) =>
        {
            if (plan == null || query.LastResult?.Query != plan.Dax || query.LastResult.Results.Count == 0) return;
            try { matrix.ItemsSource = PivotMatrix.Create(plan, query.LastResult.Results[0]).DefaultView; status.Text = "Totals are evaluated by the engine. Blank members are distinct from totals. The Query / results tab contains raw rows and CSV export."; }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        foreach (var option in new[] { rowTotals, columnTotals, auto }) { option.Checked += (_, _) => Changed(); option.Unchecked += (_, _) => Changed(); }
        timer.Tick += async (_, _) =>
        {
            timer.Stop(); if (disposed || auto.IsChecked != true) return;
            try { if (query.IsRunning) { query.Cancel(); timer.Start(); return; } BuildPlan(); await query.RunAsync(); }
            catch (OperationCanceledException) { } catch (Exception ex) { status.Text = ex.Message; }
        };
        Content = root; status.Text = "Choose at least one measure or aggregation. Generate DAX, then use Run in Query / results. Auto refresh runs only after you enable it.";
    }
    private Button Action(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4, 0, 4, 6) };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { status.Text = ex.Message; } }; return button;
    }
    private void AddZone(Grid root, int position, string title, ListBox list, Action<Field> add)
    {
        var panel = new DockPanel { Margin = new Thickness(2) }; Grid.SetColumn(panel, position); root.Children.Add(panel);
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
        toolbar.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3) });
        toolbar.Children.Add(Action("+", () => { if (fields.SelectedItem is Field field) add(field); return Task.CompletedTask; }));
        toolbar.Children.Add(Action("−", () => { var selected = list.SelectedIndex; if (selected >= 0) { if (ReferenceEquals(list, filters)) filterValues.RemoveAt(selected); list.Items.RemoveAt(selected); Changed(); } return Task.CompletedTask; }));
        list.AllowDrop = true; list.Drop += (_, e) => { try { if (e.Data.GetData(typeof(Field)) is Field field) add(field); } catch (Exception ex) { status.Text = ex.Message; } }; panel.Children.Add(list);
    }
    private void AddAxis(ListBox target, Field field)
    {
        if (field.Measure) throw new InvalidOperationException("Rows and Columns require columns. Add measures to Values.");
        if (rows.Items.Cast<Field>().Concat(columns.Items.Cast<Field>()).Any(f => f.Table == field.Table && f.Name == field.Name)) return;
        target.Items.Add(field); Changed();
    }
    private void AddValue(Field field)
    {
        var value = new PivotValue(field.Table, field.Name, field.Measure ? PivotAggregation.Measure : (PivotAggregation)aggregation.SelectedItem);
        if (value.Aggregation == PivotAggregation.Measure && !field.Measure) throw new InvalidOperationException("Choose a column aggregation.");
        if (!values.Items.Cast<ValueField>().Any(f => f.Value == value)) { values.Items.Add(new ValueField(value)); Changed(); }
    }
    private void AddFilter(Field field)
    {
        if (field.Measure) throw new InvalidOperationException("Filters require a model column.");
        var text = new TextBox { Margin = new Thickness(0, 8, 0, 12), MinWidth = 260 };
        var op = new ComboBox { ItemsSource = new[] { DataFilterOperator.Equals, DataFilterOperator.NotEquals, DataFilterOperator.GreaterThan, DataFilterOperator.LessThan, DataFilterOperator.Contains, DataFilterOperator.IsBlank, DataFilterOperator.IsNotBlank }, SelectedIndex = 0 };
        var content = new StackPanel { Margin = new Thickness(18) }; content.Children.Add(new TextBlock { Text = field.ToString(), TextWrapping = TextWrapping.Wrap }); content.Children.Add(op); content.Children.Add(text);
        content.Children.Add(new TextBlock { Text = "Numbers use invariant notation; dates use ISO format.", TextWrapping = TextWrapping.Wrap });
        var dialog = new Window { Title = "Pivot filter", Owner = Window.GetWindow(this), Content = content, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var add = new Button { Content = "Add filter", Margin = new Thickness(0, 10, 0, 0) }; content.Children.Add(add); add.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() == true)
        {
            var filter = new DataFilter(field.Table, field.Name, (DataFilterOperator)op.SelectedItem, text.Text);
            DaxDataSyntax.Predicate(filter, schema().GetTable(field.Table).Columns.First(c => c.Name == field.Name));
            filterValues.Add(filter); filters.Items.Add(FilterCaption(filter)); Changed();
        }
    }
    private void Changed()
    {
        if (loading) return;
        query.Cancel(); plan = null; matrix.ItemsSource = null;
        try { BuildPlan(); } catch (Exception ex) { status.Text = ex.Message; query.Invalidate(); }
        timer.Stop(); if (auto.IsChecked == true) timer.Start();
    }
    private void BuildPlan()
    {
        plan = PivotQueryBuilder.Build(Layout, schema()); query.RowLimit = plan.RowLimit; query.SetPlan(plan.Dax, plan.Warnings);
        status.Text = "Generated SUMMARIZECOLUMNS query. Up to " + plan.RowLimit + " rows are displayed; engine work may be larger.";
    }
    private async Task SaveLayoutAsync()
    {
        var dialog = SaveDialog("Pivot layout|*.pivot.json", "pivot.pivot.json"); if (dialog.ShowDialog(Window.GetWindow(this)) == true) await PivotLayoutStore.SaveAsync(dialog.FileName, Layout, CancellationToken.None);
    }
    private async Task OpenLayoutAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Pivot layout|*.pivot.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var layout = await PivotLayoutStore.LoadAsync(dialog.FileName, CancellationToken.None);
        LoadLayout(layout);
    }
    public void LoadLayout(PivotLayout layout)
    {
        PivotQueryBuilder.Build(layout, schema()); loading = true;
        try
        {
            rows.Items.Clear(); columns.Items.Clear(); values.Items.Clear(); filters.Items.Clear(); filterValues.Clear();
            foreach (var field in layout.Rows) rows.Items.Add(new Field(field.Table, field.Column, false, field.Descending));
            foreach (var field in layout.Columns) columns.Items.Add(new Field(field.Table, field.Column, false, field.Descending));
            foreach (var field in layout.Values) values.Items.Add(new ValueField(field));
            foreach (var filter in layout.Filters) { filterValues.Add(filter); filters.Items.Add(FilterCaption(filter)); }
            name.Text = layout.Name; rowLimit.Text = layout.RowLimit.ToString(CultureInfo.InvariantCulture); rowTotals.IsChecked = layout.IncludeRowTotals; columnTotals.IsChecked = layout.IncludeColumnTotals;
            // Opening a saved layout never starts a remote query implicitly.
            auto.IsChecked = false;
        }
        finally { loading = false; }
        Changed();
    }
    private async Task SaveTestAsync()
    {
        if (plan == null || query.LastResult == null) throw new InvalidOperationException("Run the current pivot successfully before saving a regression test.");
        var test = PivotTestArtifact.Create(name.Text, plan, query.LastResult);
        var dialog = SaveDialog("PbiBench regression test|*.pbtest.json", "pivot.pbtest.json");
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await PivotTestArtifact.SaveAsync(dialog.FileName, test, CancellationToken.None);
    }
    private SaveFileDialog SaveDialog(string filter, string file)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = file }; var path = artifactDirectory(); if (Directory.Exists(path)) dialog.InitialDirectory = path; return dialog;
    }
    private static string FilterCaption(DataFilter filter) => $"'{filter.Table}'[{filter.Column}] {filter.Operator} {filter.Value}";
    public void Dispose() { disposed = true; timer.Stop(); query.Dispose(); }
    private sealed record Field(string Table, string Name, bool Measure, bool Descending = false) { public override string ToString() => (Measure ? "∑ " : "") + $"'{Table}'[{Name}]" + (Descending ? " ↓" : ""); }
    private sealed record ValueField(PivotValue Value) { public override string ToString() => Value.Name + (Value.Aggregation == PivotAggregation.Measure ? "" : " · " + Value.Aggregation); }
}

/// <summary>Reshapes engine cells; totals are not recomputed from retained rows.</summary>
public static class PivotMatrix
{
    public static DataTable Create(PivotQueryPlan plan, QueryResultSet result)
    {
        var output = new DataTable();
        var rowFields = plan.ResultColumns.Where(c => c.Role == PivotResultRole.Row).ToArray();
        var columnFields = plan.ResultColumns.Where(c => c.Role == PivotResultRole.Column).ToArray();
        var valueFields = plan.ResultColumns.Where(c => c.Role == PivotResultRole.Value).ToArray();
        var rowFlag = plan.ResultColumns.FirstOrDefault(c => c.Role == PivotResultRole.RowTotalFlag);
        var columnFlag = plan.ResultColumns.FirstOrDefault(c => c.Role == PivotResultRole.ColumnTotalFlag);
        foreach (var field in rowFields) { var column = output.Columns.Add("R" + field.Ordinal, typeof(object)); column.Caption = field.Caption; }
        if (rowFields.Length == 0) output.Columns.Add("Measure context", typeof(object));
        var matrixRows = new Dictionary<string, DataRow>(StringComparer.Ordinal);
        var matrixColumns = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenCells = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in result.Rows)
        {
            object? At(PivotResultColumn field) => field.Ordinal < source.Length ? source[field.Ordinal] : throw new InvalidDataException("Pivot engine row is missing expected columns.");
            var rowTotal = rowFlag != null && Convert.ToBoolean(At(rowFlag) ?? false, CultureInfo.InvariantCulture);
            var columnTotal = columnFlag != null && Convert.ToBoolean(At(columnFlag) ?? false, CultureInfo.InvariantCulture);
            var key = JsonSerializer.Serialize(new object?[] { rowTotal, rowFields.Select(At).ToArray() });
            if (!matrixRows.TryGetValue(key, out var row))
            {
                if (matrixRows.Count >= 10000) throw new InvalidDataException("Pivot matrix is limited to 10,000 row groups; refine the layout or filters.");
                row = output.NewRow();
                if (rowFields.Length == 0) row[0] = "All";
                else for (var i = 0; i < rowFields.Length; i++) row[i] = rowTotal ? "Total" : At(rowFields[i]) ?? "(Blank)";
                output.Rows.Add(row); matrixRows.Add(key, row);
            }
            foreach (var field in valueFields)
            {
                var columnKey = JsonSerializer.Serialize(new object?[] { columnTotal, columnFields.Select(At).ToArray(), field.Key });
                if (!matrixColumns.TryGetValue(columnKey, out var columnName))
                {
                    if (matrixColumns.Count >= 200) throw new InvalidDataException("Pivot matrix is limited to 200 value columns; refine column fields or filters. Raw results remain available.");
                    columnName = "V" + matrixColumns.Count;
                    var prefix = columnTotal ? "Total" : string.Join(" / ", columnFields.Select(c => At(c) is null or DBNull ? "(Blank)" : Convert.ToString(At(c), CultureInfo.InvariantCulture)));
                    var column = output.Columns.Add(columnName, typeof(object)); column.Caption = (prefix.Length == 0 ? "" : prefix + " · ") + field.Caption; matrixColumns.Add(columnKey, columnName);
                }
                if (!seenCells.Add(JsonSerializer.Serialize(new[] { key, columnKey }))) throw new InvalidDataException("The result contains duplicate pivot cells; inspect the raw query result.");
                row[columnName] = At(field) ?? DBNull.Value;
            }
        }
        return output;
    }
}
