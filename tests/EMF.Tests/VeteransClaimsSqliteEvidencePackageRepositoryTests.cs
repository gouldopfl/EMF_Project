using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransClaimsSqliteEvidencePackageRepositoryTests
{
    [Fact]
    public void Repository_ImplementsEvidencePackageContract()
    {
        IEvidencePackageRepository repository =
            new SqliteEvidencePackageRepository("test.db");

        Assert.NotNull(repository);
    }
}

public sealed partial class VeteransClaimsSqliteEvidencePackageRepositoryTests
{
    [Fact]
    public async Task Repository_PersistsEvidencePackage()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
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
                Id = new ClaimIssueId("claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(claimIssue);

            var package = new EvidencePackage
            {
                Id = new EvidencePackageId("package-001"),
                ClaimIssueId = new ClaimIssueId("claim-issue-001"),
                Purpose = "Medical review",
                ReviewerRole = "MedicalProfessional"
            };

            IEvidencePackageRepository repository =
                new SqliteEvidencePackageRepository(
                    databasePath);

            await repository.AddEvidencePackageAsync(package);

            var stored =
                await repository.GetEvidencePackageAsync(
                    package.Id);

            Assert.NotNull(stored);
            Assert.Equal(package.Id, stored!.Id);
            Assert.Equal(
                package.ClaimIssueId,
                stored.ClaimIssueId);
            Assert.Equal(
                package.Purpose,
                stored.Purpose);
            Assert.Equal(
                package.ReviewerRole,
                stored.ReviewerRole);

            var issuePackages =
                await repository.GetEvidencePackagesAsync(
                    package.ClaimIssueId);

            var issuePackage =
                Assert.Single(issuePackages);

            Assert.Equal(package.Id, issuePackage.Id);


            var artifact = new EvidencePackageArtifact
            {
                EvidencePackageId = package.Id,
                ArtifactId = new ArtifactId("artifact-001"),
                ContentRole =
                    EvidencePackageContentRoles.UnderlyingEvidence
            };

            await repository
                .AddEvidencePackageArtifactAsync(
                    artifact);

            var storedArtifacts =
                await repository
                    .GetEvidencePackageArtifactsAsync(
                        package.Id);

            var storedArtifact =
                Assert.Single(storedArtifacts);

            Assert.Equal(
                artifact.EvidencePackageId,
                storedArtifact.EvidencePackageId);
            Assert.Equal(
                artifact.ArtifactId,
                storedArtifact.ArtifactId);
            Assert.Equal(
                artifact.ContentRole,
                storedArtifact.ContentRole);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
