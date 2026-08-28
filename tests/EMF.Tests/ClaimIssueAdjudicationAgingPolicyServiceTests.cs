using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed partial class ClaimIssueAdjudicationAgingPolicyServiceTests
{
    private readonly ClaimIssueAdjudicationAgingPolicyService
        _service = new();

    private readonly ClaimIssueAdjudicationAgingPolicy
        _policy = new()
        {
            AttentionAfterDays = 60,
            ConsiderFollowUpAfterDays = 90
        };

    [Theory]
    [InlineData(59, ClaimIssueAdjudicationAgingAlertLevels.Normal)]
    [InlineData(60, ClaimIssueAdjudicationAgingAlertLevels.Attention)]
    [InlineData(
        90,
        ClaimIssueAdjudicationAgingAlertLevels.ConsiderFollowUp)]
    public void Evaluate_returns_expected_level(
        int ageInDays,
        string expected)
    {
        var aging =
            new ClaimIssueAdjudicationAging
            {
                ClaimIssueId =
                    new ClaimIssueId("issue-001"),
                PendingSince =
                    new DateTimeOffset(
                        2026, 1, 1, 0, 0, 0,
                        TimeSpan.Zero),
                AgeInDays = ageInDays
            };

        var result =
            _service.Evaluate(aging, _policy);

        Assert.Equal(expected, result);
    }
}

public sealed partial class ClaimIssueAdjudicationAgingPolicyServiceTests
{
    [Fact]
    public void Evaluate_rejects_invalid_policy()
    {
        var aging =
            new ClaimIssueAdjudicationAging
            {
                ClaimIssueId =
                    new ClaimIssueId("issue-001"),
                PendingSince =
                    new DateTimeOffset(
                        2026, 1, 1, 0, 0, 0,
                        TimeSpan.Zero),
                AgeInDays = 30
            };

        var policy =
            new ClaimIssueAdjudicationAgingPolicy
            {
                AttentionAfterDays = 90,
                ConsiderFollowUpAfterDays = 60
            };

        Assert.Throws<InvalidOperationException>(
            () => _service.Evaluate(aging, policy));
    }
}
