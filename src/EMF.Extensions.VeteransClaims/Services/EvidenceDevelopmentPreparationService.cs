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

        var gaps =
            await _gaps.EnsureGapsAsync(
                claimIssueId,
                cancellationToken);

        if (gaps.Count == 0)
            return null;

        var existing =
            await _plans.GetEvidenceDevelopmentPlanAsync(
                planId,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.Plan.ClaimIssueId != claimIssueId)
            {
                throw new InvalidOperationException(
                    "Evidence development plan belongs to another claim issue.");
            }

            return existing;
        }

        return await _plans.CreateEvidenceDevelopmentPlanAsync(
            new CreateEvidenceDevelopmentPlanRequest
            {
                PlanId = planId,
                ClaimIssueId = claimIssueId,
                Description = description,
                EvidenceGapIds =
                    gaps.Select(x => x.Id).ToArray()
            },
            cancellationToken);
    }
}
