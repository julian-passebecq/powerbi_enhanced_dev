using System.Diagnostics;
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
    public PowerBiGalleryView(Func<TabularModelHandler?> handler, Func<IReadOnlyList<TabularNamedObject>> selection, Action changed, Action<ActionRecipe> insert, Action<PowerBiGalleryCard, IReadOnlyDictionary<string, string>> native)
    {
        this.handler = handler; this.selection = selection; this.changed = changed; this.insert = insert; this.native = native;
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(260) }); grid.ColumnDefinitions.Add(new());
        var left = new DockPanel(); var filter = new ComboBox { ItemsSource = new[] { "Essentials", "Measures", "Hygiene", "Quality" }, Margin = new Thickness(4) }; DockPanel.SetDock(filter, Dock.Top); left.Children.Add(filter); left.Children.Add(cards); grid.Children.Add(left);
        filter.SelectionChanged += (_, _) => cards.ItemsSource = PowerBiGallery.All.Where(c => (string)filter.SelectedItem == "Essentials" || c.Category == (string)filter.SelectedItem).ToArray(); filter.SelectedIndex = 0;
        var right = new DockPanel { Margin = new Thickness(10) }; Grid.SetColumn(right, 1); grid.Children.Add(right);
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); right.Children.Add(top); top.Children.Add(details); top.Children.Add(parameters);
        var bar = new WrapPanel(); top.Children.Add(bar);
        bar.Children.Add(Button("Preview", PreviewAsync));
        bar.Children.Add(Button("Generate exact C#", () => { Generate(); return Task.CompletedTask; }));
        insertButton = Button("Insert C#", () =>
        {
            var recipe = Recipe(); var text = RecipeCSharpGenerator.Generate(recipe).Source;
            if (source.Text != text) throw new InvalidOperationException("Selection or parameters changed. Generate and review the source again before inserting.");
            insert(recipe); return Task.CompletedTask;
        }); insertButton.IsEnabled = false; bar.Children.Add(insertButton);
        bar.Children.Add(Button("Open source", () => { Process.Start(new ProcessStartInfo(((PowerBiGalleryCard)cards.SelectedItem).Source) { UseShellExecute = true }); return Task.CompletedTask; }));
        right.Children.Add(source); Content = grid;
        cards.SelectionChanged += (_, _) => Configure(); cards.SelectedIndex = 0;
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
        if (cards.SelectedItem is not PowerBiGalleryCard card) return;
        details.Text = card.Title + "\n" + card.Purpose + "\n\n" + card.Mode + " · Required: " + card.Selection + "\n" + card.Compatibility + "\nRisk: " + card.Risk + "\n\n" + card.License + "\n" + card.Source;
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
    private void Generate() { var recipe = Recipe(); source.Text = RecipeCSharpGenerator.Generate(recipe).Source; insertButton.IsEnabled = true; }
    private async Task PreviewAsync()
    {
        var card = (PowerBiGalleryCard)cards.SelectedItem;
        if (card.Mode != "SAFE RECIPE") { native(card, Values()); return; }
        var active = handler() ?? throw new InvalidOperationException("Open a model first."); var service = new ScriptPreviewService(active);
        var recipe = Recipe(); source.Text = RecipeCSharpGenerator.Generate(recipe).Source; insertButton.IsEnabled = true;
        var prepared = service.PrepareRecipe(recipe, selection()); var computed = await service.ComputeAsync(prepared, CancellationToken.None); var preview = service.Materialize(computed);
        if (!ReferenceEquals(active, handler())) throw new InvalidOperationException("The model session changed. Preview again.");
        AuthoringReview.Show(this, preview, handler, changed);
    }
}
