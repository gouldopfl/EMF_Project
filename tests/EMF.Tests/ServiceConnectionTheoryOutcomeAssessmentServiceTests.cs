using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ServiceConnectionTheoryOutcomeAssessmentServiceTests
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
                FindingOutcomes.Unfavorable
            },
            FindingOutcomes.Favorable
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.PartiallyFavorable,
                FindingOutcomes.Unfavorable
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
            FindingOutcomes.Disputed
        ];

        yield return
        [
            new[]
            {
                FindingOutcomes.Unresolved,
                FindingOutcomes.Unfavorable
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

        yield return
        [
            new[]
            {
                FindingOutcomes.Favorable,
                FindingOutcomes.Disputed
            },
            FindingOutcomes.Favorable
        ];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Assess_DerivesExpectedTheoryOutcome(
        string[] outcomes,
        string expected)
    {
        var theory =
            new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-1"),
                ClaimIssueId = new ClaimIssueId("issue-1"),
                TheoryType =
                    ServiceConnectionTheoryTypes.Secondary
            };

        var assessments =
            outcomes
                .Select(
                    (outcome, index) =>
                        new ServiceConnectionBasisOutcomeAssessment
                        {
                            Basis =
                                new ServiceConnectionBasis
                                {
                                    Id =
                                        new ServiceConnectionBasisId(
                                            $"basis-{index}"),
                                    ClaimIssueId =
                                        theory.ClaimIssueId,
                                    ServiceConnectionTheoryId =
                                        theory.Id
                                },
                            RequirementOutcomes = [],
                            Outcome = outcome
                        })
                .ToArray();

        var result =
            new ServiceConnectionTheoryOutcomeAssessmentService()
                .Assess(
                    theory,
                    assessments);

        Assert.Same(theory, result.Theory);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(
            outcomes.Length,
            result.BasisOutcomes.Count);
    }

    [Fact]
    public void Assess_RejectsBasisForDifferentClaimIssue()
    {
        var theory = CreateTheory();

        var outcome =
            CreateBasisOutcome(
                new ClaimIssueId("issue-other"),
                theory.Id);

        Assert.Throws<InvalidOperationException>(
            () => new ServiceConnectionTheoryOutcomeAssessmentService()
                .Assess(theory, [outcome]));
    }

    [Fact]
    public void Assess_RejectsBasisForDifferentTheory()
    {
        var theory = CreateTheory();

        var outcome =
            CreateBasisOutcome(
                theory.ClaimIssueId,
                new ServiceConnectionTheoryId("theory-other"));

        Assert.Throws<InvalidOperationException>(
            () => new ServiceConnectionTheoryOutcomeAssessmentService()
                .Assess(theory, [outcome]));
    }

    private static ServiceConnectionTheory CreateTheory() =>
        new()
        {
            Id = new ServiceConnectionTheoryId("theory-test"),
            ClaimIssueId = new ClaimIssueId("issue-1"),
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

    private static ServiceConnectionBasisOutcomeAssessment
        CreateBasisOutcome(
            ClaimIssueId claimIssueId,
            ServiceConnectionTheoryId theoryId) =>
        new()
        {
            Basis =
                new ServiceConnectionBasis
                {
                    Id = new ServiceConnectionBasisId("basis-test"),
                    ClaimIssueId = claimIssueId,
                    ServiceConnectionTheoryId = theoryId
                },
            RequirementOutcomes = [],
            Outcome = FindingOutcomes.Favorable
        };

}
