using EMF.Extensions.VeteransClaims.Services;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed partial class EvidencePackagePreparationServiceTests
{
    [Fact]
    public void Service_CanBeConstructed()
    {
        _ = typeof(EvidencePackagePreparationService);
    }
}

public sealed partial class EvidencePackagePreparationServiceTests
{
    [Fact]
    public void Constructor_RequiresDependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new EvidencePackagePreparationService(
                    null!,
                    null!));
    }
}

public sealed partial class EvidencePackagePreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_CreatesPackage()
    {
        var classifications =
            new RecordingClassificationRepository();

        var packages =
            new RecordingPackageService();

        var service =
            new EvidencePackagePreparationService(
                classifications,
                packages);

        var claimIssueId =
            new ClaimIssueId("issue-1");

        var result =
            await service.PrepareAsync(
                claimIssueId,
                "Medical review",
                "MedicalProfessional");

        Assert.Equal(
            claimIssueId,
            packages.CreatedClaimIssueId);

        Assert.Equal(
            "Medical review",
            packages.CreatedPurpose);

        Assert.Equal(
            "MedicalProfessional",
            packages.CreatedReviewerRole);

        Assert.Equal(
            claimIssueId,
            result.ClaimIssueId);
    }
}

public sealed partial class EvidencePackagePreparationServiceTests
{
    private sealed class RecordingPackageService :
        EMF.Extensions.VeteransClaims.Contracts.IEvidencePackageService
    {
        public ClaimIssueId? CreatedClaimIssueId { get; private set; }
        public string? CreatedPurpose { get; private set; }
        public string? CreatedReviewerRole { get; private set; }

        public List<EvidencePackageArtifact>
            AddedArtifacts { get; } = [];

        public Task<EvidencePackage> CreateAsync(
            ClaimIssueId claimIssueId,
            string purpose,
            string reviewerRole,
            CancellationToken cancellationToken = default)
        {
            CreatedClaimIssueId = claimIssueId;
            CreatedPurpose = purpose;
            CreatedReviewerRole = reviewerRole;

            return Task.FromResult(
                new EvidencePackage
                {
                    Id = new EvidencePackageId("package-1"),
                    ClaimIssueId = claimIssueId,
                    Purpose = purpose,
                    ReviewerRole = reviewerRole
                });
        }

        public Task<EvidencePackageArtifact> AddArtifactAsync(
            EvidencePackageId evidencePackageId,
            EMF.Core.Models.Identities.ArtifactId artifactId,
            string contentRole,
            CancellationToken cancellationToken = default)
        {
            var artifact =
                new EvidencePackageArtifact
                {
                    EvidencePackageId = evidencePackageId,
                    ArtifactId = artifactId,
                    ContentRole = contentRole
                };

            AddedArtifacts.Add(artifact);

            return Task.FromResult(artifact);
        }

        public Task<EvidencePackageDetails?> GetAsync(
            EvidencePackageId evidencePackageId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidencePackageDetails>> GetAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

public sealed partial class EvidencePackagePreparationServiceTests
{
    private sealed class RecordingClassificationRepository :
        EMF.Extensions.VeteransClaims.Contracts.IEvidenceClassificationRepository
    {
        public IReadOnlyList<EvidenceClassification>
            ExistingClassifications { get; set; } = [];

        public Task AddEvidenceClassificationAsync(
            EvidenceClassification classification,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<EvidenceClassification?> GetEvidenceClassificationAsync(
            EvidenceClassificationId classificationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceClassification?>(null);

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceClassificationsAsync(
                EMF.Core.Models.Identities.ArtifactId artifactId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceClassification>>([]);

        public Task<EvidenceClassification?> FindEvidenceClassificationAsync(
            EMF.Core.Models.Identities.ArtifactId artifactId,
            ClaimIssueId? claimIssueId,
            string classification,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceClassification?>(null);

        public Task AddEvidenceClassificationRequirementAsync(
            EvidenceClassificationRequirement association,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<EvidenceClassificationRequirement>>
            GetEvidenceClassificationRequirementsAsync(
                EvidenceClassificationId classificationId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceClassificationRequirement>>([]);

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceClassificationsAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceClassification>>([]);

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceClassificationsAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingClassifications);
    }
}

public sealed partial class EvidencePackagePreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_AddsClassifiedEvidence()
    {
        var classifications =
            new RecordingClassificationRepository
            {
                ExistingClassifications =
                [
                    new EvidenceClassification
                    {
                        Id =
                            new EvidenceClassificationId(
                                "classification-1"),
                        ArtifactId =
                            new EMF.Core.Models.Identities.ArtifactId(
                                "artifact-1"),
                        ClaimIssueId =
                            new ClaimIssueId("issue-1"),
                        Classification =
                            EvidenceClassifications.MedicalEvidence
                    }
                ]
            };

        var packages =
            new RecordingPackageService();

        var service =
            new EvidencePackagePreparationService(
                classifications,
                packages);

        await service.PrepareAsync(
            new ClaimIssueId("issue-1"),
            "Medical review",
            "MedicalProfessional");

        var artifact =
            Assert.Single(
                packages.AddedArtifacts);

        Assert.Equal(
            "artifact-1",
            artifact.ArtifactId.Value);

        Assert.Equal(
            EvidencePackageContentRoles.UnderlyingEvidence,
            artifact.ContentRole);
    }
}

public sealed partial class EvidencePackagePreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_DeduplicatesArtifacts()
    {
        var artifactId =
            new EMF.Core.Models.Identities.ArtifactId(
                "artifact-1");

        var classifications =
            new RecordingClassificationRepository
            {
                ExistingClassifications =
                [
                    new EvidenceClassification
                    {
                        Id =
                            new EvidenceClassificationId(
                                "classification-1"),
                        ArtifactId = artifactId,
                        ClaimIssueId =
                            new ClaimIssueId("issue-1"),
                        Classification =
                            EvidenceClassifications.MedicalEvidence
                    },
                    new EvidenceClassification
                    {
                        Id =
                            new EvidenceClassificationId(
                                "classification-2"),
                        ArtifactId = artifactId,
                        ClaimIssueId =
                            new ClaimIssueId("issue-1"),
                        Classification =
                            EvidenceClassifications.MedicalOpinion
                    }
                ]
            };

        var packages =
            new RecordingPackageService();

        var service =
            new EvidencePackagePreparationService(
                classifications,
                packages);

        await service.PrepareAsync(
            new ClaimIssueId("issue-1"),
            "Medical review",
            "MedicalProfessional");

        var artifact =
            Assert.Single(
                packages.AddedArtifacts);

        Assert.Equal(
            artifactId,
            artifact.ArtifactId);
    }
}

public sealed partial class EvidencePackagePreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_AddsGeneratedOrganizationalMaterial()
    {
        var packages =
            new RecordingPackageService();

        var service =
            new EvidencePackagePreparationService(
                new RecordingClassificationRepository(),
                packages);

        var generatedArtifactId =
            new EMF.Core.Models.Identities.ArtifactId(
                "generated-summary-1");

        await service.PrepareAsync(
            new ClaimIssueId("issue-1"),
            "Physician reviewer package",
            "MedicalProfessional",
            [generatedArtifactId]);

        var artifact =
            Assert.Single(
                packages.AddedArtifacts);

        Assert.Equal(
            generatedArtifactId,
            artifact.ArtifactId);

        Assert.Equal(
            EvidencePackageContentRoles
                .GeneratedOrganizationalMaterial,
            artifact.ContentRole);
    }
}
