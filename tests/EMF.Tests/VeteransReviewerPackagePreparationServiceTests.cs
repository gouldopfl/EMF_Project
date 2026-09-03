using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransReviewerPackagePreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_PromotesAndPackagesSummary()
    {
        var promoted = new RecordingSummaryPromotionService();
        var packages = new RecordingPreparationService();
        var service =
            new VeteransReviewerPackagePreparationService(
                promoted,
                packages);

        var occurredUtc =
            new DateTimeOffset(
                2026, 9, 1, 21, 30, 0,
                TimeSpan.Zero);

        var result =
            new IntelligenceAgentResult<string>
            {
                Success = true,
                Output = "Reviewed summary.",
                AgentId = new AgentId("summary-agent"),
                CorrelationId =
                    new IntelligenceCorrelationId("operation-1"),
                StartedUtc = occurredUtc,
                CompletedUtc = occurredUtc.AddSeconds(1),
                SourceArtifactIds =
                [
                    new ArtifactId("source-1"),
                    new ArtifactId("source-2")
                ]
            };

        var prepared =
            await service.PrepareAsync(
                new ClaimIssueId("issue-1"),
            "Physician reviewer package",
            "MedicalProfessional",
            "OSA evidence summary",
            "reviewer-package-service",
            "physician-reviewer",
            occurredUtc.AddSeconds(2),
            new EvidenceGapId("gap-1"),
            new RequirementId("requirement-1"),
            result);

        Assert.Same(
            promoted.Artifact,
            prepared.SummaryArtifact);

        Assert.Equal(
            new EvidencePackageId("package-1"),
            prepared.Package.Id);

        Assert.Equal(
            promoted.Artifact.Id,
            Assert.Single(packages.GeneratedArtifactIds));

        Assert.Equal(
            result.SourceArtifactIds,
            packages.UnderlyingArtifactIds);
    }

    [Fact]
    public async Task PrepareAsync_PromotesWholeClaimIssueSummary()
    {
        var promoted = new RecordingSummaryPromotionService();
        var packages = new RecordingPreparationService();

        var service =
            new VeteransReviewerPackagePreparationService(
                promoted,
                packages);

        var occurredUtc =
            new DateTimeOffset(
                2026, 9, 3, 16, 30, 0,
                TimeSpan.Zero);

        var claimIssueId =
            new ClaimIssueId("issue-whole-1");

        var result =
            new IntelligenceAgentResult<string>
            {
                Success = true,
                Output = "Whole claim issue summary.",
                AgentId = new AgentId("summary-agent"),
                CorrelationId =
                    new IntelligenceCorrelationId("operation-2"),
                StartedUtc = occurredUtc,
                CompletedUtc = occurredUtc.AddSeconds(1),
                SourceArtifactIds =
                [
                    new ArtifactId("source-1"),
                    new ArtifactId("source-2")
                ]
            };

        var prepared =
            await service.PrepareAsync(
                claimIssueId,
                "Physician reviewer package",
                "MedicalProfessional",
                "Claim issue reviewer summary",
                "reviewer-package-service",
                "physician-reviewer",
                occurredUtc.AddSeconds(2),
                result);

        Assert.Equal(
            claimIssueId,
            promoted.ClaimIssueId);

        Assert.Same(
            promoted.Artifact,
            prepared.SummaryArtifact);

        Assert.Equal(
            result.SourceArtifactIds,
            packages.UnderlyingArtifactIds);

        Assert.Equal(
            promoted.Artifact.Id,
            Assert.Single(packages.GeneratedArtifactIds));
    }

    private sealed class RecordingSummaryPromotionService :
        IVeteransEvidenceSummaryPromotionService
    {
        public Artifact Artifact { get; } =
            new()
            {
                Id = new ArtifactId("summary-1"),
                Name = "Summary",
                ArtifactType = "text-summary",
                Fingerprint =
                    new ContentFingerprint
                    {
                        Algorithm = "SHA-256",
                        Value = "hash"
                    },
                CreatedUtc =
                    new DateTimeOffset(
                        2026, 9, 1, 21, 30, 0,
                        TimeSpan.Zero)
            };

        public ClaimIssueId? ClaimIssueId
        { get; private set; }

        public Task<Artifact> PromoteAsync(
            string name,
            string promotedBy,
            string reviewedBy,
            DateTimeOffset promotedUtc,
            ClaimIssueId claimIssueId,
            IntelligenceAgentResult<string> result,
            CancellationToken cancellationToken = default)
        {
            ClaimIssueId = claimIssueId;
            return Task.FromResult(Artifact);
        }

        public Task<Artifact> PromoteAsync(
            string name,
            string promotedBy,
            string reviewedBy,
            DateTimeOffset promotedUtc,
            EvidenceGapId evidenceGapId,
            RequirementId requirementId,
            IntelligenceAgentResult<string> result,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Artifact);
    }

    private sealed class RecordingPreparationService :
        IEvidencePackagePreparationService
    {
        public IReadOnlyCollection<ArtifactId>
            UnderlyingArtifactIds
        { get; private set; } = [];

        public IReadOnlyCollection<ArtifactId>
            GeneratedArtifactIds
        { get; private set; } = [];

        public Task<EvidencePackage> PrepareAsync(
            ClaimIssueId claimIssueId,
            string purpose,
            string reviewerRole,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EvidencePackage> PrepareAsync(
            ClaimIssueId claimIssueId,
            string purpose,
            string reviewerRole,
            IReadOnlyCollection<ArtifactId>
                generatedOrganizationalMaterialArtifactIds,
            CancellationToken cancellationToken = default)
        {
            GeneratedArtifactIds =
                generatedOrganizationalMaterialArtifactIds;

            return Task.FromResult(
                new EvidencePackage
                {
                    Id = new EvidencePackageId("package-1"),
                    ClaimIssueId = claimIssueId,
                    Purpose = purpose,
                    ReviewerRole = reviewerRole
                });
        }

        public Task<EvidencePackage> PrepareAsync(
            ClaimIssueId claimIssueId,
            string purpose,
            string reviewerRole,
            IReadOnlyCollection<ArtifactId>
                underlyingEvidenceArtifactIds,
            IReadOnlyCollection<ArtifactId>
                generatedOrganizationalMaterialArtifactIds,
            CancellationToken cancellationToken = default)
        {
            UnderlyingArtifactIds =
                underlyingEvidenceArtifactIds;

            GeneratedArtifactIds =
                generatedOrganizationalMaterialArtifactIds;

            return Task.FromResult(
                new EvidencePackage
                {
                    Id = new EvidencePackageId("package-1"),
                    ClaimIssueId = claimIssueId,
                    Purpose = purpose,
                    ReviewerRole = reviewerRole
                });
        }
    }
}
