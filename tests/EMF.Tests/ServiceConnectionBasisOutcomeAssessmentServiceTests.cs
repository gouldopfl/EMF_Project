using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ServiceConnectionBasisOutcomeAssessmentServiceTests
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
            new[]
            {
                FindingOutcomes.Favorable,
                FindingOutcomes.Favorable
            },
            FindingOutcomes.Favorable
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Favorable,
                FindingOutcomes.Unfavorable
            },
            FindingOutcomes.Unfavorable
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
                FindingOutcomes.PartiallyFavorable
            },
            FindingOutcomes.PartiallyFavorable
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Disputed,
                FindingOutcomes.Unfavorable
            },
            FindingOutcomes.Unfavorable
        ];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Assess_DerivesExpectedBasisOutcome(
        string[] outcomes,
        string expected)
    {
        var basis =
            new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-1"),
                ClaimIssueId = new ClaimIssueId("issue-1"),
                ServiceConnectionTheoryId =
                    new ServiceConnectionTheoryId("theory-1")
            };

        var assessments =
            outcomes
                .Select(
                    (outcome, index) =>
                        new RequirementFindingOutcomeAssessment
                        {
                            RequirementId =
                                new RequirementId(
                                    $"requirement-{index}"),
                            Outcome = outcome,
                            Findings = []
                        })
                .ToArray();

        var result =
            new ServiceConnectionBasisOutcomeAssessmentService()
                .Assess(
                    basis,
                    assessments);

        Assert.Same(basis, result.Basis);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(
            outcomes.Length,
            result.RequirementOutcomes.Count);
    }
}
