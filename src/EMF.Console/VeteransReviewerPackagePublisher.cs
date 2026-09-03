using EMF.Common;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Services;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;

namespace EMF.ConsoleApplication;

internal static class VeteransReviewerPackagePublisher
{
    public static async
        Task<VeteransReviewerPackagePreparationResult>
        PublishAsync(
            string databasePath,
            ClaimIssueId claimIssueId,
            string purpose,
            string reviewerRole,
            string name,
            string promotedBy,
            string reviewedBy,
            DateTimeOffset promotedUtc,
            IntelligenceAgentResult<string> result)
    {
        await new VeteransClaimsSqliteSchema(
                databasePath)
            .InitializeAsync();

        var evidenceRepository =
            new SqliteEvidenceRepository(databasePath);

        await evidenceRepository.InitializeAsync();

        var summaryPromotion =
            VeteransEvidenceOrchestrationFactory
                .CreateEvidenceSummaryPromotionService(
                    new IntelligenceEvidencePromotionService(
                        evidenceRepository));

        var packagePreparation =
            new EvidencePackagePreparationService(
                new SqliteEvidenceClassificationRepository(
                    databasePath),
                new EvidencePackageService(
                    new SqliteEvidencePackageRepository(
                        databasePath),
                    new GuidIdGenerator()));

        return await VeteransEvidenceOrchestrationFactory
            .CreateReviewerPackagePreparationService(
                summaryPromotion,
                packagePreparation)
            .PrepareAsync(
                claimIssueId,
                purpose,
                reviewerRole,
                name,
                promotedBy,
                reviewedBy,
                promotedUtc,
                result);
    }

    public static async
        Task<VeteransReviewerPackagePreparationResult>
        PublishAsync(
            string databasePath,
            ClaimIssueId claimIssueId,
            string purpose,
            string reviewerRole,
            string name,
            string promotedBy,
            string reviewedBy,
            DateTimeOffset promotedUtc,
            EvidenceGapId evidenceGapId,
            RequirementId requirementId,
            IntelligenceAgentResult<string> result)
    {
        await new VeteransClaimsSqliteSchema(
                databasePath)
            .InitializeAsync();

        var evidenceRepository =
            new SqliteEvidenceRepository(databasePath);

        await evidenceRepository.InitializeAsync();

        var summaryPromotion =
            VeteransEvidenceOrchestrationFactory
                .CreateEvidenceSummaryPromotionService(
                    new IntelligenceEvidencePromotionService(
                        evidenceRepository));

        var packagePreparation =
            new EvidencePackagePreparationService(
                new SqliteEvidenceClassificationRepository(
                    databasePath),
                new EvidencePackageService(
                    new SqliteEvidencePackageRepository(
                        databasePath),
                    new GuidIdGenerator()));

        return await VeteransEvidenceOrchestrationFactory
            .CreateReviewerPackagePreparationService(
                summaryPromotion,
                packagePreparation)
            .PrepareAsync(
                claimIssueId,
                purpose,
                reviewerRole,
                name,
                promotedBy,
                reviewedBy,
                promotedUtc,
                evidenceGapId,
                requirementId,
                result);
    }
}
