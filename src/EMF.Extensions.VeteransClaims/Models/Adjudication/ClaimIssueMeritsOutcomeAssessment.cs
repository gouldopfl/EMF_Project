using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueMeritsOutcomeAssessment
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required IReadOnlyList<ServiceConnectionTheoryOutcomeAssessment>
        TheoryOutcomes { get; init; }

    public required string Outcome { get; init; }
}
