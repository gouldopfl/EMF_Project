using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimAdjudicationAssessmentServiceTests
{
    [Fact]
    public async Task GetAsync_ComposesIssueAssessments()
    {
        var claimId = new ClaimId("claim-001");

        var claim =
            new Claim
            {
                Id = claimId,
                VeteranId = new VeteranId("veteran-001")
            };

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-001"),
                ClaimId = claimId,
                ClaimIssueType = "ServiceConnection"
            };

        var assessment =
            CreateAssessment(
                issue,
                requiresAttention: true,
                shouldConsiderFollowUp: true);

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(issue),
                new FakeIssueAssessmentService(assessment));

        var result = await service.GetAsync(claimId);

        Assert.NotNull(result);
        Assert.Equal(claimId, result!.Claim.Id);
        Assert.Single(result.Issues);
        Assert.True(result.RequiresAttention);
        Assert.True(result.ShouldConsiderFollowUp);
    }

    [Fact]
    public async Task GetAsync_AggregatesAttentionAcrossIssues()
    {
        var claimId = new ClaimId("claim-002");

        var claim =
            new Claim
            {
                Id = claimId,
                VeteranId = new VeteranId("veteran-002")
            };

        var normalIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-normal"),
                ClaimId = claimId,
                ClaimIssueType = "ServiceConnection"
            };

        var agingIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-aging"),
                ClaimId = claimId,
                ClaimIssueType = "ServiceConnection"
            };

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(
                    normalIssue,
                    agingIssue),
                new FakeIssueAssessmentService(
                    CreateAssessment(
                        normalIssue,
                        requiresAttention: false,
                        shouldConsiderFollowUp: false),
                    CreateAssessment(
                        agingIssue,
                        requiresAttention: true,
                        shouldConsiderFollowUp: true)));

        var result = await service.GetAsync(claimId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Issues.Count);
        Assert.True(result.RequiresAttention);
        Assert.True(result.ShouldConsiderFollowUp);
    }

    [Fact]
    public async Task GetAsync_SummarizesIssueReadiness()
    {
        var claimId = new ClaimId("claim-readiness-summary");

        var claim =
            new Claim
            {
                Id = claimId,
                VeteranId = new VeteranId("veteran-summary")
            };

        var readyIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-ready"),
                ClaimId = claimId,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var blockedIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-blocked"),
                ClaimId = claimId,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var readyAssessment =
            CreateAssessment(
                readyIssue,
                requiresAttention: false,
                shouldConsiderFollowUp: false);

        var blockedAssessment =
            CreateAssessment(
                blockedIssue,
                requiresAttention: false,
                shouldConsiderFollowUp: false,
                isReady: false);

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(
                    readyIssue,
                    blockedIssue),
                new FakeIssueAssessmentService(
                    readyAssessment,
                    blockedAssessment));

        var result = await service.GetAsync(claimId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.IssueCount);
        Assert.Equal(1, result.ReadyIssueCount);
        Assert.Equal(1, result.BlockedIssueCount);
    }


    [Fact]
    public async Task GetAsync_DoesNotEscalateAttentionToFollowUp()
    {
        var claimId = new ClaimId("claim-003");

        var claim =
            new Claim
            {
                Id = claimId,
                VeteranId = new VeteranId("veteran-003")
            };

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-attention"),
                ClaimId = claimId,
                ClaimIssueType = "ServiceConnection"
            };

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(issue),
                new FakeIssueAssessmentService(
                    CreateAssessment(
                        issue,
                        requiresAttention: true,
                        shouldConsiderFollowUp: false)));

        var result = await service.GetAsync(claimId);

        Assert.NotNull(result);
        Assert.True(result!.RequiresAttention);
        Assert.False(result.ShouldConsiderFollowUp);
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenIssueAssessmentIsMissing()
    {
        var claimId = new ClaimId("claim-missing-assessment");

        var claim =
            new Claim
            {
                Id = claimId,
                VeteranId = new VeteranId("veteran-001")
            };

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-missing"),
                ClaimId = claimId,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(issue),
                new FakeIssueAssessmentService());

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(claimId));

        Assert.Equal(
            "Claim issue adjudication assessment could not be read.",
            exception.Message);
    }


    [Fact]
    public async Task GetAsync_ReturnsNullWhenClaimDoesNotExist()
    {
        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(),
                new FakeClaimIssueRepository(),
                new FakeIssueAssessmentService());

        var result =
            await service.GetAsync(
                new ClaimId("missing"));

        Assert.Null(result);
    }

    private static ClaimIssueAdjudicationAssessment CreateAssessment(
        ClaimIssue issue,
        bool requiresAttention,
        bool shouldConsiderFollowUp,
        bool isReady = true)
    {
        ClaimIssueAdjudicationAgingStatus? aging = null;

        if (requiresAttention)
        {
            aging =
                new ClaimIssueAdjudicationAgingStatus
                {
                    Aging =
                        new ClaimIssueAdjudicationAging
                        {
                            ClaimIssueId = issue.Id,
                            PendingSince = DateTimeOffset.UnixEpoch,
                            AgeInDays = 100,
                            LastActivityAt = null,
                            DaysSinceLastActivity = 100
                        },
                    AlertLevel =
                        shouldConsiderFollowUp
                            ? ClaimIssueAdjudicationAgingAlertLevels
                                .ConsiderFollowUp
                            : ClaimIssueAdjudicationAgingAlertLevels
                                .Attention
                };
        }

        return new ClaimIssueAdjudicationAssessment
        {
            Details =
                new ClaimIssueAdjudicationDetails
                {
                    ClaimIssue = issue,
                    ClaimedConditions = [],
                    ServiceConnectionTheories = [],
                    ServiceConnectionBases = [],
                    ServiceConnectedConditions = [],
                    Requirements = [],
                    Evidence =
                        new ClaimIssueEvidenceDetails
                        {
                            ClaimIssue = issue,
                            Checklist =
                                new ClaimIssueEvidenceChecklist
                                {
                                    ClaimIssueId = issue.Id,
                                    RequirementChecklists = []
                                },
                            DevelopmentPlans = []
                        },
                    Timeline = [],
                    ServiceEvents = [],
                },
            Readiness =
                new ClaimIssueAdjudicationReadiness
                {
                    ClaimIssueId = issue.Id,
                    BlockingRequirements =
                        isReady
                            ? []
                            : [CreateBlockingRequirement()]
                },
            Aging = aging
        };
    }

    private static ServiceConnectionBasisRequirementDetails
        CreateBlockingRequirement()
    {
        var requirementId =
            new RequirementId("requirement-blocking");

        return new ServiceConnectionBasisRequirementDetails
        {
            Basis =
                new ServiceConnectionBasis
                {
                    Id =
                        new ServiceConnectionBasisId("basis-blocking"),
                    ClaimIssueId =
                        new ClaimIssueId("issue-blocked"),
                    ServiceConnectionTheoryId =
                        new ServiceConnectionTheoryId("theory-blocking")
                },
            Requirement =
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId =
                        new RegulatoryProvisionId("regulation-blocking"),
                    Description = "Missing evidence."
                },
            RegulatoryProvision =
                new RegulatoryProvision
                {
                    Id =
                        new RegulatoryProvisionId("regulation-blocking"),
                    RegulatoryAuthorityId =
                        new RegulatoryAuthorityId("authority-test"),
                    ProvisionType = "Test",
                    Citation = "38 CFR"
                },
            Responsiveness =
                new RequirementEvidenceResponsivenessAssessment
                {
                    RequirementId = requirementId,
                    Items = []
                },
            DevelopmentChecklist =
                new EvidenceDevelopmentChecklist
                {
                    RequirementId = requirementId,
                    Items =
                    [
                        new EvidenceDevelopmentChecklistItem
                        {
                            RequirementId = requirementId,
                            EvidenceClassification = "Medical",
                            GuidanceRole = "Required",
                            Description = "Missing evidence."
                        }
                    ]
                }
        };
    }

    private sealed class FakeClaimRepository : IClaimRepository
    {
        private readonly Claim? _claim;

        public FakeClaimRepository(Claim? claim = null) =>
            _claim = claim;

        public Task<Claim?> GetClaimAsync(
            ClaimId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_claim);

        public Task<IReadOnlyList<Claim>> GetClaimsAsync(
            VeteranId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddClaimAsync(
            Claim claim,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeClaimIssueRepository :
        IClaimIssueRepository
    {
        private readonly IReadOnlyList<ClaimIssue> _issues;

        public FakeClaimIssueRepository(
            params ClaimIssue[] issues) =>
            _issues = issues;

        public Task<IReadOnlyList<ClaimIssue>>
            GetClaimIssuesAsync(
                ClaimId id,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(_issues);

        public Task<ClaimIssue?> GetClaimIssueAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddClaimIssueAsync(
            ClaimIssue issue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeIssueAssessmentService :
        IClaimIssueAdjudicationAssessmentService
    {
        private readonly IReadOnlyList<ClaimIssueAdjudicationAssessment>
            _assessments;

        public FakeIssueAssessmentService(
            params ClaimIssueAdjudicationAssessment[] assessments) =>
            _assessments = assessments;

        public Task<ClaimIssueAdjudicationAssessment?> GetAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _assessments.FirstOrDefault(
                    x => x.Details.ClaimIssue.Id == id));
    }
}
