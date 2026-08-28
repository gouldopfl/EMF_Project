using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationAgingServiceTests
{
    [Fact]
    public void Assess_calculates_age_from_first_event()
    {
        var issueId = new ClaimIssueId("issue-001");

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

        var result =
            new ClaimIssueAdjudicationAgingService()
                .Assess(
                    issueId,
                    timeline,
                    new DateTimeOffset(
                        2026, 8, 28, 0, 0, 0,
                        TimeSpan.Zero));

        Assert.Equal(98, result.AgeInDays);
        Assert.Equal(timeline[0].OccurredAt, result.PendingSince);
        Assert.Equal(timeline[0].OccurredAt, result.LastActivityAt);
    }
    [Fact]
    public void Assess_rejects_closed_adjudication_cycle()
    {
        var issueId = new ClaimIssueId("issue-001");

        var timeline =
            new[]
            {
                new ClaimIssueAdjudicationEvent
                {
                    ClaimIssueId = issueId,
                    EventType =
                        ClaimIssueAdjudicationEventTypes
                            .SubmissionReceived,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 5, 22, 0, 0, 0,
                            TimeSpan.Zero)
                },
                new ClaimIssueAdjudicationEvent
                {
                    ClaimIssueId = issueId,
                    EventType =
                        ClaimIssueAdjudicationEventTypes
                            .VaDecision,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 6, 1, 0, 0, 0,
                            TimeSpan.Zero)
                }
            };

        Assert.Throws<InvalidOperationException>(
            () =>
                new ClaimIssueAdjudicationAgingService()
                    .Assess(
                        issueId,
                        timeline,
                        new DateTimeOffset(
                            2026, 8, 28, 0, 0, 0,
                            TimeSpan.Zero)));
    }


}

