using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueDecisionReviewHistoryServiceTests
{
    [Fact]
    public async Task GetAsync_AnalyzesAllHistoricalDecisions()
    {
        var issueId =
            new ClaimIssueId("issue-1");

        var repository =
            new RecordingRepository(
                new IssueDecision
                {
                    Id = new IssueDecisionId("decision-1"),
                    VaDecisionId = new VaDecisionId("va-1"),
                    ClaimIssueId = issueId,
                    Outcome = IssueDecisionOutcomes.Denied
                },
                new IssueDecision
                {
                    Id = new IssueDecisionId("decision-2"),
                    VaDecisionId = new VaDecisionId("va-2"),
                    ClaimIssueId = issueId,
                    Outcome = IssueDecisionOutcomes.Granted
                });

        var comparisons =
            new ClaimIssueDecisionComparisonHistoryService(
                repository,
                new ClaimIssueDecisionComparisonService());

        var service =
            new ClaimIssueDecisionReviewHistoryService(
                comparisons,
                new ClaimIssueDecisionReviewService(),
                new ClaimIssueDecisionReviewAnalysisService());

        var merits =
            new ClaimIssueMeritsOutcomeAssessment
            {
                ClaimIssueId = issueId,
                TheoryOutcomes = [],
                Outcome = FindingOutcomes.Favorable
            };

        var recommendation =
            new ClaimIssueDecisionRecommendation
            {
                ClaimIssueId = issueId,
                IsReadyForAdjudication = true,
                MeritsOutcome = FindingOutcomes.Favorable,
                RecommendedOutcome =
                    IssueDecisionOutcomes.Granted
            };

        var result =
            await service.GetAsync(
                recommendation,
                merits);

        Assert.Equal(2, result.Count);

        Assert.Contains(
            result,
            x =>
                x.Review.Comparison.IssueDecision.Id ==
                    new IssueDecisionId("decision-1") &&
                x.Review.RequiresReview);

        Assert.Contains(
            result,
            x =>
                x.Review.Comparison.IssueDecision.Id ==
                    new IssueDecisionId("decision-2") &&
                !x.Review.RequiresReview);
    }

    private sealed class RecordingRepository :
        IVaDecisionRepository
    {
        private readonly IReadOnlyList<IssueDecision> _decisions;

        public RecordingRepository(
            params IssueDecision[] decisions)
        {
            _decisions = decisions;
        }

        public Task<IReadOnlyList<IssueDecision>>
            GetIssueDecisionsAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(_decisions);

        public Task AddDecisionAsync(
            VaDecision decision,
            IReadOnlyCollection<IssueDecision> issueDecisions,
            IReadOnlyCollection<IssueDecisionSubmission>
                submissionAssociations,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<VaDecision?> GetDecisionAsync(
            VaDecisionId vaDecisionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VaDecision?>(
                new VaDecision
                {
                    Id = vaDecisionId,
                    DecisionDate = DateTimeOffset.UnixEpoch
                });

        public Task<IReadOnlyList<IssueDecision>>
            GetIssueDecisionsAsync(
                VaDecisionId vaDecisionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SubmissionId>>
            GetSubmissionIdsAsync(
                IssueDecisionId issueDecisionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddDecisionArtifactAsync(
            VaDecisionArtifact association,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EMF.Core.Models.Identities.ArtifactId>>
            GetArtifactIdsAsync(
                VaDecisionId vaDecisionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
