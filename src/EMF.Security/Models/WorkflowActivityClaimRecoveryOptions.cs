namespace EMF.Security.Models;

public sealed class WorkflowActivityClaimRecoveryOptions
{
    public TimeSpan AbandonmentThreshold { get; init; }
        = TimeSpan.FromMinutes(15);

    public DateTimeOffset CalculateAbandonedBeforeUtc(
        DateTimeOffset reclaimedUtc)
    {
        if (AbandonmentThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AbandonmentThreshold));
        }

        return reclaimedUtc - AbandonmentThreshold;
    }
}
