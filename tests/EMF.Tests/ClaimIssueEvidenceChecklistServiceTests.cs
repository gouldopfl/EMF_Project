using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueEvidenceChecklistServiceTests
{
    [Fact]
    public async Task CreateChecklistAsync_DeduplicatesRequirements()
    {
        var issueId = new ClaimIssueId("issue-1");
        var requirementId = new RequirementId("requirement-1");

        var gaps =
            new FakeGapRepository(
                new EvidenceGap
                {
                    Id = new EvidenceGapId("gap-1"),
                    ClaimIssueId = issueId,
                    RequirementId = requirementId,
                    Description = "First gap."
                },
                new EvidenceGap
                {
                    Id = new EvidenceGapId("gap-2"),
                    ClaimIssueId = issueId,
                    RequirementId = requirementId,
                    Description = "Second gap."
                });

        var requirements =
            new FakeRequirementEvidenceService();

        var service =
            new ClaimIssueEvidenceChecklistService(
                gaps,
                requirements);

        await service.CreateChecklistAsync(issueId);

        Assert.Equal(
            requirementId,
            Assert.Single(requirements.Requested));
    }

    [Fact]
    public async Task CreateChecklistAsync_OmitsCompletedRequirements()
    {
        var issueId = new ClaimIssueId("issue-2");
        var requirementId = new RequirementId("requirement-2");

        var gaps =
            new FakeGapRepository(
                new EvidenceGap
                {
                    Id = new EvidenceGapId("gap-3"),
                    ClaimIssueId = issueId,
                    RequirementId = requirementId,
                    Description = "Gap."
                });

        var requirements =
            new FakeRequirementEvidenceService();

        var service =
            new ClaimIssueEvidenceChecklistService(
                gaps,
                requirements);

        var result =
            await service.CreateChecklistAsync(issueId);

        Assert.Empty(result.RequirementChecklists);
        Assert.False(result.HasOutstandingItems);
    }

    private sealed class FakeGapRepository :
        IEvidenceGapRepository
    {
        private readonly IReadOnlyList<EvidenceGap> _gaps;

        public FakeGapRepository(params EvidenceGap[] gaps)
        {
            _gaps = gaps;
        }

        public Task<IReadOnlyList<EvidenceGap>>
            GetEvidenceGapsAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(_gaps);

        public Task AddEvidenceGapAsync(
            EvidenceGap evidenceGap,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EvidenceGap?> GetEvidenceGapAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceGap>>
            GetEvidenceGapsAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRequirementEvidenceService :
        IRequirementEvidenceService
    {
        public List<RequirementId> Requested { get; } = [];

        public Task<EvidenceDevelopmentChecklist>
            CreateChecklistAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default)
        {
            Requested.Add(requirementId);

            return Task.FromResult(
                new EvidenceDevelopmentChecklist
                {
                    RequirementId = requirementId,
                    Items = []
                });
        }

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceAsync(
                RequirementId id,
                CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<RequirementEvidenceAssessment>
            AssessAsync(
                RequirementId id,
                CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<RequirementEvidenceResponsivenessAssessment>
            AssessResponsivenessAsync(
                RequirementId id,
                CancellationToken c = default) =>
            throw new NotSupportedException();
    }
}
