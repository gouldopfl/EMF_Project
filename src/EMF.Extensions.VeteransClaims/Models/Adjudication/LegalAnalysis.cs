using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class LegalAnalysis
{
    public required LegalAnalysisId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public RequirementId? RequirementId { get; init; }

    public required string Analysis { get; init; }
}
