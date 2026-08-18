using EMF.Security.Models;

namespace EMF.Orchestration.Models;

public sealed class WorkflowActivityClaimHeartbeatOptions
{
    public WorkflowActivityClaimHeartbeatOptions(
        TimeSpan? interval = null)
    {
        Interval =
            interval ??
            TimeSpan.FromMinutes(5);

        var abandonmentThreshold =
            new WorkflowActivityClaimRecoveryOptions()
                .AbandonmentThreshold;

        if (Interval <= TimeSpan.Zero ||
            Interval >= abandonmentThreshold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval));
        }
    }

    public TimeSpan Interval { get; }
}
