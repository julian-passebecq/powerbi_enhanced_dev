using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PbiBench.Semantic;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;
using Binding = System.Windows.Data.Binding;

namespace PbiBench.App;

/// <summary>Original draft-based matrices; no editable UI binding points at a live TOM wrapper.</summary>
public sealed class MetadataEditorsView : UserControl, IDisposable
{
    private readonly Func<TabularModelHandler?> currentHandler;
    private readonly Action changed;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 7, 5, 12) };
    private readonly TabControl tabs = new();
    private readonly ComboBox perspectives = new() { MinWidth = 160, DisplayMemberPath = "Name" };
    private readonly DataGrid perspectiveGrid = Grid();
    private readonly TextBox perspectiveSearch = new() { Width = 180 };
    private readonly CheckBox showHidden = new() { Content = "Show hidden fields", Margin = new Thickness(8, 4, 8, 4) };
    private readonly ComboBox cultures = new() { MinWidth = 125 };
    private readonly ComboBox translationProperty = new() { Width = 145, ItemsSource = Enum.GetValues(typeof(TranslationProperty)), SelectedIndex = 0 };
    private readonly DataGrid translationGrid = Grid();
    private readonly TextBox translationSearch = new() { Width = 180 };
    private readonly CheckBox missingOnly = new() { Content = "Missing translations", Margin = new Thickness(8, 4, 8, 4) };
    private readonly ComboBox calendars = new() { Width = 230, DisplayMemberPath = "Name" };
    private readonly ComboBox calendarTable = new() { Width = 200, DisplayMemberPath = "Name" };
    private readonly TextBox calendarName = new() { MinWidth = 180 };
    private readonly TextBox calendarDescription = new() { MinWidth = 230 };
    private readonly DataGrid calendarGrid = Grid();
    private readonly DataGrid sortGrid = Grid();
    private readonly ListBox associated = new() { SelectionMode = SelectionMode.Multiple, MinHeight = 80, MaxHeight = 160 };
    private readonly ListBox timeRelated = new() { SelectionMode = SelectionMode.Multiple, MinHeight = 80, MaxHeight = 160 };
    private readonly ComboBox sampleMeasure = new() { MinWidth = 170 };
    private TabularModelHandler? handler;
    private string fingerprint = "";
    private bool loading, dirty, stale, disposed;
    private DataTable perspectiveData = new();
    private DataTable translationData = new();
    private PerspectiveSnapshot? perspectiveSnapshot;
    private Dictionary<string, PerspectiveMember> perspectiveMembers = new();
    private TranslationSnapshot? translationSnapshot;
    private Dictionary<(string Id, string Culture, TranslationProperty Property), string?> originalTranslations = new();
    private CalendarSnapshot? calendarSnapshot;
    private readonly Dictionary<(string Id, string Culture, TranslationProperty Property), string?> translationDraft = new();
    private readonly Dictionary<(string Id, string Perspective), bool> perspectiveDraft = new();
    private List<CalendarMappingRow> mappingRows = new();
    private List<CalendarSortRow> sortRows = new();
    private CalendarMappingRow? selectedMapping;
    private CalendarDraft? originalCalendar;
    public event Action<string>? DaxQueryRequested;
    public void ShowTool(string tool) => tabs.SelectedIndex = tool switch { "Calendar" => 0, "Perspectives" => 1, "Translations" => 2, _ => throw new ArgumentException("Unknown metadata editor: " + tool) };

    public MetadataEditorsView(Func<TabularModelHandler?> currentHandler, Action changed)
    {
        this.currentHandler = currentHandler; this.changed = changed;
        var root = new DockPanel { Margin = new Thickness(12) };
        var heading = new DockPanel(); var reload = Button("Reload model / discard drafts", () => ReloadConfirmed()); DockPanel.SetDock(reload, Dock.Right); heading.Children.Add(reload); heading.Children.Add(status); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        tabs.Items.Add(new TabItem { Header = "Calendars", Content = CalendarPanel() });
        tabs.Items.Add(new TabItem { Header = "Perspectives", Content = PerspectivePanel() });
        tabs.Items.Add(new TabItem { Header = "Translations", Content = TranslationPanel() });
        root.Children.Add(tabs); Content = root; RefreshModel(); if (handler == null) Reload();
    }
    private FrameworkElement PerspectivePanel()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Help("Select fields across perspectives. A table checkbox applies to every column, measure and hierarchy; an indeterminate checkbox means some fields are included."));
        top.Children.Add(Row(perspectives, Button("New…", () => { var name = Prompt("New perspective", "Perspective name"); if (name != null) Review(new PerspectiveEditorService(Require()).PreviewCreate(name)); }),
            Button("Rename…", () => { var selected = SelectedPerspective(); var name = Prompt("Rename perspective", selected); if (name != null) Review(new PerspectiveEditorService(Require()).PreviewRename(selected, name)); }),
            Button("Delete…", () => Review(new PerspectiveEditorService(Require()).PreviewDelete(SelectedPerspective()))),
            Button("Preview membership…", () => { CommitPerspective(); Review(new PerspectiveEditorService(Require()).PreviewMembership(perspectiveDraft.Select(item => new PerspectiveMembershipChange(item.Key.Id, item.Key.Perspective, item.Value)))); })));
        top.Children.Add(Row(Label("Find"), perspectiveSearch, showHidden));
        perspectiveSearch.TextChanged += (_, _) => FilterPerspective(); showHidden.Checked += (_, _) => FilterPerspective(); showHidden.Unchecked += (_, _) => FilterPerspective();
        perspectiveGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(new Action(() => { CommitPerspective(); MarkDirty(); }));
        panel.Children.Add(perspectiveGrid); return panel;
    }
    private FrameworkElement TranslationPanel()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Help("Edit metadata across culture columns. Clear a cell to inherit the model value. JSON import previews only supplied cells and preserves other translations."));
        top.Children.Add(Row(cultures, Button("Add culture…", () => { var name = Prompt("Add translation culture", "de-CH"); if (name != null) Review(new TranslationEditorService(Require()).PreviewCreateCulture(name)); }),
            Button("Rename…", () => { var selected = SelectedCulture(); var name = Prompt("Rename translation culture", selected); if (name != null) Review(new TranslationEditorService(Require()).PreviewRenameCulture(selected, name)); }),
            Button("Delete…", () => Review(new TranslationEditorService(Require()).PreviewDeleteCulture(SelectedCulture()))),
            Button("Preview edits…", () => { CommitTranslation(); Review(new TranslationEditorService(Require()).PreviewCells(translationDraft.Select(item => new TranslationCell(item.Key.Id, item.Key.Culture, item.Key.Property, item.Value)))); })));
        top.Children.Add(Row(translationProperty, Label("Find"), translationSearch, missingOnly, Button("Inherit selected cell", () => ClearTranslation()), Button("Export JSON…", ExportTranslations), Button("Import JSON…", ImportTranslations)));
        translationProperty.SelectionChanged += (_, _) => { if (!loading) { CommitTranslation(); RebuildTranslation(); } };
        translationSearch.TextChanged += (_, _) => FilterTranslation(); missingOnly.Checked += (_, _) => FilterTranslation(); missingOnly.Unchecked += (_, _) => FilterTranslation();
        translationGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(new Action(() => { CommitTranslation(); MarkDirty(); }));
        panel.Children.Add(translationGrid); return panel;
    }
    private FrameworkElement CalendarPanel()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Help("Map time categories to columns, associate alternate labels, and choose time-related columns. Calendar metadata needs compatibility level 1701 and a supporting target engine."));
        top.Children.Add(Row(calendars, Button("Load calendar", () => { if (ConfirmDiscard()) LoadCalendar(calendars.SelectedItem as CalendarDraft); }),
            Button("New draft", () => { if (ConfirmDiscard()) LoadCalendar(null); }), Button("Delete…", () => { if (originalCalendar == null) throw new InvalidOperationException("Load an existing calendar first."); Review(new CalendarEditorService(Require()).PreviewDelete(originalCalendar.Table, originalCalendar.OriginalName!)); }),
            Button("Preview calendar…", () => Review(new CalendarEditorService(Require()).Preview(CalendarRequest())))));
        top.Children.Add(Row(Label("Table"), calendarTable, Label("Name"), calendarName, Label("Description"), calendarDescription));
        top.Children.Add(Row(sampleMeasure, Button("Generate YTD sample", () => { var measure = sampleMeasure.SelectedItem as MeasureChoice ?? throw new InvalidOperationException("Choose a measure."); OpenQuery(new CalendarEditorService(Require()).GenerateSample(CalendarRequest(), measure.Table, measure.Name)); }),
            Button("Generate data validation", () => OpenQuery(new CalendarEditorService(Require()).GenerateValidationQuery(CalendarRequest())))));
        calendarTable.SelectionChanged += (_, _) => { if (!loading && originalCalendar == null) LoadCalendar(null, false); };
        calendarName.TextChanged += (_, _) => MarkDirty(); calendarDescription.TextChanged += (_, _) => MarkDirty();
        calendarGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(new Action(MarkDirty)); sortGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(new Action(MarkDirty));
        calendarGrid.SelectionChanged += (_, _) =>
        {
            if (loading) return; selectedMapping = calendarGrid.SelectedItem as CalendarMappingRow;
            loading = true; associated.SelectedItems.Clear(); if (selectedMapping != null) foreach (var name in selectedMapping.AssociatedColumns) associated.SelectedItems.Add(name); loading = false;
        };
        associated.SelectionChanged += (_, _) => { if (!loading && selectedMapping != null) { selectedMapping.AssociatedColumns = associated.SelectedItems.Cast<string>().ToArray(); MarkDirty(); } };
        timeRelated.SelectionChanged += (_, _) => MarkDirty();
        var split = new System.Windows.Controls.Grid(); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) }); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.Children.Add(calendarGrid); var options = new StackPanel { Margin = new Thickness(14, 0, 0, 0) }; System.Windows.Controls.Grid.SetColumn(options, 1);
        options.Children.Add(Help("Associated columns for the selected category")); options.Children.Add(associated); options.Children.Add(Help("Time-related columns")); options.Children.Add(timeRelated); options.Children.Add(Help("Column sort order (affects all calendars and visuals)")); sortGrid.Height = 200; options.Children.Add(sortGrid);
        split.Children.Add(new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); System.Windows.Controls.Grid.SetColumn(split.Children[split.Children.Count - 1], 1);
        panel.Children.Add(split); return panel;
    }
    public void RefreshModel()
    {
        if (disposed) return;
        var active = currentHandler();
        if (!ReferenceEquals(active, handler)) { handler = active; Reload(); return; }
        if (active == null) return;
        var current = new SemanticModelService(active).Fingerprint();
        if (current == fingerprint) return;
        if (dirty) { stale = true; status.Text = "The model changed. Your drafts are retained. Reload and discard them before preparing a new preview."; }
        else Reload();
    }
    private void Reload()
    {
        loading = true; dirty = stale = false; perspectiveDraft.Clear(); translationDraft.Clear();
        try
        {
            tabs.IsEnabled = handler != null;
            if (handler == null) { status.Text = "Open or connect a semantic model to author metadata."; return; }
            fingerprint = new SemanticModelService(handler).Fingerprint();
            perspectiveSnapshot = new PerspectiveEditorService(handler).Capture(); translationSnapshot = new TranslationEditorService(handler).Capture(); calendarSnapshot = new CalendarEditorService(handler).Capture();
            perspectiveMembers = perspectiveSnapshot.Members.ToDictionary(member => member.Id);
            originalTranslations = translationSnapshot.Cells.ToDictionary(cell => (cell.ObjectId, cell.Culture, cell.Property), cell => cell.Value);
            perspectives.ItemsSource = perspectiveSnapshot.Perspectives; perspectives.SelectedIndex = 0; cultures.ItemsSource = translationSnapshot.Cultures; cultures.SelectedIndex = 0;
            calendars.ItemsSource = calendarSnapshot.Calendars; calendars.SelectedIndex = 0; calendarTable.ItemsSource = calendarSnapshot.Tables; calendarTable.SelectedIndex = 0;
            sampleMeasure.ItemsSource = handler.Model.AllMeasures.Select(measure => new MeasureChoice(measure.Table.Name, measure.Name)).ToArray(); sampleMeasure.SelectedIndex = 0;
            RebuildPerspective(); RebuildTranslation(); LoadCalendar(calendarSnapshot.Calendars.FirstOrDefault());
            status.Text = "Draft editors · " + handler.Database.Name + " · compatibility " + handler.CompatibilityLevel + ". Changes apply locally with Undo.";
        }
        finally { loading = false; dirty = false; }
    }
    private void RebuildPerspective()
    {
        if (perspectiveSnapshot == null) return;
        perspectiveData = new DataTable(); perspectiveData.Columns.Add("Id"); perspectiveData.Columns.Add("Object"); perspectiveData.Columns.Add("Kind"); perspectiveData.Columns.Add("Hidden", typeof(bool));
        perspectiveGrid.Columns.Clear(); TextColumn(perspectiveGrid, "Object", "Object", true); TextColumn(perspectiveGrid, "Kind", "Kind", true);
        for (var i = 0; i < perspectiveSnapshot.Perspectives.Count; i++)
        {
            var key = "p" + i; perspectiveData.Columns.Add(key, typeof(bool));
            perspectiveGrid.Columns.Add(new DataGridCheckBoxColumn { Header = perspectiveSnapshot.Perspectives[i].Name, Binding = new Binding(key) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, IsThreeState = true, MinWidth = 100 });
        }
        foreach (var member in perspectiveSnapshot.Members)
        {
            var row = perspectiveData.NewRow(); row["Id"] = member.Id; row["Object"] = (member.Table == null ? "" : member.Table + " / ") + member.Name; row["Kind"] = member.Kind; row["Hidden"] = member.IsHidden;
            for (var i = 0; i < perspectiveSnapshot.Perspectives.Count; i++) row["p" + i] = (object?)member.Membership[perspectiveSnapshot.Perspectives[i].Name] ?? DBNull.Value;
            perspectiveData.Rows.Add(row);
        }
        perspectiveGrid.ItemsSource = perspectiveData.DefaultView; FilterPerspective();
    }
    private void CommitPerspective()
    {
        if (loading || perspectiveSnapshot == null) return;
        perspectiveGrid.CommitEdit(DataGridEditingUnit.Cell, true); perspectiveGrid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (DataRow row in perspectiveData.Rows)
        {
            var id = (string)row["Id"]; var original = perspectiveMembers[id];
            for (var i = 0; i < perspectiveSnapshot.Perspectives.Count; i++)
            {
                var name = perspectiveSnapshot.Perspectives[i].Name; var value = row["p" + i]; var key = (id, name);
                if (value != DBNull.Value && (bool)value != original.Membership[name]) perspectiveDraft[key] = (bool)value; else perspectiveDraft.Remove(key);
            }
        }
    }
    private void RebuildTranslation()
    {
        if (translationSnapshot == null) return;
        var property = (TranslationProperty)translationProperty.SelectedItem;
        translationData = new DataTable(); translationData.ExtendedProperties["Property"] = property;
        translationData.Columns.Add("Id"); translationData.Columns.Add("Object"); translationData.Columns.Add("Default"); translationData.Columns.Add("Missing", typeof(bool));
        translationGrid.Columns.Clear(); TextColumn(translationGrid, "Object", "Object", true); TextColumn(translationGrid, "Default", "Model value", true);
        for (var i = 0; i < translationSnapshot.Cultures.Count; i++) { translationData.Columns.Add("c" + i); TextColumn(translationGrid, "c" + i, translationSnapshot.Cultures[i]); }
        foreach (var member in translationSnapshot.Members.Where(member => property != TranslationProperty.DisplayFolder || member.DisplayFolder != null))
        {
            var row = translationData.NewRow(); row["Id"] = member.Id; row["Object"] = (member.Table == null ? "" : member.Table + " / ") + member.Name;
            row["Default"] = property == TranslationProperty.Name ? member.Name : property == TranslationProperty.Description ? member.Description : member.DisplayFolder ?? "";
            var missing = false;
            for (var i = 0; i < translationSnapshot.Cultures.Count; i++)
            {
                var culture = translationSnapshot.Cultures[i]; var key = (member.Id, culture, property);
                var value = translationDraft.TryGetValue(key, out var draft) ? draft : originalTranslations[key];
                row["c" + i] = (object?)value ?? DBNull.Value; missing |= value == null;
            }
            row["Missing"] = missing; translationData.Rows.Add(row);
        }
        translationGrid.ItemsSource = translationData.DefaultView; FilterTranslation();
    }
    private void CommitTranslation()
    {
        if (loading || translationSnapshot == null || !translationData.ExtendedProperties.ContainsKey("Property")) return;
        translationGrid.CommitEdit(DataGridEditingUnit.Cell, true); translationGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var property = (TranslationProperty)translationData.ExtendedProperties["Property"]!;
        foreach (DataRow row in translationData.Rows)
        {
            var id = (string)row["Id"]; var missing = false;
            for (var i = 0; i < translationSnapshot.Cultures.Count; i++)
            {
                var culture = translationSnapshot.Cultures[i]; var value = row.IsNull("c" + i) ? null : (string)row["c" + i];
                var key = (id, culture, property); var original = originalTranslations[key];
                if (value == original) translationDraft.Remove(key); else translationDraft[key] = value; missing |= value == null;
            }
            row["Missing"] = missing;
        }
    }
    private void ClearTranslation()
    {
        Require();
        if (translationGrid.CurrentCell.Item is not DataRowView row || translationGrid.CurrentCell.Column is not DataGridTextColumn column || column.IsReadOnly) throw new InvalidOperationException("Select an editable translation cell first.");
        var key = ((Binding)column.Binding).Path.Path; row[key] = DBNull.Value; CommitTranslation(); MarkDirty();
    }
    private void LoadCalendar(CalendarDraft? calendar, bool resetName = true)
    {
        if (calendarSnapshot == null) return;
        var wasLoading = loading; loading = true;
        try
        {
            originalCalendar = calendar;
            if (calendar != null) calendarTable.SelectedItem = calendarSnapshot.Tables.First(table => table.Name == calendar.Table);
            var table = calendarTable.SelectedItem as CalendarTable; calendarTable.IsEnabled = calendar == null;
            if (resetName) { calendarName.Text = calendar?.Name ?? "New Calendar"; calendarDescription.Text = calendar?.Description ?? ""; }
            var columns = table?.Columns.Select(column => column.Name).ToArray() ?? Array.Empty<string>();
            mappingRows = CalendarEditorService.TimeUnits.Select(unit => { var mapping = calendar?.Mappings.FirstOrDefault(item => item.TimeUnit == unit); return new CalendarMappingRow { TimeUnit = unit, PrimaryColumn = mapping?.PrimaryColumn ?? "", AssociatedColumns = mapping?.AssociatedColumns.ToArray() ?? Array.Empty<string>() }; }).ToList();
            calendarGrid.Columns.Clear(); TextColumn(calendarGrid, "TimeUnit", "Time category", true);
            calendarGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Primary column", ItemsSource = new[] { "" }.Concat(columns).ToArray(), SelectedItemBinding = new Binding("PrimaryColumn") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            calendarGrid.ItemsSource = mappingRows; calendarGrid.SelectedIndex = 0; selectedMapping = mappingRows.FirstOrDefault();
            associated.ItemsSource = columns; associated.SelectedItems.Clear(); if (selectedMapping != null) foreach (var name in selectedMapping.AssociatedColumns) associated.SelectedItems.Add(name);
            timeRelated.ItemsSource = columns; timeRelated.SelectedItems.Clear(); if (calendar != null) foreach (var name in calendar.TimeRelatedColumns) timeRelated.SelectedItems.Add(name);
            sortRows = table?.Columns.Select(column => new CalendarSortRow { Column = column.Name, SortByColumn = column.SortByColumn ?? "", OriginalSort = column.SortByColumn }).ToList() ?? new();
            sortGrid.Columns.Clear(); TextColumn(sortGrid, "Column", "Column", true); sortGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Sort by", ItemsSource = new[] { "" }.Concat(columns).ToArray(), SelectedItemBinding = new Binding("SortByColumn") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); sortGrid.ItemsSource = sortRows;
        }
        finally { loading = wasLoading; }
    }
    private CalendarDraft CalendarRequest()
    {
        Require(); calendarGrid.CommitEdit(DataGridEditingUnit.Cell, true); calendarGrid.CommitEdit(DataGridEditingUnit.Row, true); sortGrid.CommitEdit(DataGridEditingUnit.Cell, true); sortGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var table = calendarTable.SelectedItem as CalendarTable ?? throw new InvalidOperationException("Choose a calendar table.");
        return new(table.Name, originalCalendar?.OriginalName, calendarName.Text, calendarDescription.Text, mappingRows.Where(row => !string.IsNullOrWhiteSpace(row.PrimaryColumn)).Select(row => new CalendarMapping(row.TimeUnit, row.PrimaryColumn, row.AssociatedColumns)).ToArray(), timeRelated.SelectedItems.Cast<string>().ToArray())
        { SortChanges = sortRows.Where(row => (string.IsNullOrEmpty(row.SortByColumn) ? null : row.SortByColumn) != row.OriginalSort).Select(row => new CalendarSortChange(row.Column, string.IsNullOrEmpty(row.SortByColumn) ? null : row.SortByColumn)).ToArray() };
    }
    private void Review(AuthoringPreview preview) => AuthoringReview.Show(this, preview, currentHandler, () => { dirty = false; Reload(); changed(); });
    private TabularModelHandler Require()
    {
        if (handler == null || !ReferenceEquals(handler, currentHandler())) throw new InvalidOperationException("The model session changed. Reload this editor.");
        if (stale || new SemanticModelService(handler).Fingerprint() != fingerprint) { stale = true; throw new InvalidOperationException("The model changed while this draft was open. Reload before previewing."); }
        return handler;
    }
    private void MarkDirty()
    {
        if (loading || disposed || handler == null) return;
        dirty = true;
        if (stale) return;
        status.Text = "Draft changes are local to this editor. Preview the changes before applying.";
        if (tabs.SelectedIndex == 0)
        {
            try { var issues = new CalendarEditorService(handler).Validate(CalendarRequest()); var visible = issues.Where(issue => issue.Severity != AuthoringIssueSeverity.Information).ToArray(); status.Text += visible.Length == 0 ? " Calendar metadata checks passed." : "\n" + string.Join("\n", visible.Select(issue => issue.Severity + ": " + issue.Message)); }
            catch (Exception ex) { status.Text = ex.Message; }
        }
    }
    private void ExportTranslations()
    {
        var json = new TranslationEditorService(Require()).ExportJson();
        var dialog = new SaveFileDialog { Filter = "PbiBench translations (*.json)|*.json", FileName = "metadata-translations.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) { File.WriteAllText(dialog.FileName, json); status.Text = "Exported the applied model translations. Draft changes are exported after Apply."; }
    }
    private void ImportTranslations()
    {
        var service = new TranslationEditorService(Require()); var dialog = new OpenFileDialog { Filter = "PbiBench translations (*.json)|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (new FileInfo(dialog.FileName).Length > 16 * 1024 * 1024) throw new InvalidOperationException("Choose a translation file smaller than 16 MB.");
        var overwrite = MessageBox.Show(Window.GetWindow(this), "Replace supplied existing cells? Choose No to fill only missing cells. Unspecified cells are preserved in both cases.", "Translation import mode", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (overwrite == MessageBoxResult.Cancel) return;
        Review(service.PreviewImportJson(File.ReadAllText(dialog.FileName), overwrite == MessageBoxResult.Yes));
    }
    private void OpenQuery(string query)
    {
        if (DaxQueryRequested != null) DaxQueryRequested(query);
        else { var window = new Window { Owner = Window.GetWindow(this), Title = "Generated calendar test", Width = 820, Height = 500, Content = new TextBox { Text = query, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(15) } }; window.Show(); }
    }
    private void ReloadConfirmed() { if (ConfirmDiscard()) { handler = currentHandler(); Reload(); } }
    private bool ConfirmDiscard() => !dirty || MessageBox.Show(Window.GetWindow(this), "Discard the unapplied drafts in these metadata editors?", "Reload draft", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    private string SelectedPerspective() => (perspectives.SelectedItem as PerspectiveDefinition)?.Name ?? throw new InvalidOperationException("Select a perspective.");
    private string SelectedCulture() => cultures.SelectedItem as string ?? throw new InvalidOperationException("Select a culture.");
    private void FilterPerspective() { if (perspectiveData.Columns.Contains("Hidden")) perspectiveData.DefaultView.RowFilter = "Object LIKE '%" + EscapeFilter(perspectiveSearch.Text) + "%'" + (showHidden.IsChecked == true ? "" : " AND Hidden = false"); }
    private void FilterTranslation() { if (translationData.Columns.Contains("Missing")) translationData.DefaultView.RowFilter = "Object LIKE '%" + EscapeFilter(translationSearch.Text) + "%'" + (missingOnly.IsChecked == true ? " AND Missing = true" : ""); }
    private static string EscapeFilter(string text) => string.Concat(text.Select(character => character switch { '\'' => "''", '[' => "[[]", ']' => "[]]", '%' => "[%]", '*' => "[*]", _ => character.ToString() }));
    private Button Button(string title, Action action) { var button = new Button { Content = title, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) }; button.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }; return button; }
    private static DataGrid Grid() => new() { AutoGenerateColumns = false, IsReadOnly = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(4) };
    private static void TextColumn(DataGrid grid, string property, string header, bool readOnly = false) => grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, IsReadOnly = readOnly, MinWidth = 120, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
    private static TextBlock Help(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 9) };
    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 3, 4, 3) };
    private static WrapPanel Row(params UIElement[] elements) { var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) }; foreach (var element in elements) panel.Children.Add(element); return panel; }
    private string? Prompt(string title, string initial)
    {
        var input = new TextBox { Text = initial, Margin = new Thickness(15), MinWidth = 280 };
        var window = new Window { Owner = Window.GetWindow(this), Title = title, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel(); panel.Children.Add(input); var ok = new Button { Content = "Prepare draft", IsDefault = true, Margin = new Thickness(15), Padding = new Thickness(12, 6, 12, 6) }; ok.Click += (_, _) => window.DialogResult = true; panel.Children.Add(ok); window.Content = panel; input.SelectAll(); input.Focus(); return window.ShowDialog() == true ? input.Text : null;
    }
    public void Dispose() { disposed = true; }
    public sealed class CalendarMappingRow { public string TimeUnit { get; set; } = ""; public string PrimaryColumn { get; set; } = ""; public string[] AssociatedColumns { get; set; } = Array.Empty<string>(); }
    public sealed class CalendarSortRow { public string Column { get; set; } = ""; public string SortByColumn { get; set; } = ""; public string? OriginalSort { get; set; } }
    private sealed record MeasureChoice(string Table, string Name) { public override string ToString() => Table + " / " + Name; }
}
