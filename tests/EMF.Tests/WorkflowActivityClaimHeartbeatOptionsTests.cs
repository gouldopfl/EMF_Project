using EMF.Orchestration.Models;

namespace EMF.Tests;

public sealed class WorkflowActivityClaimHeartbeatOptionsTests
{
    [Fact]
    public void Default_interval_is_five_minutes()
    {
        var options =
            new WorkflowActivityClaimHeartbeatOptions();

        Assert.Equal(
            TimeSpan.FromMinutes(5),
            options.Interval);
    }

    [Fact]
    public void Non_positive_interval_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorkflowActivityClaimHeartbeatOptions(
                TimeSpan.Zero));
    }

    [Fact]
    public void Interval_at_or_above_abandonment_threshold_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorkflowActivityClaimHeartbeatOptions(
                TimeSpan.FromMinutes(15)));
    }
}
