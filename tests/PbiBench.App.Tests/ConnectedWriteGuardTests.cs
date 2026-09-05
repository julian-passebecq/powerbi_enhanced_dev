using PbiBench.App;
using Xunit;

namespace PbiBench.App.Tests;

public sealed class ConnectedWriteGuardTests
{
    [Fact]
    public void BackgroundCompletionDuringModalReviewInvalidatesTheApproval()
    {
        var owner = new object();
        Assert.True(MainWindow.CanFinishWriteReview(owner, owner, null));
        Assert.False(MainWindow.CanFinishWriteReview(owner, owner, owner));
        Assert.False(MainWindow.CanFinishWriteReview(owner, new object(), null));
        Assert.False(MainWindow.CanFinishWriteReview(owner, null, null));
    }

    [Fact]
    public void ReconnectedSessionStillMatchesTheSubmittedTargetButOtherTargetsDoNot()
    {
        Assert.True(MainWindow.SameConnectedTarget("localhost:5500", "model-id", "LOCALHOST:5500", "model-id"));
        Assert.False(MainWindow.SameConnectedTarget("localhost:5500", "model-id", "localhost:6600", "model-id"));
        Assert.False(MainWindow.SameConnectedTarget("localhost:5500", "model-id", "localhost:5500", "other-model"));
        Assert.False(MainWindow.SameConnectedTarget(null, "model-id", null, "model-id"));
    }
}
