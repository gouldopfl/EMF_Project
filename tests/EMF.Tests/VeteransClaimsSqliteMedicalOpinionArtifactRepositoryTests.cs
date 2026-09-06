using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteMedicalOpinionArtifactRepositoryTests
{
    [Fact]
    public async Task Association_RoundTripsInBothDirections()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteMedicalOpinionRepository(databasePath);

            await repository.InitializeAsync();

            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync();

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
                Id = new ClaimIssueId("claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(claimIssue);

            var opinion = new MedicalOpinion
            {
                Id = new MedicalOpinionId("opinion-001"),
                ClaimIssueId = claimIssue.Id,
                Question = "Is the condition related to service?",
                Opinion = "At least as likely as not."
            };

            await repository.AddMedicalOpinionAsync(opinion);

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-001"),
                Name = "Medical opinion",
                ArtifactType = "MedicalOpinion"
            };

            await evidence.AddArtifactAsync(artifact);

            await repository.AddMedicalOpinionArtifactAsync(
                new MedicalOpinionArtifact
                {
                    MedicalOpinionId = opinion.Id,
                    ArtifactId = artifact.Id
                });

            Assert.Equal(
                artifact.Id,
                Assert.Single(
                    await repository.GetArtifactIdsAsync(
                        opinion.Id)));

            Assert.Equal(
                opinion.Id,
                Assert.Single(
                    await repository.GetMedicalOpinionIdsAsync(
                        artifact.Id)));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Association_RejectsMissingMedicalOpinion()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteMedicalOpinionRepository(databasePath);

            await repository.InitializeAsync();

            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync();

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-missing-opinion"),
                Name = "Medical opinion",
                ArtifactType = "MedicalOpinion"
            };

            await evidence.AddArtifactAsync(artifact);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddMedicalOpinionArtifactAsync(
                    new MedicalOpinionArtifact
                    {
                        MedicalOpinionId =
                            new MedicalOpinionId("missing-opinion"),
                        ArtifactId = artifact.Id
                    }));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
