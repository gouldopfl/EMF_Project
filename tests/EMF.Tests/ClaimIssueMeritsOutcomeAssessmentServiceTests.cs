using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueMeritsOutcomeAssessmentServiceTests
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
            new[]
            {
                FindingOutcomes.Unfavorable,
                FindingOutcomes.Favorable
            },
            FindingOutcomes.Favorable
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Unfavorable,
                FindingOutcomes.PartiallyFavorable
            },
            FindingOutcomes.PartiallyFavorable
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Unfavorable,
                FindingOutcomes.Disputed
            },
            FindingOutcomes.Disputed
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Unfavorable,
                FindingOutcomes.Unresolved
            },
            FindingOutcomes.Unresolved
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Unfavorable,
                FindingOutcomes.Unfavorable
            },
            FindingOutcomes.Unfavorable
        ];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Assess_DerivesExpectedClaimIssueOutcome(
        string[] outcomes,
        string expected)
    {
        var claimIssueId =
            new ClaimIssueId("issue-1");

        var theories =
            outcomes
                .Select(
                    (outcome, index) =>
                        new ServiceConnectionTheoryOutcomeAssessment
                        {
                            Theory =
                                new ServiceConnectionTheory
                                {
                                    Id =
                                        new ServiceConnectionTheoryId(
                                            $"theory-{index}"),
                                    ClaimIssueId = claimIssueId,
                                    TheoryType =
                                        index == 0
                                            ? ServiceConnectionTheoryTypes.Direct
                                            : ServiceConnectionTheoryTypes.Secondary
                                },
                            BasisOutcomes = [],
                            Outcome = outcome
                        })
                .ToArray();

        var result =
            new ClaimIssueMeritsOutcomeAssessmentService()
                .Assess(
                    claimIssueId,
                    theories);

        Assert.Equal(claimIssueId, result.ClaimIssueId);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(
            outcomes.Length,
            result.TheoryOutcomes.Count);
    }
}
