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


    public async Task<EvidenceDevelopmentPlanDetails>
        CreateEvidenceDevelopmentPlanAsync(
            CreateEvidenceDevelopmentPlanRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan =
            new EvidenceDevelopmentPlan
            {
                Id = request.PlanId,
                ClaimIssueId = request.ClaimIssueId,
                Description = request.Description
            };

        var evidenceGaps =
            request.EvidenceGapIds
                .Select(
                    evidenceGapId =>
                        new EvidenceDevelopmentPlanEvidenceGap
                        {
                            EvidenceDevelopmentPlanId = plan.Id,
                            EvidenceGapId = evidenceGapId
                        })
                .ToArray();

        await _repository.CreateEvidenceDevelopmentPlanAsync(
            plan,
            evidenceGaps,
            cancellationToken);

        var details =
            await GetEvidenceDevelopmentPlanAsync(
                plan.Id,
                cancellationToken);

        return details
            ?? throw new InvalidOperationException(
                "Created evidence development plan could not be read back.");
    }


    public Task<IReadOnlyList<EvidenceDevelopmentPlan>>
        GetEvidenceDevelopmentPlansAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        return _repository.GetEvidenceDevelopmentPlansAsync(
            claimIssueId,
            cancellationToken);
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

        var executions = new List<EvidenceDevelopmentExecution>();
        var results = new List<EvidenceDevelopmentResult>();

        foreach (var evidenceGap in evidenceGaps)
        {
            var execution =
                await _repository.GetEvidenceDevelopmentExecutionAsync(
                    planId,
                    evidenceGap.EvidenceGapId,
                    cancellationToken);

            if (execution is not null)
                executions.Add(execution);

            var result =
                await _repository.GetEvidenceDevelopmentResultAsync(
                    evidenceGap.EvidenceGapId,
                    cancellationToken);

            if (result is not null)
                results.Add(result);
        }

        return new EvidenceDevelopmentPlanDetails
        {
            Plan = plan,
            Requirements = requirements,
            EvidenceGaps = evidenceGaps,
            Artifacts = artifacts,
            Executions = executions,
            Results = results
        };
    }
}
