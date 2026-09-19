using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IMedicalLiteratureRepository
{
    Task<ServiceConnectionBasisId> ResolveServiceConnectionBasisAsync(
        RequirementId requirementId,
        ServiceConnectionBasisId? serviceConnectionBasisId = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<RequirementMedicalLiterature>>
        GetRequirementMedicalLiteratureAsync(
            ServiceConnectionBasisId serviceConnectionBasisId,
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<RequirementMedicalLiterature>>
        GetActiveRequirementMedicalLiteratureAsync(
            ServiceConnectionBasisId serviceConnectionBasisId,
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>>
        GetReviewedClassificationsAsync(
            ServiceConnectionBasisId serviceConnectionBasisId,
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>>
        GetReviewedClassificationsAsync(
            ServiceConnectionBasisId serviceConnectionBasisId,
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

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

    Task<IReadOnlyList<RequirementMedicalLiterature>>
        GetActiveRequirementMedicalLiteratureAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
        GetRequirementMedicalLiteratureAsync(requirementId, cancellationToken);

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

    Task SupersedeReviewedClassificationsAsync(
        string supersededCorrelationId,
        IReadOnlyList<ReviewedMedicalLiteratureClassification>
            classifications,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task SupersedeReviewedClassificationAsync(
        string supersededCorrelationId,
        ReviewedMedicalLiteratureClassification classification,
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

    Task UpsertReviewerTextAsync(
        MedicalLiteratureReviewerText reviewerText,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<MedicalLiteratureReviewerText?> GetReviewerTextAsync(
        MedicalLiteratureSourceId sourceId,
        ArtifactId artifactId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

}
