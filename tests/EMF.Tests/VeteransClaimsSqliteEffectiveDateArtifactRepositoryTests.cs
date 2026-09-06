using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteEffectiveDateArtifactRepositoryTests
{
    [Fact]
    public async Task Association_RoundTripsInBothDirections()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("v-effective-artifact")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-effective-artifact"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var claimIssue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-effective-artifact"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.IncreasedEvaluation
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(claimIssue);

            var decision = new VaDecision
            {
                Id = new VaDecisionId("decision-effective-artifact"),
                DecisionDate = DateTimeOffset.UtcNow
            };

            var issueDecision = new IssueDecision
            {
                Id = new IssueDecisionId("issue-decision-effective-artifact"),
                VaDecisionId = decision.Id,
                ClaimIssueId = claimIssue.Id,
                Outcome = IssueDecisionOutcomes.Granted
            };

            await new SqliteVaDecisionRepository(databasePath)
                .AddDecisionAsync(
                    decision,
                    new[] { issueDecision },
                    Array.Empty<IssueDecisionSubmission>());

            var evaluation = new DisabilityEvaluation
            {
                Id = new DisabilityEvaluationId("evaluation-effective-artifact"),
                IssueDecisionId = issueDecision.Id,
                Evaluation = "50%"
            };

            var effectiveDate = new EffectiveDate
            {
                Id = new EffectiveDateId("effective-date-artifact"),
                DisabilityEvaluationId = evaluation.Id,
                Date = new DateOnly(2026, 1, 1)
            };

            var repository =
                new SqliteDisabilityEvaluationRepository(databasePath);

            await repository.AddEvaluationsAsync(
                issueDecision.Id,
                new[] { evaluation },
                new[] { effectiveDate });

            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync();

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-effective-date"),
                Name = "Effective date evidence",
                ArtifactType = "Test"
            };

            await evidence.AddArtifactAsync(artifact);

            await repository.AddEffectiveDateArtifactAsync(
                new EffectiveDateArtifact
                {
                    EffectiveDateId = effectiveDate.Id,
                    ArtifactId = artifact.Id
                });

            Assert.Equal(
                artifact.Id,
                Assert.Single(
                    await repository.GetArtifactIdsAsync(
                        effectiveDate.Id)));

            Assert.Equal(
                effectiveDate.Id,
                Assert.Single(
                    await repository.GetEffectiveDateIdsAsync(
                        artifact.Id)));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Association_RejectsMissingRecords()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync();

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-existing"),
                Name = "Existing artifact",
                ArtifactType = "Test"
            };

            await evidence.AddArtifactAsync(artifact);

            var repository =
                new SqliteDisabilityEvaluationRepository(databasePath);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddEffectiveDateArtifactAsync(
                    new EffectiveDateArtifact
                    {
                        EffectiveDateId =
                            new EffectiveDateId("missing-effective-date"),
                        ArtifactId = artifact.Id
                    }));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddEffectiveDateArtifactAsync(
                    new EffectiveDateArtifact
                    {
                        EffectiveDateId =
                            new EffectiveDateId("missing-effective-date"),
                        ArtifactId =
                            new ArtifactId("missing-artifact")
                    }));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
