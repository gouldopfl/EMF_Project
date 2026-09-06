using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteIssueDecisionArtifactRepositoryTests
{
    [Fact]
    public async Task Association_RoundTripsAndAllowsSharedArtifact()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = new ArtifactId("artifact-001"),
                    Name = "VA decision",
                    ArtifactType = "Decision"
                });

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
                    VALUES
                        ('issue-001', 'claim-001', 'ServiceConnection'),
                        ('issue-002', 'claim-001', 'Evaluation');
                INSERT INTO VeteransClaims_VaDecisions
                    (Id, DecisionDate)
                    VALUES ('decision-001', '2026-09-06T00:00:00+00:00');
                INSERT INTO VeteransClaims_IssueDecisions
                    (Id, VaDecisionId, ClaimIssueId, Outcome)
                    VALUES
                        ('issue-decision-001',
                         'decision-001',
                         'issue-001',
                         'Granted'),
                        ('issue-decision-002',
                         'decision-001',
                         'issue-002',
                         'Denied');
                """;
            await seed.ExecuteNonQueryAsync();

            var repository = new SqliteVaDecisionRepository(path);
            var artifactId = new ArtifactId("artifact-001");

            foreach (var id in new[]
                     {
                         "issue-decision-001",
                         "issue-decision-002"
                     })
            {
                await repository.AddIssueDecisionArtifactAsync(
                    new IssueDecisionArtifact
                    {
                        IssueDecisionId = new IssueDecisionId(id),
                        ArtifactId = artifactId
                    });
            }

            Assert.Equal(
                artifactId,
                Assert.Single(
                    await repository.GetArtifactIdsAsync(
                        new IssueDecisionId("issue-decision-001"))));

            var decisionIds =
                await repository.GetIssueDecisionIdsAsync(artifactId);

            Assert.Equal(2, decisionIds.Count);
            Assert.Contains(
                new IssueDecisionId("issue-decision-001"),
                decisionIds);
            Assert.Contains(
                new IssueDecisionId("issue-decision-002"),
                decisionIds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Association_RejectsMissingRecords()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = new ArtifactId("artifact-001"),
                    Name = "VA decision",
                    ArtifactType = "Decision"
                });

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
                """;
            await seed.ExecuteNonQueryAsync();

            var repository = new SqliteVaDecisionRepository(path);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddIssueDecisionArtifactAsync(
                    new IssueDecisionArtifact
                    {
                        IssueDecisionId =
                            new IssueDecisionId("missing-decision"),
                        ArtifactId =
                            new ArtifactId("artifact-001")
                    }));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddIssueDecisionArtifactAsync(
                    new IssueDecisionArtifact
                    {
                        IssueDecisionId =
                            new IssueDecisionId("issue-decision-001"),
                        ArtifactId =
                            new ArtifactId("missing-artifact")
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
