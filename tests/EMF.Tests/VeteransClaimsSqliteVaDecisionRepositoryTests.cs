using Microsoft.Data.Sqlite;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransClaimsSqliteVaDecisionRepositoryTests
{
    [Fact]
    public async Task Repository_PersistsDecisionAtomically()
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

            var veteranRepository =
                new SqliteVeteranRepository(databasePath);

            await veteranRepository.AddVeteranAsync(
                veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            var claimRepository =
                new SqliteClaimRepository(databasePath);

            await claimRepository.AddClaimAsync(claim);

            var claimIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var claimIssueRepository =
                new SqliteClaimIssueRepository(databasePath);

            await claimIssueRepository.AddClaimIssueAsync(
                claimIssue);

            var submission = new Submission
            {
                Id =
                    new SubmissionId("submission-001"),
                ClaimId = claim.Id,
                SubmissionType =
                    SubmissionTypes.InitialClaim
            };

            var submissionRepository =
                new SqliteSubmissionRepository(
                    databasePath);

            await submissionRepository.AddSubmissionAsync(
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

            var association =
                new IssueDecisionSubmission
                {
                    IssueDecisionId =
                        issueDecision.Id,
                    SubmissionId =
                        submission.Id
                };

            IVaDecisionRepository repository =
                new SqliteVaDecisionRepository(
                    databasePath);

            await repository.AddDecisionAsync(
                decision,
                new[] { issueDecision },
                new[] { association });

            var storedDecision =
                await repository.GetDecisionAsync(
                    decision.Id);

            var storedIssueDecisions =
                await repository
                    .GetIssueDecisionsAsync(
                        decision.Id);

            var storedSubmissionIds =
                await repository
                    .GetSubmissionIdsAsync(
                        issueDecision.Id);

            Assert.NotNull(storedDecision);
            Assert.Equal(
                decision.Id,
                storedDecision!.Id);

            Assert.Equal(
                decision.DecisionDate,
                storedDecision.DecisionDate);

            var storedIssueDecision =
                Assert.Single(storedIssueDecisions);

            Assert.Equal(
                issueDecision.Id,
                storedIssueDecision.Id);

            Assert.Equal(
                claimIssue.Id,
                storedIssueDecision.ClaimIssueId);

            Assert.Equal(
                IssueDecisionOutcomes.Granted,
                storedIssueDecision.Outcome);

            Assert.Equal(
                submission.Id,
                Assert.Single(storedSubmissionIds));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RollsBackDecisionDocumentWhenArtifactInsertFails()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            await using (var connection =
                new SqliteConnection(
                    $"Data Source={databasePath}"))
            {
                await connection.OpenAsync();

                await using var command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    CREATE TRIGGER FailVaDecisionArtifactInsert
                    BEFORE INSERT ON VeteransClaims_VaDecisionArtifacts
                    BEGIN
                        SELECT RAISE(ABORT, 'artifact insert failed');
                    END;
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-artifact-rollback")
                };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("claim-artifact-rollback"),
                    VeteranId = veteran.Id
                };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue =
                new ClaimIssue
                {
                    Id =
                        new ClaimIssueId(
                            "issue-artifact-rollback"),
                    ClaimId = claim.Id,
                    ClaimIssueType =
                        ClaimIssueTypes.ServiceConnection
                };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var decision =
                new VaDecision
                {
                    Id =
                        new VaDecisionId(
                            "decision-artifact-rollback"),
                    DecisionDate =
                        DateTimeOffset.UtcNow
                };

            var issueDecision =
                new IssueDecision
                {
                    Id =
                        new IssueDecisionId(
                            "issue-decision-artifact-rollback"),
                    VaDecisionId = decision.Id,
                    ClaimIssueId = issue.Id,
                    Outcome =
                        IssueDecisionOutcomes.Denied
                };

            var repository =
                new SqliteVaDecisionRepository(databasePath);

            await Assert.ThrowsAsync<SqliteException>(
                () => repository.AddDecisionDocumentAsync(
                    decision,
                    [issueDecision],
                    new VaDecisionArtifact
                    {
                        VaDecisionId = decision.Id,
                        ArtifactId =
                            new ArtifactId(
                                "artifact-rollback")
                    }));

            Assert.Null(
                await repository.GetDecisionAsync(
                    decision.Id));

            Assert.Empty(
                await repository.GetIssueDecisionsAsync(
                    decision.Id));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RejectsMismatchedDecisionIdentity()
    {
        var repository =
            new SqliteVaDecisionRepository(
                Path.Combine(
                    Path.GetTempPath(),
                    $"{Guid.NewGuid():N}.db"));

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
            VaDecisionId =
                new VaDecisionId("different-decision"),
            ClaimIssueId =
                new ClaimIssueId("claim-issue-001"),
            Outcome =
                IssueDecisionOutcomes.Denied
        };

        await Assert.ThrowsAsync<
            InvalidOperationException>(
                () => repository.AddDecisionAsync(
                    decision,
                    new[] { issueDecision },
                    Array.Empty<
                        IssueDecisionSubmission>()));
    }

    [Fact]
    public async Task Repository_RollsBackInvalidSubmissionAssociation()
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

            var presentedIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var differentIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-002"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.IncreasedEvaluation
            };

            var claimIssueRepository =
                new SqliteClaimIssueRepository(databasePath);

            await claimIssueRepository.AddClaimIssueAsync(
                presentedIssue);

            await claimIssueRepository.AddClaimIssueAsync(
                differentIssue);

            var submission = new Submission
            {
                Id =
                    new SubmissionId("submission-001"),
                ClaimId = claim.Id,
                SubmissionType =
                    SubmissionTypes.InitialClaim
            };

            await new SqliteSubmissionRepository(
                databasePath)
                .AddSubmissionAsync(
                    submission,
                    new[] { presentedIssue.Id });

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
                ClaimIssueId = differentIssue.Id,
                Outcome =
                    IssueDecisionOutcomes.Denied
            };

            var association =
                new IssueDecisionSubmission
                {
                    IssueDecisionId =
                        issueDecision.Id,
                    SubmissionId =
                        submission.Id
                };

            var repository =
                new SqliteVaDecisionRepository(
                    databasePath);

            await Assert.ThrowsAsync<
                InvalidOperationException>(
                    () => repository.AddDecisionAsync(
                        decision,
                        new[] { issueDecision },
                        new[] { association }));

            var stored =
                await repository.GetDecisionAsync(
                    decision.Id);

            Assert.Null(stored);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}

public sealed partial class VeteransClaimsSqliteVaDecisionRepositoryTests
{
    [Fact]
    public async Task Repository_ReturnsDecisionHistoryForClaimIssue()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var schema =
                new VeteransClaimsSqliteSchema(databasePath);

            await schema.InitializeAsync();

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-history")
                };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("claim-history"),
                    VeteranId = veteran.Id
                };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue =
                new ClaimIssue
                {
                    Id = new ClaimIssueId("issue-history"),
                    ClaimId = claim.Id,
                    ClaimIssueType =
                        ClaimIssueTypes.ServiceConnection
                };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var repository =
                new SqliteVaDecisionRepository(databasePath);

            var firstDecision =
                new VaDecision
                {
                    Id = new VaDecisionId("decision-001"),
                    DecisionDate =
                        new DateTimeOffset(
                            2026, 1, 1, 0, 0, 0,
                            TimeSpan.Zero)
                };

            var secondDecision =
                new VaDecision
                {
                    Id = new VaDecisionId("decision-002"),
                    DecisionDate =
                        new DateTimeOffset(
                            2026, 6, 1, 0, 0, 0,
                            TimeSpan.Zero)
                };

            await repository.AddDecisionAsync(
                firstDecision,
                [
                    new IssueDecision
                    {
                        Id =
                            new IssueDecisionId(
                                "issue-decision-001"),
                        VaDecisionId = firstDecision.Id,
                        ClaimIssueId = issue.Id,
                        Outcome =
                            IssueDecisionOutcomes.Denied
                    }
                ],
                []);

            await repository.AddDecisionAsync(
                secondDecision,
                [
                    new IssueDecision
                    {
                        Id =
                            new IssueDecisionId(
                                "issue-decision-002"),
                        VaDecisionId = secondDecision.Id,
                        ClaimIssueId = issue.Id,
                        Outcome =
                            IssueDecisionOutcomes.Granted
                    }
                ],
                []);

            var history =
                await repository.GetIssueDecisionsAsync(
                    issue.Id);

            Assert.Equal(2, history.Count);

            Assert.Contains(
                history,
                x =>
                    x.VaDecisionId ==
                        firstDecision.Id &&
                    x.Outcome ==
                        IssueDecisionOutcomes.Denied);

            Assert.Contains(
                history,
                x =>
                    x.VaDecisionId ==
                        secondDecision.Id &&
                    x.Outcome ==
                        IssueDecisionOutcomes.Granted);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
