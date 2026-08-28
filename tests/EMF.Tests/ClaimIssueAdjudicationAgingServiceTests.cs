using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed partial class ClaimIssueAdjudicationAgingServiceTests
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


public sealed partial class ClaimIssueAdjudicationAgingServiceTests
{
    [Fact]
    public void Assess_ages_from_new_submission_after_decision()
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
                            2026, 1, 1, 0, 0, 0,
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
                            2026, 2, 1, 0, 0, 0,
                            TimeSpan.Zero)
                },
                new ClaimIssueAdjudicationEvent
                {
                    ClaimIssueId = issueId,
                    EventType =
                        ClaimIssueAdjudicationEventTypes
                            .SubmissionSubmitted,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 8, 1, 0, 0, 0,
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

        Assert.Equal(
            timeline[2].OccurredAt,
            result.PendingSince);

        Assert.Equal(27, result.AgeInDays);
    }
}

public sealed partial class ClaimIssueAdjudicationAgingServiceTests
{
    [Fact]
    public void Assess_rejects_closed_court_cycle()
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
                            .CourtAppeal,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 6, 1, 0, 0, 0,
                            TimeSpan.Zero)
                },
                new ClaimIssueAdjudicationEvent
                {
                    ClaimIssueId = issueId,
                    EventType =
                        ClaimIssueAdjudicationEventTypes
                            .CourtDecision,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 8, 1, 0, 0, 0,
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

public sealed partial class ClaimIssueAdjudicationAgingServiceTests
{
    [Fact]
    public void Assess_ages_from_court_appeal_after_va_decision()
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
                            .VaDecision,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 5, 1, 0, 0, 0,
                            TimeSpan.Zero)
                },
                new ClaimIssueAdjudicationEvent
                {
                    ClaimIssueId = issueId,
                    EventType =
                        ClaimIssueAdjudicationEventTypes
                            .CourtAppeal,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 6, 1, 0, 0, 0,
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

        Assert.Equal(
            timeline[1].OccurredAt,
            result.PendingSince);

        Assert.Equal(88, result.AgeInDays);
    }
}

public sealed partial class ClaimIssueAdjudicationAgingServiceTests
{
    [Fact]
    public void Assess_keeps_remanded_cycle_pending()
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
                            .VaDecision,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 5, 1, 0, 0, 0,
                            TimeSpan.Zero)
                },
                new ClaimIssueAdjudicationEvent
                {
                    ClaimIssueId = issueId,
                    EventType =
                        ClaimIssueAdjudicationEventTypes
                            .CourtAppeal,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 6, 1, 0, 0, 0,
                            TimeSpan.Zero)
                },
                new ClaimIssueAdjudicationEvent
                {
                    ClaimIssueId = issueId,
                    EventType =
                        ClaimIssueAdjudicationEventTypes
                            .Remand,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 8, 1, 0, 0, 0,
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

        Assert.Equal(
            timeline[1].OccurredAt,
            result.PendingSince);

        Assert.Equal(
            timeline[2].OccurredAt,
            result.LastActivityAt);

        Assert.Equal(88, result.AgeInDays);
    }
}
