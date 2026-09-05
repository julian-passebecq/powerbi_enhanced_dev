using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PbiBench.DesignExchange;
using PbiBench.ExternalTools;

namespace PbiBench.App;

/// <summary>Provider-neutral local file exchange. Does not apply model/report changes or call providers.</summary>
public sealed class DesignExchangeView : UserControl
{
    private readonly Func<ModelContext?> capture;
    private readonly Action<ToolContext> open;
    private readonly CancellationToken ct;
    private readonly TextBox model = PathBox(), spec = PathBox(), theme = PathBox();
    private readonly TextBox detail = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) };
    private readonly TextBlock status = new() { Text = "Export or open model context, then choose a dashboard spec and/or theme.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly Button validate = new() { Content = "Validate files" }, launch = new() { Content = "Open in Report Studio", IsEnabled = false };
    private int revision;
    private bool exported;
    internal DesignPackage? CurrentPackage { get; private set; }
    public DesignExchangeView(Func<ModelContext?> capture, Action<ToolContext> open, CancellationToken ct)
    {
        this.capture = capture; this.open = open; this.ct = ct;
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Design Exchange", FontSize = 26, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = "Exchange model metadata, dashboard intent and Power BI themes with any design tool. Local files only.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) });
        var bar = new WrapPanel(); top.Children.Add(bar);
        Add(bar, "Export model context…", ExportAsync); Add(bar, "Copy Design Prompt", () => { Clipboard.SetText(DesignPackage.Prompt); status.Text = "Provider-neutral prompt copied. Supply the JSON files to your chosen tool."; return Task.CompletedTask; });
        Row(top, "Model context", model); Row(top, "Dashboard spec", spec); Row(top, "Theme", theme);
        var actions = new WrapPanel(); actions.Children.Add(validate); actions.Children.Add(launch); top.Children.Add(actions); top.Children.Add(status);
        validate.Click += async (_, _) => await RunAsync(ValidateAsync);
        launch.Click += async (_, _) => await RunAsync(async () =>
        {
            // Files can change after a review. Validate current bytes again; the child validates independently.
            await ValidateAsync(); if (CurrentPackage?.IsValid == true) open(new(ModelContextFile: model.Text, DashboardSpecFile: Empty(spec.Text), ThemeFile: Empty(theme.Text)));
        });
        root.Children.Add(detail);
        foreach (var path in new[] { model, spec, theme }) path.TextChanged += (_, _) => Invalidate();
    }
    private static string? Empty(string text) => string.IsNullOrWhiteSpace(text) ? null : text;
    private static TextBox PathBox() => new() { IsReadOnly = true, MinWidth = 320, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8), VerticalContentAlignment = VerticalAlignment.Center };
    private void Row(Panel parent, string label, TextBox input)
    {
        parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 4) });
        var row = new DockPanel(); parent.Children.Add(row); var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
        Add(buttons, "Choose…", () => { var dialog = new OpenFileDialog { Title = "Choose " + label, Filter = "JSON files|*.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) { if (input == model) exported = false; input.Text = dialog.FileName; } return Task.CompletedTask; });
        Add(buttons, "Clear", () => { input.Clear(); return Task.CompletedTask; }); row.Children.Add(input);
    }
    private void Add(Panel panel, string label, Func<Task> action)
    { var button = new Button { Content = label }; button.Click += async (_, _) => await RunAsync(action); panel.Children.Add(button); }
    private void Invalidate() { revision++; CurrentPackage = null; launch.IsEnabled = false; detail.Clear(); status.Text = "Inputs changed. Validate the current files."; }
    private async Task RunAsync(Func<Task> action)
    { try { await action(); } catch (OperationCanceledException) { } catch (Exception error) { Invalidate(); status.Text = error.Message; } }
    private async Task ExportAsync()
    {
        var context = capture() ?? throw new InvalidOperationException("Open or connect a semantic model before exporting.");
        detail.Text = context.ToJson(); status.Text = "Metadata-only context: " + context.Model.Objects.Count + " objects. Credentials, connection strings, partition sources, roles, rows and local paths are excluded. Review the JSON below.";
        var dialog = new SaveFileDialog { Filter = "Model context|*.json", FileName = "pbibench-model-context.json", Title = "Save model metadata to a new file" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await context.SaveAsync(dialog.FileName, ct); model.Text = dialog.FileName; exported = true;
        detail.Text = context.ToJson(); status.Text = "Model context exported · " + context.ModelFingerprint;
    }
    internal void SetInputs(string modelPath, string? specPath, string? themePath) { model.Text = modelPath; spec.Text = specPath ?? ""; theme.Text = themePath ?? ""; }
    internal async Task ValidateAsync()
    {
        CurrentPackage = null; launch.IsEnabled = false; var token = ++revision;
        var modelPath = model.Text; var specPath = Empty(spec.Text); var themePath = Empty(theme.Text);
        if (!File.Exists(modelPath)) throw new InvalidOperationException("Choose or export model context first.");
        status.Text = "Validating local files…"; validate.IsEnabled = false;
        try
        {
            var package = await Task.Run(() => DesignPackage.LoadAsync(modelPath, specPath, themePath, ct), ct);
            if (token != revision) return;
            if (exported && capture()?.ModelFingerprint != package.Model.ModelFingerprint) throw new InvalidOperationException("The loaded model changed after export. Export current model context and regenerate the design.");
            CurrentPackage = package; launch.IsEnabled = package.IsValid;
            status.Text = package.IsValid ? "Validated for Design Preview · no PBIR files will be changed." : "Validation blocked. Review diagnostics below.";
            detail.Text = "Model: " + package.Model.Model.Name + "\n" + package.Model.ModelFingerprint + "\n\n" +
                string.Join("\n", package.Dashboard?.Bindings.Select(b => b.Page + "/" + b.Visual + " · " + b.Field + " · " + b.Status) ?? Array.Empty<string>()) +
                "\n" + string.Join("\n", (package.Dashboard?.Diagnostics ?? Array.Empty<DesignDiagnostic>()).Select(d => d.Severity + " " + d.Location + " · " + d.Message)) +
                (package.Theme is { } t ? "\n\nTheme: " + t.Name + "\n" + t.SchemaVersion + " · Schema valid: " + t.IsValid + "\nData colors: " + string.Join(", ", t.DataColors) + "\nVisual styles: " + string.Join(", ", t.VisualStyleFamilies) + "\n" + string.Join("\n", t.Diagnostics.Select(d => d.Severity + " " + d.Location + " · " + d.Message)) : "") +
                "\n\nProposed layout only; Desktop remains the final renderer. Optional reviewed samples/statistics remain available in Tools > AI Context Export.";
        }
        finally { validate.IsEnabled = true; }
    }
}
