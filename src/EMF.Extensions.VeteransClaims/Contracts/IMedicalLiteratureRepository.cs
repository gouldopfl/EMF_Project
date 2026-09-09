using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IMedicalLiteratureRepository
{
    Task AddMedicalLiteratureSourceAsync(
        MedicalLiteratureSource source,
        CancellationToken cancellationToken = default);

    Task<MedicalLiteratureSource?>
        GetMedicalLiteratureSourceAsync(
            MedicalLiteratureSourceId sourceId,
            CancellationToken cancellationToken = default);

    Task AddRequirementMedicalLiteratureAsync(
        RequirementMedicalLiterature literature,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RequirementMedicalLiterature>>
        GetRequirementMedicalLiteratureAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default);
}
