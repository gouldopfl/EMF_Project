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

    public IReadOnlyList<RequirementEvidenceResponsivenessItem>
        MatchingItems =>
            Items
                .Where(x => x.HasMatchingEvidence)
                .ToArray();

    public IReadOnlyList<RequirementEvidenceResponsivenessItem>
        MissingItems =>
            Items
                .Where(x => !x.HasMatchingEvidence)
                .ToArray();
}
