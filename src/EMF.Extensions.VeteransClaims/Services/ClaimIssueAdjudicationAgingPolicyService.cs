using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationAgingPolicyService
{
    public string Evaluate(
        ClaimIssueAdjudicationAging aging,
        ClaimIssueAdjudicationAgingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(aging);
        ArgumentNullException.ThrowIfNull(policy);

        if (policy.AttentionAfterDays < 0 ||
            policy.ConsiderFollowUpAfterDays <
                policy.AttentionAfterDays)
        {
            throw new InvalidOperationException(
                "Aging policy thresholds are invalid.");
        }

        if (aging.AgeInDays >=
            policy.ConsiderFollowUpAfterDays)
        {
            return ClaimIssueAdjudicationAgingAlertLevels
                .ConsiderFollowUp;
        }

        if (aging.AgeInDays >=
            policy.AttentionAfterDays)
        {
            return ClaimIssueAdjudicationAgingAlertLevels
                .Attention;
        }

        return ClaimIssueAdjudicationAgingAlertLevels.Normal;
    }
}
