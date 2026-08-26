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
    public async Task CreateChecklistAsync_IncludesOnlyRequirementsWithMissingEvidence()
    {
        var issueId = new ClaimIssueId("issue-3");
        var matchingRequirementId =
            new RequirementId("requirement-matching");
        var missingRequirementId =
            new RequirementId("requirement-missing");

        var gaps =
            new FakeGapRepository(
                new EvidenceGap
                {
                    Id = new EvidenceGapId("gap-4"),
                    ClaimIssueId = issueId,
                    RequirementId = matchingRequirementId,
                    Description = "Matching evidence."
                },
                new EvidenceGap
                {
                    Id = new EvidenceGapId("gap-5"),
                    ClaimIssueId = issueId,
                    RequirementId = missingRequirementId,
                    Description = "Missing evidence."
                });

        var requirements =
            new SelectiveRequirementEvidenceService(
                matchingRequirementId);

        var service =
            new ClaimIssueEvidenceChecklistService(
                gaps,
                requirements);

        var result =
            await service.CreateChecklistAsync(issueId);

        var checklist =
            Assert.Single(result.RequirementChecklists);

        Assert.Equal(
            missingRequirementId,
            checklist.RequirementId);
        Assert.True(result.HasOutstandingItems);
        Assert.Equal(2, requirements.Requested.Count);
        Assert.Equal(
            new[] { matchingRequirementId, missingRequirementId },
            requirements.Requested);
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

    [Fact]
    public async Task CreateChecklistAsync_IgnoresResolvedGaps()
    {
        var issueId = new ClaimIssueId("issue-resolved");
        var requirementId =
            new RequirementId("requirement-resolved");

        var gaps =
            new FakeGapRepository(
                new EvidenceGap
                {
                    Id = new EvidenceGapId("gap-resolved"),
                    ClaimIssueId = issueId,
                    RequirementId = requirementId,
                    Description = "Resolved gap.",
                    Status = EvidenceGapStatuses.Resolved
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
        Assert.Empty(requirements.Requested);
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

    private sealed class SelectiveRequirementEvidenceService :
        IRequirementEvidenceService
    {
        private readonly RequirementId _matchingRequirementId;

        public SelectiveRequirementEvidenceService(
            RequirementId matchingRequirementId)
        {
            _matchingRequirementId = matchingRequirementId;
        }

        public List<RequirementId> Requested { get; } = [];

        public Task<EvidenceDevelopmentChecklist>
            CreateChecklistAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default)
        {
            Requested.Add(requirementId);

            var items =
                requirementId.Value == _matchingRequirementId.Value
                    ? Array.Empty<EvidenceDevelopmentChecklistItem>()
                    : new[]
                    {
                        new EvidenceDevelopmentChecklistItem
                        {
                            RequirementId = requirementId,
                            EvidenceClassification =
                                EvidenceClassifications.MedicalOpinion,
                            GuidanceRole =
                                EvidenceGuidanceRoles.SupportsRequirement,
                            Description = "Missing medical opinion."
                        }
                    };

            return Task.FromResult(
                new EvidenceDevelopmentChecklist
                {
                    RequirementId = requirementId,
                    Items = items
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
