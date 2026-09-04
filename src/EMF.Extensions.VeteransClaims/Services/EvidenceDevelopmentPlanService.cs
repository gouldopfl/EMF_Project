using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidenceDevelopmentPlanService :
    IEvidenceDevelopmentPlanService
{
    private readonly IEvidenceDevelopmentPlanRepository _repository;
    private readonly IEvidenceGapRepository? _gaps;
    private readonly EvidenceDevelopmentPlanStatusService _status;

    public EvidenceDevelopmentPlanService(
        IEvidenceDevelopmentPlanRepository repository)
        : this(repository, null)
    {
    }

    public EvidenceDevelopmentPlanService(
        IEvidenceDevelopmentPlanRepository repository,
        IEvidenceGapRepository? gaps)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
        _gaps = gaps;
        _status = new EvidenceDevelopmentPlanStatusService();
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


    public async Task<IReadOnlyList<EvidenceDevelopmentPlan>>
        GetEvidenceDevelopmentPlansAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        var plans =
            await _repository.GetEvidenceDevelopmentPlansAsync(
                claimIssueId,
                cancellationToken);

        if (plans.Any(x => x.ClaimIssueId != claimIssueId))
            throw new InvalidOperationException(
                $"Claim issue '{claimIssueId.Value}' plan lookup " +
                "returned a plan for a different claim issue.");

        return plans;
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

        if (plan.Id != planId)
        {
            throw new InvalidOperationException(
                $"Evidence development plan lookup for '{planId.Value}' " +
                $"returned plan '{plan.Id.Value}'.");
        }

        var requirements =
            await _repository.GetEvidenceDevelopmentPlanRequirementsAsync(
                planId,
                cancellationToken);

        var badRequirement =
            requirements.FirstOrDefault(
                x => x.EvidenceDevelopmentPlanId != planId);

        if (badRequirement is not null)
        {
            throw new InvalidOperationException(
                $"Plan '{planId.Value}' returned requirement " +
                $"'{badRequirement.RequirementId.Value}' for plan " +
                $"'{badRequirement.EvidenceDevelopmentPlanId.Value}'.");
        }

        var evidenceGaps =
            await _repository.GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                planId,
                cancellationToken);

        var mismatchedEvidenceGap =
            evidenceGaps.FirstOrDefault(
                x =>
                    x.EvidenceDevelopmentPlanId != planId);

        if (mismatchedEvidenceGap is not null)
        {
            throw new InvalidOperationException(
                $"Evidence development plan '{planId.Value}' lookup returned " +
                $"evidence gap '{mismatchedEvidenceGap.EvidenceGapId.Value}' " +
                $"associated with plan " +
                $"'{mismatchedEvidenceGap.EvidenceDevelopmentPlanId.Value}'.");
        }

        var artifacts =
            await _repository.GetEvidenceDevelopmentPlanArtifactsAsync(
                planId,
                cancellationToken);

        var badArtifact =
            artifacts.FirstOrDefault(
                x => x.EvidenceDevelopmentPlanId != planId);

        if (badArtifact is not null)
        {
            throw new InvalidOperationException(
                $"Plan '{planId.Value}' returned artifact " +
                $"'{badArtifact.ArtifactId.Value}' for plan " +
                $"'{badArtifact.EvidenceDevelopmentPlanId.Value}'.");
        }

        var gapDetails = new List<EvidenceGap>();
        var executions = new List<EvidenceDevelopmentExecution>();
        var results = new List<EvidenceDevelopmentResult>();

        foreach (var evidenceGap in evidenceGaps)
        {
            if (_gaps is not null)
            {
                var gap =
                    await _gaps.GetEvidenceGapAsync(
                        evidenceGap.EvidenceGapId,
                        cancellationToken);

                if (gap is not null)
                {
                    if (gap.Id != evidenceGap.EvidenceGapId)
                    {
                        throw new InvalidOperationException(
                            $"Gap '{evidenceGap.EvidenceGapId.Value}' lookup " +
                            $"returned gap '{gap.Id.Value}'.");
                    }

                    if (gap.ClaimIssueId != plan.ClaimIssueId)
                    {
                        throw new InvalidOperationException(
                            $"Gap '{gap.Id.Value}' belongs to claim issue " +
                            $"'{gap.ClaimIssueId.Value}', not " +
                            $"'{plan.ClaimIssueId.Value}'.");
                    }

                    gapDetails.Add(gap);
                }
            }

            var execution =
                await _repository.GetEvidenceDevelopmentExecutionAsync(
                    planId,
                    evidenceGap.EvidenceGapId,
                    cancellationToken);

            if (execution is not null)
            {
                if (execution.EvidenceDevelopmentPlanId != planId)
                {
                    throw new InvalidOperationException(
                        $"Plan '{planId.Value}' returned execution for plan " +
                        $"'{execution.EvidenceDevelopmentPlanId.Value}'.");
                }

                if (execution.EvidenceGapId != evidenceGap.EvidenceGapId)
                {
                    throw new InvalidOperationException(
                        $"Gap '{evidenceGap.EvidenceGapId.Value}' returned " +
                        $"execution for gap '{execution.EvidenceGapId.Value}'.");
                }

                executions.Add(execution);
            }

            var result =
                await _repository.GetEvidenceDevelopmentResultAsync(
                    evidenceGap.EvidenceGapId,
                    cancellationToken);

            if (result is not null)
            {
                if (result.EvidenceGapId != evidenceGap.EvidenceGapId)
                {
                    throw new InvalidOperationException(
                        $"Gap '{evidenceGap.EvidenceGapId.Value}' returned " +
                        $"result for gap '{result.EvidenceGapId.Value}'.");
                }

                results.Add(result);
            }
        }

        var details = new EvidenceDevelopmentPlanDetails
        {
            Plan = plan,
            Requirements = requirements,
            EvidenceGaps = evidenceGaps,
            GapDetails = gapDetails,
            Artifacts = artifacts,
            Executions = executions,
            Results = results
        };

        return new EvidenceDevelopmentPlanDetails
        {
            Plan = details.Plan,
            Requirements = details.Requirements,
            EvidenceGaps = details.EvidenceGaps,
            GapDetails = details.GapDetails,
            Artifacts = details.Artifacts,
            Executions = details.Executions,
            Results = details.Results,
            Status = _status.Assess(details)
        };
    }
}
