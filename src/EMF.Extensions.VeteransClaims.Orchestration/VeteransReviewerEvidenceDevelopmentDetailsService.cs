using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerEvidenceDevelopmentDetailsService
{
    private readonly IEvidenceDevelopmentPlanRepository _development;
    private readonly IEvidenceGapRepository _gaps;

    public VeteransReviewerEvidenceDevelopmentDetailsService(
        IEvidenceDevelopmentPlanRepository development,
        IEvidenceGapRepository gaps)
    {
        ArgumentNullException.ThrowIfNull(development);
        ArgumentNullException.ThrowIfNull(gaps);

        _development = development;
        _gaps = gaps;
    }

    public async Task<
        IReadOnlyList<VeteransReviewerEvidenceDevelopmentDetails>>
        GetAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        var plans =
            await _development.GetEvidenceDevelopmentPlansAsync(
                claimIssueId,
                cancellationToken);

        if (plans.Any(x => x.ClaimIssueId != claimIssueId))
            throw new InvalidOperationException(
                "Evidence development plan claim issue mismatch.");

        var seenGaps = new HashSet<EvidenceGapId>();
        var details =
            new List<VeteransReviewerEvidenceDevelopmentDetails>();

        foreach (var plan in plans.OrderBy(x => x.Id.Value))
        {
            var links =
                await _development
                    .GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                        plan.Id,
                        cancellationToken);

            if (links.Any(
                    x => x.EvidenceDevelopmentPlanId != plan.Id))
            {
                throw new InvalidOperationException(
                    "Evidence development plan gap lineage mismatch.");
            }

            foreach (var link in
                     links.OrderBy(x => x.EvidenceGapId.Value))
            {
                if (!seenGaps.Add(link.EvidenceGapId))
                    continue;

                var gap =
                    await _gaps.GetEvidenceGapAsync(
                        link.EvidenceGapId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Evidence development gap was not found.");

                if (gap.Id != link.EvidenceGapId)
                    throw new InvalidOperationException(
                        "Evidence development gap identity mismatch.");

                if (gap.ClaimIssueId != claimIssueId)
                    throw new InvalidOperationException(
                        "Evidence development gap claim issue mismatch.");

                var result =
                    await _development
                        .GetEvidenceDevelopmentResultAsync(
                            gap.Id,
                            cancellationToken);

                if (result is null)
                    continue;

                if (result.EvidenceGapId != gap.Id)
                    throw new InvalidOperationException(
                        "Evidence development result gap mismatch.");

                if (result.RequirementId != gap.RequirementId)
                    throw new InvalidOperationException(
                        "Evidence development result requirement mismatch.");

                details.Add(
                    new VeteransReviewerEvidenceDevelopmentDetails
                    {
                        Gap = gap,
                        Result = result
                    });
            }
        }

        return details;
    }
}
