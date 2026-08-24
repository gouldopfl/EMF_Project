using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueEvidenceChecklist
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required IReadOnlyList<EvidenceDevelopmentChecklist>
        RequirementChecklists { get; init; }

    public bool HasOutstandingItems =>
        RequirementChecklists.Any(
            x => x.HasOutstandingItems);
}
