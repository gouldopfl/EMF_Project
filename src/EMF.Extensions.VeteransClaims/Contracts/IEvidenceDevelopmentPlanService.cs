using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidenceDevelopmentPlanService
{

    Task<EvidenceDevelopmentPlanDetails>
        CreateEvidenceDevelopmentPlanAsync(
            CreateEvidenceDevelopmentPlanRequest request,
            CancellationToken cancellationToken = default);


    Task<EvidenceDevelopmentPlanDetails?>
        GetEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default);
}
