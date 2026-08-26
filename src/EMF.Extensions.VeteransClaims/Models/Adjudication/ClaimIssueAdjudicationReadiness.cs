using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationReadiness
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required IReadOnlyList<ServiceConnectionBasisRequirementDetails>
        BlockingRequirements { get; init; }

    public int OutstandingRequirementCount =>
        BlockingRequirements.Count;

    public IReadOnlyList<EvidenceDevelopmentChecklistItem>
        BlockingItems =>
            BlockingRequirements
                .SelectMany(
                    x => x.DevelopmentChecklist.Items)
                .ToArray();

    public int OutstandingItemCount =>
        BlockingItems.Count;

    public bool IsReadyForAdjudication =>
        BlockingRequirements.Count == 0;
}
