using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IMedicalLiteratureService
{
    Task<MedicalLiteratureSource> AddSourceAsync(
        MedicalLiteratureSource source,
        CancellationToken cancellationToken = default);

    Task<RequirementMedicalLiteratureDetails> AddRequirementLiteratureAsync(
        RequirementId requirementId,
        MedicalLiteratureSourceId sourceId,
        string guidanceRole,
        string description,
        CancellationToken cancellationToken = default);
}
