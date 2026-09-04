using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VaDecisionDocumentProcessingAttemptServiceTests
{
    [Fact]
    public async Task RecordAsync_RecordsPersistedAttempt()
    {
        var repository = new RecordingRepository();
        var service =
            new VaDecisionDocumentProcessingAttemptService(repository);

        var claimId = new ClaimId("claim-1");
        var artifactId = new ArtifactId("artifact-1");
        var decisionId = new VaDecisionId("decision-1");
        var processedAt =
            new DateTimeOffset(2026, 8, 27, 20, 0, 0, TimeSpan.Zero);

        var issue = CreateIssue();

        await service.RecordAsync(
            claimId,
            CreateInterpretation(artifactId, issue),
            new VaDecisionDocumentProcessingResult
            {
                Decision =
                    new VaDecision
                    {
                        Id = decisionId,
                        DecisionDate = processedAt
                    },
                Matches =
                [
                    CreateMatch(
                        issue,
                        VaDecisionDocumentIssueMatchStatuses.Matched,
                        new ClaimIssueId("issue-1"))
                ]
            },
            processedAt);

        var attempt = Assert.Single(repository.Attempts);

        Assert.Equal(claimId, attempt.ClaimId);
        Assert.Equal(artifactId, attempt.ArtifactId);
        Assert.Equal(processedAt, attempt.ProcessedAt);
        Assert.Equal(decisionId, attempt.VaDecisionId);
        Assert.True(attempt.Persisted);
    }

    [Fact]
    public async Task RecordAsync_RecordsUnresolvedAttempt()
    {
        var repository = new RecordingRepository();
        var service =
            new VaDecisionDocumentProcessingAttemptService(repository);

        var claimId = new ClaimId("claim-1");
        var processedAt =
            new DateTimeOffset(2026, 8, 27, 20, 0, 0, TimeSpan.Zero);

        var issue = CreateIssue();

        await service.RecordAsync(
            claimId,
            CreateInterpretation(
                new ArtifactId("artifact-1"),
                issue),
            new VaDecisionDocumentProcessingResult
            {
                Matches =
                [
                    CreateMatch(
                        issue,
                        VaDecisionDocumentIssueMatchStatuses.Unmatched,
                        null)
                ]
            },
            processedAt);

        var attempt = Assert.Single(repository.Attempts);

        Assert.Null(attempt.VaDecisionId);
        Assert.False(attempt.Persisted);
        Assert.True(attempt.HasUnresolvedIssues);
        Assert.Single(attempt.Matches);
    }

    [Fact]
    public async Task RecordAsync_RejectsPersistedResultWithUnresolvedMatch()
    {
        var repository = new RecordingRepository();
        var service =
            new VaDecisionDocumentProcessingAttemptService(repository);
        var issue = CreateIssue();

        var result = new VaDecisionDocumentProcessingResult
        {
            Decision = new VaDecision
            {
                Id = new VaDecisionId("decision-1"),
                DecisionDate = DateTimeOffset.UtcNow
            },
            Matches =
            [
                CreateMatch(
                    issue,
                    VaDecisionDocumentIssueMatchStatuses.Unmatched,
                    null)
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordAsync(
                new ClaimId("claim-1"),
                CreateInterpretation(
                    new ArtifactId("artifact-1"),
                    issue),
                result,
                DateTimeOffset.UtcNow));

        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task RecordAsync_RejectsUnpersistedResultWithAllMatchesResolved()
    {
        var repository = new RecordingRepository();
        var service =
            new VaDecisionDocumentProcessingAttemptService(repository);
        var issue = CreateIssue();

        var result = new VaDecisionDocumentProcessingResult
        {
            Matches =
            [
                CreateMatch(
                    issue,
                    VaDecisionDocumentIssueMatchStatuses.Matched,
                    new ClaimIssueId("issue-1"))
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordAsync(
                new ClaimId("claim-1"),
                CreateInterpretation(
                    new ArtifactId("artifact-1"),
                    issue),
                result,
                DateTimeOffset.UtcNow));

        Assert.Empty(repository.Attempts);
    }

    private static VaIssueDecisionInterpretation CreateIssue() =>
        new()
        {
            IssueDescription = "GERD",
            Outcome = IssueDecisionOutcomes.Denied,
            Rationale = "Test rationale",
            FavorableFindings = [],
            AdverseFindings = [],
            CitedRegulations = [],
            ReferencedEvidence = [],
            SourceExcerpts = []
        };

    private static VaDecisionDocumentInterpretation CreateInterpretation(
        ArtifactId artifactId,
        VaIssueDecisionInterpretation issue) =>
        new()
        {
            ArtifactId = artifactId,
            IssueDecisions = [issue]
        };

    private static VaDecisionDocumentIssueMatch CreateMatch(
        VaIssueDecisionInterpretation issue,
        string status,
        ClaimIssueId? claimIssueId) =>
        new()
        {
            Interpretation = issue,
            Status = status,
            ClaimIssueId = claimIssueId,
            CandidateClaimIssueIds =
                claimIssueId is null ? [] : [claimIssueId.Value]
        };

    private sealed class RecordingRepository :
        IVaDecisionDocumentProcessingAttemptRepository
    {
        public List<VaDecisionDocumentProcessingAttempt>
            Attempts { get; } = [];

        public Task AddAsync(
            VaDecisionDocumentProcessingAttempt attempt,
            CancellationToken cancellationToken = default)
        {
            Attempts.Add(attempt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VaDecisionDocumentProcessingAttempt>>
            GetByClaimAsync(
                ClaimId claimId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
