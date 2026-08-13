using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteEvidenceClassificationRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsEvidenceClassifications()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceClassificationRepository(
                    databasePath);

            await repository.InitializeAsync();

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

            var artifactId =
                new ArtifactId("artifact-001");

            var classification =
                new EvidenceClassification
                {
                    Id =
                        new EvidenceClassificationId(
                            "classification-001"),
                    ArtifactId = artifactId,
                    ClaimIssueId = claimIssue.Id,
                    Classification =
                        EvidenceClassifications.MedicalEvidence
                };

            await repository
                .AddEvidenceClassificationAsync(
                    classification);

            var stored =
                await repository
                    .GetEvidenceClassificationAsync(
                        classification.Id);

            var byArtifact =
                await repository
                    .GetEvidenceClassificationsAsync(
                        artifactId);

            var byIssue =
                await repository
                    .GetEvidenceClassificationsAsync(
                        claimIssue.Id);

            Assert.NotNull(stored);
            Assert.Equal(classification.Id, stored!.Id);
            Assert.Equal(
                classification.ArtifactId,
                stored.ArtifactId);
            Assert.Equal(
                classification.ClaimIssueId,
                stored.ClaimIssueId);
            Assert.Equal(
                classification.Classification,
                stored.Classification);

            Assert.Equal(
                classification.Id,
                Assert.Single(byArtifact).Id);

            Assert.Equal(
                classification.Id,
                Assert.Single(byIssue).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_PreservesNullClaimIssue()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceClassificationRepository(
                    databasePath);

            await repository.InitializeAsync();

            var classification = new EvidenceClassification
            {
                Id = new EvidenceClassificationId(
                    "classification-002"),
                ArtifactId = new ArtifactId("artifact-002"),
                ClaimIssueId = null,
                Classification =
                    EvidenceClassifications.ServiceRecord
            };

            await repository.AddEvidenceClassificationAsync(
                classification);

            var stored =
                await repository.GetEvidenceClassificationAsync(
                    classification.Id);

            Assert.NotNull(stored);
            Assert.Null(stored!.ClaimIssueId);
            Assert.Equal(
                classification.ArtifactId,
                stored.ArtifactId);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
