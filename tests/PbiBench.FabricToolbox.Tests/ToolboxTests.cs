using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using PbiBench.Core.Fabric;
using PbiBench.Fabric;
using PbiBench.FabricToolbox;
using Xunit;

namespace PbiBench.FabricToolbox.Tests;

public sealed class ToolboxTests
{
    [Fact] public Task OpeningFilteringInspectingAndLinkingItemsDoNotSendRequests() => Sta(async () =>
    {
        using var handler = new Responses(); var window = new ToolboxWindow(new HttpClient(handler), new Auth());
        try
        {
            var items = Inventory(); window.SetInventory(items);
            Field<TextBox>(window, "itemSearch").Text = "night";
            var grid = Field<DataGrid>(window, "items"); Assert.Single(grid.Items.Cast<object>()); Assert.Contains(items[0].Id, Field<TextBox>(window, "itemDetail").Text);
            Field<TextBox>(window, "itemSearch").Text = ""; Field<ComboBox>(window, "itemType").SelectedItem = "Report"; Assert.Single(grid.Items.Cast<object>());
            Field<TabControl>(window, "pages").SelectedIndex = 3; Assert.Equal(0, handler.Calls);
            Field<ComboBox>(window, "jobItem").SelectedItem = items[1]; await window.RefreshJobsAsync(default); Assert.Equal(0, handler.Calls); Assert.Contains("not supported", Field<TextBlock>(window, "jobNotice").Text);
            Assert.Empty(Field<DataGrid>(window, "jobs").Items);
        }
        finally { window.Close(); }
    });
    [Fact] public Task ManualRefreshPopulatesReadOnlyGridAndChangingItemClearsResults() => Sta(async () =>
    {
        using var handler = new Responses(); var window = new ToolboxWindow(new HttpClient(handler), new Auth());
        try
        {
            var inventory = Inventory(); window.SetInventory(inventory); var choice = Field<ComboBox>(window, "jobItem"); choice.SelectedItem = inventory[0];
            Assert.Equal(0, handler.Calls); await window.RefreshJobsAsync(default); Assert.Equal(1, handler.Calls);
            var grid = Field<DataGrid>(window, "jobs"); Assert.True(grid.IsReadOnly); Assert.Single(grid.Items.Cast<object>()); grid.SelectedIndex = 0;
            Assert.Contains("Fixture failure", Field<TextBox>(window, "jobDetail").Text); Assert.Contains("Correlation", Field<TextBox>(window, "jobDetail").Text);
            Field<TextBox>(window, "jobSearch").Text = "Completed"; Assert.Empty(grid.Items); Field<TextBox>(window, "jobSearch").Text = "Failed"; Assert.Single(grid.Items.Cast<object>());
            choice.SelectedItem = inventory[1]; Assert.Empty(grid.Items); Assert.Equal("", Field<TextBox>(window, "jobDetail").Text); Assert.Equal(1, handler.Calls);
        }
        finally { window.Close(); }
    });
    [Fact] public Task WorkspaceChangeAndSignoutCannotLeaveStaleInventory() => Sta(() =>
    {
        using var handler = new Responses(); var window = new ToolboxWindow(new HttpClient(handler), new Auth());
        try
        {
            window.SetInventory(Inventory()); var workspaces = Field<ComboBox>(window, "workspaces"); workspaces.ItemsSource = new[] { new FabricWorkspace(Guid.NewGuid().ToString(), "Other") }; workspaces.SelectedIndex = 0;
            Assert.Empty(Field<DataGrid>(window, "items").Items); Assert.Empty(Field<ComboBox>(window, "jobItem").Items); Assert.Empty(Field<DataGrid>(window, "jobs").Items);
            Assert.Equal(0, handler.Calls);
        }
        finally { window.Close(); } return Task.CompletedTask;
    });
    private static FabricItem[] Inventory() => new[] { new FabricItem("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "Nightly pipeline", "DataPipeline"), new FabricItem("11111111-1111-1111-1111-111111111111", "33333333-3333-3333-3333-333333333333", "Sales report", "Report") };
    private sealed class Responses : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[{\"id\":\"44444444-4444-4444-4444-444444444444\",\"itemId\":\"22222222-2222-2222-2222-222222222222\",\"jobType\":\"Pipeline\",\"status\":\"Failed\",\"failureReason\":{\"message\":\"Fixture failure\"}}]}") });
        }
    }
    private sealed class Auth : IFabricAuthenticator
    {
        public string? AccountLabel => "Fixture";
        public Task SignInAsync(FabricSignInOptions options, FabricAudience audience, CancellationToken cancellationToken) => throw new InvalidOperationException("No interactive sign-in allowed.");
        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> GetAccessTokenAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default) => Task.FromResult("fixture-token");
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static Task Sta(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher)); dispatcher.BeginInvoke(new Action(async () => { try { await action(); completion.TrySetResult(true); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } })); Dispatcher.Run(); });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
