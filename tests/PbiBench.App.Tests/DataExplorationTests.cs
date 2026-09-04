using System.Data;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PbiBench.App;
using PbiBench.Core.DataExploration;
using PbiBench.Core.Queries;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class DataExplorationTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;
    public DataExplorationTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;
    private static readonly DataModelSchema Model = new("Fixture", new[]
    {
        new DataTableSchema("Facts", DataStorageMode.Import, new[] { new DataColumnSchema("Group", "String"), new DataColumnSchema("Category", "String") }, new[] { new DataMeasureSchema("Rate", "0.25") }, Array.Empty<string>())
    }, Array.Empty<DataRelationshipSchema>());

    [Fact]
    public void MatrixKeepsBlankColumnsSeparateFromEngineTotalsAndDoesNotSumMeasures()
    {
        var plan = PivotQueryBuilder.Build(new PivotLayout { Rows = new[] { new PivotAxisField("Facts", "Group") }, Columns = new[] { new PivotAxisField("Facts", "Category") }, Values = new[] { new PivotValue("Facts", "Rate") } }, Model);
        object?[] Row(object? category, double value, bool total) => plan.ResultColumns.Select(c => c.Role switch
        {
            PivotResultRole.Row => "A", PivotResultRole.Column => category, PivotResultRole.Value => value,
            PivotResultRole.ColumnTotalFlag => (object)total, _ => false
        }).ToArray();
        var result = new QueryResultSet(0, "Fixture", plan.ResultColumns.Select((c, i) => new QueryColumn("C" + i, c.Key, "Object")).ToArray(), new[] { Row(null, .25, false), Row(null, .9, true) }, false);
        var matrix = PivotMatrix.Create(plan, result);
        Assert.Single(matrix.Rows.Cast<DataRow>());
        Assert.Contains(matrix.Columns.Cast<DataColumn>(), c => c.Caption.Contains("(Blank)"));
        Assert.Contains(matrix.Columns.Cast<DataColumn>(), c => c.Caption.Contains("Total"));
        Assert.Equal(.25, matrix.Rows[0]["V0"]); Assert.Equal(.9, matrix.Rows[0]["V1"]);
    }

    [Fact]
    public Task ReplacingAQueryPlanWhileItsSessionRunsDoesNotDisplayStaleData() => Sta(async () =>
    {
        var service = new PendingQueries(); using var panel = Panel(service);
        panel.SetPlan("EVALUATE ROW ( \"A\", 1 )", Array.Empty<string>());
        var run = panel.RunAsync();
        panel.SetPlan("EVALUATE ROW ( \"B\", 2 )", Array.Empty<string>());
        service.Complete(); await run;
        Assert.Equal(0, panel.ResultCount); Assert.Null(panel.LastResult); Assert.Contains("changed", panel.Status);
    });

    [Fact]
    public Task CancellationAllowsASecondExplorationRun() => Sta(async () =>
    {
        var service = new PendingQueries(); using var panel = Panel(service);
        panel.SetPlan("EVALUATE ROW ( \"A\", 1 )", Array.Empty<string>());
        var run = panel.RunAsync(); panel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var next = panel.RunAsync(); service.Complete(); await next;
        Assert.Equal(1, panel.ResultCount);
    });

    [Fact]
    public Task OpeningPivotLayoutPreservesOrderingAndRowCapWithoutExecuting() => Sta(() =>
    {
        var service = new PendingQueries(); using var panel = Panel(service); using var pivot = new PivotLabView(() => Model, panel, () => System.IO.Path.GetTempPath());
        pivot.LoadLayout(new PivotLayout { Rows = new[] { new PivotAxisField("Facts", "Group", true) }, Values = new[] { new PivotValue("Facts", "Rate") }, RowLimit = 5000, AutoRefresh = true });
        Assert.True(pivot.Layout.Rows[0].Descending); Assert.Equal(5000, pivot.Layout.RowLimit); Assert.False(pivot.Layout.AutoRefresh); Assert.Null(service.Request);
        return Task.CompletedTask;
    });

    private static DataQueryView Panel(IDaxQueryService service) => new(() => ("fixture", "fixture"), () => null, service);
    [Fact]
    public Task LargeResultGridRealizesOnlyTheVisibleRows() => Sta(() =>
    {
        using var panel = Panel(new PendingQueries());
        var columns = Enumerable.Range(0, 12).Select(i => new QueryColumn("C" + i, "Column " + i, "Int64")).ToArray();
        var rows = Enumerable.Range(0, 10000).Select(row => Enumerable.Range(0, 12).Select(column => (object?)(long)(row * 12 + column)).ToArray()).ToArray();
        var watch = Stopwatch.StartNew();
        panel.ShowResults(new QueryResult(Guid.NewGuid(), "EVALUATE fixture", "fixture", "fixture", DateTimeOffset.UtcNow, TimeSpan.Zero,
            new[] { new QueryResultSet(0, "Large fixture", columns, rows, false) }, 0, Array.Empty<string>()));
        panel.Measure(new Size(1200, 720)); panel.Arrange(new Rect(0, 0, 1200, 720)); panel.UpdateLayout();
        watch.Stop();
        var grid = Visuals(panel).OfType<DataGrid>().Single(); var realized = Visuals(grid).OfType<DataGridRow>().Count();
        Assert.Equal(10000, grid.Items.Count); Assert.True(grid.EnableRowVirtualization); Assert.True(grid.EnableColumnVirtualization);
        Assert.InRange(realized, 1, 100);
        output.WriteLine($"10,000 rows × 12 columns: {watch.Elapsed.TotalMilliseconds:N0} ms to materialize and lay out; {realized} realized rows.");
        return Task.CompletedTask;
    });
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Visuals(child)) yield return nested; }
    }
    private sealed class PendingQueries : IDaxQueryService
    {
        public QueryRequest? Request { get; private set; }
        private TaskCompletionSource<QueryResult> pending = null!;
        public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken token)
        {
            Request = request; var completion = pending = new TaskCompletionSource<QueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => completion.TrySetCanceled()); return completion.Task;
        }
        public void Complete() => pending.TrySetResult(new QueryResult(Guid.NewGuid(), Request!.Query, Request.Server, Request.Database, DateTimeOffset.UtcNow, TimeSpan.Zero,
            new[] { new QueryResultSet(0, "Fixture", new[] { new QueryColumn("C0", "Value", "Int64") }, new[] { new object?[] { 1L } }, false) }, Request.DocumentRevision, Array.Empty<string>()));
    }
    private static Task Sta(Func<Task> action)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () => { try { await action(); done.TrySetResult(true); } catch (Exception ex) { done.TrySetException(ex); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task;
    }
}
