using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationReadinessService
{
    public ClaimIssueAdjudicationReadiness Assess(
        ClaimIssueAdjudicationDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var blocking =
            details.Requirements
                .Where(
                    x =>
                        x.DevelopmentChecklist
                            .HasOutstandingItems)
                .ToArray();

        return new ClaimIssueAdjudicationReadiness
        {
            ClaimIssueId = details.ClaimIssue.Id,
            BlockingRequirements = blocking
        };
    }
}
