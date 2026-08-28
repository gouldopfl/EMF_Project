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

        await service.RecordAsync(
            claimId,
            new VaDecisionDocumentInterpretation
            {
                ArtifactId = artifactId,
                IssueDecisions = []
            },
            new VaDecisionDocumentProcessingResult
            {
                Decision =
                    new VaDecision
                    {
                        Id = decisionId,
                        DecisionDate = processedAt
                    },
                Matches = []
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

        await service.RecordAsync(
            claimId,
            new VaDecisionDocumentInterpretation
            {
                ArtifactId = new ArtifactId("artifact-1"),
                IssueDecisions = []
            },
            new VaDecisionDocumentProcessingResult
            {
                Matches =
                [
                    new VaDecisionDocumentIssueMatch
                    {
                        Interpretation =
                            new VaIssueDecisionInterpretation
                            {
                                IssueDescription = "GERD",
                                Outcome = "Denied",
                                Rationale = "Test rationale",
                                FavorableFindings = [],
                                AdverseFindings = [],
                                CitedRegulations = [],
                                ReferencedEvidence = [],
                                SourceExcerpts = []
                            },
                        Status =
                            VaDecisionDocumentIssueMatchStatuses.Unmatched,
                        CandidateClaimIssueIds = []
                    }
                ]
            },
            processedAt);

        var attempt = Assert.Single(repository.Attempts);

        Assert.Null(attempt.VaDecisionId);
        Assert.False(attempt.Persisted);
        Assert.True(attempt.HasUnresolvedIssues);
        Assert.Single(attempt.Matches);
    }

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
