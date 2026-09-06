using Microsoft.Data.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteIssueDecisionFindingRepositoryTests
{
    [Fact]
    public async Task Migration62_AddsIssueDecisionFindings()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            await using var connection =
                new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = 'VeteransClaims_IssueDecisionFindings';
                """;

            Assert.Equal(
                1,
                Convert.ToInt32(
                    await command.ExecuteScalarAsync()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Association_RoundTripsInBothDirections()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            await using var connection =
                new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();

            await using var seed = connection.CreateCommand();
            seed.CommandText =
                """
                INSERT INTO VeteransClaims_Veterans (Id)
                    VALUES ('v-001');
                INSERT INTO VeteransClaims_Claims (Id, VeteranId)
                    VALUES ('claim-001', 'v-001');
                INSERT INTO VeteransClaims_ClaimIssues
                    (Id, ClaimId, ClaimIssueType)
                    VALUES ('issue-001', 'claim-001', 'ServiceConnection');
                INSERT INTO VeteransClaims_VaDecisions
                    (Id, DecisionDate)
                    VALUES ('decision-001', '2026-09-06T00:00:00+00:00');
                INSERT INTO VeteransClaims_IssueDecisions
                    (Id, VaDecisionId, ClaimIssueId, Outcome)
                    VALUES (
                        'issue-decision-001',
                        'decision-001',
                        'issue-001',
                        'Granted');
                INSERT INTO VeteransClaims_Findings
                    (Id, ClaimIssueId, RequirementId, Outcome, Description)
                    VALUES (
                        'finding-001',
                        'issue-001',
                        NULL,
                        'Favorable',
                        'Test finding');
                """;
            await seed.ExecuteNonQueryAsync();

            var repository =
                new SqliteVaDecisionRepository(path);

            await repository.AddIssueDecisionFindingAsync(
                new IssueDecisionFinding
                {
                    IssueDecisionId =
                        new IssueDecisionId("issue-decision-001"),
                    FindingId =
                        new FindingId("finding-001")
                });

            Assert.Equal(
                new FindingId("finding-001"),
                Assert.Single(
                    await repository.GetFindingIdsAsync(
                        new IssueDecisionId("issue-decision-001"))));

            Assert.Equal(
                new IssueDecisionId("issue-decision-001"),
                Assert.Single(
                    await repository.GetIssueDecisionIdsAsync(
                        new FindingId("finding-001"))));
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Fact]
    public async Task Association_RejectsDifferentClaimIssue()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            await using var connection =
                new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();

            await using var seed = connection.CreateCommand();
            seed.CommandText =
                """
                INSERT INTO VeteransClaims_Veterans (Id)
                    VALUES ('v-002');
                INSERT INTO VeteransClaims_Claims (Id, VeteranId)
                    VALUES ('claim-002', 'v-002');
                INSERT INTO VeteransClaims_ClaimIssues
                    (Id, ClaimId, ClaimIssueType)
                    VALUES
                        ('issue-a', 'claim-002', 'ServiceConnection'),
                        ('issue-b', 'claim-002', 'ServiceConnection');
                INSERT INTO VeteransClaims_VaDecisions
                    (Id, DecisionDate)
                    VALUES ('decision-002', '2026-09-06T00:00:00+00:00');
                INSERT INTO VeteransClaims_IssueDecisions
                    (Id, VaDecisionId, ClaimIssueId, Outcome)
                    VALUES (
                        'issue-decision-002',
                        'decision-002',
                        'issue-a',
                        'Denied');
                INSERT INTO VeteransClaims_Findings
                    (Id, ClaimIssueId, RequirementId, Outcome, Description)
                    VALUES (
                        'finding-002',
                        'issue-b',
                        NULL,
                        'Unfavorable',
                        'Different issue');
                """;
            await seed.ExecuteNonQueryAsync();

            var repository =
                new SqliteVaDecisionRepository(path);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddIssueDecisionFindingAsync(
                    new IssueDecisionFinding
                    {
                        IssueDecisionId =
                            new IssueDecisionId("issue-decision-002"),
                        FindingId =
                            new FindingId("finding-002")
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }

}
