using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PbiBench.Automation.Agent;
using PbiBench.Automation.Commands;
using PbiBench.Core.Agent;
using PbiBench.Core.Automation;
using PbiBench.Core.Commands;
using PbiBench.Core.Quality;
using PbiBench.Core.Queries;
using PbiBench.Core.Tasks;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.App;

/// <summary>Explicit context sharing and typed proposals over the common GUI/CLI command engine.</summary>
public sealed class AgentWorkspaceView : UserControl, IDisposable
{
    private readonly Func<TabularModelHandler?> currentHandler;
    private readonly Func<IReadOnlyList<TabularNamedObject>> selection;
    private readonly Func<AgentContextExtras> extras;
    private readonly Action changed;
    private readonly Action<string> openQuery;
    private readonly Action<SemanticTestArtifact> stageTests;
    private readonly AgentProposalService service;
    private readonly BackgroundTaskQueue queue;
    private readonly bool ownsQueue;
    private readonly IAgentProvider? suppliedProvider;
    private readonly HttpClient http = OpenAiAgentProvider.CreateHttpClient();
    private readonly TextBox prompt = Editor("Review the selected model objects and propose a concrete improvement.");
    private readonly TextBox source = Editor("");
    private readonly TextBox contextText = Editor("Capture context to inspect its exact contents.", true);
    private readonly TextBox explanation = Editor("Offline mode is ready. No model metadata is sent until you select the optional provider, capture context, review the payload and explicitly send it.", true);
    private readonly DataGrid diff = Grid();
    private readonly TextBlock status = Note("Offline. Capture context or load a typed proposal to begin.");
    private readonly ComboBox provider = new() { Width = 150, Margin = new Thickness(4), ItemsSource = new[] { "Offline", "OpenAI Responses" }, SelectedIndex = 0 };
    private readonly TextBox model = new() { Width = 170, Margin = new Thickness(4), ToolTip = "OpenAI model ID enabled for your project" };
    private readonly PasswordBox apiKey = new() { Width = 210, Margin = new Thickness(4), ToolTip = "API key, kept only in this window's memory" };
    private readonly CheckBox consent = new() { Content = "I reviewed the context below and agree to send it with this request to OpenAI.", Margin = new Thickness(6) };
    private readonly TextBox folder = new() { Text = "Measures", Width = 150, Margin = new Thickness(4) };
    private readonly TabControl workspaceTabs = new();
    private readonly CheckBox[] sections = { Option("Selected objects"), Option("Model inventory"), Option("Current DAX"), Option("BPA findings"), Option("Semantic workspace diff"), Option("Test results"), Option("Capabilities") };
    private CancellationTokenSource? active;
    private AgentContextDocument? context;
    private TabularModelHandler? observedHandler;
    private string observedFingerprint = "", selectedSignature = "";
    private long version;
    private bool loading, disposed;
    public PreparedCommand? LastPreview { get; private set; }
    public AgentProposal? Proposal { get; private set; }
    public string SharedContextJson => context?.SharedJson ?? "";
    public string Status => status.Text;
    public bool IsRunning => active != null;

    public AgentWorkspaceView(Func<TabularModelHandler?> currentHandler, Func<IReadOnlyList<TabularNamedObject>> selection,
        Action changed, Action<string> openQuery, Action<SemanticTestArtifact> stageTests, Func<AgentContextExtras>? extras = null,
        BackgroundTaskQueue? queue = null, IAgentProvider? provider = null)
    {
        this.currentHandler = currentHandler; this.selection = selection; this.changed = changed; this.openQuery = openQuery; this.stageTests = stageTests;
        this.extras = extras ?? (() => new AgentContextExtras()); this.queue = queue ?? new BackgroundTaskQueue(); ownsQueue = queue == null; suppliedProvider = provider; service = new(currentHandler);
        var root = new DockPanel(); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        root.Children.Add(workspaceTabs); Content = root;
        workspaceTabs.Items.Add(new TabItem { Header = "Request and context", Content = ContextPage() });
        workspaceTabs.Items.Add(new TabItem { Header = "Proposal and preview", Content = ProposalPage() });
        var contracts = new TabControl();
        contracts.Items.Add(new TabItem { Header = "Shared tool contract", Content = Editor(CommandSchema.Export(modelFacing: true), true) });
        contracts.Items.Add(new TabItem { Header = "Proposal JSON Schema", Content = Editor(AgentProposalJson.SchemaJson, true) });
        workspaceTabs.Items.Add(new TabItem { Header = "Contract", Content = contracts });
        foreach (var section in sections) { section.Checked += (_, _) => InvalidateContext(); section.Unchecked += (_, _) => InvalidateContext(); }
        prompt.TextChanged += (_, _) => DraftChanged(); source.TextChanged += (_, _) => DraftChanged();
        this.provider.SelectionChanged += (_, _) => { consent.IsChecked = false; DraftChanged(); };
        model.TextChanged += (_, _) => { consent.IsChecked = false; DraftChanged(); }; apiKey.PasswordChanged += (_, _) => DraftChanged();
        RefreshModel();
    }
    public void RefreshModel()
    {
        if (disposed) return; var handler = currentHandler(); var fingerprint = handler == null ? "" : new SemanticModelService(handler).Fingerprint();
        var signature = SelectionSignature();
        if (!ReferenceEquals(observedHandler, handler) || observedFingerprint != fingerprint || selectedSignature != signature)
        { InvalidateContext(); observedHandler = handler; observedFingerprint = fingerprint; selectedSignature = signature; }
    }
    public AgentContextDocument CaptureContext(AgentContextOptions options)
    {
        loading = true;
        try
        {
            var flags = new[] { options.SelectedObjects, options.Inventory, options.CurrentDax, options.BpaFindings, options.WorkspaceDiff, options.TestResults, options.Capabilities };
            for (var index = 0; index < sections.Length; index++) sections[index].IsChecked = flags[index];
        }
        finally { loading = false; }
        active?.Cancel(); version++; LastPreview = null; Proposal = null; diff.ItemsSource = null; consent.IsChecked = false;
        context = service.Capture(selection(), options, extras()); contextText.Text = context.SharedJson;
        observedHandler = currentHandler(); observedFingerprint = context.ModelFingerprint; selectedSignature = SelectionSignature();
        status.Text = "Context captured locally. Review every shared section and omission marker before sending."; return context;
    }
    public void LoadProposal(string json)
    {
        var parsed = AgentProposalJson.Parse(json); if (context == null) CaptureContext(Options());
        active?.Cancel(); version++; LastPreview = null; diff.ItemsSource = null;
        loading = true; try { source.Text = AgentProposalJson.Serialize(parsed); } finally { loading = false; }
        Proposal = parsed; explanation.Text = parsed.Explanation; ShowProposal(); status.Text = "Typed proposal loaded and validated. No changes or queries have run.";
    }
    public async Task GenerateAsync(string requestText, bool approveContextSharing = false)
    {
        loading = true; try { prompt.Text = requestText; } finally { loading = false; }
        var captured = Context(); service.ValidateContext(captured);
        var selected = suppliedProvider ?? (provider.SelectedIndex == 0 ? new OfflineAgentProvider() : (IAgentProvider)new OpenAiAgentProvider(http, model.Text.Trim(), Key(apiKey.Password)));
        if (selected.IsOnline && !approveContextSharing) throw new InvalidOperationException("Review and approve the exact context before sending it to OpenAI.");
        var request = new AgentRequest(requestText, captured, approveContextSharing); request.Validate();
        var runVersion = ++version; active?.Cancel(); var cancellation = active = new CancellationTokenSource(); LastPreview = null; Proposal = null; diff.ItemsSource = null;
        status.Text = selected.IsOnline ? "Requesting one proposal from OpenAI…" : "Preparing an offline context review…";
        try
        {
            var task = queue.Enqueue("Agent proposal — " + selected.DisplayName, worker => selected.ProposeAsync(request, worker.CancellationToken), cancellation.Token);
            var result = await task.Completion;
            if (disposed || runVersion != version || cancellation.IsCancellationRequested) return;
            service.ValidateContext(captured); AgentProposalJson.Validate(result);
            loading = true; try { source.Text = AgentProposalJson.Serialize(result); } finally { loading = false; }
            Proposal = result; explanation.Text = result.Explanation; ShowProposal();
            status.Text = "Proposal received. Validate and preview an action, or stage its query/test as a draft. Nothing was executed.";
        }
        finally { cancellation.Dispose(); if (ReferenceEquals(active, cancellation)) active = null; consent.IsChecked = false; }
    }
    public async Task PreparePreviewAsync()
    {
        var proposal = AgentProposalJson.Parse(source.Text); var captured = Context(); service.ValidateContext(captured);
        var runVersion = ++version; active?.Cancel(); var cancellation = active = new CancellationTokenSource(); LastPreview = null; diff.ItemsSource = null;
        try
        {
            var prepared = await service.PrepareAsync(proposal, captured, cancellation.Token);
            if (disposed || runVersion != version || cancellation.IsCancellationRequested) return;
            Proposal = proposal; LastPreview = prepared; diff.ItemsSource = prepared.Review.Changes;
            explanation.Text = proposal.Explanation + "\n\n" + string.Join("\n", prepared.Review.Issues.Select(issue => issue.Severity + ": " + issue.Message));
            status.Text = "Shared command preview prepared: " + prepared.Review.Changes.Count + " exact local changes. Review / apply uses the existing native undo transaction.";
        }
        finally { cancellation.Dispose(); if (ReferenceEquals(active, cancellation)) active = null; }
    }
    public async Task<CommandResult> ApplyPreviewAsync(string reviewHash, string actor)
    {
        var prepared = LastPreview ?? throw new InvalidOperationException("Prepare a current preview first.");
        var result = await service.ApplyAsync(prepared, Context(), reviewHash, actor, CancellationToken.None);
        LastPreview = null; diff.ItemsSource = null; changed(); RefreshModel(); status.Text = result.Message; return result;
    }
    public void StageProposal()
    {
        var proposal = AgentProposalJson.Parse(source.Text); service.ValidateContext(Context());
        if (proposal.Kind == AgentProposalKind.Query)
        { ValidateQuery(proposal.Query!); openQuery(proposal.Query!); status.Text = "Query opened as a DAX draft. Review it and explicitly run it there."; }
        else if (proposal.Kind == AgentProposalKind.Test)
        { ValidateQuery(proposal.Test!.Query); stageTests(proposal.ToTestArtifact()); status.Text = "Scalar assertion staged in QA. Review the expected value and explicitly run it there."; }
        else throw new InvalidOperationException("Only query and test proposals can be staged. Action proposals use Preview.");
        Proposal = proposal;
    }
    public void ShowProposal() => workspaceTabs.SelectedIndex = 1;
    private UIElement ContextPage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Note("Select the metadata sections to capture. Default offline mode keeps every operation local. The OpenAI option sends the displayed payload and your request only after explicit Send; no tools execute automatically."));
        top.Children.Add(Bar(sections.Cast<UIElement>().ToArray()));
        top.Children.Add(Bar(Button("Capture / review context", () => CaptureContext(Options())), provider, Button("Generate proposal", () => GenerateAsync(prompt.Text, consent.IsChecked == true)), Button("Cancel", () => { active?.Cancel(); status.Text = "Cancellation requested."; })));
        var configuration = new StackPanel(); configuration.Children.Add(Bar(Note("Model ID"), model, Note("API key (session only)"), apiKey, Button("Forget key", () => apiKey.Clear())));
        configuration.Children.Add(Note("Configure your own OpenAI API project and a model supporting Structured Outputs. The key is never saved. Requests set store=false; provider retention still follows your organization's API data controls."));
        top.Children.Add(new Expander { Header = "Optional OpenAI provider configuration", Content = configuration, Margin = new Thickness(4) }); top.Children.Add(consent);
        panel.Children.Add(Split(prompt, contextText)); return panel;
    }
    private UIElement ProposalPage()
    {
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Note("Typed proposals are data. Only explicit local model recipes reach the shared command preview. Queries/tests are staged as drafts; explanations and reviews never execute."));
        top.Children.Add(Bar(Button("Validate", () => { Proposal = AgentProposalJson.Parse(source.Text); explanation.Text = Proposal.Explanation; status.Text = "Proposal schema and supported operations are valid. Model applicability requires Preview."; }),
            Button("Preview action", PreparePreviewAsync), Button("Review / apply…", ReviewApplyAsync), Button("Stage query / test", StageProposal), Button("Open proposal…", OpenAsync), Button("Save proposal…", SaveAsync)));
        top.Children.Add(Bar(Note("Offline template: selected measure folders"), folder, Button("Create typed proposal", FolderTemplate)));
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Exact changes", Content = diff }); tabs.Items.Add(new TabItem { Header = "Explanation / diagnostics", Content = explanation });
        panel.Children.Add(Split(source, tabs)); return panel;
    }
    private void FolderTemplate()
    {
        var measures = selection().OfType<Measure>().ToArray(); if (measures.Length == 0 || measures.Length > 100) throw new InvalidOperationException("Select 1 to 100 measures for this explicit folder template.");
        if (context == null) CaptureContext(Options());
        var recipe = new ActionRecipe("Organize selected measures", measures.Select(measure => new RecipeStep(new(RecipeScope.Measure, measure.Table.Name, measure.Name), RecipeOperation.SetProperty, "DisplayFolder", RecipeValue.Literal(folder.Text))).ToArray());
        LoadProposal(AgentProposalJson.Serialize(new(1, AgentProposalKind.Action, recipe.Name, "Offline template: set the named measures to the literal folder shown in the proposal. Preview resolves the exact current-model changes.", recipe, null, null)));
    }
    private async Task ReviewApplyAsync()
    {
        var preview = LastPreview ?? throw new InvalidOperationException("Prepare a current preview first."); var review = preview.Review;
        var rows = review.Changes.Select(change => new PreviewRow(change.ObjectPath, change.Property, change.Before, change.After, change.Reason)).ToArray();
        if (!PreviewDialog.Show(Window.GetWindow(this), Proposal?.Title ?? "Agent action", "Review these exact local changes. One native Undo restores the batch.\n" + string.Join("\n", review.Issues.Select(issue => issue.Severity + ": " + issue.Message)), rows, review.CanApply, "Apply to model")) return;
        await ApplyPreviewAsync(review.Hash, "Interactive Agent review");
    }
    private async Task OpenAsync()
    {
        var dialog = new OpenFileDialog { Filter = "PbiBench Agent proposal|*.pbiagent;*.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (new FileInfo(dialog.FileName).Length > AgentProposalJson.MaximumBytes) throw new InvalidDataException("Proposal files are limited to 256 KiB.");
        using var reader = File.OpenText(dialog.FileName); LoadProposal(await reader.ReadToEndAsync());
    }
    private async Task SaveAsync()
    {
        var proposal = AgentProposalJson.Parse(source.Text); var dialog = new SaveFileDialog { Filter = "PbiBench Agent proposal|*.pbiagent", FileName = "model-proposal.pbiagent" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await PbiBench.Dax.LanguageService.DaxScriptFile.SaveAsync(dialog.FileName, AgentProposalJson.Serialize(proposal), CancellationToken.None);
    }
    private AgentContextDocument Context() => context ?? throw new InvalidOperationException("Capture and review the current context first.");
    private string SelectionSignature() => System.Text.Json.JsonSerializer.Serialize(selection().Select(SemanticModelService.ObjectPath).ToArray());
    private AgentContextOptions Options() => new(sections[0].IsChecked == true, sections[1].IsChecked == true, sections[2].IsChecked == true, sections[3].IsChecked == true, sections[4].IsChecked == true, sections[5].IsChecked == true, sections[6].IsChecked == true);
    private static Func<CancellationToken, Task<string>> Key(string value) => ct => { ct.ThrowIfCancellationRequested(); return Task.FromResult(value); };
    private static void ValidateQuery(string query) => new QueryRequest("draft-validation", "draft-validation", query, 1000, 60).Validate();
    private void DraftChanged() { if (loading) return; version++; active?.Cancel(); Proposal = null; LastPreview = null; diff.ItemsSource = null; consent.IsChecked = false; }
    private void InvalidateContext() { if (loading) return; DraftChanged(); service.Invalidate(); context = null; contextText.Text = "Context changed. Capture and review it again."; }
    private Button Button(string text, Action action) => Button(text, () => { action(); return Task.CompletedTask; });
    private Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(8, 4, 8, 4) };
        button.Click += async (_, _) => { try { button.IsEnabled = false; await action(); } catch (OperationCanceledException) { status.Text = "Canceled. No new proposal was accepted."; } catch (Exception error) { status.Text = error.Message; } finally { if (!disposed) button.IsEnabled = true; } }; return button;
    }
    private static CheckBox Option(string text) => new() { Content = text, Margin = new Thickness(5) };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private static TextBox Editor(string value, bool readOnly = false) => new() { Text = value, IsReadOnly = readOnly, AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas"), FontSize = 13, Margin = new Thickness(4), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private static DataGrid Grid() => new() { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, Margin = new Thickness(4) };
    private static WrapPanel Bar(params UIElement[] children) { var panel = new WrapPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    private static UIElement Split(UIElement first, UIElement second)
    {
        var grid = new System.Windows.Controls.Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) }); grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(first); var split = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; System.Windows.Controls.Grid.SetRow(split, 1); grid.Children.Add(split); System.Windows.Controls.Grid.SetRow(second, 2); grid.Children.Add(second); return grid;
    }
    public void Dispose() { if (disposed) return; disposed = true; active?.Cancel(); apiKey.Clear(); service.Invalidate(); if (ownsQueue) queue.Dispose(); http.Dispose(); }
}
