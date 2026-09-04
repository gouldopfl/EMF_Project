using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueDecisionRecommendationServiceTests
{
    public static IEnumerable<object?[]> ReadyCases()
    {
        yield return
        [
            FindingOutcomes.Favorable,
            IssueDecisionOutcomes.Granted
        ];

        yield return
        [
            FindingOutcomes.PartiallyFavorable,
            IssueDecisionOutcomes.PartiallyGranted
        ];

        yield return
        [
            FindingOutcomes.Unfavorable,
            IssueDecisionOutcomes.Denied
        ];

        yield return
        [
            FindingOutcomes.Disputed,
            null
        ];

        yield return
        [
            FindingOutcomes.Unresolved,
            null
        ];
    }

    [Theory]
    [MemberData(nameof(ReadyCases))]
    public void Assess_DerivesExpectedRecommendation(
        string meritsOutcome,
        string? expected)
    {
        var assessment =
            CreateAssessment(
                isReady: true,
                meritsOutcome);

        var result =
            new ClaimIssueDecisionRecommendationService()
                .Assess(assessment);

        Assert.Equal(
            assessment.Merits!.ClaimIssueId,
            result.ClaimIssueId);

        Assert.True(result.IsReadyForAdjudication);
        Assert.Equal(meritsOutcome, result.MeritsOutcome);
        Assert.Equal(expected, result.RecommendedOutcome);
        Assert.Equal(expected is not null, result.HasRecommendation);
    }

    [Fact]
    public void Assess_DoesNotRecommendWhenNotReady()
    {
        var assessment =
            CreateAssessment(
                isReady: false,
                FindingOutcomes.Favorable);

        var result =
            new ClaimIssueDecisionRecommendationService()
                .Assess(assessment);

        Assert.False(result.IsReadyForAdjudication);
        Assert.Null(result.RecommendedOutcome);
        Assert.False(result.HasRecommendation);
    }

    [Fact]
    public void Assess_ThrowsForUnknownMeritsOutcome()
    {
        var assessment =
            CreateAssessment(
                isReady: true,
                "Unexpected");

        var ex =
            Assert.Throws<InvalidOperationException>(
                () =>
                    new ClaimIssueDecisionRecommendationService()
                        .Assess(assessment));

        Assert.Equal(
            "Unknown merits outcome 'Unexpected'.",
            ex.Message);
    }


    [Fact]
    public void Assess_RejectsReadinessForDifferentClaimIssue()
    {
        var assessment =
            CreateAssessment(
                isReady: true,
                FindingOutcomes.Favorable);

        assessment =
            new ClaimIssueAdjudicationAssessment
            {
                Details = assessment.Details,
                Readiness =
                    new ClaimIssueAdjudicationReadiness
                    {
                        ClaimIssueId =
                            new ClaimIssueId("issue-other"),
                        BlockingRequirements = []
                    },
                Merits = assessment.Merits
            };

        Assert.Throws<InvalidOperationException>(
            () => new ClaimIssueDecisionRecommendationService()
                .Assess(assessment));
    }

    [Fact]
    public void Assess_RejectsMeritsForDifferentClaimIssue()
    {
        var assessment =
            CreateAssessment(
                isReady: true,
                FindingOutcomes.Favorable);

        assessment =
            new ClaimIssueAdjudicationAssessment
            {
                Details = assessment.Details,
                Readiness = assessment.Readiness,
                Merits =
                    new ClaimIssueMeritsOutcomeAssessment
                    {
                        ClaimIssueId =
                            new ClaimIssueId("issue-other"),
                        TheoryOutcomes = [],
                        Outcome = FindingOutcomes.Favorable
                    }
            };

        Assert.Throws<InvalidOperationException>(
            () => new ClaimIssueDecisionRecommendationService()
                .Assess(assessment));
    }

    private static ClaimIssueAdjudicationAssessment
        CreateAssessment(
            bool isReady,
            string meritsOutcome)
    {
        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = new ClaimId("claim-1"),
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var readiness =
            new ClaimIssueAdjudicationReadiness
            {
                ClaimIssueId = issue.Id,
                BlockingRequirements =
                    isReady
                        ? []
                        : [CreateBlockingRequirement()]
            };

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
                    Timeline = [],
                    ServiceEvents = [],
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
                        }
                },
            Readiness = readiness,
            Merits =
                new ClaimIssueMeritsOutcomeAssessment
                {
                    ClaimIssueId = issue.Id,
                    TheoryOutcomes = [],
                    Outcome = meritsOutcome
                }
        };
    }

    private static ServiceConnectionBasisRequirementDetails
        CreateBlockingRequirement()
    {
        var requirementId =
            new RequirementId("requirement-1");

        return new ServiceConnectionBasisRequirementDetails
        {
            Basis =
                new ServiceConnectionBasis
                {
                    Id =
                        new ServiceConnectionBasisId("basis-1"),
                    ClaimIssueId =
                        new ClaimIssueId("issue-1"),
                    ServiceConnectionTheoryId =
                        new ServiceConnectionTheoryId("theory-1")
                },
            Requirement =
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId =
                        new RegulatoryProvisionId("regulation-1"),
                    Description = "Missing evidence."
                },
            RegulatoryProvision =
                new RegulatoryProvision
                {
                    Id =
                        new RegulatoryProvisionId("regulation-1"),
                    RegulatoryAuthorityId =
                        new RegulatoryAuthorityId(
                            "authority-test"),
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
}
