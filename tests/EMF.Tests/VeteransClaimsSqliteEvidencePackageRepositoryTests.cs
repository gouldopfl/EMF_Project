using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
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

            Assert.Null(
                stored.ServiceConnectionBasisId);

            var issuePackages =
                await repository.GetEvidencePackagesAsync(
                    package.ClaimIssueId);

            var issuePackage =
                Assert.Single(issuePackages);

            Assert.Equal(package.Id, issuePackage.Id);

            Assert.Null(
                issuePackage.ServiceConnectionBasisId);

            var connections =
                new SqliteServiceConnectionRepository(databasePath);

            var theory = new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-osa-secondary"),
                ClaimIssueId = claimIssue.Id,
                TheoryType = ServiceConnectionTheoryTypes.Secondary
            };

            await connections.AddServiceConnectionTheoryAsync(theory);

            var basis = new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-osa-secondary"),
                ClaimIssueId = claimIssue.Id,
                ServiceConnectionTheoryId = theory.Id
            };

            await connections.AddServiceConnectionBasisAsync(basis);

            var scopedPackage = new EvidencePackage
            {
                Id = new EvidencePackageId("package-osa-secondary"),
                ClaimIssueId = claimIssue.Id,
                Purpose = "Medical review",
                ReviewerRole = "MedicalProfessional",
                ServiceConnectionBasisId = basis.Id
            };

            await repository.AddEvidencePackageAsync(scopedPackage);

            var storedScoped =
                await repository.GetEvidencePackageAsync(scopedPackage.Id);

            Assert.NotNull(storedScoped);
            Assert.Equal(
                basis.Id,
                storedScoped!.ServiceConnectionBasisId);

            var allPackages =
                await repository.GetEvidencePackagesAsync(claimIssue.Id);

            var listedScoped =
                Assert.Single(
                    allPackages.Where(x => x.Id == scopedPackage.Id));

            Assert.Equal(
                basis.Id,
                listedScoped.ServiceConnectionBasisId);

            var artifact = new EvidencePackageArtifact
            {
                EvidencePackageId = package.Id,
                ArtifactId = new ArtifactId("artifact-001"),
                ContentRole =
                    EvidencePackageContentRoles.UnderlyingEvidence,
                ReviewerPageSelection = "11,13-17"
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

            Assert.Equal(
                "11,13-17",
                storedArtifact.ReviewerPageSelection);

            await repository.SetReviewerPageSelectionAsync(
                package.Id,
                artifact.ArtifactId,
                "2,4-5");

            storedArtifacts =
                await repository.GetEvidencePackageArtifactsAsync(
                    package.Id);

            storedArtifact = Assert.Single(storedArtifacts);

            Assert.Equal(
                "2,4-5",
                storedArtifact.ReviewerPageSelection);

            await repository.SetReviewerPageSelectionAsync(
                package.Id,
                artifact.ArtifactId,
                null);

            storedArtifacts =
                await repository.GetEvidencePackageArtifactsAsync(
                    package.Id);

            storedArtifact = Assert.Single(storedArtifacts);

            Assert.Null(
                storedArtifact.ReviewerPageSelection);

            await using (var connection =
                new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={databasePath}"))
            {
                await connection.OpenAsync();

                await using var command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    CREATE TRIGGER FailEvidencePackageArtifactInsert
                    BEFORE INSERT ON VeteransClaims_EvidencePackageArtifacts
                    BEGIN
                        SELECT RAISE(ABORT, 'artifact insert failed');
                    END;
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var failingPackage =
                new EvidencePackage
                {
                    Id =
                        new EvidencePackageId(
                            "package-artifact-rollback"),
                    ClaimIssueId = claimIssue.Id,
                    Purpose = "Rollback test",
                    ReviewerRole = "MedicalProfessional"
                };

            var failingArtifact =
                new EvidencePackageArtifact
                {
                    EvidencePackageId = failingPackage.Id,
                    ArtifactId =
                        new ArtifactId(
                            "artifact-rollback"),
                    ContentRole =
                        EvidencePackageContentRoles.UnderlyingEvidence
                };

            await Assert.ThrowsAsync<
                Microsoft.Data.Sqlite.SqliteException>(
                    () => repository.AddEvidencePackageAsync(
                        failingPackage,
                        [failingArtifact]));

            Assert.Null(
                await repository.GetEvidencePackageAsync(
                    failingPackage.Id));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
