using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidenceDevelopmentPlanService :
    IEvidenceDevelopmentPlanService
{
    private readonly IEvidenceDevelopmentPlanRepository _repository;

    public EvidenceDevelopmentPlanService(
        IEvidenceDevelopmentPlanRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<EvidenceDevelopmentPlanDetails?>
        GetEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default)
    {
        var plan =
            await _repository.GetEvidenceDevelopmentPlanAsync(
                planId,
                cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var requirements =
            await _repository.GetEvidenceDevelopmentPlanRequirementsAsync(
                planId,
                cancellationToken);

        var evidenceGaps =
            await _repository.GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                planId,
                cancellationToken);

        var artifacts =
            await _repository.GetEvidenceDevelopmentPlanArtifactsAsync(
                planId,
                cancellationToken);

        return new EvidenceDevelopmentPlanDetails
        {
            Plan = plan,
            Requirements = requirements,
            EvidenceGaps = evidenceGaps,
            Artifacts = artifacts
        };
    }
}
