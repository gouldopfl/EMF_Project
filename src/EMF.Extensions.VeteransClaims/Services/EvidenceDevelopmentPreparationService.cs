using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidenceDevelopmentPreparationService :
    IEvidenceDevelopmentPreparationService
{
    private readonly IServiceConnectionEvidenceGapService _gaps;
    private readonly IEvidenceDevelopmentPlanService _plans;

    public EvidenceDevelopmentPreparationService(
        IServiceConnectionEvidenceGapService gaps,
        IEvidenceDevelopmentPlanService plans)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(plans);

        _gaps = gaps;
        _plans = plans;
    }

    public async Task<EvidenceDevelopmentPlanDetails?> PrepareAsync(
        EvidenceDevelopmentPlanId planId,
        ClaimIssueId claimIssueId,
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);


        var existing =
            await _plans.GetEvidenceDevelopmentPlanAsync(
                planId,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.Plan.Id != planId)
            {
                throw new InvalidOperationException(
                    "Evidence development plan identity mismatch.");
            }

            if (existing.Plan.ClaimIssueId != claimIssueId)
            {
                throw new InvalidOperationException(
                    "Evidence development plan belongs to another claim issue.");
            }

            return existing;
        }

        var gaps =
            await _gaps.EnsureGapsAsync(
                claimIssueId,
                cancellationToken);

        if (gaps.Any(x => x.ClaimIssueId != claimIssueId))
        {
            throw new InvalidOperationException(
                "Evidence development gap belongs to another claim issue.");
        }

        if (gaps.Count == 0)
            return null;

        var gapIds =
            gaps
                .Select(x => x.Id)
                .Distinct()
                .ToArray();

        var created =
            await _plans.CreateEvidenceDevelopmentPlanAsync(
                new CreateEvidenceDevelopmentPlanRequest
                {
                    PlanId = planId,
                    ClaimIssueId = claimIssueId,
                    Description = description,
                    EvidenceGapIds = gapIds
                },
                cancellationToken);

        if (created.Plan.Id != planId)
        {
            throw new InvalidOperationException(
                "Created evidence development plan identity mismatch.");
        }

        if (created.Plan.ClaimIssueId != claimIssueId)
        {
            throw new InvalidOperationException(
                "Created evidence development plan belongs to another claim issue.");
        }

        if (created.EvidenceGaps.Any(
                x => x.EvidenceDevelopmentPlanId != planId))
        {
            throw new InvalidOperationException(
                "Created evidence development gap association has a different plan identity.");
        }

        var returnedGapIds =
            created.EvidenceGaps
                .Select(x => x.EvidenceGapId)
                .ToArray();

        if (returnedGapIds.Length != gapIds.Length ||
            !returnedGapIds.ToHashSet().SetEquals(gapIds))
        {
            throw new InvalidOperationException(
                "Created evidence development plan returned unexpected evidence gaps.");
        }

        return created;
    }
}
