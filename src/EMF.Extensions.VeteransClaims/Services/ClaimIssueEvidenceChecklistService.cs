using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueEvidenceChecklistService :
    IClaimIssueEvidenceChecklistService
{
    private readonly IEvidenceGapRepository _gaps;
    private readonly IRequirementEvidenceService _requirements;

    public ClaimIssueEvidenceChecklistService(
        IEvidenceGapRepository gaps,
        IRequirementEvidenceService requirements)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(requirements);

        _gaps = gaps;
        _requirements = requirements;
    }

    public async Task<ClaimIssueEvidenceChecklist>
        CreateChecklistAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        var gaps =
            await _gaps.GetEvidenceGapsAsync(
                claimIssueId,
                cancellationToken);

        if (gaps.Any(
            x => x.ClaimIssueId != claimIssueId))
        {
            throw new InvalidOperationException(
                "Evidence gap claim issue mismatch.");
        }

        var results =
            new List<EvidenceDevelopmentChecklist>();

        foreach (var requirementId in
            gaps
                .Where(x => x.Status == EvidenceGapStatuses.Open)
                .Select(x => x.RequirementId)
                .Distinct())
        {
            var checklist =
                await _requirements.CreateChecklistAsync(
                    requirementId,
                    cancellationToken);

            if (checklist.RequirementId != requirementId)
            {
                throw new InvalidOperationException(
                    "Evidence checklist requirement mismatch.");
            }

            if (checklist.Items.Any(
                x => x.RequirementId != requirementId))
            {
                throw new InvalidOperationException(
                    "Evidence checklist item requirement mismatch.");
            }

            if (checklist.HasOutstandingItems)
                results.Add(checklist);
        }

        return new ClaimIssueEvidenceChecklist
        {
            ClaimIssueId = claimIssueId,
            RequirementChecklists = results
        };
    }
}
