using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueDecisionRecommendation
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required bool IsReadyForAdjudication { get; init; }

    public required string MeritsOutcome { get; init; }

    public string? RecommendedOutcome { get; init; }

    public bool HasRecommendation =>
        RecommendedOutcome is not null;
}
