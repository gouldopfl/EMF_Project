using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidenceClassificationRepository
{
    Task AddEvidenceClassificationAsync(
        EvidenceClassification classification,
        CancellationToken cancellationToken = default);

    Task<EvidenceClassification?> GetEvidenceClassificationAsync(
        EvidenceClassificationId classificationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceClassification>>
        GetEvidenceClassificationsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceClassification>>
        GetEvidenceClassificationsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);
}
