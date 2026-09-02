using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class EvidenceDevelopmentPlanExecutionService :
    IEvidenceDevelopmentPlanExecutionService
{
    private readonly IEvidenceDevelopmentPlanService _plans;
    private readonly IEvidenceDevelopmentWorkflowCoordinator _workflow;

    public EvidenceDevelopmentPlanExecutionService(
        IEvidenceDevelopmentPlanService plans,
        IEvidenceDevelopmentWorkflowCoordinator workflow)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(workflow);

        _plans = plans;
        _workflow = workflow;
    }

    public async Task<IReadOnlyList<EvidenceDevelopmentExecution>?>
        ExecuteAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default)
    {
        var details =
            await _plans.GetEvidenceDevelopmentPlanAsync(
                planId,
                cancellationToken);

        if (details is null)
            return null;

        if (details.EvidenceGaps.Any(
            gap =>
                details.GapDetails.All(
                    detail =>
                        detail.Id != gap.EvidenceGapId)))
        {
            throw new InvalidOperationException(
                "Evidence development plan references a missing evidence gap.");
        }

        var executions =
            new List<EvidenceDevelopmentExecution>();

        foreach (var gap in details.EvidenceGaps)
        {
            var detail =
                details.GapDetails.FirstOrDefault(
                    x => x.Id == gap.EvidenceGapId);

            if (detail is not null &&
                detail.Status == EvidenceGapStatuses.Resolved)
            {
                continue;
            }

            var existing =
                details.Executions.FirstOrDefault(
                    x => x.EvidenceGapId == gap.EvidenceGapId);

            if (existing is not null)
            {
                executions.Add(existing);
                continue;
            }

            executions.Add(
                await _workflow.StartAsync(
                    planId,
                    gap.EvidenceGapId,
                    cancellationToken));
        }

        return executions;
    }
}
