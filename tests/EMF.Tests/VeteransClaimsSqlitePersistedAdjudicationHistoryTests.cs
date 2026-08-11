using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqlitePersistedAdjudicationHistoryTests
{
    [Fact]
    public async Task RepositoryChain_ReconstructsAdjudicationHistory()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(
                databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var claimIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.IncreasedEvaluation
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(claimIssue);

            var submission = new Submission
            {
                Id =
                    new SubmissionId("submission-001"),
                ClaimId = claim.Id,
                SubmissionType =
                    SubmissionTypes.SupplementalClaim
            };

            await new SqliteSubmissionRepository(
                databasePath)
                .AddSubmissionAsync(
                    submission,
                    new[] { claimIssue.Id });

            var decision = new VaDecision
            {
                Id = new VaDecisionId("decision-001"),
                DecisionDate =
                    new DateTimeOffset(
                        2026,
                        8,
                        11,
                        0,
                        0,
                        0,
                        TimeSpan.Zero)
            };

            var issueDecision = new IssueDecision
            {
                Id =
                    new IssueDecisionId(
                        "issue-decision-001"),
                VaDecisionId = decision.Id,
                ClaimIssueId = claimIssue.Id,
                Outcome =
                    IssueDecisionOutcomes.Granted
            };

            await new SqliteVaDecisionRepository(databasePath)
                .AddDecisionAsync(
                    decision,
                    new[] { issueDecision },
                    new[]
                    {
                        new IssueDecisionSubmission
                        {
                            IssueDecisionId =
                                issueDecision.Id,
                            SubmissionId =
                                submission.Id
                        }
                    });

            var initialEvaluation =
                new DisabilityEvaluation
                {
                    Id =
                        new DisabilityEvaluationId(
                            "evaluation-001"),
                    IssueDecisionId =
                        issueDecision.Id,
                    Evaluation = "0%"
                };

            var increasedEvaluation =
                new DisabilityEvaluation
                {
                    Id =
                        new DisabilityEvaluationId(
                            "evaluation-002"),
                    IssueDecisionId =
                        issueDecision.Id,
                    Evaluation = "50%"
                };

            var evaluationRepository =
                new SqliteDisabilityEvaluationRepository(
                    databasePath);

            await evaluationRepository.AddEvaluationsAsync(
                issueDecision.Id,
                new[]
                {
                    initialEvaluation,
                    increasedEvaluation
                },
                new[]
                {
                    new EffectiveDate
                    {
                        Id =
                            new EffectiveDateId(
                                "effective-date-001"),
                        DisabilityEvaluationId =
                            initialEvaluation.Id,
                        Date =
                            new DateOnly(2024, 1, 1)
                    },
                    new EffectiveDate
                    {
                        Id =
                            new EffectiveDateId(
                                "effective-date-002"),
                        DisabilityEvaluationId =
                            increasedEvaluation.Id,
                        Date =
                            new DateOnly(2026, 1, 1)
                    }
                });

            var storedClaim =
                await new SqliteClaimRepository(databasePath)
                    .GetClaimAsync(claim.Id);

            var storedIssue =
                await new SqliteClaimIssueRepository(
                    databasePath)
                    .GetClaimIssueAsync(claimIssue.Id);

            var storedSubmissions =
                await new SqliteSubmissionRepository(
                    databasePath)
                    .GetSubmissionsAsync(claim.Id);

            var decisionRepository =
                new SqliteVaDecisionRepository(
                    databasePath);

            var storedIssueDecisions =
                await decisionRepository
                    .GetIssueDecisionsAsync(decision.Id);

            var storedSubmissionIds =
                await decisionRepository
                    .GetSubmissionIdsAsync(
                        issueDecision.Id);

            var storedEvaluations =
                await evaluationRepository
                    .GetEvaluationsAsync(
                        issueDecision.Id);

            var initialDate =
                await evaluationRepository
                    .GetEffectiveDateAsync(
                        initialEvaluation.Id);

            var increasedDate =
                await evaluationRepository
                    .GetEffectiveDateAsync(
                        increasedEvaluation.Id);

            Assert.NotNull(storedClaim);
            Assert.Equal(veteran.Id, storedClaim!.VeteranId);

            Assert.NotNull(storedIssue);
            Assert.Equal(claim.Id, storedIssue!.ClaimId);

            Assert.Equal(
                submission.Id,
                Assert.Single(storedSubmissions).Id);

            Assert.Equal(
                claimIssue.Id,
                Assert.Single(
                    storedIssueDecisions).ClaimIssueId);

            Assert.Equal(
                submission.Id,
                Assert.Single(storedSubmissionIds));

            Assert.Equal(2, storedEvaluations.Count);

            Assert.Equal(
                new DateOnly(2024, 1, 1),
                initialDate!.Date);

            Assert.Equal(
                new DateOnly(2026, 1, 1),
                increasedDate!.Date);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
