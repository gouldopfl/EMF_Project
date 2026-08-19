using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidenceDevelopmentPlanRepository
{
    Task AddEvidenceDevelopmentPlanAsync(
        EvidenceDevelopmentPlan plan,
        CancellationToken cancellationToken = default);

    Task<EvidenceDevelopmentPlan?> GetEvidenceDevelopmentPlanAsync(
        EvidenceDevelopmentPlanId planId,
        CancellationToken cancellationToken = default);




    Task AddEvidenceDevelopmentPlanArtifactAsync(
        EvidenceDevelopmentPlanArtifact artifact,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceDevelopmentPlanArtifact>>
        GetEvidenceDevelopmentPlanArtifactsAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default);


    Task AddEvidenceDevelopmentPlanEvidenceGapAsync(
        EvidenceDevelopmentPlanEvidenceGap evidenceGap,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
        GetEvidenceDevelopmentPlanEvidenceGapsAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default);


    Task AddEvidenceDevelopmentPlanRequirementAsync(
        EvidenceDevelopmentPlanRequirement requirement,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceDevelopmentPlanRequirement>>
        GetEvidenceDevelopmentPlanRequirementsAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default);


    Task<IReadOnlyList<EvidenceDevelopmentPlan>>
        GetEvidenceDevelopmentPlansAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);
}
