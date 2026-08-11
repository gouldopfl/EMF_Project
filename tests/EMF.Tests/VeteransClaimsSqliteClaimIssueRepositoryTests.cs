using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteClaimIssueRepositoryTests
{
    [Fact]
    public async Task Repository_StoresRetrievesAndFiltersClaimIssues()
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

            IClaimIssueRepository repository =
                new SqliteClaimIssueRepository(databasePath);

            var serviceConnectionIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var increasedEvaluationIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-002"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.IncreasedEvaluation
            };

            await repository.AddClaimIssueAsync(
                serviceConnectionIssue);

            await repository.AddClaimIssueAsync(
                increasedEvaluationIssue);

            var stored =
                await repository.GetClaimIssueAsync(
                    serviceConnectionIssue.Id);

            var claimIssues =
                await repository.GetClaimIssuesAsync(
                    claim.Id);

            Assert.NotNull(stored);
            Assert.Equal(
                serviceConnectionIssue.Id,
                stored!.Id);

            Assert.Equal(
                ClaimIssueTypes.ServiceConnection,
                stored.ClaimIssueType);

            Assert.Equal(2, claimIssues.Count);
            Assert.All(
                claimIssues,
                item => Assert.Equal(
                    claim.Id,
                    item.ClaimId));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RejectsIssueForMissingClaim()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteClaimIssueRepository(
                    databasePath);

            await repository.InitializeAsync();

            var claimIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-001"),
                ClaimId =
                    new ClaimId("missing-claim"),
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await Assert.ThrowsAsync<SqliteException>(
                () => repository.AddClaimIssueAsync(
                    claimIssue));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
