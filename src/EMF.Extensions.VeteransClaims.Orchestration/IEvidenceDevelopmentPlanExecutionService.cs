using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public interface IEvidenceDevelopmentPlanExecutionService
{
    Task<IReadOnlyList<EvidenceDevelopmentExecution>?>
        ExecuteAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default);
}
