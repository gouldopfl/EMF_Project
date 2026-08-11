using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteDisabilityEvaluationRepositoryTests
{
    [Fact]
    public async Task Repository_PersistsStagedEvaluationsAtomically()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var schema =
                new VeteransClaimsSqliteSchema(databasePath);

            await schema.InitializeAsync();

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

            var decision = new VaDecision
            {
                Id = new VaDecisionId("decision-001"),
                DecisionDate = DateTimeOffset.UtcNow
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
                    Array.Empty<
                        IssueDecisionSubmission>());

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

            var initialEffectiveDate =
                new EffectiveDate
                {
                    Id =
                        new EffectiveDateId(
                            "effective-date-001"),
                    DisabilityEvaluationId =
                        initialEvaluation.Id,
                    Date = new DateOnly(2024, 1, 1)
                };

            var increasedEffectiveDate =
                new EffectiveDate
                {
                    Id =
                        new EffectiveDateId(
                            "effective-date-002"),
                    DisabilityEvaluationId =
                        increasedEvaluation.Id,
                    Date = new DateOnly(2026, 1, 1)
                };

            IDisabilityEvaluationRepository repository =
                new SqliteDisabilityEvaluationRepository(
                    databasePath);

            await repository.AddEvaluationsAsync(
                issueDecision.Id,
                new[]
                {
                    initialEvaluation,
                    increasedEvaluation
                },
                new[]
                {
                    initialEffectiveDate,
                    increasedEffectiveDate
                });

            var storedEvaluations =
                await repository.GetEvaluationsAsync(
                    issueDecision.Id);

            var storedInitialDate =
                await repository.GetEffectiveDateAsync(
                    initialEvaluation.Id);

            var storedIncreasedDate =
                await repository.GetEffectiveDateAsync(
                    increasedEvaluation.Id);

            Assert.Equal(2, storedEvaluations.Count);

            Assert.Contains(
                storedEvaluations,
                item => item.Evaluation == "0%");

            Assert.Contains(
                storedEvaluations,
                item => item.Evaluation == "50%");

            Assert.NotNull(storedInitialDate);
            Assert.Equal(
                new DateOnly(2024, 1, 1),
                storedInitialDate!.Date);

            Assert.NotNull(storedIncreasedDate);
            Assert.Equal(
                new DateOnly(2026, 1, 1),
                storedIncreasedDate!.Date);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RejectsUnrelatedEffectiveDate()
    {
        var repository =
            new SqliteDisabilityEvaluationRepository(
                Path.Combine(
                    Path.GetTempPath(),
                    $"{Guid.NewGuid():N}.db"));

        var issueDecisionId =
            new IssueDecisionId("issue-decision-001");

        var evaluation = new DisabilityEvaluation
        {
            Id =
                new DisabilityEvaluationId(
                    "evaluation-001"),
            IssueDecisionId = issueDecisionId,
            Evaluation = "50%"
        };

        var effectiveDate = new EffectiveDate
        {
            Id =
                new EffectiveDateId(
                    "effective-date-001"),
            DisabilityEvaluationId =
                new DisabilityEvaluationId(
                    "different-evaluation"),
            Date = new DateOnly(2026, 1, 1)
        };

        await Assert.ThrowsAsync<
            InvalidOperationException>(
                () => repository.AddEvaluationsAsync(
                    issueDecisionId,
                    new[] { evaluation },
                    new[] { effectiveDate }));
    }
}
