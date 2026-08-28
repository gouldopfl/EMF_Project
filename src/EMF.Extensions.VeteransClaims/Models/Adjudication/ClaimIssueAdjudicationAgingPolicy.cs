namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationAgingPolicy
{
    public required int AttentionAfterDays { get; init; }

    public required int ConsiderFollowUpAfterDays { get; init; }
}
