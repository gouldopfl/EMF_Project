using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidenceRequirementGuidanceRepository
{
    Task AddEvidenceRequirementGuidanceAsync(
        EvidenceRequirementGuidance guidance,
        CancellationToken cancellationToken = default);

    Task<EvidenceRequirementGuidance?>
        GetEvidenceRequirementGuidanceAsync(
            EvidenceRequirementGuidanceId guidanceId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceRequirementGuidance>>
        GetEvidenceRequirementGuidanceAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default);
}
