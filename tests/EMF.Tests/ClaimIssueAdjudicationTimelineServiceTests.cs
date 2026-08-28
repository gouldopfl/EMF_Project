using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationTimelineServiceTests
{
    [Fact]
    public void Compose_orders_va_and_judicial_events()
    {
        var issueId = new ClaimIssueId("issue-001");

        var vaEntries = new[]
        {
            Entry(issueId, "1", "InitialClaim", 1),
            Entry(issueId, "2", "HigherLevelReview", 3),
            Entry(issueId, "3", "BoardAppeal", 5)
        };

        var courtEvents = new[]
        {
            Event(issueId, "Remand", 9),
            Event(issueId, "CourtAppeal", 7)
        };

        var result =
            new ClaimIssueAdjudicationTimelineService()
                .Compose(vaEntries, courtEvents);

        Assert.Equal(5, result.Count);
        Assert.Equal("InitialClaim", result[0].Description);
        Assert.Equal("HigherLevelReview", result[1].Description);
        Assert.Equal("BoardAppeal", result[2].Description);
        Assert.Equal("CourtAppeal", result[3].EventType);
        Assert.Equal("Remand", result[4].EventType);
    }

    private static ClaimIssueAdjudicationLifecycleEntry Entry(
        ClaimIssueId issueId,
        string suffix,
        string type,
        int month) =>
        new()
        {
            ClaimIssueId = issueId,
            Submission = new()
            {
                Id = new($"submission-{suffix}"),
                ClaimId = new("claim-001"),
                SubmissionType = type
            },
            IssueDecision = new()
            {
                Id = new($"issue-decision-{suffix}"),
                VaDecisionId = new($"decision-{suffix}"),
                ClaimIssueId = issueId,
                Outcome = "Denied"
            },
            VaDecision = new()
            {
                Id = new($"decision-{suffix}"),
                DecisionDate =
                    new DateTimeOffset(
                        2026, month, 1, 0, 0, 0,
                        TimeSpan.Zero)
            }
        };

    private static ClaimIssueAdjudicationEvent Event(
        ClaimIssueId issueId,
        string type,
        int month) =>
        new()
        {
            ClaimIssueId = issueId,
            EventType = type,
            OccurredAt =
                new DateTimeOffset(
                    2026, month, 1, 0, 0, 0,
                    TimeSpan.Zero)
        };
}
