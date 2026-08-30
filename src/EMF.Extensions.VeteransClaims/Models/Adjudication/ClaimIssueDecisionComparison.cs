using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueDecisionComparison
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required IssueDecision IssueDecision { get; init; }

    public VaDecision? VaDecision { get; init; }

    public required ClaimIssueDecisionRecommendation
        Recommendation { get; init; }

    public required string ComparisonOutcome { get; init; }
}
