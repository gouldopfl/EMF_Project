using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Agents;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackagePreparationService
{
    private readonly IVeteransEvidenceSummaryPromotionService
        _summaryPromotion;
    private readonly IEvidencePackagePreparationService
        _packagePreparation;

    public VeteransReviewerPackagePreparationService(
        IVeteransEvidenceSummaryPromotionService summaryPromotion,
        IEvidencePackagePreparationService packagePreparation)
    {
        ArgumentNullException.ThrowIfNull(summaryPromotion);
        ArgumentNullException.ThrowIfNull(packagePreparation);

        _summaryPromotion = summaryPromotion;
        _packagePreparation = packagePreparation;
    }

    public async Task<VeteransReviewerPackagePreparationResult> PrepareAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        string summaryName,
        string promotedBy,
        string reviewedBy,
        DateTimeOffset promotedUtc,
        IntelligenceAgentResult<string> summaryResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summaryResult);

        var summaryArtifact =
            await _summaryPromotion.PromoteAsync(
                summaryName,
                promotedBy,
                reviewedBy,
                promotedUtc,
                claimIssueId,
                summaryResult,
                cancellationToken);

        var package =
            await _packagePreparation.PrepareAsync(
                claimIssueId,
                purpose,
                reviewerRole,
                summaryResult.SourceArtifactIds,
                [summaryArtifact.Id],
                cancellationToken);

        return new VeteransReviewerPackagePreparationResult
        {
            SummaryArtifact = summaryArtifact,
            Package = package
        };
    }

    public async Task<VeteransReviewerPackagePreparationResult> PrepareAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        string summaryName,
        string promotedBy,
        string reviewedBy,
        DateTimeOffset promotedUtc,
        EvidenceGapId evidenceGapId,
        RequirementId requirementId,
        IntelligenceAgentResult<string> summaryResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summaryResult);

        var summaryArtifact =
            await _summaryPromotion.PromoteAsync(
                summaryName,
                promotedBy,
                reviewedBy,
                promotedUtc,
                evidenceGapId,
                requirementId,
                summaryResult,
                cancellationToken);

        var package =
            await _packagePreparation.PrepareAsync(
                claimIssueId,
                purpose,
                reviewerRole,
                summaryResult.SourceArtifactIds,
                [summaryArtifact.Id],
                cancellationToken);

        return new VeteransReviewerPackagePreparationResult
        {
            SummaryArtifact = summaryArtifact,
            Package = package
        };
    }
}
