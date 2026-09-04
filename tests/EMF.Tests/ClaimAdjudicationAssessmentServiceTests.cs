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
    public async Task GetAsync_SummarizesAttentionAndFollowUpCounts()
    {
        var claimId =
            new ClaimId("claim-aging-summary");

        var claim =
            new Claim
            {
                Id = claimId,
                VeteranId =
                    new VeteranId("veteran-aging-summary")
            };

        var normalIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-normal-summary"),
                ClaimId = claimId,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var attentionIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-attention-summary"),
                ClaimId = claimId,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var followUpIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-follow-up-summary"),
                ClaimId = claimId,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(
                    normalIssue,
                    attentionIssue,
                    followUpIssue),
                new FakeIssueAssessmentService(
                    CreateAssessment(
                        normalIssue,
                        requiresAttention: false,
                        shouldConsiderFollowUp: false),
                    CreateAssessment(
                        attentionIssue,
                        requiresAttention: true,
                        shouldConsiderFollowUp: false),
                    CreateAssessment(
                        followUpIssue,
                        requiresAttention: true,
                        shouldConsiderFollowUp: true)));

        var result =
            await service.GetAsync(claimId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.AttentionIssueCount);
        Assert.Equal(1, result.FollowUpIssueCount);
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
    public async Task GetAsync_SummarizesDecisionProgression()
    {
        var claim =
            new Claim
            {
                Id = new ClaimId("claim-progression"),
                VeteranId = new VeteranId("veteran-1")
            };

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-progression"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var recommendation =
            new ClaimIssueDecisionRecommendation
            {
                ClaimIssueId = issue.Id,
                IsReadyForAdjudication = true,
                MeritsOutcome = FindingOutcomes.Favorable,
                RecommendedOutcome = IssueDecisionOutcomes.Granted
            };

        var review =
            CreateReviewAnalysis(issue, recommendation);

        var currentDecision =
            new ClaimIssueCurrentDecision
            {
                IssueDecision =
                    new IssueDecision
                    {
                        Id =
                            new IssueDecisionId(
                                "issue-decision-progression"),
                        VaDecisionId =
                            new VaDecisionId(
                                "va-decision-progression"),
                        ClaimIssueId = issue.Id,
                        Outcome = IssueDecisionOutcomes.Granted
                    },
                VaDecision =
                    new VaDecision
                    {
                        Id =
                            new VaDecisionId(
                                "va-decision-progression"),
                        DecisionDate =
                            new DateTimeOffset(
                                2026, 8, 11,
                                0, 0, 0,
                                TimeSpan.Zero)
                    }
            };

        var assessment =
            CreateAssessment(
                issue,
                false,
                false,
                recommendation: recommendation,
                reviewHistory: [review],
                currentDecision: currentDecision);

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository([issue]),
                new FakeIssueAssessmentService(assessment));

        var result =
            await service.GetAsync(claim.Id);

        Assert.NotNull(result);
        Assert.Equal(1, result!.RecommendedIssueCount);
        Assert.Equal(1, result.CurrentDecisionCount);
        Assert.Equal(1, result.GrantedIssueCount);
        Assert.Equal(1, result.ReviewedDecisionCount);
        Assert.Equal(1, result.ReviewRequiredCount);
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

    [Fact]
    public async Task GetAsync_RejectsReturnedDifferentClaim()
    {
        var requestedId = new ClaimId("claim-requested");

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(
                    new Claim
                    {
                        Id = new ClaimId("claim-other"),
                        VeteranId = new VeteranId("veteran-1")
                    }),
                new FakeClaimIssueRepository(),
                new FakeIssueAssessmentService());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(requestedId));
    }

    [Fact]
    public async Task GetAsync_RejectsIssueForDifferentClaim()
    {
        var claimId = new ClaimId("claim-1");

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = new ClaimId("claim-other"),
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(
                    new Claim
                    {
                        Id = claimId,
                        VeteranId = new VeteranId("veteran-1")
                    }),
                new FakeClaimIssueRepository(issue),
                new FakeIssueAssessmentService());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(claimId));
    }

    [Fact]
    public async Task GetAsync_RejectsAssessmentForDifferentIssue()
    {
        var claimId = new ClaimId("claim-1");

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = claimId,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var otherIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-other"),
                ClaimId = claimId,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(
                    new Claim
                    {
                        Id = claimId,
                        VeteranId = new VeteranId("veteran-1")
                    }),
                new FakeClaimIssueRepository(issue),
                new FixedIssueAssessmentService(
                    CreateAssessment(otherIssue, false, false)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(claimId));
    }

    [Fact]
    public async Task GetAsync_RejectsAssessmentForDifferentClaim()
    {
        var claimId = new ClaimId("claim-1");

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = claimId,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var wrongParent =
            new ClaimIssue
            {
                Id = issue.Id,
                ClaimId = new ClaimId("claim-other"),
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(
                    new Claim
                    {
                        Id = claimId,
                        VeteranId = new VeteranId("veteran-1")
                    }),
                new FakeClaimIssueRepository(issue),
                new FixedIssueAssessmentService(
                    CreateAssessment(wrongParent, false, false)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(claimId));
    }

    private static ClaimIssueDecisionReviewAnalysis CreateReviewAnalysis(
        ClaimIssue issue,
        ClaimIssueDecisionRecommendation recommendation)
    {
        var decision =
            new IssueDecision
            {
                Id = new IssueDecisionId("decision-progression"),
                VaDecisionId = new VaDecisionId("va-progression"),
                ClaimIssueId = issue.Id,
                Outcome = IssueDecisionOutcomes.Denied
            };

        var comparison =
            new ClaimIssueDecisionComparison
            {
                ClaimIssueId = issue.Id,
                IssueDecision = decision,
                Recommendation = recommendation,
                ComparisonOutcome =
                    ClaimIssueDecisionComparisonOutcomes.Disagreement
            };

        return new ClaimIssueDecisionReviewAnalysis
        {
            ClaimIssueId = issue.Id,
            Review =
                new ClaimIssueDecisionReview
                {
                    ClaimIssueId = issue.Id,
                    Comparison = comparison,
                    RequiresReview = true
                },
            Merits =
                new ClaimIssueMeritsOutcomeAssessment
                {
                    ClaimIssueId = issue.Id,
                    Outcome = FindingOutcomes.Favorable,
                    TheoryOutcomes = []
                },
            ContributingTheoryOutcomes = []
        };
    }


    [Fact]
    public async Task GetAsync_CountsDeniedCurrentDecisions()
    {
        var claim =
            new Claim
            {
                Id = new ClaimId("claim-denied-summary"),
                VeteranId = new VeteranId("veteran-denied-summary")
            };

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-denied-summary"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var currentDecision =
            new ClaimIssueCurrentDecision
            {
                IssueDecision =
                    new IssueDecision
                    {
                        Id =
                            new IssueDecisionId(
                                "issue-decision-denied-summary"),
                        VaDecisionId =
                            new VaDecisionId(
                                "va-decision-denied-summary"),
                        ClaimIssueId = issue.Id,
                        Outcome = IssueDecisionOutcomes.Denied
                    },
                VaDecision =
                    new VaDecision
                    {
                        Id =
                            new VaDecisionId(
                                "va-decision-denied-summary"),
                        DecisionDate =
                            new DateTimeOffset(
                                2026, 8, 12,
                                0, 0, 0,
                                TimeSpan.Zero)
                    }
            };

        var assessment =
            CreateAssessment(
                issue,
                false,
                false,
                currentDecision: currentDecision);

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(issue),
                new FakeIssueAssessmentService(assessment));

        var result =
            await service.GetAsync(claim.Id);

        Assert.NotNull(result);
        Assert.Equal(1, result!.CurrentDecisionCount);
        Assert.Equal(1, result.DeniedIssueCount);
        Assert.Equal(0, result.GrantedIssueCount);
    }



    [Fact]
    public async Task GetAsync_CountsDeferredCurrentDecisions()
    {
        var claim =
            new Claim
            {
                Id = new ClaimId("claim-deferred-summary"),
                VeteranId = new VeteranId("veteran-deferred-summary")
            };

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-deferred-summary"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var currentDecision =
            new ClaimIssueCurrentDecision
            {
                IssueDecision =
                    new IssueDecision
                    {
                        Id =
                            new IssueDecisionId(
                                "issue-decision-deferred-summary"),
                        VaDecisionId =
                            new VaDecisionId(
                                "va-decision-deferred-summary"),
                        ClaimIssueId = issue.Id,
                        Outcome = IssueDecisionOutcomes.Deferred
                    },
                VaDecision =
                    new VaDecision
                    {
                        Id =
                            new VaDecisionId(
                                "va-decision-deferred-summary"),
                        DecisionDate =
                            new DateTimeOffset(
                                2026, 8, 13,
                                0, 0, 0,
                                TimeSpan.Zero)
                    }
            };

        var assessment =
            CreateAssessment(
                issue,
                false,
                false,
                currentDecision: currentDecision);

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(issue),
                new FakeIssueAssessmentService(assessment));

        var result =
            await service.GetAsync(claim.Id);

        Assert.NotNull(result);
        Assert.Equal(1, result!.CurrentDecisionCount);
        Assert.Equal(1, result.DeferredIssueCount);
        Assert.Equal(0, result.GrantedIssueCount);
        Assert.Equal(0, result.DeniedIssueCount);
    }



    [Fact]
    public async Task GetAsync_CountsPartiallyGrantedCurrentDecisions()
    {
        var claim =
            new Claim
            {
                Id =
                    new ClaimId(
                        "claim-partially-granted-summary"),
                VeteranId =
                    new VeteranId(
                        "veteran-partially-granted-summary")
            };

        var issue =
            new ClaimIssue
            {
                Id =
                    new ClaimIssueId(
                        "issue-partially-granted-summary"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var currentDecision =
            new ClaimIssueCurrentDecision
            {
                IssueDecision =
                    new IssueDecision
                    {
                        Id =
                            new IssueDecisionId(
                                "issue-decision-partially-granted-summary"),
                        VaDecisionId =
                            new VaDecisionId(
                                "va-decision-partially-granted-summary"),
                        ClaimIssueId = issue.Id,
                        Outcome =
                            IssueDecisionOutcomes.PartiallyGranted
                    },
                VaDecision =
                    new VaDecision
                    {
                        Id =
                            new VaDecisionId(
                                "va-decision-partially-granted-summary"),
                        DecisionDate =
                            new DateTimeOffset(
                                2026, 8, 14,
                                0, 0, 0,
                                TimeSpan.Zero)
                    }
            };

        var assessment =
            CreateAssessment(
                issue,
                false,
                false,
                currentDecision: currentDecision);

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(issue),
                new FakeIssueAssessmentService(assessment));

        var result =
            await service.GetAsync(claim.Id);

        Assert.NotNull(result);
        Assert.Equal(1, result!.CurrentDecisionCount);
        Assert.Equal(1, result.PartiallyGrantedIssueCount);
        Assert.Equal(0, result.GrantedIssueCount);
        Assert.Equal(0, result.DeniedIssueCount);
        Assert.Equal(0, result.DeferredIssueCount);
    }



    [Fact]
    public async Task GetAsync_CountsUndecidedIssues()
    {
        var claim =
            new Claim
            {
                Id = new ClaimId("claim-undecided-summary"),
                VeteranId = new VeteranId("veteran-undecided-summary")
            };

        var decidedIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-decided-summary"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var undecidedIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-undecided-summary"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var currentDecision =
            new ClaimIssueCurrentDecision
            {
                IssueDecision =
                    new IssueDecision
                    {
                        Id =
                            new IssueDecisionId(
                                "issue-decision-undecided-summary"),
                        VaDecisionId =
                            new VaDecisionId(
                                "va-decision-undecided-summary"),
                        ClaimIssueId = decidedIssue.Id,
                        Outcome = IssueDecisionOutcomes.Granted
                    },
                VaDecision =
                    new VaDecision
                    {
                        Id =
                            new VaDecisionId(
                                "va-decision-undecided-summary"),
                        DecisionDate =
                            new DateTimeOffset(
                                2026, 8, 15,
                                0, 0, 0,
                                TimeSpan.Zero)
                    }
            };

        var service =
            new ClaimAdjudicationAssessmentService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(
                    decidedIssue,
                    undecidedIssue),
                new FakeIssueAssessmentService(
                    CreateAssessment(
                        decidedIssue,
                        false,
                        false,
                        currentDecision: currentDecision),
                    CreateAssessment(
                        undecidedIssue,
                        false,
                        false)));

        var result =
            await service.GetAsync(claim.Id);

        Assert.NotNull(result);
        Assert.Equal(2, result!.IssueCount);
        Assert.Equal(1, result.CurrentDecisionCount);
        Assert.Equal(1, result.UndecidedIssueCount);
    }


    private static ClaimIssueAdjudicationAssessment CreateAssessment(
        ClaimIssue issue,
        bool requiresAttention,
        bool shouldConsiderFollowUp,
        bool isReady = true,
        ClaimIssueDecisionRecommendation? recommendation = null,
        IReadOnlyList<ClaimIssueDecisionReviewAnalysis>? reviewHistory = null,
        ClaimIssueCurrentDecision? currentDecision = null)
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
            Aging = aging,
            Recommendation = recommendation,
            CurrentDecision = currentDecision,
            DecisionReviewHistory = reviewHistory ?? []
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

    private sealed class FixedIssueAssessmentService :
        IClaimIssueAdjudicationAssessmentService
    {
        private readonly ClaimIssueAdjudicationAssessment _assessment;

        public FixedIssueAssessmentService(
            ClaimIssueAdjudicationAssessment assessment) =>
            _assessment = assessment;

        public Task<ClaimIssueAdjudicationAssessment?> GetAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ClaimIssueAdjudicationAssessment?>(
                _assessment);
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
