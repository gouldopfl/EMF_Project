using EMF.Security.Models;

namespace EMF.Tests;

public sealed class WorkflowActivityClaimRecoveryOptionsTests
{
    [Fact]
    public void Default_abandonment_threshold_is_fifteen_minutes()
    {
        var options =
            new WorkflowActivityClaimRecoveryOptions();

        Assert.Equal(
            TimeSpan.FromMinutes(15),
            options.AbandonmentThreshold);
    }

    [Fact]
    public void CalculateCutoff_subtracts_threshold()
    {
        var reclaimedUtc =
            new DateTimeOffset(
                2026, 8, 18, 12, 0, 0,
                TimeSpan.Zero);

        var options =
            new WorkflowActivityClaimRecoveryOptions
            {
                AbandonmentThreshold =
                    TimeSpan.FromMinutes(20)
            };

        Assert.Equal(
            reclaimedUtc.AddMinutes(-20),
            options.CalculateAbandonedBeforeUtc(
                reclaimedUtc));
    }

    [Fact]
    public void Non_positive_threshold_is_rejected()
    {
        var options =
            new WorkflowActivityClaimRecoveryOptions
            {
                AbandonmentThreshold = TimeSpan.Zero
            };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => options.CalculateAbandonedBeforeUtc(
                DateTimeOffset.UtcNow));
    }
}
