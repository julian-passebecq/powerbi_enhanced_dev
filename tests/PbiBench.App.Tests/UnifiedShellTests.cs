using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PbiBench.AI.ContextExport;
using PbiBench.App;
using PbiBench.DesignExchange;
using PbiBench.DesignSystem;
using PbiBench.ExternalTools;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class UnifiedShellTests
{
    [Fact] public Task NavigationAndSharedIconsCoverTheProductWithoutLegacyPanelOverload() => Sta(() =>
    {
        Assert.Equal(new[] { "Home", "Model", "DAX", "Automate", "Report", "Project", "Fabric", "Tools", "Settings", "About" }, MainWindow.ModuleNames);
        foreach (var module in MainWindow.ModuleNames) { Assert.Contains(module, PbiBenchTheme.IconNames); Assert.IsType<Viewbox>(PbiBenchTheme.Icon(module)); }
        var window = new Window(); PbiBenchTheme.Apply(window); Assert.Equal(PbiBenchTheme.Background, window.Background); Assert.Equal("Segoe UI", window.FontFamily.Source); window.Close();
        return Task.CompletedTask;
    });
    [Fact] public Task ChangingDesignInputsInvalidatesPreviewAndNeverLaunchesImplicitly() => Sta(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var context = ModelContext.Create(new ContextModel("Fixture", 1600, Array.Empty<ContextObject>(), Array.Empty<ContextRelationship>(), Array.Empty<ContextDependency>()));
            var path = Path.Combine(folder, "model.json"); var theme = Path.Combine(folder, "theme.json"); await context.SaveAsync(path, default); File.WriteAllText(theme, "{\"name\":\"Neutral\",\"dataColors\":[\"#315DA8\"]}");
            var launches = 0; var view = new DesignExchangeView(() => context, _ => launches++, default); view.SetInputs(path, null, theme); await view.ValidateAsync();
            Assert.True(view.CurrentPackage!.IsValid); Assert.Equal(0, launches);
            view.SetInputs(path, null, null); Assert.Null(view.CurrentPackage);
            view.SetInputs(path, null, theme); File.WriteAllText(theme, "{\"name\":42}"); await view.ValidateAsync(); Assert.False(view.CurrentPackage!.IsValid); Assert.Equal(0, launches);
        }
        finally { Directory.Delete(folder, true); }
    });
    private static Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)); Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () => { try { await action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } })); Dispatcher.Run(); });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
