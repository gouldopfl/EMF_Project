using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationAgingStatusServiceTests
{
    [Fact]
    public void Assess_combines_aging_and_policy()
    {
        var issueId =
            new ClaimIssueId("issue-001");

        var timeline =
            new[]
            {
                new ClaimIssueAdjudicationEvent
                {
                    ClaimIssueId = issueId,
                    EventType =
                        ClaimIssueAdjudicationEventTypes
                            .SubmissionSubmitted,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 5, 22, 0, 0, 0,
                            TimeSpan.Zero)
                }
            };

        var service =
            new ClaimIssueAdjudicationAgingStatusService(
                new ClaimIssueAdjudicationAgingService(),
                new ClaimIssueAdjudicationAgingPolicyService());

        var policy =
            new ClaimIssueAdjudicationAgingPolicy
            {
                AttentionAfterDays = 60,
                ConsiderFollowUpAfterDays = 90
            };

        var result =
            service.Assess(
                issueId,
                timeline,
                new DateTimeOffset(
                    2026, 8, 28, 0, 0, 0,
                    TimeSpan.Zero),
                policy);

        Assert.Equal(98, result.Aging.AgeInDays);
        Assert.Equal(
            ClaimIssueAdjudicationAgingAlertLevels
                .ConsiderFollowUp,
            result.AlertLevel);
    }
}
