using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteDisabilityEvaluationArtifactRepositoryTests
{
    [Fact]
    public async Task Association_RoundTripsInBothDirections()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            await SeedEvaluationAsync(
                path,
                "evaluation-001",
                "issue-decision-001");

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-001"),
                Name = "Evaluation evidence",
                ArtifactType = "Test"
            };
            await evidence.AddArtifactAsync(artifact);

            var repository =
                new SqliteDisabilityEvaluationRepository(path);

            await repository.AddDisabilityEvaluationArtifactAsync(
                new DisabilityEvaluationArtifact
                {
                    DisabilityEvaluationId =
                        new DisabilityEvaluationId("evaluation-001"),
                    ArtifactId = artifact.Id
                });

            Assert.Equal(
                artifact.Id,
                Assert.Single(
                    await repository.GetArtifactIdsAsync(
                        new DisabilityEvaluationId("evaluation-001"))));

            Assert.Equal(
                new DisabilityEvaluationId("evaluation-001"),
                Assert.Single(
                    await repository.GetDisabilityEvaluationIdsAsync(
                        artifact.Id)));
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

            await SeedEvaluationAsync(
                path,
                "evaluation-001",
                "issue-decision-001");

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-001"),
                Name = "Evaluation evidence",
                ArtifactType = "Test"
            };
            await evidence.AddArtifactAsync(artifact);

            var repository =
                new SqliteDisabilityEvaluationRepository(path);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddDisabilityEvaluationArtifactAsync(
                    new DisabilityEvaluationArtifact
                    {
                        DisabilityEvaluationId =
                            new DisabilityEvaluationId("missing-evaluation"),
                        ArtifactId = artifact.Id
                    }));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddDisabilityEvaluationArtifactAsync(
                    new DisabilityEvaluationArtifact
                    {
                        DisabilityEvaluationId =
                            new DisabilityEvaluationId("evaluation-001"),
                        ArtifactId =
                            new ArtifactId("missing-artifact")
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task SeedEvaluationAsync(
        string path,
        string evaluationId,
        string issueDecisionId)
    {
        await using var connection =
            new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            INSERT INTO VeteransClaims_Veterans (Id)
                VALUES ('v-001');
            INSERT INTO VeteransClaims_Claims (Id, VeteranId)
                VALUES ('claim-001', 'v-001');
            INSERT INTO VeteransClaims_ClaimIssues
                (Id, ClaimId, ClaimIssueType)
                VALUES (
                    'issue-001',
                    'claim-001',
                    'IncreasedEvaluation');
            INSERT INTO VeteransClaims_VaDecisions
                (Id, DecisionDate)
                VALUES (
                    'decision-001',
                    '2026-09-06T00:00:00+00:00');
            INSERT INTO VeteransClaims_IssueDecisions
                (Id, VaDecisionId, ClaimIssueId, Outcome)
                VALUES (
                    '{issueDecisionId}',
                    'decision-001',
                    'issue-001',
                    'Granted');
            INSERT INTO VeteransClaims_DisabilityEvaluations
                (Id, IssueDecisionId, Evaluation)
                VALUES (
                    '{evaluationId}',
                    '{issueDecisionId}',
                    '50%');
            """;

        await command.ExecuteNonQueryAsync();
    }
}
