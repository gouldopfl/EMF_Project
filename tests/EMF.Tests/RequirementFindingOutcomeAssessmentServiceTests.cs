using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class RequirementFindingOutcomeAssessmentServiceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return
        [
            Array.Empty<string>(),
            FindingOutcomes.Unresolved
        ];

        yield return
        [
            new[] { FindingOutcomes.Favorable },
            FindingOutcomes.Favorable
        ];

        yield return
        [
            new[] { FindingOutcomes.Unfavorable },
            FindingOutcomes.Unfavorable
        ];

        yield return
        [
            new[] { FindingOutcomes.PartiallyFavorable },
            FindingOutcomes.PartiallyFavorable
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Favorable,
                FindingOutcomes.Unfavorable
            },
            FindingOutcomes.Disputed
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Favorable,
                FindingOutcomes.PartiallyFavorable
            },
            FindingOutcomes.PartiallyFavorable
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Unfavorable,
                FindingOutcomes.PartiallyFavorable
            },
            FindingOutcomes.Disputed
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Favorable,
                FindingOutcomes.Unresolved
            },
            FindingOutcomes.Unresolved
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Favorable,
                FindingOutcomes.Disputed
            },
            FindingOutcomes.Disputed
        ];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Assess_DerivesExpectedOutcome(
        string[] outcomes,
        string expected)
    {
        var requirementId =
            new RequirementId("requirement-1");

        var assessment =
            new RequirementFindingAssessment
            {
                RequirementId = requirementId,
                Findings =
                    outcomes
                        .Select(
                            (outcome, index) =>
                                new Finding
                                {
                                    Id =
                                        new FindingId(
                                            $"finding-{index}"),
                                    ClaimIssueId =
                                        new ClaimIssueId(
                                            "issue-1"),
                                    RequirementId =
                                        requirementId,
                                    Outcome = outcome,
                                    Description = outcome
                                })
                        .ToArray()
            };

        var result =
            new RequirementFindingOutcomeAssessmentService()
                .Assess(assessment);

        Assert.Equal(requirementId, result.RequirementId);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(outcomes.Length, result.Findings.Count);
    }

    [Fact]
    public void Assess_RejectsUnknownOutcome()
    {
        var assessment =
            new RequirementFindingAssessment
            {
                RequirementId =
                    new RequirementId("requirement-unknown"),
                Findings =
                [
                    new Finding
                    {
                        Id = new FindingId("finding-unknown"),
                        ClaimIssueId =
                            new ClaimIssueId("issue-unknown"),
                        RequirementId =
                            new RequirementId(
                                "requirement-unknown"),
                        Outcome = "SomethingElse",
                        Description = "Unknown outcome."
                    }
                ]
            };

        var service =
            new RequirementFindingOutcomeAssessmentService();

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => service.Assess(assessment));

        Assert.Contains(
            "Unknown finding outcome",
            exception.Message);
    }
}
