using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidenceGapRepository
{
    Task AddEvidenceGapAsync(
        EvidenceGap evidenceGap,
        CancellationToken cancellationToken = default);

    Task<EvidenceGap?> GetEvidenceGapAsync(
        EvidenceGapId evidenceGapId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
        RequirementId requirementId,
        CancellationToken cancellationToken = default);

    Task AddEvidenceGapArtifactAsync(
        EvidenceGapArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Evidence gap artifacts are not supported by this repository.");
    }

    Task<IReadOnlyList<EvidenceGapArtifact>>
        GetEvidenceGapArtifactsAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Evidence gap artifacts are not supported by this repository.");
    }
}
