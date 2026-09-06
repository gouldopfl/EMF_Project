using EMF.Core.Models.Identities;
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

    Task AddMedicalOpinionArtifactAsync(
        MedicalOpinionArtifact association,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<ArtifactId>> GetArtifactIdsAsync(
        MedicalOpinionId medicalOpinionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<MedicalOpinionId>> GetMedicalOpinionIdsAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
