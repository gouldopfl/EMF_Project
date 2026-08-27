using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueDecisionReview
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required ClaimIssueDecisionComparison Comparison { get; init; }

    public required bool RequiresReview { get; init; }
}
