using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IFindingRepository
{
    Task AddFindingAsync(
        Finding finding,
        CancellationToken cancellationToken = default);

    Task<Finding?> GetFindingAsync(
        FindingId findingId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Finding>> GetFindingsAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default);

    Task AddFindingRegulatoryProvisionAsync(
        FindingRegulatoryProvision association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FindingRegulatoryProvision>>
        GetFindingRegulatoryProvisionsAsync(
            FindingId findingId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FindingRegulatoryProvision>>
        GetFindingRegulatoryProvisionsAsync(
            RegulatoryProvisionId regulatoryProvisionId,
            CancellationToken cancellationToken = default);

    Task AddFindingArtifactAsync(
        FindingArtifact association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FindingArtifact>>
        GetFindingArtifactsAsync(
            FindingId findingId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FindingArtifact>>
        GetFindingArtifactsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default);
}
