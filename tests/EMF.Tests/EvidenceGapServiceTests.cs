using EMF.Common;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class EvidenceGapServiceTests
{
    [Fact]
    public async Task EnsureGapAsync_ReturnsNullWhenSatisfied()
    {
        var repository = new RecordingGapRepository();
        var requirements = new SatisfiedRequirementEvidenceService();

        var service = new EvidenceGapService(
            repository,
            requirements,
            new GuidIdGenerator());

        var result = await service.EnsureGapAsync(
            new ClaimIssueId("issue-1"),
            new RequirementId("requirement-1"));

        Assert.Null(result);
        Assert.Null(repository.Added);
    }

    [Fact]
    public async Task EnsureGapAsync_CreatesGapWhenEvidenceIsMissing()
    {
        var repository = new RecordingGapRepository();
        var requirements =
            new SatisfiedRequirementEvidenceService
            {
                HasMissingEvidence = true
            };

        var service = new EvidenceGapService(
            repository,
            requirements,
            new GuidIdGenerator());

        var claimIssueId = new ClaimIssueId("issue-1");
        var requirementId =
            new RequirementId("requirement-1");

        var result = await service.EnsureGapAsync(
            claimIssueId,
            requirementId);

        Assert.NotNull(result);
        Assert.Same(result, repository.Added);
        Assert.Equal(claimIssueId, result!.ClaimIssueId);
        Assert.Equal(requirementId, result.RequirementId);
        Assert.NotEmpty(result.Id.Value);
    }

    [Fact]
    public async Task EnsureGapAsync_ReturnsExistingGap()
    {
        var claimIssueId = new ClaimIssueId("issue-1");
        var requirementId =
            new RequirementId("requirement-1");

        var existing = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-existing"),
            ClaimIssueId = claimIssueId,
            RequirementId = requirementId,
            Description = "Existing missing evidence."
        };

        var repository = new RecordingGapRepository
        {
            Existing = [existing]
        };

        var requirements =
            new SatisfiedRequirementEvidenceService
            {
                HasMissingEvidence = true
            };

        var service = new EvidenceGapService(
            repository,
            requirements,
            new GuidIdGenerator());

        var result = await service.EnsureGapAsync(
            claimIssueId,
            requirementId);

        Assert.Same(existing, result);
        Assert.Null(repository.Added);
    }

    [Fact]
    public async Task EnsureGapAsync_DoesNotReuseGapFromDifferentClaimIssue()
    {
        var requirementId =
            new RequirementId("requirement-1");

        var existing = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-other"),
            ClaimIssueId = new ClaimIssueId("issue-other"),
            RequirementId = requirementId,
            Description = "Other claim issue gap."
        };

        var repository = new RecordingGapRepository
        {
            Existing = [existing]
        };

        var requirements =
            new SatisfiedRequirementEvidenceService
            {
                HasMissingEvidence = true
            };

        var service = new EvidenceGapService(
            repository,
            requirements,
            new GuidIdGenerator());

        var claimIssueId = new ClaimIssueId("issue-1");

        var result = await service.EnsureGapAsync(
            claimIssueId,
            requirementId);

        Assert.NotNull(result);
        Assert.Same(result, repository.Added);
        Assert.Equal(claimIssueId, result!.ClaimIssueId);
        Assert.NotEqual(existing.Id, result.Id);
    }

    private sealed class SatisfiedRequirementEvidenceService :
        IRequirementEvidenceService
    {
        public bool HasMissingEvidence { get; set; }

        public Task<EvidenceDevelopmentChecklist>
            CreateChecklistAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new EvidenceDevelopmentChecklist
                {
                    RequirementId = requirementId,
                    Items = HasMissingEvidence
                        ? [new EvidenceDevelopmentChecklistItem
                            {
                                RequirementId = requirementId,
                                EvidenceClassification = "MedicalOpinion",
                                GuidanceRole = "SupportsRequirement",
                                Description = "Medical opinion evidence."
                            }]
                        : []
                });

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RequirementEvidenceAssessment>
            AssessAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RequirementEvidenceResponsivenessAssessment>
            AssessResponsivenessAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingGapRepository :
        IEvidenceGapRepository
    {
        public EvidenceGap? Added { get; private set; }

        public IReadOnlyList<EvidenceGap> Existing { get; set; } = [];

        public Task AddEvidenceGapAsync(
            EvidenceGap evidenceGap,
            CancellationToken cancellationToken = default)
        {
            Added = evidenceGap;
            return Task.CompletedTask;
        }

        public Task<EvidenceGap?> GetEvidenceGapAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceGap?>(null);

        public Task<IReadOnlyList<EvidenceGap>>
            GetEvidenceGapsAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceGap>>([]);

        public Task<IReadOnlyList<EvidenceGap>>
            GetEvidenceGapsAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing);
    }
}
