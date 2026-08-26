using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationReadiness
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required IReadOnlyList<ServiceConnectionBasisRequirementDetails>
        BlockingRequirements { get; init; }

    public int OutstandingRequirementCount =>
        BlockingRequirements.Count;

    public IReadOnlyList<ClaimIssueAdjudicationBlocker>
        BlockingItems =>
            BlockingRequirements
                .SelectMany(
                    x =>
                        x.DevelopmentChecklist.Items.Select(
                            item =>
                                new ClaimIssueAdjudicationBlocker
                                {
                                    BlockerType =
                                        ClaimIssueAdjudicationBlockerTypes
                                            .MissingEvidence,
                                    RequirementId =
                                        item.RequirementId,
                                    EvidenceClassification =
                                        item.EvidenceClassification,
                                    GuidanceRole =
                                        item.GuidanceRole,
                                    Description =
                                        item.Description
                                }))
                .ToArray();

    public int OutstandingItemCount =>
        BlockingItems.Count;

    public bool IsReadyForAdjudication =>
        BlockingRequirements.Count == 0;
}
