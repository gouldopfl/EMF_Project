using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteClaimRepositoryTests
{
    [Fact]
    public async Task Repository_StoresRetrievesAndFiltersClaims()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var schema =
                new VeteransClaimsSqliteSchema(databasePath);

            await schema.InitializeAsync();

            var veteranRepository =
                new SqliteVeteranRepository(databasePath);

            var veteranOne = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            var veteranTwo = new Veteran
            {
                Id = new VeteranId("veteran-002")
            };

            await veteranRepository.AddVeteranAsync(
                veteranOne);

            await veteranRepository.AddVeteranAsync(
                veteranTwo);

            IClaimRepository repository =
                new SqliteClaimRepository(databasePath);

            var claimOne = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteranOne.Id
            };

            var claimTwo = new Claim
            {
                Id = new ClaimId("claim-002"),
                VeteranId = veteranTwo.Id
            };

            await repository.AddClaimAsync(claimOne);
            await repository.AddClaimAsync(claimTwo);

            var stored =
                await repository.GetClaimAsync(claimOne.Id);

            var veteranOneClaims =
                await repository.GetClaimsAsync(
                    veteranOne.Id);

            Assert.NotNull(stored);
            Assert.Equal(claimOne.Id, stored!.Id);
            Assert.Equal(
                veteranOne.Id,
                stored.VeteranId);

            var filteredClaim =
                Assert.Single(veteranOneClaims);

            Assert.Equal(
                claimOne.Id,
                filteredClaim.Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RejectsClaimForMissingVeteran()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteClaimRepository(databasePath);

            await repository.InitializeAsync();

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId =
                    new VeteranId("missing-veteran")
            };

            await Assert.ThrowsAsync<SqliteException>(
                () => repository.AddClaimAsync(claim));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
