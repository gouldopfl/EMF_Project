namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public static class ClaimIssueAdjudicationAgingPolicies
{
    public static ClaimIssueAdjudicationAgingPolicy Default =>
        new()
        {
            AttentionAfterDays = 60,
            ConsiderFollowUpAfterDays = 90
        };
}
