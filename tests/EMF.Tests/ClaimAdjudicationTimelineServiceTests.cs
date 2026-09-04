using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimAdjudicationTimelineServiceTests
{
    [Fact]
    public async Task GetAsync_RejectsIssueForDifferentClaim()
    {
        var claimId =
            new ClaimId("claim-1");

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = new ClaimId("claim-other"),
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var service =
            new ClaimAdjudicationTimelineService(
                new FakeClaimIssueRepository(issue),
                new FakeTimelineService());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(claimId));

        Assert.Equal(
            "Claim timeline issue ownership mismatch.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsEventForDifferentClaimIssue()
    {
        var claimId =
            new ClaimId("claim-1");

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = claimId,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var foreignEvent =
            new ClaimIssueAdjudicationEvent
            {
                ClaimIssueId =
                    new ClaimIssueId("issue-other"),
                EventType =
                    ClaimIssueAdjudicationEventTypes.VaDecision,
                OccurredAt =
                    new DateTimeOffset(
                        2026, 1, 1, 0, 0, 0,
                        TimeSpan.Zero)
            };

        var service =
            new ClaimAdjudicationTimelineService(
                new FakeClaimIssueRepository(issue),
                new FixedTimelineService(foreignEvent));

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(claimId));

        Assert.Equal(
            "Claim timeline event ownership mismatch.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_CombinesClaimIssueTimelines()
    {
        var claimId = new ClaimId("claim-1");

        var issue1 =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = claimId,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var issue2 =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-2"),
                ClaimId = claimId,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var service =
            new ClaimAdjudicationTimelineService(
                new FakeClaimIssueRepository(issue1, issue2),
                new FakeTimelineService());

        var result =
            await service.GetAsync(claimId);

        Assert.Equal(3, result.Count);

        Assert.Equal(
            "issue-2",
            result[0].ClaimIssueId.Value);

        Assert.Equal(
            "issue-1",
            result[1].ClaimIssueId.Value);

        Assert.Equal(
            "issue-1",
            result[2].ClaimIssueId.Value);
    }

    private sealed class FixedTimelineService :
        IClaimIssueAdjudicationTimelineService
    {
        private readonly IReadOnlyList<
            ClaimIssueAdjudicationEvent> _events;

        public FixedTimelineService(
            params ClaimIssueAdjudicationEvent[] events) =>
            _events = events;

        public Task<IReadOnlyList<
            ClaimIssueAdjudicationEvent>> GetAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(_events);
    }

    private sealed class FakeTimelineService :
        IClaimIssueAdjudicationTimelineService
    {
        public Task<IReadOnlyList<ClaimIssueAdjudicationEvent>>
            GetAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default)
        {
            if (claimIssueId.Value == "issue-1")
            {
                return Task.FromResult<
                    IReadOnlyList<ClaimIssueAdjudicationEvent>>(
                [
                    CreateEvent(
                        claimIssueId,
                        new DateTimeOffset(
                            2026, 6, 1, 0, 0, 0,
                            TimeSpan.Zero)),
                    CreateEvent(
                        claimIssueId,
                        new DateTimeOffset(
                            2026, 9, 1, 0, 0, 0,
                            TimeSpan.Zero))
                ]);
            }

            return Task.FromResult<
                IReadOnlyList<ClaimIssueAdjudicationEvent>>(
            [
                CreateEvent(
                    claimIssueId,
                    new DateTimeOffset(
                        2026, 1, 1, 0, 0, 0,
                        TimeSpan.Zero))
            ]);
        }

        private static ClaimIssueAdjudicationEvent CreateEvent(
            ClaimIssueId issueId,
            DateTimeOffset occurredAt) =>
            new()
            {
                ClaimIssueId = issueId,
                EventType =
                    ClaimIssueAdjudicationEventTypes.VaDecision,
                OccurredAt = occurredAt
            };
    }

    private sealed class FakeClaimIssueRepository :
        IClaimIssueRepository
    {
        private readonly IReadOnlyList<ClaimIssue> _issues;

        public FakeClaimIssueRepository(
            params ClaimIssue[] issues) =>
            _issues = issues;

        public Task<IReadOnlyList<ClaimIssue>>
            GetClaimIssuesAsync(
                ClaimId id,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(_issues);

        public Task<ClaimIssue?> GetClaimIssueAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddClaimIssueAsync(
            ClaimIssue issue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
