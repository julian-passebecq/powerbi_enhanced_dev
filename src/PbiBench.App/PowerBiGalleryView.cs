using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PbiBench.Core.Automation;
using PbiBench.CSharp.LanguageService;
using PbiBench.Semantic.ModelAuthoring;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

public sealed class PowerBiGalleryView : UserControl
{
    private readonly ListBox cards = new() { DisplayMemberPath = "Title" };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly StackPanel parameters = new();
    private readonly TextBox source = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Dictionary<string, TextBox> inputs = new();
    private readonly Func<TabularModelHandler?> handler;
    private readonly Func<IReadOnlyList<TabularNamedObject>> selection;
    private readonly Action changed;
    private readonly Action<ActionRecipe> insert;
    private readonly Action<PowerBiGalleryCard, IReadOnlyDictionary<string, string>> native;
    private readonly Button insertButton;
    private readonly Action<string> insertDraft;
    private readonly TextBox search = new() { Margin = new Thickness(4), ToolTip = "Search gallery" };
    private readonly ComboBox filter = new() { ItemsSource = new[] { "All", "Measures", "Hygiene", "Quality", "Advanced drafts", "Favorites", "Recent" }, Margin = new Thickness(4) };
    private readonly HashSet<string> favorites = new(StringComparer.Ordinal);
    private readonly List<string> recent = new();
    private readonly string? preferences;
    private readonly TextBlock compatibility = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    public PowerBiGalleryView(Func<TabularModelHandler?> handler, Func<IReadOnlyList<TabularNamedObject>> selection, Action changed, Action<ActionRecipe> insert, Action<PowerBiGalleryCard, IReadOnlyDictionary<string, string>> native, Action<string>? insertDraft = null, string? settingsDirectory = null)
    {
        this.handler = handler; this.selection = selection; this.changed = changed; this.insert = insert; this.native = native;
        this.insertDraft = insertDraft ?? (_ => throw new InvalidOperationException("Draft insertion is unavailable."));
        preferences = settingsDirectory == null ? null : Path.Combine(settingsDirectory, "gallery-preferences.json"); LoadPreferences();
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(260) }); grid.ColumnDefinitions.Add(new());
        var left = new DockPanel(); DockPanel.SetDock(search, Dock.Top); left.Children.Add(search); DockPanel.SetDock(filter, Dock.Top); left.Children.Add(filter); left.Children.Add(cards); grid.Children.Add(left);
        filter.SelectionChanged += (_, _) => FilterCards(); search.TextChanged += (_, _) => FilterCards(); filter.SelectedIndex = 0;
        var right = new DockPanel { Margin = new Thickness(10) }; Grid.SetColumn(right, 1); grid.Children.Add(right);
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); right.Children.Add(top); top.Children.Add(details); top.Children.Add(compatibility); top.Children.Add(parameters);
        var bar = new WrapPanel(); top.Children.Add(bar);
        bar.Children.Add(Button("Preview", PreviewAsync));
        bar.Children.Add(Button("Generate exact C#", () => { Generate(); return Task.CompletedTask; }));
        insertButton = Button("Insert C#", () =>
        {
            var card = (PowerBiGalleryCard)cards.SelectedItem;
            if (card.ExecutionMode == GalleryExecutionMode.TrustedDraft)
            {
                var generated = PowerBiGallery.GenerateDraft(card, Symbols(), Values());
                if (source.Text != generated) throw new InvalidOperationException("Selection or parameters changed. Generate and review again.");
                this.insertDraft(generated); return Task.CompletedTask;
            }
            var recipe = Recipe(); var text = RecipeCSharpGenerator.Generate(recipe).Source;
            if (source.Text != text) throw new InvalidOperationException("Selection or parameters changed. Generate and review the source again before inserting.");
            insert(recipe); return Task.CompletedTask;
        }); insertButton.IsEnabled = false; bar.Children.Add(insertButton);
        bar.Children.Add(Button("Open optional reference", () => { var url = ((PowerBiGalleryCard)cards.SelectedItem).ReferenceUrl; if (url != null) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return Task.CompletedTask; }));
        bar.Children.Add(Button("★ Toggle favorite", () => { var id = ((PowerBiGalleryCard)cards.SelectedItem).Id; if (!favorites.Add(id)) favorites.Remove(id); SavePreferences(); FilterCards(); return Task.CompletedTask; }));
        right.Children.Add(source); Content = grid;
        cards.SelectionChanged += (_, _) => Configure(); Configure();
        IsVisibleChanged += (_, _) => RefreshContext();
    }
    private Button Button(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(4), Padding = new Thickness(8) };
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) { MessageBox.Show(Window.GetWindow(this), error.Message, "Automation gallery"); } }; return button;
    }
    private Dictionary<string, string> Values() => inputs.ToDictionary(p => p.Key, p => p.Value.Text, StringComparer.Ordinal);
    private void Configure()
    {
        source.Clear(); insertButton.IsEnabled = false; inputs.Clear(); parameters.Children.Clear();
        if (cards.SelectedItem is not PowerBiGalleryCard card) { details.Text = "No matching gallery entries."; compatibility.Text = ""; return; }
        details.Text = card.Title + "\n" + card.Purpose + "\n\n" + card.Mode + " · Required: " + card.Selection + "\n" + card.Compatibility + "\nRisk: " + card.Risk + "\n\nImplementation: " + card.ImplementationOrigin + " · " + card.Verification + "\n" + card.License + "\nReference: " + (card.ReferenceUrl ?? "None (native PbiBench)");
        RefreshContext();
        foreach (var parameter in card.Parameters)
        {
            var row = new WrapPanel(); row.Children.Add(new TextBlock { Text = parameter.Name + (parameter.Choices == null ? "" : " (" + string.Join(" / ", parameter.Choices) + ")"), Width = 230, VerticalAlignment = VerticalAlignment.Center });
            var input = new TextBox { Text = parameter.Default, Width = 250, MaxLength = parameter.MaxLength, Margin = new Thickness(4) }; input.TextChanged += (_, _) => { source.Clear(); insertButton.IsEnabled = false; }; inputs.Add(parameter.Name, input); row.Children.Add(input); parameters.Children.Add(row);
        }
    }
    private ActionRecipe Recipe()
    {
        if (handler() == null) throw new InvalidOperationException("Open a model first.");
        var symbols = selection().Select(o => new AutomationSymbol(o is Measure ? "Measure" : o is Column ? "Column" : o is Table ? "Table" : o.ObjectType.ToString(), o.Name, (o as ITabularTableObject)?.Table.Name, true, (o as Column)?.DataType.ToString())).ToArray();
        return PowerBiGallery.Generate((PowerBiGalleryCard)cards.SelectedItem, symbols, Values());
    }
    private void Generate()
    {
        var card = (PowerBiGalleryCard)cards.SelectedItem;
        source.Text = card.ExecutionMode == GalleryExecutionMode.TrustedDraft ? PowerBiGallery.GenerateDraft(card, Symbols(), Values()) : RecipeCSharpGenerator.Generate(Recipe()).Source;
        insertButton.IsEnabled = true; Remember(card.Id);
    }
    private async Task PreviewAsync()
    {
        var card = (PowerBiGalleryCard)cards.SelectedItem;
        Remember(card.Id);
        if (card.ExecutionMode == GalleryExecutionMode.TrustedDraft) { Generate(); return; }
        if (card.Mode != "SAFE RECIPE") { native(card, Values()); return; }
        var active = handler() ?? throw new InvalidOperationException("Open a model first."); var service = new ScriptPreviewService(active);
        var recipe = Recipe(); source.Text = RecipeCSharpGenerator.Generate(recipe).Source; insertButton.IsEnabled = true;
        var prepared = service.PrepareRecipe(recipe, selection()); var computed = await service.ComputeAsync(prepared, CancellationToken.None); var preview = service.Materialize(computed);
        if (!ReferenceEquals(active, handler())) throw new InvalidOperationException("The model session changed. Preview again.");
        AuthoringReview.Show(this, preview, handler, changed);
    }
    private AutomationSymbol[] Symbols() => selection().Select(o => new AutomationSymbol(o is Measure ? "Measure" : o is Column ? "Column" : o is Table ? "Table" : o.ObjectType.ToString(), o.Name, (o as ITabularTableObject)?.Table.Name, true, (o as Column)?.DataType.ToString())).ToArray();
    public void RefreshContext()
    {
        if (cards.SelectedItem is PowerBiGalleryCard card) compatibility.Text = PowerBiGallery.CompatibilityReason(card, Symbols(), handler()?.CompatibilityLevel);
        if (insertButton != null) insertButton.IsEnabled = false;
    }
    private void FilterCards()
    {
        var previous = cards.SelectedItem as PowerBiGalleryCard; var category = filter.SelectedItem as string ?? "All";
        var rows = PowerBiGallery.All.Where(c => (category == "All" || category == c.Category || category == "Favorites" && favorites.Contains(c.Id) || category == "Recent" && recent.Contains(c.Id)) &&
            (c.Title + " " + c.Purpose + " " + c.Mode).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0);
        if (category == "Recent") rows = rows.OrderBy(c => recent.IndexOf(c.Id));
        cards.ItemsSource = rows.ToArray(); if (previous != null && cards.Items.Contains(previous)) cards.SelectedItem = previous; else cards.SelectedIndex = 0;
    }
    private void Remember(string id) { recent.Remove(id); recent.Insert(0, id); if (recent.Count > 20) recent.RemoveAt(20); SavePreferences(); }
    private void LoadPreferences()
    {
        try
        {
            if (preferences == null || !File.Exists(preferences) || new FileInfo(preferences).Length > 16384) return;
            var state = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(preferences)); if (state == null) return;
            var ids = PowerBiGallery.All.Select(c => c.Id).ToArray();
            if (state.TryGetValue("favorites", out var saved)) favorites.UnionWith(saved.Where(ids.Contains));
            if (state.TryGetValue("recent", out saved)) recent.AddRange(saved.Where(ids.Contains).Distinct().Take(20));
        }
        catch (Exception error) when (error is IOException || error is JsonException || error is UnauthorizedAccessException) { }
    }
    private void SavePreferences()
    {
        if (preferences == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
        File.WriteAllText(preferences, JsonSerializer.Serialize(new { favorites = favorites.ToArray(), recent = recent.ToArray() }));
    }
}
