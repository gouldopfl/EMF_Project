using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationBlocker
{
    public required string BlockerType { get; init; }

    public required RequirementId RequirementId { get; init; }

    public required string EvidenceClassification { get; init; }

    public required string GuidanceRole { get; init; }

    public required string Description { get; init; }
}
