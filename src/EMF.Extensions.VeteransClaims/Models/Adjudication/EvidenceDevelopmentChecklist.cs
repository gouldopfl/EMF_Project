using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceDevelopmentChecklist
{
    public required RequirementId RequirementId { get; init; }

    public required IReadOnlyList<EvidenceDevelopmentChecklistItem>
        Items { get; init; }

    public bool HasOutstandingItems => Items.Count > 0;
}
