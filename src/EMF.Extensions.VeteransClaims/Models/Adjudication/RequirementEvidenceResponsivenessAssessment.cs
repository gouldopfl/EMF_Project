using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class RequirementEvidenceResponsivenessAssessment
{
    public required RequirementId RequirementId { get; init; }

    public required IReadOnlyList<RequirementEvidenceResponsivenessItem>
        Items { get; init; }

    public int MatchingItemCount =>
        Items.Count(x => x.HasMatchingEvidence);

    public int MissingItemCount =>
        Items.Count(x => !x.HasMatchingEvidence);
}
