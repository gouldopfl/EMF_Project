using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed partial class VeteransReviewerPackageDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_MissingPackageReturnsNull()
    {
        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService(),
                new InMemoryEvidenceRepository());

        var result =
            await service.GetAsync(
                new EvidencePackageId("missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ResolvesPackageArtifacts()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);
        Assert.Equal(packageId, result.PackageDetails.Package.Id);
        Assert.Same(artifact, Assert.Single(result.Artifacts));
    }

    [Fact]
    public async Task GetAsync_ExtractsReviewerArtifactContent()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var extractor =
            new RecordingTextExtractor("reviewable text");

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence,
                extractor);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);

        var content =
            Assert.Single(result.ArtifactContents);

        Assert.Same(artifact, content.Artifact);
        Assert.Equal("reviewable text", content.Text);
        Assert.Equal(artifact.Id, extractor.ArtifactId);
    }

    [Fact]
    public async Task GetAsync_RejectsMissingArtifacts()
    {
        var packageId = new EvidencePackageId("package-1");
        var existing = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(existing);

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        existing.Id,
                        new ArtifactId("missing"))
                },
                evidence);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(packageId));

        Assert.Contains(
            packageId.Value,
            exception.Message);

        Assert.Contains(
            "missing",
            exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_UsesTextSummaryMetadata(
        bool persistedMetadataShape)
    {
        object summary =
            persistedMetadataShape
                ? global::System.Text.Json.JsonDocument
                    .Parse("\"reviewer summary\"")
                    .RootElement
                    .Clone()
                : "reviewer summary";

        var packageId = new EvidencePackageId("package-1");

        var artifact =
            new Artifact
            {
                Id = new ArtifactId("summary-1"),
                Name = "Reviewer summary",
                ArtifactType = "text-summary",
                Metadata =
                    new Dictionary<string, object>
                    {
                        ["summary"] = summary
                    }
            };

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var extractor =
            new RecordingTextExtractor("extracted text");

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence,
                extractor);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);

        var content =
            Assert.Single(result.ArtifactContents);

        Assert.Same(artifact, content.Artifact);
        Assert.Equal("reviewer summary", content.Text);
        Assert.Null(extractor.ArtifactId);
    }

    private static Artifact CreateArtifact(string id) =>
        new()
        {
            Id = new ArtifactId(id),
            Name = id,
            ArtifactType = "test"
        };

    private static EvidencePackageDetails CreateDetails(
        EvidencePackageId packageId,
        params ArtifactId[] artifactIds) =>
        new()
        {
            Package = new EvidencePackage
            {
                Id = packageId,
                ClaimIssueId = new ClaimIssueId("issue-1"),
                Purpose = "Physician reviewer package",
                ReviewerRole = "MedicalProfessional"
            },
            Artifacts =
                artifactIds
                    .Select(
                        id => new EvidencePackageArtifact
                        {
                            EvidencePackageId = packageId,
                            ArtifactId = id,
                            ContentRole =
                                EvidencePackageContentRoles
                                    .UnderlyingEvidence
                        })
                    .ToArray()
        };
}

file sealed class RecordingTextExtractor(string? text) :
    EMF.Core.Contracts.IArtifactTextExtractor
{
    public ArtifactId? ArtifactId { get; private set; }

    public Task<string?> ExtractTextAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        ArtifactId = artifactId;
        return Task.FromResult(text);
    }
}

file sealed class RecordingPackageService :
    IEvidencePackageService
{
    public EvidencePackageDetails? Details { get; init; }

    public Task<EvidencePackageDetails?> GetAsync(
        EvidencePackageId evidencePackageId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Details);

    public Task<EvidencePackage> CreateAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<EvidencePackageArtifact> AddArtifactAsync(
        EvidencePackageId evidencePackageId,
        ArtifactId artifactId,
        string contentRole,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<EvidencePackageDetails>> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

public sealed partial class VeteransReviewerPackageDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_AssignsMedicalEvidenceAppendix()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

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
                        ArtifactId = artifact.Id,
                        ClaimIssueId =
                            new ClaimIssueId("issue-1"),
                        Classification =
                            EvidenceClassifications.MedicalEvidence
                    }
                ]
            };

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence,
                classifications,
                new RecordingTextExtractor("reviewable text"));

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);

        Assert.Equal(
            VeteransReviewerPackageAppendix.MedicalEvidence,
            Assert.Single(result.ArtifactContents).Appendix);
    }
}

file sealed class RecordingClassificationRepository :
    IEvidenceClassificationRepository
{
    public IReadOnlyList<EvidenceClassification>
        ExistingClassifications { get; init; } = [];

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
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
        Task.FromResult(
            ExistingClassifications
                .Where(x => x.ArtifactId == artifactId)
                .ToArray() as IReadOnlyList<EvidenceClassification>);

    public Task<EvidenceClassification?> FindEvidenceClassificationAsync(
        ArtifactId artifactId,
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

public sealed partial class VeteransReviewerPackageDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_RejectsConflictingAppendixes()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var classifications =
            new RecordingClassificationRepository
            {
                ExistingClassifications =
                [
                    new EvidenceClassification
                    {
                        Id =
                            new EvidenceClassificationId(
                                "classification-medical"),
                        ArtifactId = artifact.Id,
                        ClaimIssueId =
                            new ClaimIssueId("issue-1"),
                        Classification =
                            EvidenceClassifications.MedicalEvidence
                    },
                    new EvidenceClassification
                    {
                        Id =
                            new EvidenceClassificationId(
                                "classification-lay"),
                        ArtifactId = artifact.Id,
                        ClaimIssueId =
                            new ClaimIssueId("issue-1"),
                        Classification =
                            EvidenceClassifications.LayEvidence
                    }
                ]
            };

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence,
                classifications,
                new RecordingTextExtractor("reviewable text"));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(packageId));

        Assert.Contains(
            artifact.Id.Value,
            exception.Message);
    }
}
