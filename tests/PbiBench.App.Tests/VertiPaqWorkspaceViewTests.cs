using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using PbiBench.App;
using PbiBench.Core.Quality;
using TabularEditor.TOMWrapper;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class VertiPaqWorkspaceViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CockpitNavigatesMeasuresAndColumnsButOnlyProfilesColumns(bool measure) => Sta(() =>
    {
        using var handler = new TabularModelHandler(1600);
        var table = handler.Model.AddTable("Sales");
        table.AddDataColumn("Amount", dataType: DataType.Int64); table.AddMeasure("Total", "SUM('Sales'[Amount])");
        var member = measure ? "Total" : "Amount"; string? navigated = null, profiled = null;
        using var view = new VertiPaqWorkspaceView();
        view.Configure(handler, null, null, (selectedTable, selectedMember) => navigated = selectedTable + "/" + selectedMember,
            (selectedTable, selectedMember) => profiled = selectedTable + "/" + selectedMember);
        view.ShowSnapshot(new VertiPaqSnapshot("Test fixture", "Fixture", null, null, "1.0", false,
            new[] { new VertiPaqTable("Sales", null, null, null, null, null, null, "Import", null) },
            Array.Empty<VertiPaqColumn>(), Array.Empty<VertiPaqPartition>(), Array.Empty<VertiPaqSegment>(),
            Array.Empty<VertiPaqRelationship>(), Array.Empty<string>()));
        typeof(VertiPaqWorkspaceView).GetMethod("BindCurrentModel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
        var signal = new OptimizationSignal("fixture", "BPA", "DAX", "MANUAL", "Review object", "Fixture", "Sales", member);
        view.SetQualitySignals(new[] { signal }); Field<DataGrid>(view, "signalGrid").SelectedItem = signal;
        Field<Button>(view, "navigateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Sales/" + member, navigated);
        var profile = Field<Button>(view, "profileButton"); Assert.Equal(!measure, profile.IsEnabled);
        profile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (measure) { Assert.Null(profiled); Assert.Contains("Select a model column", view.Status); }
        else Assert.Equal("Sales/Amount", profiled);
    });

    private static T Field<T>(VertiPaqWorkspaceView view, string name) =>
        (T)typeof(VertiPaqWorkspaceView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
    private static Task Sta(Action action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
