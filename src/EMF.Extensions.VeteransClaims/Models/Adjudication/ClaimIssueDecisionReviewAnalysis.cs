using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueDecisionReviewAnalysis
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required ClaimIssueDecisionReview Review { get; init; }

    public required ClaimIssueMeritsOutcomeAssessment Merits { get; init; }

    public required IReadOnlyList<ServiceConnectionTheoryOutcomeAssessment>
        ContributingTheoryOutcomes { get; init; }
}
