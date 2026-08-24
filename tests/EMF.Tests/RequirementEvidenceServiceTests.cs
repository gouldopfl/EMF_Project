using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class RequirementEvidenceServiceTests
{
    [Fact]
    public async Task GetEvidenceAsync_ReturnsRepositoryEvidence()
    {
        var requirementId =
            new RequirementId("requirement-001");

        var classification =
            new EvidenceClassification
            {
                Id =
                    new EvidenceClassificationId(
                        "classification-001"),
                ArtifactId =
                    new ArtifactId("artifact-001"),
                Classification =
                    EvidenceClassifications.MedicalEvidence
            };

        var repository =
            new RecordingRepository(classification);

        var service =
            new RequirementEvidenceService(repository);

        var result =
            await service.GetEvidenceAsync(requirementId);

        Assert.Equal(
            requirementId,
            repository.RequirementId);

        Assert.Equal(
            classification.Id,
            Assert.Single(result).Id);
    }

    [Fact]
    public async Task AssessAsync_ReportsEvidencePresent()
    {
        var requirementId =
            new RequirementId("requirement-assessment-1");

        var classification =
            new EvidenceClassification
            {
                Id =
                    new EvidenceClassificationId(
                        "classification-assessment-1"),
                ArtifactId =
                    new ArtifactId("artifact-assessment-1"),
                Classification =
                    EvidenceClassifications.MedicalEvidence
            };

        var service =
            new RequirementEvidenceService(
                new RecordingRepository(classification));

        var result =
            await service.AssessAsync(requirementId);

        Assert.Equal(requirementId, result.RequirementId);
        Assert.True(result.HasEvidence);
        Assert.Single(result.Evidence);
    }

    [Fact]
    public async Task AssessAsync_ReportsNoEvidence()
    {
        var requirementId =
            new RequirementId("requirement-assessment-2");

        var service =
            new RequirementEvidenceService(
                new RecordingRepository());

        var result =
            await service.AssessAsync(requirementId);

        Assert.Equal(requirementId, result.RequirementId);
        Assert.False(result.HasEvidence);
        Assert.Empty(result.Evidence);
    }

    private sealed class RecordingRepository :
        IEvidenceClassificationRepository
    {
        private readonly IReadOnlyList<EvidenceClassification>
            _classifications;

        public RecordingRepository(
            params EvidenceClassification[] classifications)
        {
            _classifications = classifications;
        }

        public RequirementId? RequirementId { get; private set; }

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceClassificationsAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default)
        {
            RequirementId = requirementId;

            return Task.FromResult(
                _classifications);
        }

        public Task AddEvidenceClassificationAsync(
            EvidenceClassification classification,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EvidenceClassification?>
            GetEvidenceClassificationAsync(
                EvidenceClassificationId classificationId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceClassificationsAsync(
                ArtifactId artifactId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EvidenceClassification?>
            FindEvidenceClassificationAsync(
                ArtifactId artifactId,
                ClaimIssueId? claimIssueId,
                string classification,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceClassificationRequirementAsync(
            EvidenceClassificationRequirement association,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceClassificationRequirement>>
            GetEvidenceClassificationRequirementsAsync(
                EvidenceClassificationId classificationId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceClassificationsAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
