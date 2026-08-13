using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IMedicalOpinionRepository
{
    Task AddMedicalOpinionAsync(
        MedicalOpinion medicalOpinion,
        CancellationToken cancellationToken = default);

    Task<MedicalOpinion?> GetMedicalOpinionAsync(
        MedicalOpinionId medicalOpinionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MedicalOpinion>> GetMedicalOpinionsAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default);
}
