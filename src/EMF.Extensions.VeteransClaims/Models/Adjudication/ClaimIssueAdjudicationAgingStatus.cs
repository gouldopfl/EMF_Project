namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationAgingStatus
{
    public required ClaimIssueAdjudicationAging Aging { get; init; }

    public required string AlertLevel { get; init; }

    public bool RequiresAttention =>
        AlertLevel !=
        ClaimIssueAdjudicationAgingAlertLevels.Normal;

    public bool ShouldConsiderFollowUp =>
        AlertLevel ==
        ClaimIssueAdjudicationAgingAlertLevels
            .ConsiderFollowUp;
}
