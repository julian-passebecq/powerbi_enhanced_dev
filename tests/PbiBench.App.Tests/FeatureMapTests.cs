using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PbiBench.App;
using PbiBench.Core.Platform;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class FeatureMapTests
{
    [Fact] public Task AboutOpensOfflineWithFeatureMapAndPreservesProvenanceTab() => Sta(() =>
    {
        var owner = new Window { Opacity = 0, ShowInTaskbar = false, ShowActivated = false, Width = 1, Height = 1 };
        owner.Show(); var window = MainWindow.CreateAboutWindow(owner);
        try
        {
            Assert.Same(owner, window.Owner); Assert.Equal(0, window.Pages.SelectedIndex); Assert.Contains("11.3.0", window.Title);
            Assert.Equal(new[] { "Feature Map", "Provenance / About" }, window.Pages.Items.Cast<TabItem>().Select(t => (string)t.Header));
            Assert.Equal(21, window.Map.VisibleRows.Count);
            var provenance = (DataGrid)((TabItem)window.Pages.Items[1]).Content; Assert.True(provenance.IsReadOnly);
            Assert.Equal(ProvenanceCatalog.Bundled().Components.Count, provenance.Items.Count);
            window.Pages.SelectedIndex = 1; Assert.Same(provenance, ((TabItem)window.Pages.SelectedItem).Content);
        }
        finally { window.Close(); owner.Close(); }
    });
    [Fact] public Task MapUsesSixConciseReadOnlyColumnsAndUserOperableFilters() => Sta(() =>
    {
        var view = View(); Assert.True(view.FeatureGrid.IsReadOnly); Assert.False(view.FeatureGrid.CanUserAddRows);
        Assert.Equal(new[] { "Feature", "Status", "Origin", "Our implementation", "TE3 comparable capability", "Lifecycle" }, view.FeatureGrid.Columns.Select(c => (string)c.Header));
        var filters = Descendants(view).OfType<RadioButton>().ToDictionary(b => (string)b.Content);
        Assert.Equal(new[] { "All", "Core", "Companions", "Labs", "TE3 gaps" }, filters.Keys);
        filters["Core"].IsChecked = true; Assert.Equal(10, view.VisibleRows.Count); Assert.All(view.VisibleRows, r => Assert.Equal("Core", r.Status));
        filters["Companions"].IsChecked = true; Assert.Equal(3, view.VisibleRows.Count); Assert.Contains(view.VisibleRows, r => r.Status == "External");
        filters["Labs"].IsChecked = true; Assert.Equal(5, view.VisibleRows.Count); Assert.All(view.VisibleRows, r => Assert.Contains(r.Status, new[] { "Labs", "Future" }));
        filters["TE3 gaps"].IsChecked = true; Assert.Contains(view.VisibleRows, r => r.Feature.Id == "dax-debugger");
        filters["All"].IsChecked = true; Assert.Equal(21, view.VisibleRows.Count);
    });
    [Fact] public Task SelectionDetailFollowsFiltersAndClearlyMarksFutureAndGapRows() => Sta(() =>
    {
        var view = View(); Assert.Contains("semantic.model-editor.te2", view.SelectedDetail); Assert.Contains("update lane: te2", view.SelectedDetail);
        view.FeatureGrid.SelectedItem = view.VisibleRows.Single(r => r.Feature.Id == "dax-debugger");
        Assert.Contains("No implementation provenance claimed", view.SelectedDetail); Assert.Contains("No expression stepping", view.SelectedDetail);
        view.SelectFilter(FeatureMapFilter.Core); Assert.Contains("semantic.model-editor.te2", view.SelectedDetail); Assert.DoesNotContain("No expression stepping", view.SelectedDetail);
        view.SelectFilter(FeatureMapFilter.Labs); view.FeatureGrid.SelectedItem = view.VisibleRows.Single(r => r.Feature.Id == "pbir");
        Assert.Contains("No PBIR editor", view.SelectedDetail); Assert.Equal("Future", ((FeatureMapRow)view.FeatureGrid.SelectedItem).Status);
    });
    [Fact] public Task DetailedDocumentOpensOnlyByExplicitActionAndReadsTheBundledFile() => Sta(() =>
    {
        var view = View(); var requested = 0; view.DocumentationRequested += (_, _) => requested++;
        view.SelectFilter(FeatureMapFilter.Labs); Assert.Equal(0, requested);
        Descendants(view).OfType<Button>().Single(b => (string)b.Content == "Open detailed catalog").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, requested);
        var bundled = FeatureMapWindow.ReadDetailedCatalog(AppDomain.CurrentDomain.BaseDirectory).Replace("\r\n", "\n");
        Assert.Equal(FeatureCatalog.Bundled().ToMarkdown(ProvenanceCatalog.Bundled()), bundled);
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.Throws<DirectoryNotFoundException>(() => FeatureMapWindow.ReadDetailedCatalog(missing));
    });
    private static FeatureMapView View() => new(FeatureCatalog.Bundled(), ProvenanceCatalog.Bundled());
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static Task Sta(Action action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
