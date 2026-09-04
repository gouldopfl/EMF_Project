using EMF.Common;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class EvidenceClassificationServiceTests
{
    [Fact]
    public async Task ClassifyAsync_PersistsClassification()
    {
        var repository = new RecordingRepository();
        var service = new EvidenceClassificationService(
            repository,
            new GuidIdGenerator());

        var result = await service.ClassifyAsync(
            new ArtifactId("artifact-1"),
            EvidenceClassifications.MedicalEvidence);

        Assert.Equal(
            EvidenceClassifications.MedicalEvidence,
            result.Classification);
        Assert.Equal(
            result,
            repository.Classification);
        Assert.NotEmpty(result.Id.Value);
    }

    [Fact]
    public async Task ClassifyAsync_PreservesClaimIssue()
    {
        var repository = new RecordingRepository();
        var service = new EvidenceClassificationService(
            repository,
            new GuidIdGenerator());

        var claimIssueId = new ClaimIssueId("issue-1");

        var result = await service.ClassifyAsync(
            new ArtifactId("artifact-1"),
            EvidenceClassifications.MedicalOpinion,
            claimIssueId);

        Assert.Equal(claimIssueId, result.ClaimIssueId);
    }


    [Fact]
    public async Task ClassifyAsync_ReturnsExistingClassification()
    {
        var repository = new RecordingRepository();

        var existing =
            new EvidenceClassification
            {
                Id =
                    new EvidenceClassificationId(
                        "classification-existing"),
                ArtifactId =
                    new ArtifactId("artifact-1"),
                Classification =
                    EvidenceClassifications.MedicalEvidence
            };

        repository.Existing = existing;

        var service =
            new EvidenceClassificationService(
                repository,
                new GuidIdGenerator());

        var result =
            await service.ClassifyAsync(
                existing.ArtifactId,
                existing.Classification);

        Assert.Same(existing, result);
        Assert.Null(repository.Classification);
    }

    [Fact]
    public async Task ClassifyAsync_RejectsExistingForDifferentArtifact()
    {
        var repository = new RecordingRepository
        {
            Existing = new EvidenceClassification
            {
                Id = new EvidenceClassificationId("classification-existing"),
                ArtifactId = new ArtifactId("artifact-other"),
                Classification = EvidenceClassifications.MedicalEvidence
            }
        };

        var service =
            new EvidenceClassificationService(
                repository,
                new GuidIdGenerator());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ClassifyAsync(
                new ArtifactId("artifact-1"),
                EvidenceClassifications.MedicalEvidence));
    }

    [Fact]
    public async Task ClassifyAsync_RejectsExistingForDifferentClaimIssue()
    {
        var repository = new RecordingRepository
        {
            Existing = new EvidenceClassification
            {
                Id = new EvidenceClassificationId("classification-existing"),
                ArtifactId = new ArtifactId("artifact-1"),
                ClaimIssueId = new ClaimIssueId("issue-other"),
                Classification = EvidenceClassifications.MedicalEvidence
            }
        };

        var service =
            new EvidenceClassificationService(
                repository,
                new GuidIdGenerator());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ClassifyAsync(
                new ArtifactId("artifact-1"),
                EvidenceClassifications.MedicalEvidence,
                new ClaimIssueId("issue-1")));
    }

    [Fact]
    public async Task ClassifyAsync_RejectsExistingForDifferentClassification()
    {
        var repository = new RecordingRepository
        {
            Existing = new EvidenceClassification
            {
                Id = new EvidenceClassificationId("classification-existing"),
                ArtifactId = new ArtifactId("artifact-1"),
                Classification = EvidenceClassifications.MedicalOpinion
            }
        };

        var service =
            new EvidenceClassificationService(
                repository,
                new GuidIdGenerator());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ClassifyAsync(
                new ArtifactId("artifact-1"),
                EvidenceClassifications.MedicalEvidence));
    }

    [Fact]
    public async Task ClassifyAsync_RejectsUnsupportedClassification()
    {
        var service = new EvidenceClassificationService(
            new RecordingRepository(),
            new GuidIdGenerator());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ClassifyAsync(
                new ArtifactId("artifact-1"),
                "NotAClassification"));
    }

    private sealed class RecordingRepository :
        IEvidenceClassificationRepository
    {
        public EvidenceClassification? Classification { get; private set; }

        public EvidenceClassification? Existing { get; set; }

        public Task AddEvidenceClassificationAsync(
            EvidenceClassification classification,
            CancellationToken cancellationToken = default)
        {
            Classification = classification;
            return Task.CompletedTask;
        }

        public Task AddEvidenceClassificationRequirementAsync(
            EvidenceClassificationRequirement association,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<EvidenceClassificationRequirement>>
            GetEvidenceClassificationRequirementsAsync(
                EvidenceClassificationId classificationId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceClassificationRequirement>>(
                Array.Empty<EvidenceClassificationRequirement>());

        public Task<EvidenceClassification?>
            FindEvidenceClassificationAsync(
                ArtifactId artifactId,
                ClaimIssueId? claimIssueId,
                string classification,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing);

        public Task<EvidenceClassification?>
            GetEvidenceClassificationAsync(
                EvidenceClassificationId classificationId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceClassification?>(null);

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceClassificationsAsync(
                ArtifactId artifactId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceClassification>>(
                Array.Empty<EvidenceClassification>());

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceClassificationsAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceClassification>>(
                Array.Empty<EvidenceClassification>());


        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceClassificationsAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceClassification>>(
                Array.Empty<EvidenceClassification>());
    }
}
