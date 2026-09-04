using PbiBench.Core.Commands;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class WorkbenchCommandTests
{
    [Fact]
    public void SharedRoutesHonorCurrentAvailabilityAndExecuteExactlyOnce()
    {
        var registry = new WorkbenchCommandRegistry();
        var executions = 0;
        var ready = false;
        registry.Register(WorkbenchCommandId.Save, () => executions++, () => ready);
        Assert.False(registry.Execute(WorkbenchCommandId.Save));
        Assert.False(registry.CanExecute(WorkbenchCommandId.Save));
        Assert.False(registry.Execute(WorkbenchCommandId.Connect));
        Assert.Equal(0, executions);
        ready = true;
        Assert.True(registry.CanExecute(WorkbenchCommandId.Save));
        Assert.True(registry.Execute(WorkbenchCommandId.Save));
        Assert.Equal(1, executions);
        registry.Register(WorkbenchCommandId.Save, () => executions += 10);
        registry.Execute(WorkbenchCommandId.Save);
        Assert.Equal(11, executions);
    }
}
