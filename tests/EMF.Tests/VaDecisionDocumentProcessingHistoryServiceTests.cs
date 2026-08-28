using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VaDecisionDocumentProcessingHistoryServiceTests
{
    [Fact]
    public async Task GetAsync_summarizes_processing_attempts()
    {
        var repository =
            new RecordingRepository(
                new VaDecisionDocumentProcessingAttempt
                {
                    ClaimId = new ClaimId("claim-001"),
                    ArtifactId = new ArtifactId("artifact-001"),
                    ProcessedAt =
                        new DateTimeOffset(
                            2026, 8, 28, 10, 0, 0,
                            TimeSpan.Zero),
                    VaDecisionId = null,
                    Matches =
                    [
                        Match(
                            VaDecisionDocumentIssueMatchStatuses.Matched,
                            "issue-001"),
                        Match(
                            VaDecisionDocumentIssueMatchStatuses.Unmatched),
                        Match(
                            VaDecisionDocumentIssueMatchStatuses.Ambiguous)
                    ]
                });

        var service =
            new VaDecisionDocumentProcessingHistoryService(
                repository);

        var history =
            await service.GetAsync(
                new ClaimId("claim-001"));

        var entry = Assert.Single(history);

        Assert.Equal(
            new ArtifactId("artifact-001"),
            entry.ArtifactId);

        Assert.Equal(1, entry.MatchedIssueCount);
        Assert.Equal(1, entry.UnmatchedIssueCount);
        Assert.Equal(1, entry.AmbiguousIssueCount);
        Assert.False(entry.Persisted);
        Assert.True(entry.HasUnresolvedIssues);
    }


    [Fact]
    public async Task GetAsync_reports_persisted_resolved_attempt()
    {
        var repository =
            new RecordingRepository(
                new VaDecisionDocumentProcessingAttempt
                {
                    ClaimId = new ClaimId("claim-001"),
                    ArtifactId = new ArtifactId("artifact-002"),
                    ProcessedAt =
                        new DateTimeOffset(
                            2026, 8, 28, 11, 0, 0,
                            TimeSpan.Zero),
                    VaDecisionId =
                        new VaDecisionId("decision-001"),
                    Matches =
                    [
                        Match(
                            VaDecisionDocumentIssueMatchStatuses.Matched,
                            "issue-001"),
                        Match(
                            VaDecisionDocumentIssueMatchStatuses.Matched,
                            "issue-002")
                    ]
                });

        var service =
            new VaDecisionDocumentProcessingHistoryService(
                repository);

        var entry =
            Assert.Single(
                await service.GetAsync(
                    new ClaimId("claim-001")));

        Assert.Equal(2, entry.MatchedIssueCount);
        Assert.Equal(0, entry.UnmatchedIssueCount);
        Assert.Equal(0, entry.AmbiguousIssueCount);
        Assert.True(entry.Persisted);
        Assert.False(entry.HasUnresolvedIssues);
    }

    private static VaDecisionDocumentIssueMatch Match(
        string status,
        string? claimIssueId = null) =>
        new()
        {
            Status = status,
            ClaimIssueId =
                claimIssueId is null
                    ? null
                    : new ClaimIssueId(claimIssueId),
            CandidateClaimIssueIds = [],
            Interpretation =
                new VaIssueDecisionInterpretation
                {
                    IssueDescription = "Test issue",
                    Outcome = "Denied",
                    Rationale = "Test rationale",
                    FavorableFindings = [],
                    AdverseFindings = [],
                    CitedRegulations = [],
                    ReferencedEvidence = [],
                    SourceExcerpts = []
                }
        };

    private sealed class RecordingRepository :
        IVaDecisionDocumentProcessingAttemptRepository
    {
        private readonly IReadOnlyList<
            VaDecisionDocumentProcessingAttempt> _attempts;

        public RecordingRepository(
            params VaDecisionDocumentProcessingAttempt[] attempts)
        {
            _attempts = attempts;
        }

        public Task AddAsync(
            VaDecisionDocumentProcessingAttempt attempt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<VaDecisionDocumentProcessingAttempt>>
            GetByClaimAsync(
                ClaimId claimId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(_attempts);
    }
}
