using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public interface IEvidenceDevelopmentWorkflowCoordinator
{
    Task<EvidenceDevelopmentExecution> StartAsync(
        EvidenceDevelopmentPlanId planId,
        EvidenceGapId evidenceGapId,
        CancellationToken cancellationToken = default);
}
