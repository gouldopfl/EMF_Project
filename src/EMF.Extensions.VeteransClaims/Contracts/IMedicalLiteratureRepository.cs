using EMF.Core.Models.Identities;
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

    Task AddMedicalLiteratureSourceArtifactAsync(
        MedicalLiteratureSourceArtifact association,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<ArtifactId>> GetArtifactIdsAsync(
        MedicalLiteratureSourceId sourceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<MedicalLiteratureSourceId>>
        GetMedicalLiteratureSourceIdsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task AddReviewedClassificationAsync(
        ReviewedMedicalLiteratureClassification classification,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task AddReviewedClassificationsAsync(
        IReadOnlyList<ReviewedMedicalLiteratureClassification>
            classifications,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>>
        GetReviewedClassificationsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>>
        GetReviewedClassificationsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

}
