using System.IO;
using System.Windows;
using System.Windows.Threading;
using PbiBench.AI.ContextExport;
using PbiBench.DesignExchange;
using PbiBench.ExternalTools;
using PbiBench.ReportStudio;
using Xunit;

namespace PbiBench.ReportStudio.Tests;

public sealed class DesignPreviewTests
{
    [Fact] public Task PreviewLoadsOfflineShowsUnsupportedIntentAndNeverCreatesAChangePlan() => Sta(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); var window = new StudioWindow();
        try
        {
            var context = ModelContext.Create(new ContextModel("Fixture", 1600, Array.Empty<ContextObject>(), Array.Empty<ContextRelationship>(), Array.Empty<ContextDependency>()));
            var model = Path.Combine(folder, "model.json"); var spec = Path.Combine(folder, "spec.json"); await context.SaveAsync(model, default);
            var intent = new DashboardSpec(1, new("Proposed report", "Executive"), new[] {
                new DesignPage("summary", "Summary", new(1280, 720), new[] { new DesignVisual("text", "text", new Dictionary<string, DesignBinding>(), Region: "top") }),
                new DesignPage("future", "Future", new(1280, 720), new[] { new DesignVisual("unknown", "future-visual", new Dictionary<string, DesignBinding> { ["value"] = new("Measure", "Sales", "Future") }, Region: "middle") })
            }, Unbound: true);
            File.WriteAllText(spec, ContractJson.Serialize(intent)); await window.OpenDesignAsync(model, spec, null);
            Assert.NotNull(window.DesignPreview); Assert.Equal(1, window.DesignPreview.VisualCount); window.DesignPreview.SelectPage(1); Assert.Equal(1, window.DesignPreview.VisualCount);
            Assert.Contains(window.DesignPreview.Package.Dashboard!.Diagnostics, d => d.Message.Contains("Unsupported")); Assert.Null(window.CurrentPlan); Assert.Null(window.CurrentReport); Assert.Equal(2, Directory.GetFiles(folder).Length);
            File.WriteAllText(spec, "{\"script\":\"Run()\"}"); await Assert.ThrowsAsync<InvalidDataException>(() => window.OpenDesignAsync(model, spec, null)); Assert.Null(window.DesignPreview); Assert.Null(window.CurrentPlan);
        }
        finally { window.Close(); Directory.Delete(folder, true); }
    });
    private static Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)); Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () => { try { await action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } })); Dispatcher.Run(); }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
