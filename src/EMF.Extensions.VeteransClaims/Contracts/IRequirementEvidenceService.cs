using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IRequirementEvidenceService
{
    Task<IReadOnlyList<EvidenceClassification>>
        GetEvidenceAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default);
}
