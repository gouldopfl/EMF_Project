using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IRegulatoryEvidenceGuidanceService
{
    Task<IReadOnlyList<RequirementEvidenceGuidance>>
        GetEvidenceGuidanceAsync(
            RegulatoryProvisionId provisionId,
            CancellationToken cancellationToken = default);
}
